using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using bzTorrent.Helpers;
using bzTorrent.IO;
using FluentAssertions;
using Xunit;

namespace bzTorrent.Tests.IO
{
    // Exercises UTPSocket.Receive() (used by the MSE/PE handshake, which needs a real blocking
    // byte-stream read) end-to-end over real loopback UDP sockets. UTPSocket has no passive-open
    // support (see its class doc comment), so a second real UTPSocket can't act as the peer here —
    // instead a raw UdpClient plays a scripted, well-behaved uTP peer: it replies to the SYN with
    // an ST_STATE (completing the transport handshake UTPSocket does implement), then sends
    // ST_DATA packets built by hand.
    public class UTPSocketReceiveTests : IDisposable
    {
        private readonly UdpClient fakePeer = new(new IPEndPoint(IPAddress.Loopback, 0));
        private UTPSocket utp;

        public void Dispose()
        {
            utp?.Dispose();
            fakePeer.Dispose();
        }

        private static byte[] BuildPacket(UTPSocket.PacketType type, ushort connectionId, ushort seq, ushort ack, byte[] payload = null)
        {
            payload ??= Array.Empty<byte>();
            var typeAndVersion = new byte[] { (byte)(((byte)type << 4) | 1) };
            var extension = new byte[] { 0 };

            return typeAndVersion
                .Cat(extension)
                .Cat(PackHelper.UInt16(connectionId))
                .Cat(PackHelper.UInt32(0))   // timestamp
                .Cat(PackHelper.UInt32(0))   // timestamp diff
                .Cat(PackHelper.UInt32(1024 * 1024)) // wnd size
                .Cat(PackHelper.UInt16(seq))
                .Cat(PackHelper.UInt16(ack))
                .Cat(payload);
        }

        // Handshakes utp with the fake peer (SYN -> ST_STATE) and returns the connection id and
        // seq number the fake peer must use for its own ST_DATA packets.
        private (ushort ConnectionId, ushort Seq) ConnectAndHandshake()
        {
            utp = new UTPSocket { ReceiveTimeout = 5000 };
            var fakePeerEndPoint = (IPEndPoint)fakePeer.Client.LocalEndPoint;

            utp.Connect(fakePeerEndPoint);

            var synResult = fakePeer.BeginReceive(null, null);
            synResult.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5)).Should().BeTrue("the client should have sent a SYN");
            IPEndPoint clientEndPoint = null;
            var syn = fakePeer.EndReceive(synResult, ref clientEndPoint);
            var connectionId = UnpackHelper.UInt16(syn, 2, UnpackHelper.Endianness.Big);

            const ushort fakePeerSeq = 100;
            var statePacket = BuildPacket(UTPSocket.PacketType.STState, connectionId, fakePeerSeq, ack: 1);
            fakePeer.Send(statePacket, statePacket.Length, clientEndPoint);

            // Give the client's internal (already-armed) receive a moment to process the
            // ST_STATE and flip isFullyConnected before we start sending it ST_DATA.
            Thread.Sleep(200);

            return (connectionId, fakePeerSeq);
        }

        [Fact]
        public void Receive_DeliversInOrderDataPacket()
        {
            var (connectionId, seq) = ConnectAndHandshake();
            var clientEndPoint = utp.LocalEndPoint;

            var payload = Encoding.ASCII.GetBytes("hello-utp");
            var dataPacket = BuildPacket(UTPSocket.PacketType.STData, (ushort)(connectionId + 1), seq, ack: 1, payload);
            fakePeer.Send(dataPacket, dataPacket.Length, clientEndPoint);

            var buffer = new byte[64];
            var received = utp.Receive(buffer, 0, buffer.Length);

            received.Should().Be(payload.Length);
            buffer.GetBytes(0, received).Should().BeEquivalentTo(payload);
        }

        [Fact]
        public void Receive_WaitsForGapThenDeliversBufferedOutOfOrderPacket()
        {
            var (connectionId, seq) = ConnectAndHandshake();
            var clientEndPoint = utp.LocalEndPoint;

            // Send seq+1 first (the gap at `seq` is still outstanding) — Receive() must not
            // return this yet, it should keep pumping until the missing packet arrives.
            var second = Encoding.ASCII.GetBytes("second");
            var secondPacket = BuildPacket(UTPSocket.PacketType.STData, (ushort)(connectionId + 1), (ushort)(seq + 1), ack: 1, second);
            fakePeer.Send(secondPacket, secondPacket.Length, clientEndPoint);

            Thread.Sleep(100);

            var first = Encoding.ASCII.GetBytes("first!");
            var firstPacket = BuildPacket(UTPSocket.PacketType.STData, (ushort)(connectionId + 1), seq, ack: 1, first);
            fakePeer.Send(firstPacket, firstPacket.Length, clientEndPoint);

            var buffer = new byte[64];
            var receivedFirst = utp.Receive(buffer, 0, buffer.Length);
            receivedFirst.Should().Be(first.Length);
            buffer.GetBytes(0, receivedFirst).Should().BeEquivalentTo(first);

            var receivedSecond = utp.Receive(buffer, 0, buffer.Length);
            receivedSecond.Should().Be(second.Length);
            buffer.GetBytes(0, receivedSecond).Should().BeEquivalentTo(second);
        }
    }
}
