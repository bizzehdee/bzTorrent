using System.Linq;
using FluentAssertions;
using Xunit;
using bzTorrent.Data;

namespace bzTorrent.Tests.Data
{
    public class PeerHandshakeCodecTests
    {
        private static PeerClientHandshake SampleHandshake() => new()
        {
            InfoHash = "C1463792A1FF36A237E3A0F68BADEB0D3764E9BB", // 40 hex chars = 20 bytes
            PeerId = "-bz0001-abcdefghijkl",                       // 20 chars
            ReservedBytes = new byte[] { 0, 0, 0, 0, 0, 0x10, 0, 4 }
        };

        [Fact]
        public void BuildThenTryParse_RoundTrips()
        {
            var original = SampleHandshake();

            var bytes = PeerHandshakeCodec.Build(original);
            bytes.Length.Should().Be(PeerHandshakeCodec.HandshakeLength(PeerHandshakeCodec.ProtocolName.Length));

            PeerHandshakeCodec.TryParse(bytes, out var parsed, out var consumed).Should().BeTrue();
            consumed.Should().Be(bytes.Length);
            parsed.ProtocolHeader.Should().Be(PeerHandshakeCodec.ProtocolName);
            parsed.InfoHash.Should().Be(original.InfoHash);
            parsed.PeerId.Should().Be(original.PeerId);
            parsed.ReservedBytes.Should().Equal(original.ReservedBytes);
        }

        [Fact]
        public void Build_FirstByteIsProtocolNameLength()
        {
            var bytes = PeerHandshakeCodec.Build(SampleHandshake());

            bytes[0].Should().Be((byte)PeerHandshakeCodec.ProtocolName.Length);
        }

        [Fact]
        public void TryParse_IncompleteBuffer_ReturnsFalse()
        {
            var bytes = PeerHandshakeCodec.Build(SampleHandshake());

            PeerHandshakeCodec.TryParse(bytes.Take(bytes.Length - 1).ToArray(), out _, out _).Should().BeFalse();
        }

        [Fact]
        public void TryParse_EmptyBuffer_ReturnsFalse()
        {
            PeerHandshakeCodec.TryParse(System.Array.Empty<byte>(), out _, out _).Should().BeFalse();
        }

        [Fact]
        public void TryParse_WrongProtocolName_ReturnsFalse()
        {
            var bytes = PeerHandshakeCodec.Build(SampleHandshake());
            bytes[1] = (byte)'X'; // corrupt the protocol name

            PeerHandshakeCodec.TryParse(bytes, out _, out _).Should().BeFalse();
        }

        [Fact]
        public void TryParse_LeavesTrailingBytesUnconsumed()
        {
            var bytes = PeerHandshakeCodec.Build(SampleHandshake());
            var withTrailer = bytes.Concat(new byte[] { 1, 2, 3 }).ToArray();

            PeerHandshakeCodec.TryParse(withTrailer, out _, out var consumed).Should().BeTrue();
            consumed.Should().Be(bytes.Length);
        }
    }
}
