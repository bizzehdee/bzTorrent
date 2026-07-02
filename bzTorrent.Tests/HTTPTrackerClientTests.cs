using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using bzBencode;
using FluentAssertions;
using Xunit;

namespace bzTorrent.Tests
{
    public class HTTPTrackerClientTests
    {
        // A minimal loopback-only HTTP/1.1 server: real socket I/O (per project convention — no
        // external network in tests), always returns the same bencoded body regardless of path.
        private sealed class FixedResponseServer : IDisposable
        {
            private readonly TcpListener listener;
            private readonly byte[] body;

            public int Port { get; }

            public FixedResponseServer(byte[] body)
            {
                this.body = body;
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                _ = Task.Run(AcceptLoopAsync);
            }

            private async Task AcceptLoopAsync()
            {
                while (true)
                {
                    TcpClient client;
                    try { client = await listener.AcceptTcpClientAsync(); }
                    catch { return; }

                    _ = Task.Run(() => HandleClient(client));
                }
            }

            private void HandleClient(TcpClient client)
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                    {
                        string line;
                        while (!string.IsNullOrEmpty(line = reader.ReadLine())) { }
                    }

                    var header = $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                    var headerBytes = Encoding.ASCII.GetBytes(header);
                    stream.Write(headerBytes, 0, headerBytes.Length);
                    stream.Write(body, 0, body.Length);
                    stream.Flush();
                }
            }

            public void Dispose() => listener.Stop();
        }

        [Fact]
        public void Announce_ParsesIPv6PeersFromPeers6Field()
        {
            var v4Peer = new byte[] { 10, 0, 0, 1, 0x1A, 0xE1 }; // 10.0.0.1:6881
            var v6Address = IPAddress.Parse("2001:db8::1").GetAddressBytes();
            var v6Port = new byte[] { 0x1A, 0xE1 }; // 6881
            var v6Peer = new byte[16 + 2];
            Array.Copy(v6Address, v6Peer, 16);
            Array.Copy(v6Port, 0, v6Peer, 16, 2);

            var responseDict = new BDict
            {
                ["interval"] = new BInt(1800),
                ["complete"] = new BInt(5),
                ["incomplete"] = new BInt(2),
                ["peers"] = new BString { ByteValue = v4Peer },
                ["peers6"] = new BString { ByteValue = v6Peer }
            };
            var body = BencodingUtils.EncodeBytes(responseDict);

            using var server = new FixedResponseServer(body);
            var client = new HTTPTrackerClient(5);

            var result = client.Announce($"http://127.0.0.1:{server.Port}/announce", new string('a', 40), "-bz0001-000000000000", 0, 0, 0, 2, 0, -1, 6881, 0);

            result.Should().NotBeNull();
            result.Seeders.Should().Be(5);
            result.Leechers.Should().Be(2);
            var peers = result.Peers;
            peers.Should().Contain(p => p.Address.Equals(IPAddress.Parse("10.0.0.1")) && p.Port == 6881);
            peers.Should().Contain(p => p.Address.Equals(IPAddress.Parse("2001:db8::1")) && p.Port == 6881);
        }

        [Fact]
        public void Announce_IPv6OnlyResponse_StillParsesPeers()
        {
            var v6Address = IPAddress.Parse("fe80::1").GetAddressBytes();
            var v6Peer = new byte[16 + 2];
            Array.Copy(v6Address, v6Peer, 16);
            v6Peer[16] = 0x1A;
            v6Peer[17] = 0xE1;

            var responseDict = new BDict
            {
                ["interval"] = new BInt(1800),
                ["complete"] = new BInt(1),
                ["incomplete"] = new BInt(0),
                ["peers6"] = new BString { ByteValue = v6Peer }
            };
            var body = BencodingUtils.EncodeBytes(responseDict);

            using var server = new FixedResponseServer(body);
            var client = new HTTPTrackerClient(5);

            var result = client.Announce($"http://127.0.0.1:{server.Port}/announce", new string('a', 40), "-bz0001-000000000000", 0, 0, 0, 2, 0, -1, 6881, 0);

            result.Should().NotBeNull();
            result.Peers.Should().ContainSingle(p => p.Address.Equals(IPAddress.Parse("fe80::1")));
        }
    }
}
