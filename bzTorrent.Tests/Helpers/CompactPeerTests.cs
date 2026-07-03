using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Xunit;
using bzTorrent.Helpers;

namespace bzTorrent.Tests.Helpers
{
    public class CompactPeerTests
    {
        [Fact]
        public void ParsePeers_V4_DecodesAddressAndBigEndianPort()
        {
            // 1.2.3.4:6881 (0x1AE1) then 5.6.7.8:80 (0x0050)
            var data = new byte[] { 1, 2, 3, 4, 0x1A, 0xE1, 5, 6, 7, 8, 0x00, 0x50 };

            var peers = CompactPeer.ParsePeers(data, isV6: false).ToList();

            peers.Should().HaveCount(2);
            peers[0].Should().Be(new IPEndPoint(IPAddress.Parse("1.2.3.4"), 6881));
            peers[1].Should().Be(new IPEndPoint(IPAddress.Parse("5.6.7.8"), 80));
        }

        [Fact]
        public void ParsePeers_V4_IgnoresTrailingPartialEntry()
        {
            var data = new byte[] { 1, 2, 3, 4, 0x1A, 0xE1, 9, 9 };

            var peers = CompactPeer.ParsePeers(data, isV6: false).ToList();

            peers.Should().ContainSingle();
        }

        [Fact]
        public void ParsePeers_HonoursStartOffset()
        {
            // Two leading junk bytes then one v4 entry.
            var data = new byte[] { 0xFF, 0xFF, 1, 2, 3, 4, 0x1A, 0xE1 };

            var peers = CompactPeer.ParsePeers(data, isV6: false, start: 2).ToList();

            peers.Should().ContainSingle().Which.Should().Be(new IPEndPoint(IPAddress.Parse("1.2.3.4"), 6881));
        }

        [Fact]
        public void ParsePeers_NullData_ReturnsEmpty()
        {
            CompactPeer.ParsePeers(null, isV6: false).Should().BeEmpty();
        }

        [Fact]
        public void ParsePeer_ShortEntry_ReturnsNull()
        {
            CompactPeer.ParsePeer(new byte[] { 1, 2, 3 }, isV6: false).Should().BeNull();
        }

        [Fact]
        public void ParsePeer_ZeroPort_SkippedWhenRequested()
        {
            var entry = new byte[] { 1, 2, 3, 4, 0, 0 };

            CompactPeer.ParsePeer(entry, isV6: false, skipZeroPort: true).Should().BeNull();
            CompactPeer.ParsePeer(entry, isV6: false, skipZeroPort: false).Should().NotBeNull();
        }

        [Fact]
        public void WritePeers_V4_RoundTripsThroughParsePeers()
        {
            var peers = new[]
            {
                new IPEndPoint(IPAddress.Parse("192.168.0.1"), 6881),
                new IPEndPoint(IPAddress.Parse("10.0.0.5"), 51413)
            };

            var packed = CompactPeer.WritePeers(peers, isV6: false);
            packed.Should().HaveCount(peers.Length * 6);

            var parsed = CompactPeer.ParsePeers(packed, isV6: false).ToList();
            parsed.Should().Equal(peers);
        }

        [Fact]
        public void WritePeers_V4_WritesPortBigEndian()
        {
            var packed = CompactPeer.WritePeers(new[] { new IPEndPoint(IPAddress.Parse("1.2.3.4"), 6881) }, isV6: false);

            // Port 6881 = 0x1AE1 => high byte first.
            packed[4].Should().Be(0x1A);
            packed[5].Should().Be(0xE1);
        }

        [Fact]
        public void WriteAndParsePeers_V6_RoundTrips()
        {
            var peers = new[]
            {
                new IPEndPoint(IPAddress.Parse("2001:db8::1"), 6881),
                new IPEndPoint(IPAddress.Parse("fe80::abcd"), 1234)
            };

            var packed = CompactPeer.WritePeers(peers, isV6: true);
            packed.Should().HaveCount(peers.Length * 18);

            var parsed = CompactPeer.ParsePeers(packed, isV6: true).ToList();
            parsed.Should().Equal(peers);
        }

        [Fact]
        public void ParsePeers_FromEntrySequence_SkipsInvalidAndZeroPort()
        {
            var entries = new List<byte[]>
            {
                new byte[] { 1, 2, 3, 4, 0x1A, 0xE1 }, // valid
                new byte[] { 9, 9 },                    // too short
                new byte[] { 5, 6, 7, 8, 0, 0 }         // zero port
            };

            var peers = CompactPeer.ParsePeerEntries(entries, isV6: false, skipZeroPort: true).ToList();

            peers.Should().ContainSingle().Which.Should().Be(new IPEndPoint(IPAddress.Parse("1.2.3.4"), 6881));
        }

        [Fact]
        public void WriteAndParseNodes_V4_RoundTrips()
        {
            var id = Enumerable.Range(0, CompactPeer.NodeIdLength).Select(i => (byte)i).ToArray();
            var nodes = new List<(byte[] Id, IPEndPoint EndPoint)>
            {
                (id, new IPEndPoint(IPAddress.Parse("1.2.3.4"), 6881))
            };

            var packed = CompactPeer.WriteNodes(nodes, isV6: false);
            packed.Should().HaveCount(CompactPeer.NodeEntrySize(false));

            var parsed = CompactPeer.ParseNodes(packed, isV6: false).ToList();
            parsed.Should().ContainSingle();
            parsed[0].Id.Should().Equal(id);
            parsed[0].EndPoint.Should().Be(new IPEndPoint(IPAddress.Parse("1.2.3.4"), 6881));
        }

        [Fact]
        public void ParseNodes_SkipsZeroPortEntries()
        {
            var id = new byte[CompactPeer.NodeIdLength];
            var nodes = new List<(byte[] Id, IPEndPoint EndPoint)>
            {
                (id, new IPEndPoint(IPAddress.Parse("1.2.3.4"), 0))
            };

            var packed = CompactPeer.WriteNodes(nodes, isV6: false);

            CompactPeer.ParseNodes(packed, isV6: false).Should().BeEmpty();
        }
    }
}
