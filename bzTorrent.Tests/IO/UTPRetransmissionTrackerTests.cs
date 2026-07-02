using System;
using bzTorrent.IO;
using FluentAssertions;
using Xunit;

namespace bzTorrent.Tests.IO
{
    public class UTPRetransmissionTrackerTests
    {
        [Fact]
        public void OnAck_ClearsAckedPacketAndReturnsItsByteCount()
        {
            var sut = new UTPRetransmissionTracker();
            var now = DateTime.UtcNow;
            sut.OnSent(1, UTPSocket.PacketType.STData, new byte[100], now);

            var bytesAcked = sut.OnAck(1, now.AddMilliseconds(50));

            bytesAcked.Should().Be(100);
            sut.InFlightCount.Should().Be(0);
        }

        [Fact]
        public void OnAck_IsCumulative_ClearsAllPacketsUpToAckNumber()
        {
            var sut = new UTPRetransmissionTracker();
            var now = DateTime.UtcNow;
            sut.OnSent(1, UTPSocket.PacketType.STData, new byte[10], now);
            sut.OnSent(2, UTPSocket.PacketType.STData, new byte[20], now);
            sut.OnSent(3, UTPSocket.PacketType.STData, new byte[30], now);

            var bytesAcked = sut.OnAck(2, now.AddMilliseconds(10));

            bytesAcked.Should().Be(30);
            sut.InFlightCount.Should().Be(1);
        }

        [Fact]
        public void OnAck_HandlesSeqNumberWraparound()
        {
            var sut = new UTPRetransmissionTracker();
            var now = DateTime.UtcNow;
            sut.OnSent(65534, UTPSocket.PacketType.STData, new byte[5], now);
            sut.OnSent(65535, UTPSocket.PacketType.STData, new byte[5], now);
            sut.OnSent(0, UTPSocket.PacketType.STData, new byte[5], now);
            sut.OnSent(1, UTPSocket.PacketType.STData, new byte[5], now);

            var bytesAcked = sut.OnAck(0, now.AddMilliseconds(10));

            bytesAcked.Should().Be(15);
            sut.InFlightCount.Should().Be(1);
        }

        [Fact]
        public void GetDueForRetransmit_BeforeRtoElapses_ReturnsEmpty()
        {
            var sut = new UTPRetransmissionTracker();
            var now = DateTime.UtcNow;
            sut.OnSent(1, UTPSocket.PacketType.STData, new byte[10], now);

            var due = sut.GetDueForRetransmit(now.AddMilliseconds(10));

            due.Should().BeEmpty();
        }

        [Fact]
        public void GetDueForRetransmit_AfterRtoElapses_ReturnsPacket()
        {
            var sut = new UTPRetransmissionTracker();
            var now = DateTime.UtcNow;
            sut.OnSent(1, UTPSocket.PacketType.STData, new byte[10], now);

            var due = sut.GetDueForRetransmit(now.AddMilliseconds(sut.RtoMs + 1));

            due.Should().ContainSingle(p => p.Seq == 1);
        }

        [Fact]
        public void MarkRetransmitted_IncrementsTransmitCountAndAppliesExponentialBackoff()
        {
            var sut = new UTPRetransmissionTracker();
            var now = DateTime.UtcNow;
            sut.OnSent(1, UTPSocket.PacketType.STData, new byte[10], now);

            var initialRto = sut.RtoMs;
            var firstDueAt = now.AddMilliseconds(initialRto + 1);
            var due = sut.GetDueForRetransmit(firstDueAt);
            var packet = due[0];
            sut.MarkRetransmitted(packet, firstDueAt);

            packet.TransmitCount.Should().Be(2);

            // Backoff doubles the effective RTO, so it should not be due again immediately after
            // the *original* RTO interval — only after roughly 2x that.
            var notYetDue = sut.GetDueForRetransmit(firstDueAt.AddMilliseconds(initialRto + 1));
            notYetDue.Should().BeEmpty();
        }

        [Fact]
        public void HasExceededMaxRetransmits_AfterTooManyRetries_ReturnsTrue()
        {
            var sut = new UTPRetransmissionTracker();
            var now = DateTime.UtcNow;
            sut.OnSent(1, UTPSocket.PacketType.STData, new byte[10], now);

            var packet = sut.GetDueForRetransmit(now.AddSeconds(1))[0];
            for (var i = 0; i < UTPRetransmissionTracker.MaxRetransmits; i++)
            {
                sut.MarkRetransmitted(packet, now);
            }

            sut.HasExceededMaxRetransmits(now.AddSeconds(1000)).Should().BeTrue();
        }

        [Fact]
        public void OnAck_OnlySamplesRttFromNonRetransmittedPackets()
        {
            // Karn's algorithm: a retransmitted packet's ack is ambiguous about which
            // transmission it acks, so RTO must not be updated from it. We can't observe SrttMs
            // directly, but we can observe that RTO stays at its default (no sample taken) when
            // every acked packet was retransmitted.
            var sut = new UTPRetransmissionTracker();
            var now = DateTime.UtcNow;
            sut.OnSent(1, UTPSocket.PacketType.STData, new byte[10], now);

            var packet = sut.GetDueForRetransmit(now.AddSeconds(1))[0];
            sut.MarkRetransmitted(packet, now.AddSeconds(1));

            var rtoBefore = sut.RtoMs;
            sut.OnAck(1, now.AddSeconds(2));

            sut.RtoMs.Should().Be(rtoBefore, "the only acked packet was retransmitted, so no RTT sample should have been taken");
        }

        [Fact]
        public void OnAck_SamplesRttFromNonRetransmittedPacket_UpdatesRto()
        {
            var sut = new UTPRetransmissionTracker();
            var now = DateTime.UtcNow;
            sut.OnSent(1, UTPSocket.PacketType.STData, new byte[10], now);

            sut.OnAck(1, now.AddMilliseconds(200));

            sut.RtoMs.Should().NotBe(1000, "an RTT sample should have adjusted the RTO away from its initial default");
        }

        [Fact]
        public void BytesInFlight_SumsUnackedPayloadSizes()
        {
            var sut = new UTPRetransmissionTracker();
            var now = DateTime.UtcNow;
            sut.OnSent(1, UTPSocket.PacketType.STData, new byte[100], now);
            sut.OnSent(2, UTPSocket.PacketType.STData, new byte[200], now);

            sut.BytesInFlight.Should().Be(300);
        }
    }
}
