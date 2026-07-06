using FluentAssertions;
using Xunit;
using bzTorrent;
using bzTorrent.IO;

namespace bzTorrent.Tests
{
    public class LocalPeerDiscoveryTests
    {
        private const string ValidHash = "C1463792A1FF36A237E3A0F68BADEB0D3764E9BB"; // 40 chars

        private static string BuildPacket(string port, string hash) =>
            $"BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: {port}\r\nInfohash: {hash}\r\n\r\n\r\n";

        [Fact]
        public void TryParseSearchPacket_ValidPacket_ExtractsPortAndHash()
        {
            var ok = LocalPeerDiscovery<UDPSocket>.TryParseSearchPacket(BuildPacket("6881", ValidHash), out var port, out var hash);

            ok.Should().BeTrue();
            port.Should().Be(6881);
            hash.Should().Be(ValidHash);
        }

        [Fact]
        public void TryParseSearchPacket_NonSearchPacket_ReturnsFalse()
        {
            LocalPeerDiscovery<UDPSocket>.TryParseSearchPacket("GET / HTTP/1.1\r\n", out _, out _).Should().BeFalse();
        }

        [Fact]
        public void TryParseSearchPacket_ShortInfohash_DoesNotThrowAndReturnsFalse()
        {
            // Regression: the old inline Substring(10, 40) threw on a short Infohash line.
            var ok = LocalPeerDiscovery<UDPSocket>.TryParseSearchPacket(BuildPacket("6881", "TOOSHORT"), out _, out var hash);

            ok.Should().BeFalse();
            hash.Should().BeNull();
        }

        [Fact]
        public void TryParseSearchPacket_MissingPort_ReturnsFalse()
        {
            var packet = $"BT-SEARCH * HTTP/1.1\r\nInfohash: {ValidHash}\r\n\r\n\r\n";

            LocalPeerDiscovery<UDPSocket>.TryParseSearchPacket(packet, out _, out _).Should().BeFalse();
        }

        [Fact]
        public void TryParseSearchPacket_Null_ReturnsFalse()
        {
            LocalPeerDiscovery<UDPSocket>.TryParseSearchPacket(null, out _, out _).Should().BeFalse();
        }

        [Fact]
        public void OpenThenClose_WhileReceivePending_DoesNotCrashProcess()
        {
            // Regression: Close() closes the reader socket while BeginReceiveFrom's callback
            // may still be pending. That callback runs on a thread-pool thread outside
            // Close()'s own try/catch, so EndReceiveFrom throwing there used to be an
            // unhandled exception that terminated the whole process (not just this test),
            // not merely a failed assertion. Repeated a few times to make the race likely to
            // land inside the receive-pending window.
            for (var i = 0; i < 20; i++)
            {
                using var lpd = new LocalPeerDiscovery<UDPSocket>();
                lpd.Open();
                lpd.Close();
            }
        }
    }
}
