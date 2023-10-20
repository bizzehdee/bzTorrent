namespace bzTorrent.Tests.Helpers
{
    using FluentAssertions;
    using System;
    using System.Diagnostics;
    using bzTorrent.Helpers;
    using Xunit;

    public class PackHelperTests
    {
        [Theory]
        [InlineData(5, 1280)]
        [InlineData(0, 0)]
        [InlineData(-32768, 128)]
        [InlineData(32767, -129)]
        public void Int16_ToBytes_ReturnsExpectedValue(short input, short expectedResult)
        {
            // Act
            var result = PackHelper.Int16(input);

            // Assert
            result.Should().ContainInOrder(BitConverter.GetBytes(expectedResult));
        }

        [Theory]
        [InlineData(5, 83886080)]
        [InlineData(0, 0)]
        [InlineData(-2147483648, 128)]
        [InlineData(2147483647, -129)]
        public void Int32_ToBytes_ReturnsExpectedValue(int input, int expectedResult)
        {
            // Act
            var result = PackHelper.Int32(input);

            // Assert
            result.Should().ContainInOrder(BitConverter.GetBytes(expectedResult));
        }

        [Theory]
        [InlineData(5, 360287970189639680)]
        [InlineData(0, 0)]
        [InlineData(-9223372036854775808, 128)]
        [InlineData(9223372036854775807, -129)]
        public void Int64_ToBytes_ReturnsExpectedValue(long input, long expectedResult)
        {
            // Act
            var result = PackHelper.Int64(input);

            // Assert
            result.Should().ContainInOrder(BitConverter.GetBytes(expectedResult));
        }

        [Theory]
        [InlineData(5, 1280)]
        [InlineData(0, 0)]
        [InlineData(65535, 65535)]
        [InlineData(32768, 128)]
        public void UInt16_ToBytes_ReturnsExpectedValue(ushort input, ushort expectedResult)
        {
            // Act
            var result = PackHelper.UInt16(input);

            // Assert
            result.Should().ContainInOrder(BitConverter.GetBytes(expectedResult));
        }

        [Theory]
        [InlineData(5, 83886080)]
        [InlineData(0, 0)]
        [InlineData(2147483647, 4294967167)]
        [InlineData(4294967295, 4294967295)]
        public void UInt32_ToBytes_ReturnsExpectedValue(uint input, uint expectedResult)
        {
            // Act
            var result = PackHelper.UInt32(input);

            // Assert
            result.Should().ContainInOrder(BitConverter.GetBytes(expectedResult));
        }

        [Theory]
        [InlineData(5, 360287970189639680)]
        [InlineData(0, 0)]
        [InlineData(9223372036854775807, 18446744073709551487)]
        [InlineData(18446744073709551615, 18446744073709551615)]
        public void UInt64_ToBytes_ReturnsExpectedValue(ulong input, ulong expectedResult)
        {
            // Act
            var result = PackHelper.UInt64(input);

            // Assert
            result.Should().ContainInOrder(BitConverter.GetBytes(expectedResult));
        }

        [Theory]
        [InlineData("ABCD", new byte[] { 0xAB, 0xCD })]
        [InlineData("ABC", new byte[] { 0xAB, 0xC0 })]
        public void Hex_ToBytes_ReturnsExpectedValue(string input, byte[] expectedResult)
        {
            // Act
            var result = PackHelper.Hex(input);

            // Assert
            result.Should().ContainInOrder(expectedResult);
        }
    }
}
