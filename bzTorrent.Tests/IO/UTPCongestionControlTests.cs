namespace bzTorrent.Tests.IO
{
    using bzTorrent.IO;
    using FluentAssertions;
    using Xunit;

    public class UTPCongestionControlTests
    {
        [Fact]
        public void MaxWindow_InitialValue_EqualsSocketSendBufferSize()
        {
            var sut = new UTPCongestionControl(16 * 1024);

            sut.MaxWindow.Should().Be(16 * 1024);
        }

        [Fact]
        public void OnAck_WithZeroDelayAndFullWindowAck_GrowsWindowByMaxIncreasePerRtt()
        {
            // max_window is capped at socketSendBufferSize and starts there, so growth can only
            // be observed once congestion has shrunk it below the cap.
            var sut = new UTPCongestionControl(10_000_000);
            sut.OnDelaySample(UTPCongestionControl.CControlTarget * 1000);
            sut.OnAck(sut.MaxWindow, 1000);
            var shrunkWindow = sut.MaxWindow;
            shrunkWindow.Should().BeLessThan(10_000_000);

            sut.OnDelaySample(0);
            sut.OnAck(shrunkWindow, 2000);

            sut.MaxWindow.Should().Be(shrunkWindow + UTPCongestionControl.MaxCwndIncreaseBytesPerRtt);
        }

        [Fact]
        public void OnAck_WithZeroBytesAcked_DoesNotChangeWindow()
        {
            var sut = new UTPCongestionControl(1024 * 1024);
            sut.OnDelaySample(0);
            var before = sut.MaxWindow;

            sut.OnAck(0, 1000);

            sut.MaxWindow.Should().Be(before);
        }

        [Fact]
        public void OnAck_WithDelayFarAboveTarget_ShrinksWindow()
        {
            var sut = new UTPCongestionControl(1024 * 1024);
            sut.OnDelaySample(UTPCongestionControl.CControlTarget * 10);
            var before = sut.MaxWindow;

            sut.OnAck(sut.MaxWindow, 1000);

            sut.MaxWindow.Should().BeLessThan(before);
        }

        [Fact]
        public void OnAck_RepeatedlyWithHighDelay_NeverDropsBelowMinWindowSize()
        {
            var sut = new UTPCongestionControl(1024 * 1024);
            sut.OnDelaySample(UTPCongestionControl.CControlTarget * 1000);

            for (var i = 0; i < 10_000; i++)
            {
                sut.OnAck(sut.MaxWindow, (uint)(1000 + i));
            }

            sut.MaxWindow.Should().Be(UTPCongestionControl.MinWindowSize);
        }

        [Fact]
        public void OnAck_RepeatedlyWithZeroDelay_NeverExceedsSocketSendBufferSize()
        {
            const uint bufferSize = 20 * 1024;
            var sut = new UTPCongestionControl(bufferSize);
            sut.OnDelaySample(0);

            for (var i = 0; i < 10_000; i++)
            {
                sut.OnAck(sut.MaxWindow, (uint)(1000 + i));
            }

            sut.MaxWindow.Should().Be(bufferSize);
        }

        [Fact]
        public void OnAck_AfterDecayIntervalElapses_HalvesWindow()
        {
            var sut = new UTPCongestionControl(1024 * 1024);
            sut.OnDelaySample(0);

            // First call only primes the decay timer, it must not decay immediately.
            sut.OnAck(0, 0);
            var beforeDecay = sut.MaxWindow;

            sut.OnAck(0, UTPCongestionControl.MaxWindowDecay);

            sut.MaxWindow.Should().Be(beforeDecay / 2);
        }

        [Fact]
        public void OnAck_BeforeDecayIntervalElapses_DoesNotDecay()
        {
            var sut = new UTPCongestionControl(1024 * 1024);
            sut.OnDelaySample(0);

            sut.OnAck(0, 0);
            var beforeDecay = sut.MaxWindow;

            sut.OnAck(0, UTPCongestionControl.MaxWindowDecay - 1);

            sut.MaxWindow.Should().Be(beforeDecay);
        }

        [Fact]
        public void OnDelaySample_UsesMinimumOfRecentSamplesAsOurDelay()
        {
            var sut = new UTPCongestionControl(10_000_000);
            sut.OnDelaySample(UTPCongestionControl.CControlTarget * 1000);
            sut.OnAck(sut.MaxWindow, 1000);
            var shrunkWindow = sut.MaxWindow;

            // A low sample mixed in with high samples should keep our_delay pinned to the low
            // (base-delay) sample, so the window should grow rather than shrink further.
            sut.OnDelaySample(0);
            sut.OnDelaySample(UTPCongestionControl.CControlTarget * 100);
            sut.OnDelaySample(UTPCongestionControl.CControlTarget * 100);

            sut.OnAck(shrunkWindow, 2000);

            sut.MaxWindow.Should().BeGreaterThan(shrunkWindow);
        }
    }
}
