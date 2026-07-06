using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
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

        [Fact]
        public void Send_WithoutConnect_ThrowsNotConnected()
        {
            using var udp = new UDPSocket();

            // Regression: Connect() never calls the underlying Socket.Connect (see Send's
            // comment), so calling Send before Connect used to fall through to a bare
            // SocketException("Destination address required") from the unconnected raw
            // socket instead of a clear NotConnected failure.
            Action act = () => udp.Send(new byte[] { 1 });

            act.Should().Throw<SocketException>()
                .Which.SocketErrorCode.Should().Be(SocketError.NotConnected);
        }

        [Fact]
        public void Send_AfterConnect_DeliversDatagramToConnectedEndpoint()
        {
            // Regression: LocalPeerDiscovery.Announce relies on exactly this Connect-then-Send
            // sequence; before the fix, Send always threw "Destination address required"
            // because Connect() only remembered the endpoint without truly connecting the
            // socket, and the base Send() called the raw (unconnected) Socket.Send directly.
            using var receiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            receiver.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            var receiverEndpoint = (IPEndPoint)receiver.LocalEndPoint;
            receiver.ReceiveTimeout = 5000;

            using var udp = new UDPSocket();
            udp.Connect(receiverEndpoint);

            var payload = Encoding.ASCII.GetBytes("hello");
            udp.Send(payload);

            var buffer = new byte[64];
            var received = receiver.Receive(buffer);

            Encoding.ASCII.GetString(buffer, 0, received).Should().Be("hello");
        }
    }
}
