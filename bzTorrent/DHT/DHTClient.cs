using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using bzBencode;
using bzTorrent.Data;
using bzTorrent.Helpers;

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
        private readonly DHTTransactionRegistry _transactions = new DHTTransactionRegistry();
        private readonly DHTQueryResponder _queryResponder;
        private readonly object _sendLock = new();
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
            _nodeId = new byte[CompactPeer.NodeIdLength];
            using (var rng = RandomNumberGenerator.Create())
			{
				rng.GetBytes(_nodeId);
			}

            // A single dual-stack IPv6 socket serves both address families (BEP-32): sends to an
            // IPv4 destination go out as IPv4-mapped IPv6, and incoming IPv4-mapped source
            // addresses are unmapped back to plain IPv4 before use — see ToSendEndpoint /
            // NormalizeReceived. This keeps the rest of the client (routing table, node/peer
            // parsing) working in terms of each peer's natural address family.
            _udp = new UdpClient(AddressFamily.InterNetworkV6);
            _udp.Client.DualMode = true;
            _udp.Client.Bind(new IPEndPoint(IPAddress.IPv6Any, port));

            _queryResponder = new DHTQueryResponder(_nodeId, _routingTable, K, SendTo);
        }

        // Sends a datagram, applying the dual-stack destination mapping and serialising sends.
        private void SendTo(byte[] bytes, IPEndPoint to)
        {
            lock (_sendLock)
            {
                _udp.Send(bytes, bytes.Length, ToSendEndpoint(to));
            }
        }

        // Dual-stack sockets require the destination endpoint to be IPv6-family; map an IPv4
        // destination to its IPv4-mapped IPv6 form (the socket/OS then routes it as IPv4 on the
        // wire).
        private static IPEndPoint ToSendEndpoint(IPEndPoint endpoint)
        {
            return endpoint.AddressFamily == AddressFamily.InterNetwork
                ? new IPEndPoint(endpoint.Address.MapToIPv6(), endpoint.Port)
                : endpoint;
        }

        // Unmap an IPv4-mapped IPv6 source address (how an IPv4 peer's address arrives on our
        // dual-stack socket) back to a plain IPv4 endpoint, so callers/tests see each peer in its
        // natural family rather than always-IPv6.
        private static IPEndPoint NormalizeReceived(IPEndPoint endpoint)
        {
            return endpoint.Address.IsIPv4MappedToIPv6
                ? new IPEndPoint(endpoint.Address.MapToIPv4(), endpoint.Port)
                : endpoint;
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

        /// <summary>
        /// A snapshot of the current routing-table nodes, so they can be persisted and fed
        /// back in on a later run (see <see cref="AddKnownNodes"/>) to avoid a cold DHT start.
        /// </summary>
        public IReadOnlyList<DHTNode> GetKnownNodes() => _routingTable.GetAll();

        /// <summary>
        /// Seeds the routing table with previously-known nodes. This lets the search loop
        /// proceed immediately (it otherwise blocks until bootstrap finds a node); stale nodes
        /// simply fail to respond and get superseded as fresh ones are learned.
        /// </summary>
        public void AddKnownNodes(IEnumerable<DHTNode> nodes)
        {
            if (nodes == null)
            {
                return;
            }

            foreach (var node in nodes)
            {
                _routingTable.AddOrUpdate(node);
            }
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
                if (response == null || !response.TryGetDict("r", out var r))
                    return discovered;

                if (r.TryGetBytes("id", out var nodeId))
                    _routingTable.AddOrUpdate(new DHTNode(nodeId, node.EndPoint));

                if (r.TryGetList("values", out var values))
                {
                    foreach (var peer in ParseCompactPeers(values))
                        PeerFound?.Invoke(peer);
                }

                if (r.TryGetList("values6", out var values6))
                {
                    foreach (var peer in ParseCompactPeers6(values6))
                        PeerFound?.Invoke(peer);
                }

                discovered.AddRange(IngestNodes(r));
            }
            catch { }
            return discovered;
        }

        private async Task FindNodeAsync(IPEndPoint endpoint, byte[] target)
        {
            try
            {
                var (txId, txKey) = _transactions.NextTxId();
                var query = BuildQuery(txId, "find_node", new BDict
                {
                    { "id", new BString { ByteValue = _nodeId } },
                    { "target", new BString { ByteValue = target } },
                    { "want", WantBoth() }
                });
                var response = await SendQueryAsync(endpoint, query, txKey);
                if (response == null || !response.TryGetDict("r", out var r))
                {
                    return;
                }

                // Record the responder itself so bootstrap endpoints seed the routing table
                // even when they return no (or few) additional nodes.
                if (r.TryGetBytes("id", out var responderId))
                {
                    _routingTable.AddOrUpdate(new DHTNode(responderId, endpoint));
                }

                IngestNodes(r);
            }
            catch { }
        }

        private async Task<BDict> GetPeersQueryAsync(IPEndPoint endpoint, byte[] infoHash)
        {
            var (txId, txKey) = _transactions.NextTxId();
            var query = BuildQuery(txId, "get_peers", new BDict
            {
                { "id", new BString { ByteValue = _nodeId } },
                { "info_hash", new BString { ByteValue = infoHash } },
                { "want", WantBoth() }
            });
            return await SendQueryAsync(endpoint, query, txKey);
        }

        // BEP-32: signals to the queried node that we'd like both IPv4 ("n4") and IPv6 ("n6")
        // compact node lists back, since our dual-stack socket can use either.
        private static BList WantBoth()
        {
            return new BList { new BString("n4"), new BString("n6") };
        }

        private async Task<BDict> SendQueryAsync(IPEndPoint endpoint, BDict query, string txKey)
        {
            var tcs = _transactions.Register(txKey);

            try
            {
                var bytes = BencodingUtils.EncodeBytes(query);
                lock (_sendLock)
                    _udp.Send(bytes, bytes.Length, ToSendEndpoint(endpoint));
            }
            catch
            {
                _transactions.Abandon(txKey);
                return null;
            }

            var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(RequestTimeoutSeconds));
            timeoutCts.Token.Register(() => _transactions.Cancel(txKey));

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
                    _ = Task.Run(() => HandlePacket(NormalizeReceived(result.RemoteEndPoint), result.Buffer));
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
                if (!message.TryGetString("y", out var y)) return;

                if (y == "r" || y == "e")
                {
                    if (message.TryGetBytes("t", out var txId))
                    {
                        _transactions.Complete(txId, message);
                    }
                }
                else if (y == "q")
                {
                    _queryResponder.Respond(from, message);
                }
            }
            catch { }
        }

        // Builds the standard query envelope { t, y:"q", q, a } shared by every outgoing query.
        private static BDict BuildQuery(byte[] txId, string queryName, BDict args)
        {
            return new BDict
            {
                { "t", new BString { ByteValue = txId } },
                { "y", new BString("q") },
                { "q", new BString(queryName) },
                { "a", args }
            };
        }

        // Adds any "nodes"/"nodes6" entries from a response's "r" dict to the routing table and
        // returns them. Shared by the find_node and get_peers response handlers.
        private IReadOnlyList<DHTNode> IngestNodes(BDict r)
        {
            var discovered = new List<DHTNode>();

            if (r.TryGetValue("nodes", out var nodesVal) && nodesVal is BString nodesStr)
            {
                foreach (var node in ParseCompactNodes(nodesStr.ByteValue))
                {
                    _routingTable.AddOrUpdate(node);
                    discovered.Add(node);
                }
            }

            if (r.TryGetValue("nodes6", out var nodes6Val) && nodes6Val is BString nodes6Str)
            {
                foreach (var node in ParseCompactNodes6(nodes6Str.ByteValue))
                {
                    _routingTable.AddOrUpdate(node);
                    discovered.Add(node);
                }
            }

            return discovered;
        }

        private static IEnumerable<DHTNode> ParseCompactNodes(byte[] data)
        {
            return CompactPeer.ParseNodes(data, isV6: false).Select(n => new DHTNode(n.Id, n.EndPoint));
        }

        private static IEnumerable<DHTNode> ParseCompactNodes6(byte[] data)
        {
            return CompactPeer.ParseNodes(data, isV6: true).Select(n => new DHTNode(n.Id, n.EndPoint));
        }

        private static IEnumerable<IPEndPoint> ParseCompactPeers(BList values)
        {
            return CompactPeer.ParsePeerEntries(values.Select(item => ((BString)item).ByteValue), isV6: false, skipZeroPort: true);
        }

        private static IEnumerable<IPEndPoint> ParseCompactPeers6(BList values)
        {
            return CompactPeer.ParsePeerEntries(values.Select(item => ((BString)item).ByteValue), isV6: true, skipZeroPort: true);
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
