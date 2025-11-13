using System;
using FluentAssertions;
using Xunit;
using bzTorrent.Data;
using bzTorrent.Protocol.Handlers;
using bzTorrent.IO;
using Moq;

namespace bzTorrent.Tests.Protocol.Handlers
{
    public class HaveHandlerTests
    {
        [Fact]
        public void Handle_ValidPayload_ReturnsHandled()
        {
            // Arrange
            var mockConn = new Mock<IPeerConnection>();
            var client = new PeerWireClient(mockConn.Object);
            var handler = new HaveHandler();
            var payload = new byte[] { 0x00, 0x00, 0x00, 0x42 }; // 66 in big-endian
            var packet = new PeerWirePacket { Command = PeerClientCommands.Have, Payload = payload, CommandLength = 4 };
            
            bool haveInvoked = false;
            int capturedIndex = 0;
            client.Have += (c, idx) =>
            {
                haveInvoked = true;
                capturedIndex = idx;
            };

            // Act
            var result = handler.Handle(client, packet);

            // Assert
            result.Should().Be(HandlerResult.Handled);
            haveInvoked.Should().BeTrue();
            capturedIndex.Should().Be(66);
        }

        [Fact]
        public void Handle_TooShortPayload_ReturnsCloseConnection()
        {
            // Arrange
            var mockConn = new Mock<IPeerConnection>();
            var client = new PeerWireClient(mockConn.Object);
            var handler = new HaveHandler();
            var payload = new byte[] { 0x00, 0x00 }; // Only 2 bytes
            var packet = new PeerWirePacket { Command = PeerClientCommands.Have, Payload = payload, CommandLength = 4 };

            bool haveInvoked = false;
            client.Have += (c, idx) => haveInvoked = true;

            // Act
            var result = handler.Handle(client, packet);

            // Assert
            result.Should().Be(HandlerResult.CloseConnection);
            haveInvoked.Should().BeFalse();
        }

        [Fact]
        public void Handle_EmptyPayload_ReturnsCloseConnection()
        {
            // Arrange
            var mockConn = new Mock<IPeerConnection>();
            var client = new PeerWireClient(mockConn.Object);
            var handler = new HaveHandler();
            var payload = Array.Empty<byte>();
            var packet = new PeerWirePacket { Command = PeerClientCommands.Have, Payload = payload, CommandLength = 4 };

            // Act
            var result = handler.Handle(client, packet);

            // Assert
            result.Should().Be(HandlerResult.CloseConnection);
        }
    }

    public class BitfieldHandlerTests
    {
        [Fact]
        public void Handle_ValidPayload_ReturnsHandledAndInvokesBitField()
        {
            // Arrange
            var mockConn = new Mock<IPeerConnection>();
            var client = new PeerWireClient(mockConn.Object);
            var handler = new BitfieldHandler();
            var payload = new byte[] { 0b1111_1111, 0b0000_0001 }; // 16 bits
            var packet = new PeerWirePacket 
            { 
                Command = PeerClientCommands.Bitfield, 
                Payload = payload, 
                CommandLength = 2 
            };

            bool bitfieldInvoked = false;
            client.BitField += (c, size, bits) =>
            {
                bitfieldInvoked = true;
            };

            // Act
            var result = handler.Handle(client, packet);

            // Assert
            result.Should().Be(HandlerResult.Handled);
            bitfieldInvoked.Should().BeTrue();
            client.PeerBitField.Should().NotBeNull();
        }

        [Fact]
        public void Handle_PayloadTooShort_ReturnsCloseConnection()
        {
            // Arrange
            var mockConn = new Mock<IPeerConnection>();
            var client = new PeerWireClient(mockConn.Object);
            var handler = new BitfieldHandler();
            var payload = new byte[] { 0xFF }; // Only 1 byte
            var packet = new PeerWirePacket 
            { 
                Command = PeerClientCommands.Bitfield, 
                Payload = payload, 
                CommandLength = 2 // Expecting 2 bytes
            };

            // Act
            var result = handler.Handle(client, packet);

            // Assert
            result.Should().Be(HandlerResult.CloseConnection);
        }
    }

    public class RequestHandlerTests
    {
        [Fact]
        public void Handle_RequestCommand_ReturnsHandledAndInvokesRequest()
        {
            // Arrange
            var mockConn = new Mock<IPeerConnection>();
            var client = new PeerWireClient(mockConn.Object);
            var handler = new RequestHandler(isCancel: false);
            var payload = new byte[]
            {
                0x00, 0x00, 0x00, 0x01, // index = 1
                0x00, 0x00, 0x40, 0x00, // begin = 16384
                0x00, 0x00, 0x40, 0x00  // length = 16384
            };
            var packet = new PeerWirePacket 
            { 
                Command = PeerClientCommands.Request, 
                Payload = payload, 
                CommandLength = 12 
            };

            bool requestInvoked = false;
            client.Request += (c, idx, begin, len) =>
            {
                requestInvoked = true;
            };

            // Act
            var result = handler.Handle(client, packet);

            // Assert
            result.Should().Be(HandlerResult.Handled);
            requestInvoked.Should().BeTrue();
        }

        [Fact]
        public void Handle_CancelCommand_ReturnsHandledAndInvokesCancel()
        {
            // Arrange
            var mockConn = new Mock<IPeerConnection>();
            var client = new PeerWireClient(mockConn.Object);
            var handler = new RequestHandler(isCancel: true);
            var payload = new byte[]
            {
                0x00, 0x00, 0x00, 0x02, // index = 2
                0x00, 0x00, 0x20, 0x00, // begin = 8192
                0x00, 0x00, 0x20, 0x00  // length = 8192
            };
            var packet = new PeerWirePacket 
            { 
                Command = PeerClientCommands.Cancel, 
                Payload = payload, 
                CommandLength = 12 
            };

            bool cancelInvoked = false;
            client.Cancel += (c, idx, begin, len) =>
            {
                cancelInvoked = true;
            };

            // Act
            var result = handler.Handle(client, packet);

            // Assert
            result.Should().Be(HandlerResult.Handled);
            cancelInvoked.Should().BeTrue();
        }

        [Fact]
        public void Handle_PayloadTooShort_ReturnsCloseConnection()
        {
            // Arrange
            var mockConn = new Mock<IPeerConnection>();
            var client = new PeerWireClient(mockConn.Object);
            var handler = new RequestHandler();
            var payload = new byte[] { 0x00, 0x00 }; // Only 2 bytes
            var packet = new PeerWirePacket 
            { 
                Command = PeerClientCommands.Request, 
                Payload = payload, 
                CommandLength = 12 
            };

            bool requestInvoked = false;
            client.Request += (c, idx, begin, len) => requestInvoked = true;

            // Act
            var result = handler.Handle(client, packet);

            // Assert
            result.Should().Be(HandlerResult.CloseConnection);
            requestInvoked.Should().BeFalse();
        }
    }

    public class PieceHandlerTests
    {
        [Fact]
        public void Handle_ValidPayload_ReturnsHandledAndInvokesPiece()
        {
            // Arrange
            var mockConn = new Mock<IPeerConnection>();
            var client = new PeerWireClient(mockConn.Object);
            var handler = new PieceHandler();
            var data = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
            var payload = new byte[]
            {
                0x00, 0x00, 0x00, 0x01, // index = 1
                0x00, 0x00, 0x00, 0x00, // begin = 0
                0xAA, 0xBB, 0xCC, 0xDD  // data
            };
            var packet = new PeerWirePacket 
            { 
                Command = PeerClientCommands.Piece, 
                Payload = payload, 
                CommandLength = 12 
            };

            bool pieceInvoked = false;
            client.Piece += (c, idx, begin, buf) =>
            {
                pieceInvoked = true;
            };

            // Act
            var result = handler.Handle(client, packet);

            // Assert
            result.Should().Be(HandlerResult.Handled);
            pieceInvoked.Should().BeTrue();
        }

        [Fact]
        public void Handle_PayloadTooShort_ReturnsCloseConnection()
        {
            // Arrange
            var mockConn = new Mock<IPeerConnection>();
            var client = new PeerWireClient(mockConn.Object);
            var handler = new PieceHandler();
            var payload = new byte[] { 0x00, 0x00, 0x00 }; // Only 3 bytes
            var packet = new PeerWirePacket 
            { 
                Command = PeerClientCommands.Piece, 
                Payload = payload, 
                CommandLength = 8 
            };

            bool pieceInvoked = false;
            client.Piece += (c, idx, begin, buf) => pieceInvoked = true;

            // Act
            var result = handler.Handle(client, packet);

            // Assert
            result.Should().Be(HandlerResult.CloseConnection);
            pieceInvoked.Should().BeFalse();
        }
    }
}

