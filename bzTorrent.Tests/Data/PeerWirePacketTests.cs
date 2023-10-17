using FluentAssertions;
using System;
using System.Diagnostics;
using bzTorrent.Helpers;
using Xunit;
using bzTorrent.Data;

namespace bzTorrent.Tests.Data
{
    public class PeerWirePacketTests
    {
        [Fact]
        public void KeepAlivePacketHasZeroCommandLength()
        {
            var pwp = new PeerWirePacket();
            pwp.Parse(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }).Should().BeTrue();

            pwp.Command.Should().Be(PeerClientCommands.KeepAlive);
            pwp.CommandLength.Should().Be(0);
            pwp.PacketByteLength.Should().Be(4);
        }

        [Fact]
        public void HavePacketHas5ByteCommandLength()
        {
            var pwp = new PeerWirePacket();
            pwp.Parse(new byte[] { 0, 0, 0, 5, 4, 0, 0, 0, 1 }).Should().BeTrue();

            pwp.Command.Should().Be(PeerClientCommands.Have);
            pwp.CommandLength.Should().Be(5);
            pwp.PacketByteLength.Should().Be(9);
        }

        [Fact]
        public void ParseShouldReturnFalseIfNotEnoughData()
        {
            var pwp = new PeerWirePacket();
            pwp.Parse(new byte[] { 0, 0, 0, 5, 4, 0, 0 }).Should().BeFalse();
        }

        [Fact]
        public void OutputShouldEqualValidInput()
        {
            var inputOutput = new byte[] { 0, 0, 0, 5, 4, 0, 0, 0, 1 };
            var pwp = new PeerWirePacket();
            pwp.Parse(inputOutput).Should().BeTrue();

            pwp.GetBytes().Should().BeEquivalentTo(inputOutput);
        }

        [Fact]
        public void KeepAliveGetBytesShouldMatchOutput()
        {
            var inputOutput = new byte[] { 0, 0, 0, 0 };
            var pwp = new PeerWirePacket();
            pwp.Parse(inputOutput).Should().BeTrue();

            pwp.GetBytes().Should().BeEquivalentTo(inputOutput);
        }

        [Fact]
        public void CommandPacketShouldBe5Bytes()
        {
            var pwp = new PeerWirePacket();
            pwp.Parse(new byte[] { 0, 0, 0, 1, 1 }).Should().BeTrue();

            pwp.CommandLength.Should().Be(1);
            pwp.PacketByteLength.Should().Be(5);
        }

        [Fact]
        public void Command0IsChoke()
        {
            var pwp = new PeerWirePacket();
            pwp.Parse(new byte[] { 0, 0, 0, 1, 0 }).Should().BeTrue();

            pwp.Command.Should().Be(PeerClientCommands.Choke);
        }

        [Fact]
        public void Command1IsUnchoke()
        {
            var pwp = new PeerWirePacket();
            pwp.Parse(new byte[] { 0, 0, 0, 1, 1 }).Should().BeTrue();

            pwp.Command.Should().Be(PeerClientCommands.Unchoke);
        }

        [Fact]
        public void Command2IsUnchoke()
        {
            var pwp = new PeerWirePacket();
            pwp.Parse(new byte[] { 0, 0, 0, 1, 2 }).Should().BeTrue();

            pwp.Command.Should().Be(PeerClientCommands.Interested);
        }

        [Fact]
        public void Command3IsUnchoke()
        {
            var pwp = new PeerWirePacket();
            pwp.Parse(new byte[] { 0, 0, 0, 1, 3 }).Should().BeTrue();

            pwp.Command.Should().Be(PeerClientCommands.NotInterested);
        }

        [Fact]
        public void Command4IsHave()
        {
            var pwp = new PeerWirePacket();
            pwp.Parse(new byte[] { 0, 0, 0, 5, 4, 0, 0, 0, 1 }).Should().BeTrue();

            pwp.Command.Should().Be(PeerClientCommands.Have);
        }
    }
}
