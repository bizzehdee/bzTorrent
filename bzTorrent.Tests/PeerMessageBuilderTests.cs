using FluentAssertions;
using Xunit;
using bzTorrent;
using bzTorrent.Helpers;

namespace bzTorrent.Tests
{
    public class PeerMessageBuilderTests
    {
        [Fact]
        public void Add_Uint_DefaultsToBigEndian()
        {
            var payload = new PeerMessageBuilder(4).Add(1u).Message().Payload;

            payload.Should().Equal(0, 0, 0, 1);
        }

        [Fact]
        public void Add_Uint_HonoursLittleEndian()
        {
            // Previously the endianness argument was ignored and always packed big-endian.
            var payload = new PeerMessageBuilder(4).Add(1u, Endianness.Little).Message().Payload;

            payload.Should().Equal(1, 0, 0, 0);
        }

        [Fact]
        public void Add_Uint_HonoursBigEndianExplicitly()
        {
            var payload = new PeerMessageBuilder(4).Add(0x01020304u, Endianness.Big).Message().Payload;

            payload.Should().Equal(0x01, 0x02, 0x03, 0x04);
        }
    }
}
