using System;
using FluentAssertions;
using Xunit;
using bzTorrent.Helpers;

namespace bzTorrent.Tests.Helpers
{
    public class BitfieldParserTests
    {
        [Fact]
        public void Parse_NullPayload_ThrowsArgumentNullException()
        {
            Action act = () => BitfieldParser.Parse(null);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Parse_SingleByte_AllBitsParsed_MSBFirst()
        {
            // BitTorrent bitfield is MSB-first: piece 0 = bit 7 (MSB) of byte 0.
            // 0b0001_0001: bit 3 from MSB (index 3) and bit 7 from MSB (index 7, LSB) are set.
            var payload = new byte[] { 0b0001_0001 };

            var result = BitfieldParser.Parse(payload);

            result.Should().HaveCount(8);
            result[0].Should().BeFalse();
            result[1].Should().BeFalse();
            result[2].Should().BeFalse();
            result[3].Should().BeTrue();  // 4th bit from MSB
            result[4].Should().BeFalse();
            result[5].Should().BeFalse();
            result[6].Should().BeFalse();
            result[7].Should().BeTrue();  // LSB
        }

        [Fact]
        public void Parse_MultiByte_CorrectIndexesAreSet()
        {
            // BitTorrent bitfield is MSB-first.
            // first byte  0b0000_0001: only LSB set -> index 7
            // second byte 0b0000_0010: second LSB set -> index 8 + 6 = 14
            var payload = new byte[] { 0b0000_0001, 0b0000_0010 };

            var result = BitfieldParser.Parse(payload);

            result.Should().HaveCount(16);
            result[7].Should().BeTrue();   // first byte LSB  -> piece 7
            result[14].Should().BeTrue();  // second byte, bit 6 from MSB -> piece 14
            for (int i = 0; i < 16; i++)
            {
                if (i != 7 && i != 14)
                    result[i].Should().BeFalse();
            }
        }

        [Fact]
        public void Parse_WithExpectedBits_TrimsArray()
        {
            // Same bytes as Parse_MultiByte; trimmed to 10 bits.
            // result[7]=true falls within range; result[14]=true is cut off.
            var payload = new byte[] { 0b0000_0001, 0b0000_0010 };

            var trimmed = BitfieldParser.Parse(payload, expectedBits: 10);

            trimmed.Should().HaveCount(10);
            trimmed[7].Should().BeTrue();
            for (int i = 0; i < 10; i++)
            {
                if (i != 7)
                    trimmed[i].Should().BeFalse();
            }
        }
    }
}
