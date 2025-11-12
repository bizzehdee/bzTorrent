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
        public void Parse_SingleByte_AllBitsParsed_LSBFirst()
        {
            // 0b00010001 -> bit0 and bit4 set (LSB is bit 0)
            var payload = new byte[] { 0b0001_0001 };

            var result = BitfieldParser.Parse(payload);

            result.Should().HaveCount(8);
            result[0].Should().BeTrue();  // bit 0
            result[1].Should().BeFalse(); // bit 1
            result[2].Should().BeFalse(); // bit 2
            result[3].Should().BeFalse(); // bit 3
            result[4].Should().BeTrue();  // bit 4
            for (int i = 5; i < 8; i++) result[i].Should().BeFalse();
        }

        [Fact]
        public void Parse_MultiByte_CorrectIndexesAreSet()
        {
            // first byte: 0b00000001 -> bit 0 of overall array (index 0)
            // second byte: 0b00000010 -> bit 1 of second byte -> overall index 9
            var payload = new byte[] { 0b0000_0001, 0b0000_0010 };

            var result = BitfieldParser.Parse(payload);

            result.Should().HaveCount(16);
            result[0].Should().BeTrue();   // first byte, bit 0
            result[9].Should().BeTrue();   // second byte, bit 1 -> index 8 + 1 = 9
            // others should be false
            for (int i = 1; i < 9; i++) if (i != 9) result[i].Should().BeFalse();
            for (int i = 10; i < 16; i++) result[i].Should().BeFalse();
        }

        [Fact]
        public void Parse_WithExpectedBits_TrimsArray()
        {
            var payload = new byte[] { 0b0000_0001, 0b0000_0010 }; // 16 bits

            var trimmed = BitfieldParser.Parse(payload, expectedBits: 10);

            trimmed.Should().HaveCount(10);
            trimmed[0].Should().BeTrue();
            trimmed[9].Should().BeTrue();
        }
    }
}
