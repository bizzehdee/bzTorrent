using System;
using bzTorrent.Data;
using bzTorrent.Helpers;
using bzTorrent.Tests.Helpers;
using Xunit;
using FluentAssertions;

namespace bzTorrent.Tests.Integration
{
    public class PeerWireClientIntegrationTests
    {
        [Fact]
        public void Handshake_SendsExpectedHandshakeAndSetsState()
        {
            var fake = new FakePeerConnection();
            var client = new bzTorrent.PeerWireClient(fake);

            client.Hash = new string('a', 40);
            client.LocalPeerID = new string('p', 20);

            bool handshakeRaised = false;
            client.HandshakeComplete += (c) => handshakeRaised = true;

            // Act: client sends handshake to remote (recorded by fake)
            var handshakeResult = client.Handshake();

            // Assert handshake was sent to the connection
            fake.LastHandshakeSent.Should().NotBeNull();
            fake.LastHandshakeSent.InfoHash.Should().Be(client.Hash);
            fake.LastHandshakeSent.PeerId.Should().Be(client.LocalPeerID);
            handshakeResult.Should().BeTrue();

            // Now simulate a remote handshake arriving and run Process to pick it up
            fake.RemoteHandshake = new PeerClientHandshake { PeerId = new string('r', 20), InfoHash = client.Hash };

            var processed = client.Process();
            processed.Should().BeTrue();
            handshakeRaised.Should().BeTrue();
            client.ReceivedHandshake.Should().BeTrue();
            client.RemotePeerID.Should().Be(fake.RemoteHandshake.PeerId);
        }

        [Fact]
        public void MessageDispatch_RaisesHaveAndBitfieldEvents()
        {
            var fake = new FakePeerConnection();
            var client = new bzTorrent.PeerWireClient(fake);

            client.Hash = new string('a', 40);
            client.LocalPeerID = new string('p', 20);

            // Establish handshake state so client won't treat remote messages as pre-handshake
            fake.RemoteHandshake = new PeerClientHandshake { PeerId = new string('r', 20), InfoHash = client.Hash };
            client.Process();

            int? seenHave = null;
            client.Have += (c, idx) => seenHave = idx;

            int seenBitfieldSize = -1;
            bool[] seenBitfield = null;
            client.BitField += (c, size, bf) => { seenBitfieldSize = size; seenBitfield = bf; };

            // Build a Have packet for piece index 5
            var havePayload = PackHelper.UInt32(5);
            var havePacket = new PeerWirePacket
            {
                Command = PeerClientCommands.Have,
                Payload = havePayload,
                CommandLength = (uint)havePayload.Length
            };

            // Build a Bitfield packet with a single byte where LSB is set (piece 0 available)
            var bitfieldPayload = new byte[] { 0x01 };
            var bitfieldPacket = new PeerWirePacket
            {
                Command = PeerClientCommands.Bitfield,
                Payload = bitfieldPayload,
                CommandLength = (uint)bitfieldPayload.Length
            };

            fake.EnqueuePacket(havePacket);
            fake.EnqueuePacket(bitfieldPacket);

            // Act
            client.Process();

            // Assert Have fired
            seenHave.Should().NotBeNull();
            seenHave.Value.Should().Be(5);

            // Assert Bitfield fired with expected size and first bit set
            seenBitfieldSize.Should().Be(8);
            seenBitfield.Should().NotBeNull();
            seenBitfield.Length.Should().Be(8);
            seenBitfield[0].Should().BeTrue();
        }

        [Fact]
        public void MalformedPayload_ClosesConnection()
        {
            var fake = new FakePeerConnection();
            var client = new bzTorrent.PeerWireClient(fake);

            client.Hash = new string('a', 40);
            client.LocalPeerID = new string('p', 20);

            // Establish handshake state
            fake.RemoteHandshake = new PeerClientHandshake { PeerId = new string('r', 20), InfoHash = client.Hash };
            client.Process();

            // Enqueue a malformed Have packet (payload too short)
            var malformedPayload = new byte[] { 0x00, 0x01 }; // only 2 bytes
            var malformedPacket = new PeerWirePacket
            {
                Command = PeerClientCommands.Have,
                Payload = malformedPayload,
                CommandLength = 4 // claims to be 4 bytes but only 2 provided
            };

            fake.EnqueuePacket(malformedPacket);

            // Act
            var processed = client.Process();

            // Assert: dispatcher should have closed the connection via PeerWireClient.Disconnect
            processed.Should().BeFalse();
            fake.Connected.Should().BeFalse();
        }

        [Fact]
        public void Bitfield_MalformedPayload_ClosesConnection()
        {
            var fake = new FakePeerConnection();
            var client = new bzTorrent.PeerWireClient(fake);

            client.Hash = new string('a', 40);
            client.LocalPeerID = new string('p', 20);

            // Establish handshake state
            fake.RemoteHandshake = new PeerClientHandshake { PeerId = new string('r', 20), InfoHash = client.Hash };
            client.Process();

            // Enqueue a Bitfield packet that claims a larger CommandLength than actual payload
            var payload = new byte[] { 0x00, 0x00 }; // 2 bytes
            var packet = new PeerWirePacket
            {
                Command = PeerClientCommands.Bitfield,
                Payload = payload,
                CommandLength = 10 // larger than actual payload
            };

            fake.EnqueuePacket(packet);

            var processed = client.Process();

            processed.Should().BeFalse();
            fake.Connected.Should().BeFalse();
        }

        [Fact]
        public void Piece_TruncatedHeader_ClosesConnection()
        {
            var fake = new FakePeerConnection();
            var client = new bzTorrent.PeerWireClient(fake);

            client.Hash = new string('a', 40);
            client.LocalPeerID = new string('p', 20);

            // Establish handshake state
            fake.RemoteHandshake = new PeerClientHandshake { PeerId = new string('r', 20), InfoHash = client.Hash };
            client.Process();

            // Enqueue a Piece packet with payload shorter than 8 bytes
            var payload = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 }; // 6 bytes
            var packet = new PeerWirePacket
            {
                Command = PeerClientCommands.Piece,
                Payload = payload,
                CommandLength = (uint)payload.Length
            };

            fake.EnqueuePacket(packet);

            var processed = client.Process();

            processed.Should().BeFalse();
            fake.Connected.Should().BeFalse();
        }
    }
}
