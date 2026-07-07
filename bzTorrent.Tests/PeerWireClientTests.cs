using bzTorrent.Data;
using bzTorrent.IO;
using FluentAssertions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace bzTorrent.Tests
{
    public class PeerWireClientTests
    {
        [Fact]
        public void ConnectAndDisconnectBehave()
        {
            var endPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5001);

            var mockPeerConnection = new Mock<IPeerConnection>();

            mockPeerConnection.Setup(f => f.Connect(It.Is<IPEndPoint>(ip => ip == endPoint))).Verifiable();
            mockPeerConnection.Setup(f => f.Disconnect()).Verifiable();

            var peerWireClient = new PeerWireClient(mockPeerConnection.Object);

            peerWireClient.Connect(endPoint);
            peerWireClient.Disconnect();

            mockPeerConnection.Verify();
        }

        [Fact]
        public void HandshakeBehaves()
        {
            var endPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5001);
            var hashId = "C1463792A1FF36A237E3A0F68BADEB0D3764E9BB";
            var peerId = "B1463792A1FF36A237E3";

            var mockPeerConnection = new Mock<IPeerConnection>();

            mockPeerConnection.Setup(f => f.Connect(It.Is<IPEndPoint>(ip => ip == endPoint)));
            mockPeerConnection.Setup(f => f.Disconnect());

            mockPeerConnection.Setup(pc => pc.Handshake(It.IsAny<PeerClientHandshake>())).Callback((PeerClientHandshake hs) =>
            {
                hs.PeerId.Should().BeEquivalentTo(peerId);
                hs.InfoHash.Should().BeEquivalentTo(hashId);
            }).Verifiable();

            var peerWireClient = new PeerWireClient(mockPeerConnection.Object);

            peerWireClient.Connect(endPoint);
            peerWireClient.Handshake(hashId, peerId).Should().BeTrue();
            peerWireClient.Disconnect();

            mockPeerConnection.Verify();
        }

        [Fact]
        public void NullHashIdThrowsExceptionOnHandshake()
        {
            var endPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5001);
            var peerId = "B1463792A1FF36A237E3";

            var mockPeerConnection = new Mock<IPeerConnection>();

            mockPeerConnection.Setup(f => f.Connect(It.Is<IPEndPoint>(ip => ip == endPoint)));
            mockPeerConnection.Setup(f => f.Disconnect());

            var peerWireClient = new PeerWireClient(mockPeerConnection.Object);

            peerWireClient.Connect(endPoint);
            Assert.Throws<ArgumentNullException>(() => peerWireClient.Handshake(null, peerId));
            peerWireClient.Disconnect();

            mockPeerConnection.Verify();
        }

        [Fact]
        public void ShortHashIdThrowsExceptionOnHandshake()
        {
            var endPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5001);
            var hashId = "C1463792A1FF36A237E3A0D3764E9BB";
            var peerId = "B1463792A1FF36A237E3";

            var mockPeerConnection = new Mock<IPeerConnection>();

            mockPeerConnection.Setup(f => f.Connect(It.Is<IPEndPoint>(ip => ip == endPoint)));
            mockPeerConnection.Setup(f => f.Disconnect());

            var peerWireClient = new PeerWireClient(mockPeerConnection.Object);

            peerWireClient.Connect(endPoint);

            Assert.Throws<ArgumentOutOfRangeException>(() => peerWireClient.Handshake(hashId, peerId));

            peerWireClient.Disconnect();
        }

        [Fact]
        public void NullPeerIdThrowsExceptionOnHandshake()
        {
            var endPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5001);
            var hashId = "C1463792A1FF36A237E3A0F68BADEB0D3764E9BB";

            var mockPeerConnection = new Mock<IPeerConnection>();

            mockPeerConnection.Setup(f => f.Connect(It.Is<IPEndPoint>(ip => ip == endPoint)));
            mockPeerConnection.Setup(f => f.Disconnect());

            var peerWireClient = new PeerWireClient(mockPeerConnection.Object);

            peerWireClient.Connect(endPoint);
            Assert.Throws<ArgumentNullException>(() => peerWireClient.Handshake(hashId, null));
            peerWireClient.Disconnect();

            mockPeerConnection.Verify();
        }

        [Fact]
        public void ShortPeerIdThrowsExceptionOnHandshake()
        {
            var endPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5001);
            var hashId = "C1463792A1FF36A237E3A0F68BADEB0D3764E9BB";
            var peerId = "B1463792A1FF36";

            var mockPeerConnection = new Mock<IPeerConnection>();

            mockPeerConnection.Setup(f => f.Connect(It.Is<IPEndPoint>(ip => ip == endPoint)));
            mockPeerConnection.Setup(f => f.Disconnect());

            var peerWireClient = new PeerWireClient(mockPeerConnection.Object);

            peerWireClient.Connect(endPoint);

            Assert.Throws<ArgumentOutOfRangeException>(() => peerWireClient.Handshake(hashId, peerId));

            peerWireClient.Disconnect();
        }


        [Fact]
        public void ProcessWithDisconnectedSocketShouldDropConnection()
        {
            var endPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5001);
            var hashId = "C1463792A1FF36A237E3A0F68BADEB0D3764E9BB";
            var peerId = "B1463792A1FF36A237E3";

            var mockPeerConnection = new Mock<IPeerConnection>();

            mockPeerConnection.Setup(f => f.Connect(It.Is<IPEndPoint>(ip => ip == endPoint)));
            mockPeerConnection.Setup(f => f.Disconnect());
            mockPeerConnection.Setup(f => f.Connected).Returns(false);
            mockPeerConnection.Setup(pc => pc.Process()).Returns(true).Verifiable();

            var peerWireClient = new PeerWireClient(mockPeerConnection.Object);

            peerWireClient.DroppedConnection += (pwc) =>
            {
                pwc.Should().Be(peerWireClient);
            };

            peerWireClient.Connect(endPoint);
            peerWireClient.Handshake(hashId, peerId);

            peerWireClient.Process().Should().BeFalse();

            peerWireClient.Disconnect();

            mockPeerConnection.Verify();
        }

        [Fact]
        public void ProcessWithMismatchedRemoteInfoHashDisconnectsAndDoesNotCompleteHandshake()
        {
            var endPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5001);
            var hashId = "C1463792A1FF36A237E3A0F68BADEB0D3764E9BB";
            var peerId = "B1463792A1FF36A237E3";

            var mockPeerConnection = new Mock<IPeerConnection>();
            mockPeerConnection.Setup(f => f.Connect(It.IsAny<IPEndPoint>()));
            mockPeerConnection.Setup(f => f.Disconnect()).Verifiable();
            mockPeerConnection.Setup(f => f.Connected).Returns(true);
            mockPeerConnection.Setup(f => f.Process());
            mockPeerConnection.Setup(f => f.HasPackets()).Returns(false);
            mockPeerConnection.Setup(f => f.RemoteHandshake).Returns(new PeerClientHandshake
            {
                InfoHash = "0000000000000000000000000000000000000000",
                PeerId = "OTHERPEERID00000000",
            });

            var peerWireClient = new PeerWireClient(mockPeerConnection.Object);
            var handshakeCompleted = false;
            peerWireClient.HandshakeComplete += _ => handshakeCompleted = true;

            peerWireClient.Connect(endPoint);
            peerWireClient.Handshake(hashId, peerId);

            peerWireClient.Process().Should().BeFalse("a peer answering with a different info-hash is not part of this torrent's swarm");

            peerWireClient.ReceivedHandshake.Should().BeFalse();
            handshakeCompleted.Should().BeFalse();
            mockPeerConnection.Verify(f => f.Disconnect(), Times.AtLeastOnce);
        }

        [Fact]
        public void ProcessWithMatchingRemoteInfoHashCompletesHandshake()
        {
            var endPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5001);
            var hashId = "C1463792A1FF36A237E3A0F68BADEB0D3764E9BB";
            var peerId = "B1463792A1FF36A237E3";

            var mockPeerConnection = new Mock<IPeerConnection>();
            mockPeerConnection.Setup(f => f.Connect(It.IsAny<IPEndPoint>()));
            mockPeerConnection.Setup(f => f.Disconnect());
            mockPeerConnection.Setup(f => f.Connected).Returns(true);
            mockPeerConnection.Setup(f => f.Process());
            mockPeerConnection.Setup(f => f.HasPackets()).Returns(false);
            mockPeerConnection.Setup(f => f.RemoteHandshake).Returns(new PeerClientHandshake
            {
                InfoHash = hashId,
                PeerId = "OTHERPEERID00000000",
            });

            var peerWireClient = new PeerWireClient(mockPeerConnection.Object);
            var handshakeCompleted = false;
            peerWireClient.HandshakeComplete += _ => handshakeCompleted = true;

            peerWireClient.Connect(endPoint);
            peerWireClient.Handshake(hashId, peerId);

            peerWireClient.Process().Should().BeTrue();

            peerWireClient.ReceivedHandshake.Should().BeTrue();
            handshakeCompleted.Should().BeTrue();
        }

        [Theory]
        [InlineData(1, 1)]   // not a multiple of 8: used to throw IndexOutOfRangeException
        [InlineData(7, 1)]
        [InlineData(8, 1)]
        [InlineData(9, 2)]
        [InlineData(16, 2)]
        public void SendBitField_PadsToByteBoundary_InsteadOfThrowing(int pieceCount, int expectedByteCount)
        {
            var fake = new Helpers.FakePeerConnection();
            var client = new PeerWireClient(fake);
            var bitField = new bool[pieceCount];
            bitField[0] = true;

            client.SendBitField(bitField);

            var sent = fake.Outbound.Single();
            sent.Command.Should().Be(PeerClientCommands.Bitfield);
            sent.Payload.Length.Should().Be(expectedByteCount);
            (sent.Payload[0] & 0x80).Should().NotBe(0, "piece 0 is the MSB of the first byte");
        }
    }
}
