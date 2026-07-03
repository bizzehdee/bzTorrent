using System;
using FluentAssertions;
using Xunit;
using bzTorrent.Helpers;
using bzTorrent.IO;

namespace bzTorrent.Tests.IO
{
    public class UTPPacketCodecTests
    {
        [Fact]
        public void BuildThenParse_RoundTripsAllHeaderFields()
        {
            var payload = new byte[] { 1, 2, 3, 4 };

            var packet = UTPPacketCodec.BuildPacket(
                UTPPacketType.STData,
                connectionId: 0xABCD,
                timestamp: 0x11223344,
                timestampDiff: 0x55667788,
                wndSize: 0x00100000,
                seq: 0x0102,
                ack: 0x0304,
                payload);

            packet.Length.Should().Be(UTPPacketCodec.HeaderSize + payload.Length);

            var header = UTPPacketCodec.Parse(packet);
            header.Version.Should().Be(1);
            header.PacketType.Should().Be(UTPPacketType.STData);
            header.ConnectionIdRecvd.Should().Be(0xABCD);
            header.TimestampRecvd.Should().Be(0x11223344);
            header.TimestampDiffRecvd.Should().Be(0x55667788);
            header.WndSizeRecvd.Should().Be(0x00100000);
            header.SeqNumberRecvd.Should().Be(0x0102);
            header.AckNumberRecvd.Should().Be(0x0304);
        }

        [Fact]
        public void BuildPacket_EncodesTypeAndVersionInFirstByte()
        {
            var packet = UTPPacketCodec.BuildPacket(UTPPacketType.STSyn, 1, 0, 0, 0, 0, 0, null);

            // High nibble = packet type (STSyn = 4), low nibble = version (1).
            packet[0].Should().Be((4 << 4) | 1);
            packet[1].Should().Be(0); // extension byte
        }

        [Fact]
        public void BuildPacket_NullPayload_ProducesBareHeader()
        {
            var packet = UTPPacketCodec.BuildPacket(UTPPacketType.STState, 1, 0, 0, 0, 0, 0, null);

            packet.Length.Should().Be(UTPPacketCodec.HeaderSize);
        }

        [Fact]
        public void Parse_PortFieldsAreBigEndian()
        {
            var packet = UTPPacketCodec.BuildPacket(UTPPacketType.STData, 0x1234, 0, 0, 0, 0, 0, null);

            // connection id at bytes 2-3, big-endian.
            packet[2].Should().Be(0x12);
            packet[3].Should().Be(0x34);
        }

        [Fact]
        public void Parse_TooShortBuffer_Throws()
        {
            Action act = () => UTPPacketCodec.Parse(new byte[UTPPacketCodec.HeaderSize - 1]);
            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Parse_NullBuffer_Throws()
        {
            Action act = () => UTPPacketCodec.Parse(null);
            act.Should().Throw<ArgumentNullException>();
        }
    }
}
