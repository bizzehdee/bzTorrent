using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using bzTorrent.Helpers;
using FluentAssertions;
using Xunit;

namespace bzTorrent.Tests
{
    public class UDPTrackerClientTests : IDisposable
    {
        private readonly UdpClient fakeTracker = new(new IPEndPoint(IPAddress.Loopback, 0));

        public void Dispose() => fakeTracker.Dispose();

        [Fact]
        public void Announce_ParsesIPv4PeerWithCorrectByteOrder()
        {
            var serverTask = Task.Run(() =>
            {
                // Scripts the BEP-15 connect + announce exchange, replying with a single IPv4
                // peer (10.0.0.1:6881) built from raw network-order bytes.
                IPEndPoint remote = null;
                var connectResult = fakeTracker.Receive(ref remote);
                var connectTxId = connectResult.GetBytes(12, 4);
                var fakeConnectionId = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

                var connectResponse = PackHelper.Int32(0).Cat(connectTxId).Cat(fakeConnectionId);
                fakeTracker.Send(connectResponse, connectResponse.Length, remote);

                var announceResult = fakeTracker.Receive(ref remote);
                var announceTxId = announceResult.GetBytes(12, 4);

                var peer = new byte[] { 10, 0, 0, 1, 0x1A, 0xE1 }; // 10.0.0.1:6881
                var announceResponse = PackHelper.Int32(1)
                    .Cat(announceTxId)
                    .Cat(PackHelper.Int32(1800))
                    .Cat(PackHelper.Int32(0))
                    .Cat(PackHelper.Int32(1))
                    .Cat(peer);
                fakeTracker.Send(announceResponse, announceResponse.Length, remote);
            });

            var trackerPort = ((IPEndPoint)fakeTracker.Client.LocalEndPoint).Port;
            var client = new UDPTrackerClient(5);

            var result = client.Announce($"udp://127.0.0.1:{trackerPort}/announce", new string('a', 40), "-bz0001-000000000000", 0, 0, 0, 2, 0, -1, 6881, 0);

            serverTask.Wait(TimeSpan.FromSeconds(5));

            result.Should().NotBeNull();
            result.Seeders.Should().Be(1);
            result.Peers.Should().ContainSingle();
            var peerEndpoint = result.Peers.GetEnumerator();
            peerEndpoint.MoveNext();
            peerEndpoint.Current.Address.Should().Be(IPAddress.Parse("10.0.0.1"),
                "the peer address must be built from raw network-order bytes, not double-flipped through IPAddress(long)");
            peerEndpoint.Current.Port.Should().Be(6881);
        }
    }
}
