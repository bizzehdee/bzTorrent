using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using bzBencode;

namespace bzTorrent.DHT
{
    public class DHTClient : IDisposable
    {
        private const int Alpha = 3;
        private const int K = 8;
        private const int MaxNodesPerSearch = 300;
        private const int RequestTimeoutSeconds = 5;
        private const int BootstrapAttempts = 8;

        public event Action<IPEndPoint> PeerFound;

        private readonly byte[] _nodeId;
        private readonly UdpClient _udp;
        private readonly DHTRoutingTable _routingTable = new DHTRoutingTable();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<BDict>> _pending
            = new();
        private readonly object _sendLock = new();
        private int _txCounter = 0;
        private CancellationTokenSource _cts;
        private bool _disposed = false;

		public int NodeCount
        {
            get => _routingTable.Count;
		}

        // The local UDP port this client is bound to (useful for announcing and for tests).
        public int LocalPort => ((IPEndPoint)_udp.Client.LocalEndPoint).Port;

		public DHTClient(int port = 0)
        {
            _nodeId = new byte[20];
            using (var rng = RandomNumberGenerator.Create())
			{
				rng.GetBytes(_nodeId);
			}

			_udp = new UdpClient(port);
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _cts.Token.Register(() => { try { _udp.Close(); } catch { } });
            _ = ReceiveLoopAsync();
        }

		public void Stop()
		{
			_cts?.Cancel();
		}

		public async Task BootstrapAsync(IEnumerable<IPEndPoint> nodes)
        {
            var bootstrapNodes = nodes?.ToList() ?? new List<IPEndPoint>();
            if (bootstrapNodes.Count == 0)
            {
                return;
            }

            var ct = _cts?.Token ?? CancellationToken.None;

            // UDP is lossy and NAT mappings take a moment to establish, so a single round of
            // queries frequently returns nothing — which previously left the routing table
            // empty and the client idle forever. Query the bootstrap nodes with find_node
            // (which returns real nodes to seed the table, unlike ping) and retry until the
            // table is populated. Each FindNodeAsync swallows its own errors, so one bad or
            // unreachable bootstrap node can never fault the whole bootstrap.
            for (var attempt = 0; attempt < BootstrapAttempts && _routingTable.Count == 0; attempt++)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                await Task.WhenAll(bootstrapNodes.Select(node => FindNodeAsync(node, _nodeId)));

                if (_routingTable.Count == 0)
                {
                    try { await Task.Delay(TimeSpan.FromSeconds(1), ct); }
                    catch (OperationCanceledException) { return; }
                }
            }

            // Widen the routing table by asking the nodes we just learned about.
            var knownNodes = _routingTable.GetAll();
            if (knownNodes.Count > 0)
            {
                await Task.WhenAll(knownNodes.Select(node => FindNodeAsync(node.EndPoint, _nodeId)));
            }
        }

        public void StartSearch(byte[] infoHash)
        {
            var ct = _cts?.Token ?? CancellationToken.None;
            _ = SearchLoopAsync(infoHash, ct);
        }

        private async Task SearchLoopAsync(byte[] infoHash, CancellationToken ct)
        {
            // Wait until we have routing table entries from bootstrap
            while (_routingTable.Count == 0 && !ct.IsCancellationRequested)
			{
				await Task.Delay(1000, ct);
			}

			while (!ct.IsCancellationRequested)
            {
                try
                {
                    await GetPeersIterativeAsync(infoHash, ct);
                    await Task.Delay(TimeSpan.FromMinutes(5), ct);
                }
                catch (OperationCanceledException) { break; }
                catch
                {
                    try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        private async Task GetPeersIterativeAsync(byte[] infoHash, CancellationToken ct)
        {
            var queried = new HashSet<string>();
            var toQuery = new Queue<DHTNode>(_routingTable.GetClosest(infoHash, K));

            while (toQuery.Count > 0 && queried.Count < MaxNodesPerSearch && !ct.IsCancellationRequested)
            {
                var batch = new List<DHTNode>();
                while (batch.Count < Alpha && toQuery.Count > 0)
                {
                    var node = toQuery.Dequeue();
                    if (queried.Add(EndpointKey(node.EndPoint)))
					{
						batch.Add(node);
					}
				}

                if (batch.Count == 0)
				{
					break;
				}

				var results = await Task.WhenAll(batch.Select(n => QueryNodeForPeersAsync(n, infoHash)));

                foreach (var newNodes in results)
				{
					foreach (var n in newNodes)
                    {
                        if (!queried.Contains(EndpointKey(n.EndPoint)))
						{
							toQuery.Enqueue(n);
						}
					}
				}
			}
        }

        private async Task<IReadOnlyList<DHTNode>> QueryNodeForPeersAsync(DHTNode node, byte[] infoHash)
        {
            var discovered = new List<DHTNode>();
            try
            {
                var response = await GetPeersQueryAsync(node.EndPoint, infoHash);
                if (response == null || !response.TryGetValue("r", out var rv))
                    return discovered;

                var r = (BDict)rv;

                if (r.TryGetValue("id", out var idVal))
                    _routingTable.AddOrUpdate(new DHTNode(((BString)idVal).ByteValue, node.EndPoint));

                if (r.TryGetValue("values", out var valuesVal))
                {
                    foreach (var peer in ParseCompactPeers((BList)valuesVal))
                        PeerFound?.Invoke(peer);
                }

                if (r.TryGetValue("nodes", out var nodesVal))
                {
                    foreach (var n in ParseCompactNodes(((BString)nodesVal).ByteValue))
                    {
                        _routingTable.AddOrUpdate(n);
                        discovered.Add(n);
                    }
                }
            }
            catch { }
            return discovered;
        }

        private async Task FindNodeAsync(IPEndPoint endpoint, byte[] target)
        {
            try
            {
                var (txId, txKey) = NextTxId();
                var query = new BDict
                {
                    { "t", new BString { ByteValue = txId } },
                    { "y", new BString("q") },
                    { "q", new BString("find_node") },
                    { "a", new BDict
                        {
                            { "id", new BString { ByteValue = _nodeId } },
                            { "target", new BString { ByteValue = target } }
                        }
                    }
                };
                var response = await SendQueryAsync(endpoint, query, txKey);
                if (response == null || !response.TryGetValue("r", out var rv) || !(rv is BDict r))
                {
                    return;
                }

                // Record the responder itself so bootstrap endpoints seed the routing table
                // even when they return no (or few) additional nodes.
                if (r.TryGetValue("id", out var idVal) && idVal is BString idStr)
                {
                    _routingTable.AddOrUpdate(new DHTNode(idStr.ByteValue, endpoint));
                }

                if (r.TryGetValue("nodes", out var nodesVal) && nodesVal is BString nodesStr)
                {
                    foreach (var node in ParseCompactNodes(nodesStr.ByteValue))
                        _routingTable.AddOrUpdate(node);
                }
            }
            catch { }
        }

        private async Task<BDict> GetPeersQueryAsync(IPEndPoint endpoint, byte[] infoHash)
        {
            var (txId, txKey) = NextTxId();
            var query = new BDict
            {
                { "t", new BString { ByteValue = txId } },
                { "y", new BString("q") },
                { "q", new BString("get_peers") },
                { "a", new BDict
                    {
                        { "id", new BString { ByteValue = _nodeId } },
                        { "info_hash", new BString { ByteValue = infoHash } }
                    }
                }
            };
            return await SendQueryAsync(endpoint, query, txKey);
        }

        private async Task<BDict> SendQueryAsync(IPEndPoint endpoint, BDict query, string txKey)
        {
            var tcs = new TaskCompletionSource<BDict>();
            _pending[txKey] = tcs;

            try
            {
                var bytes = BencodingUtils.EncodeBytes(query);
                lock (_sendLock)
                    _udp.Send(bytes, bytes.Length, endpoint);
            }
            catch
            {
                _pending.TryRemove(txKey, out _);
                return null;
            }

            var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(RequestTimeoutSeconds));
            timeoutCts.Token.Register(() =>
            {
                _pending.TryRemove(txKey, out _);
                tcs.TrySetCanceled();
            });

            try
            {
                return await tcs.Task;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            finally
            {
                timeoutCts.Dispose();
            }
        }

        private async Task ReceiveLoopAsync()
        {
            while (!_disposed)
            {
                try
                {
                    var result = await _udp.ReceiveAsync();
                    _ = Task.Run(() => HandlePacket(result.RemoteEndPoint, result.Buffer));
                }
                catch (ObjectDisposedException) { break; }
                catch (OperationCanceledException) { break; }
                catch (SocketException)
                {
                    // A transient UDP error (e.g. an ICMP port-unreachable surfaced from a
                    // prior send on some platforms) must NOT kill the receive loop, otherwise
                    // every outstanding and future query would silently time out and the
                    // client would sit idle forever. Keep listening unless we are shutting down.
                    if (_disposed || _cts == null || _cts.IsCancellationRequested)
                    {
                        break;
                    }
                }
                catch { }
            }
        }

        private void HandlePacket(IPEndPoint from, byte[] data)
        {
            try
            {
                if (!(BencodingUtils.Decode(data) is BDict message)) return;
                if (!message.TryGetValue("y", out var yVal)) return;

                var y = ((BString)yVal).Value;

                if (y == "r" || y == "e")
                {
                    if (message.TryGetValue("t", out var tVal))
                    {
                        var key = Convert.ToBase64String(((BString)tVal).ByteValue);
                        if (_pending.TryRemove(key, out var tcs))
                            tcs.TrySetResult(message);
                    }
                }
                else if (y == "q")
                {
                    HandleQuery(from, message);
                }
            }
            catch { }
        }

        private void HandleQuery(IPEndPoint from, BDict message)
        {
            if (!message.TryGetValue("q", out var qVal)) return;
            if (!message.TryGetValue("t", out var tVal)) return;
            if (!message.TryGetValue("a", out var aVal)) return;

            var q = ((BString)qVal).Value;
            var txId = ((BString)tVal).ByteValue;
            var a = (BDict)aVal;

            if (a.TryGetValue("id", out var idVal))
                _routingTable.AddOrUpdate(new DHTNode(((BString)idVal).ByteValue, from));

            BDict responseArgs;
            switch (q)
            {
                case "ping":
                    responseArgs = new BDict { { "id", new BString { ByteValue = _nodeId } } };
                    break;

                case "find_node":
                    if (!a.TryGetValue("target", out var targetVal)) return;
                    responseArgs = new BDict
                    {
                        { "id", new BString { ByteValue = _nodeId } },
                        { "nodes", new BString { ByteValue = CompactNodes(_routingTable.GetClosest(((BString)targetVal).ByteValue, K)) } }
                    };
                    break;

                case "get_peers":
                    if (!a.TryGetValue("info_hash", out var hashVal)) return;
                    responseArgs = new BDict
                    {
                        { "id", new BString { ByteValue = _nodeId } },
                        { "token", new BString { ByteValue = from.Address.GetAddressBytes() } },
                        { "nodes", new BString { ByteValue = CompactNodes(_routingTable.GetClosest(((BString)hashVal).ByteValue, K)) } }
                    };
                    break;

                case "announce_peer":
                    responseArgs = new BDict { { "id", new BString { ByteValue = _nodeId } } };
                    break;

                default:
                    return;
            }

            var response = new BDict
            {
                { "t", new BString { ByteValue = txId } },
                { "y", new BString("r") },
                { "r", responseArgs }
            };

            try
            {
                var bytes = BencodingUtils.EncodeBytes(response);
                lock (_sendLock)
                    _udp.Send(bytes, bytes.Length, from);
            }
            catch { }
        }

        private (byte[] txId, string txKey) NextTxId()
        {
            var id = Interlocked.Increment(ref _txCounter);
            var bytes = new byte[] { (byte)((id >> 8) & 0xFF), (byte)(id & 0xFF) };
            return (bytes, Convert.ToBase64String(bytes));
        }

        private static byte[] CompactNodes(IList<DHTNode> nodes)
        {
            var result = new byte[nodes.Count * 26];
            for (int i = 0; i < nodes.Count; i++)
            {
                Array.Copy(nodes[i].Id, 0, result, i * 26, 20);
                var addr = nodes[i].EndPoint.Address.GetAddressBytes();
                Array.Copy(addr, 0, result, i * 26 + 20, 4);
                result[i * 26 + 24] = (byte)((nodes[i].EndPoint.Port >> 8) & 0xFF);
                result[i * 26 + 25] = (byte)(nodes[i].EndPoint.Port & 0xFF);
            }
            return result;
        }

        private static IEnumerable<DHTNode> ParseCompactNodes(byte[] data)
        {
            for (int i = 0; i + 26 <= data.Length; i += 26)
            {
                var id = new byte[20];
                Array.Copy(data, i, id, 0, 20);
                var ip = new IPAddress(new byte[] { data[i + 20], data[i + 21], data[i + 22], data[i + 23] });
                var port = (data[i + 24] << 8) | data[i + 25];
                if (port > 0)
                    yield return new DHTNode(id, new IPEndPoint(ip, port));
            }
        }

        private static IEnumerable<IPEndPoint> ParseCompactPeers(BList values)
        {
            foreach (var item in values)
            {
                var bytes = ((BString)item).ByteValue;
                if (bytes == null || bytes.Length < 6) continue;
                var ip = new IPAddress(new byte[] { bytes[0], bytes[1], bytes[2], bytes[3] });
                var port = (bytes[4] << 8) | bytes[5];
                if (port > 0)
                    yield return new IPEndPoint(ip, port);
            }
        }

        private static string EndpointKey(IPEndPoint ep) => $"{ep.Address}:{ep.Port}";

        public void Dispose()
        {
            _disposed = true;
            _cts?.Cancel();
            _udp?.Dispose();
        }
    }
}
