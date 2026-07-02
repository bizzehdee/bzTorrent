using System.Linq;
using System.Net;
using System.Text;
using bzBencode;
using bzTorrent.Data;
using bzTorrent.Helpers;
using bzTorrent.ProtocolExtensions;
using bzTorrent.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace bzTorrent.Tests.ProtocolExtensions
{
    public class UTHolepunchExtensionTests
    {
        private static (FakePeerConnection Fake, PeerWireClient Client, ExtendedProtocolExtensions ExtProtocol, UTHolepunchExtension Holepunch) SetUp(byte remoteMessageId = 5)
        {
            var fake = new FakePeerConnection();
            var client = new PeerWireClient(fake) { Hash = new string('a', 40), LocalPeerID = new string('p', 20) };

            var extProtocol = new ExtendedProtocolExtensions();
            var holepunch = new UTHolepunchExtension();
            extProtocol.RegisterProtocolExtension(client, holepunch);
            client.RegisterBTExtension(extProtocol);

            // Our own handshake triggers ExtendedProtocolExtensions.OnHandshake, which assigns
            // and records the local (outgoing) message ID for "ut_holepunch" — needed to decode
            // any inbound extended message, since the sender addresses it by the ID *we* assigned.
            client.Handshake();

            // Simulate the remote peer's extended handshake declaring the message ID it wants us
            // to use for ut_holepunch, so GetIncomingMessageID resolves to something real.
            fake.RemoteHandshake = new PeerClientHandshake { PeerId = new string('r', 20), InfoHash = client.Hash };
            client.Process();

            var mDict = new BDict { ["ut_holepunch"] = new BInt(remoteMessageId) };
            var handshakeDict = new BDict { ["m"] = mDict };
            var handshakePayload = Encoding.ASCII.GetBytes(BencodingUtils.EncodeString(handshakeDict));
            var extendedHandshakePacket = new PeerWirePacket
            {
                Command = PeerClientCommands.ExtendedProtocol,
                Payload = new byte[] { 0 }.Cat(handshakePayload),
                CommandLength = (uint)(2 + handshakePayload.Length)
            };
            fake.EnqueuePacket(extendedHandshakePacket);
            client.Process();

            return (fake, client, extProtocol, holepunch);
        }

        private static PeerWirePacket BuildExtendedPacket(byte messageId, byte[] holepunchPayload)
        {
            var payload = new byte[] { messageId }.Cat(holepunchPayload);
            return new PeerWirePacket
            {
                Command = PeerClientCommands.ExtendedProtocol,
                Payload = payload,
                CommandLength = (uint)(payload.Length + 1)
            };
        }

        private static byte[] BuildHolepunchPayload(UTHolepunchMessageType type, IPEndPoint endpoint, UTHolepunchErrorCode? errorCode = null)
        {
            var payload = new byte[] { (byte)type, 0x04 }
                .Cat(endpoint.Address.GetAddressBytes())
                .Cat(PackHelper.UInt16((ushort)endpoint.Port));

            if (errorCode.HasValue)
            {
                payload = payload.Cat(PackHelper.UInt32((uint)errorCode.Value));
            }

            return payload;
        }

        [Fact]
        public void SendRendezvous_SendsCorrectlyFormedExtendedMessage()
        {
            var (fake, client, _, holepunch) = SetUp(remoteMessageId: 5);
            var target = new IPEndPoint(IPAddress.Parse("203.0.113.7"), 6881);

            var baselineCount = fake.Outbound.Count;
            holepunch.SendRendezvous(client, target);

            var sent = fake.Outbound.Skip(baselineCount).Should().ContainSingle(p => p.Command == PeerClientCommands.ExtendedProtocol).Subject;
            sent.Command.Should().Be(PeerClientCommands.ExtendedProtocol);
            sent.Payload[0].Should().Be(5, "the extended message ID the remote peer assigned to ut_holepunch");
            sent.Payload[1].Should().Be((byte)UTHolepunchMessageType.Rendezvous);
            sent.Payload[2].Should().Be(0x04, "IPv4 address type");
            sent.Payload.GetBytes(3, 4).Should().BeEquivalentTo(target.Address.GetAddressBytes());
            UnpackHelper.UInt16(sent.Payload, 7, UnpackHelper.Endianness.Big).Should().Be((ushort)target.Port);
        }

        [Fact]
        public void ReceivingRendezvousMessage_RaisesRendezvousEvent()
        {
            var (fake, client, _, holepunch) = SetUp();
            var target = new IPEndPoint(IPAddress.Parse("198.51.100.9"), 51413);

            IPEndPoint received = null;
            holepunch.Rendezvous += (c, ext, endpoint) => received = endpoint;

            var payload = BuildHolepunchPayload(UTHolepunchMessageType.Rendezvous, target);
            fake.EnqueuePacket(BuildExtendedPacket(messageId: 1, payload));
            client.Process();

            received.Should().NotBeNull();
            received.Address.Should().Be(target.Address);
            received.Port.Should().Be(target.Port);
        }

        [Fact]
        public void ReceivingConnectMessage_RaisesConnectEvent()
        {
            var (fake, client, _, holepunch) = SetUp();
            var target = new IPEndPoint(IPAddress.Parse("192.0.2.55"), 6969);

            IPEndPoint received = null;
            holepunch.Connect += (c, ext, endpoint) => received = endpoint;

            var payload = BuildHolepunchPayload(UTHolepunchMessageType.Connect, target);
            fake.EnqueuePacket(BuildExtendedPacket(messageId: 1, payload));
            client.Process();

            received.Should().NotBeNull();
            received.Port.Should().Be(target.Port);
        }

        [Fact]
        public void ReceivingErrorMessage_RaisesErrorEventWithCode()
        {
            var (fake, client, _, holepunch) = SetUp();
            var target = new IPEndPoint(IPAddress.Parse("192.0.2.55"), 6969);

            UTHolepunchErrorCode? receivedCode = null;
            holepunch.Error += (c, ext, endpoint, code) => receivedCode = code;

            var payload = BuildHolepunchPayload(UTHolepunchMessageType.Error, target, UTHolepunchErrorCode.NotConnected);
            fake.EnqueuePacket(BuildExtendedPacket(messageId: 1, payload));
            client.Process();

            receivedCode.Should().Be(UTHolepunchErrorCode.NotConnected);
        }

        [Fact]
        public void SendConnect_ThenReceiveRoundTrip_ProducesSameEndpoint()
        {
            // Sanity check that our own Send*/decode logic agree with each other (not just with a
            // hand-built payload), by feeding our own sent bytes back in as if from the peer.
            var (fake, client, extProtocol, holepunch) = SetUp(remoteMessageId: 5);
            var target = new IPEndPoint(IPAddress.Parse("10.20.30.40"), 12345);

            var baselineCount = fake.Outbound.Count;
            holepunch.SendConnect(client, target);
            var sentPacket = fake.Outbound.Skip(baselineCount).Single(p => p.Command == PeerClientCommands.ExtendedProtocol);

            IPEndPoint received = null;
            holepunch.Connect += (c, ext, endpoint) => received = endpoint;

            // The message ID we sent with is the one WE assigned as the local outgoing ID during
            // our own OnHandshake; feed it back through OnExtendedMessage directly (bypassing the
            // ID-to-protocol lookup, which only exists for inbound messages we ourselves sent) to
            // confirm decode matches encode.
            holepunch.OnExtendedMessage(client, sentPacket.Payload.GetBytes(1, sentPacket.Payload.Length - 1));

            received.Address.Should().Be(target.Address);
            received.Port.Should().Be(target.Port);
        }
    }
}
