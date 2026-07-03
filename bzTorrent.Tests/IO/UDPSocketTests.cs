using System;
using FluentAssertions;
using Xunit;
using bzTorrent.IO;

namespace bzTorrent.Tests.IO
{
    public class UDPSocketTests
    {
        [Fact]
        public void Accept_ThrowsNotSupported()
        {
            using var udp = new UDPSocket();

            // UDP has no passive-open; it must fail loud rather than calling _socket.Accept()
            // (meaningless on a datagram socket) and swallowing the resulting error as null.
            Action act = () => udp.Accept();
            act.Should().Throw<NotSupportedException>();
        }

        [Fact]
        public void EndAccept_ThrowsNotSupported()
        {
            using var udp = new UDPSocket();

            Action act = () => udp.EndAccept(null);
            act.Should().Throw<NotSupportedException>();
        }
    }
}
