using System.Text;
using bzTorrent.IO;
using FluentAssertions;
using Xunit;

namespace bzTorrent.Tests.IO
{
    public class UTPReorderBufferTests
    {
        private static byte[] Payload(string s) => Encoding.ASCII.GetBytes(s);

        [Fact]
        public void Accept_InOrderPacket_DeliversImmediatelyAndAdvancesAck()
        {
            var sut = new UTPReorderBuffer();

            var result = sut.Accept(seq: 2, ackNumber: 1, Payload("a"));

            result.AckNumber.Should().Be(2);
            result.DeliverablePayloads.Should().ContainSingle();
            result.DeliverablePayloads[0].Should().BeEquivalentTo(Payload("a"));
        }

        [Fact]
        public void Accept_FuturePacket_BuffersAndDoesNotAdvanceAck()
        {
            var sut = new UTPReorderBuffer();

            var result = sut.Accept(seq: 3, ackNumber: 1, Payload("c"));

            result.AckNumber.Should().Be(1);
            result.DeliverablePayloads.Should().BeEmpty();
            sut.BufferedCount.Should().Be(1);
        }

        [Fact]
        public void Accept_PacketThatFillsGap_DeliversBufferedPacketsInOrder()
        {
            var sut = new UTPReorderBuffer();
            var ack = (ushort)1;

            // Packet 3 arrives first (gap at 2) and gets buffered.
            var r1 = sut.Accept(seq: 3, ackNumber: ack, Payload("c"));
            ack = r1.AckNumber;
            r1.DeliverablePayloads.Should().BeEmpty();

            // Packet 2 arrives, filling the gap — both 2 and the buffered 3 become deliverable.
            var r2 = sut.Accept(seq: 2, ackNumber: ack, Payload("b"));

            r2.AckNumber.Should().Be(3);
            r2.DeliverablePayloads.Should().HaveCount(2);
            r2.DeliverablePayloads[0].Should().BeEquivalentTo(Payload("b"));
            r2.DeliverablePayloads[1].Should().BeEquivalentTo(Payload("c"));
            sut.BufferedCount.Should().Be(0);
        }

        [Fact]
        public void Accept_DuplicatePacket_IsDroppedAndAckUnchanged()
        {
            var sut = new UTPReorderBuffer();

            var result = sut.Accept(seq: 1, ackNumber: 2, Payload("old"));

            result.AckNumber.Should().Be(2);
            result.DeliverablePayloads.Should().BeEmpty();
            sut.BufferedCount.Should().Be(0);
        }

        [Fact]
        public void Accept_HandlesSeqNumberWraparound()
        {
            var sut = new UTPReorderBuffer();

            var result = sut.Accept(seq: 0, ackNumber: 65535, Payload("wrap"));

            result.AckNumber.Should().Be(0);
            result.DeliverablePayloads.Should().ContainSingle();
        }

        [Fact]
        public void Accept_MultipleOutOfOrderPackets_AllDeliveredOnceGapFills()
        {
            var sut = new UTPReorderBuffer();
            var ack = (ushort)0;

            sut.Accept(seq: 3, ackNumber: ack, Payload("c"));
            sut.Accept(seq: 4, ackNumber: ack, Payload("d"));
            sut.Accept(seq: 2, ackNumber: ack, Payload("b"));

            var result = sut.Accept(seq: 1, ackNumber: ack, Payload("a"));

            result.AckNumber.Should().Be(4);
            result.DeliverablePayloads.Should().HaveCount(4);
            result.DeliverablePayloads[0].Should().BeEquivalentTo(Payload("a"));
            result.DeliverablePayloads[1].Should().BeEquivalentTo(Payload("b"));
            result.DeliverablePayloads[2].Should().BeEquivalentTo(Payload("c"));
            result.DeliverablePayloads[3].Should().BeEquivalentTo(Payload("d"));
        }
    }
}
