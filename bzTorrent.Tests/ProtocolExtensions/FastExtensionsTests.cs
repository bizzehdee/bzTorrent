using FluentAssertions;
using Xunit;
using bzTorrent.ProtocolExtensions;
using bzTorrent.Helpers;

namespace bzTorrent.Tests.ProtocolExtensions
{
    public class FastExtensionsTests
    {
        [Theory]
        [InlineData(14)] // Have all
        [InlineData(15)] // Have none
        public void OnCommand_HandledNoPayloadCommand_ReturnsTrue(byte commandId)
        {
            var ext = new FastExtensions();

            ext.OnCommand(null, 1, commandId, System.Array.Empty<byte>()).Should().BeTrue();
        }

        [Theory]
        [InlineData(13)] // Suggest Piece
        [InlineData(17)] // Allowed Fast
        public void OnCommand_HandledSingleIndexCommand_ReturnsTrue(byte commandId)
        {
            var ext = new FastExtensions();
            var payload = PackHelper.UInt32(5); // 4-byte piece index

            ext.OnCommand(null, 5, commandId, payload).Should().BeTrue();
        }

        [Fact]
        public void OnCommand_RejectCommand_ReturnsTrue()
        {
            var ext = new FastExtensions();
            var payload = PackHelper.UInt32(1).Cat(PackHelper.UInt32(0)).Cat(PackHelper.UInt32(16384));

            ext.OnCommand(null, 13, 16, payload).Should().BeTrue();
        }

        [Fact]
        public void OnCommand_UnknownCommand_ReturnsFalse()
        {
            var ext = new FastExtensions();

            ext.OnCommand(null, 1, 99, System.Array.Empty<byte>()).Should().BeFalse();
        }

        [Fact]
        public void OnCommand_FiresEventForHandledCommand()
        {
            var ext = new FastExtensions();
            var fired = false;
            ext.HaveAll += _ => fired = true;

            ext.OnCommand(null, 1, 14, System.Array.Empty<byte>());

            fired.Should().BeTrue();
        }
    }
}
