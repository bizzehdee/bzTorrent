using FluentAssertions;
using System;
using System.Diagnostics;
using bzTorrent.Helpers;
using Xunit;
using bzTorrent.Data;
using System.IO;
using System.Text;

namespace bzTorrent.Tests.Data
{
    public class MetadataTests
    {
        private readonly string magnetLink = "magnet:?xt=urn:btih:C1463792A1FF36A237E3A0F68BADEB0D3764E9BB&dn=ubuntu-23.10-live-server-amd64.iso&xl=2662275072&tr=https%3A%2F%2Ftorrent.ubuntu.com%2Fannounce";
        private readonly string magnetLinkWithNoHash = "magnet:?dn=ubuntu-23.10-live-server-amd64.iso&xl=2662275072&tr=https%3A%2F%2Ftorrent.ubuntu.com%2Fannounce";

        public MetadataTests() 
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        [Fact]
        public void ValidFileCreatesValidMetadata()
        {
            var metadata = new Metadata();

            var file = File.OpenRead("TestFiles//ubuntu-23.10-live-server-amd64.iso.torrent");

            metadata.Load(file).Should().BeTrue();

            metadata.HashString.Should().Be("C1463792A1FF36A237E3A0F68BADEB0D3764E9BB");
            metadata.Name.Should().Be("ubuntu-23.10-live-server-amd64.iso");
            metadata.CreationDate.Should().Be(DateTime.Parse("12/10/2023 14:24:45"));

            file.Close();
        }

        [Fact]
        public void InvalidTorrentReturnsFalseOnLoad()
        {
            var metadata = new Metadata();

            var file = File.OpenRead("TestFiles//InvalidTorrent.torrent");

            metadata.Load(file).Should().BeFalse();

            file.Close();
        }

        [Fact]
        public void ValidFileCreatesValidMetadata2()
        {
            var file = File.OpenRead("TestFiles//ubuntu-23.10-live-server-amd64.iso.torrent");

            var metadata = new Metadata(file);

            file.Close();

            metadata.HashString.Should().Be("C1463792A1FF36A237E3A0F68BADEB0D3764E9BB");
            metadata.Name.Should().Be("ubuntu-23.10-live-server-amd64.iso");
            metadata.CreationDate.Should().Be(DateTime.Parse("12/10/2023 14:24:45"));
            metadata.Pieces.Should().HaveCount(10156);
        }

        [Fact]
        public void PrivateTorrentShouldSetPrivateFlag()
        {
            var file = File.OpenRead("TestFiles//UbuntuTestTorrent.torrent");

            var metadata = new Metadata();

            metadata.Load(file).Should().BeTrue();

            file.Close();

            metadata.Private.Should().BeTrue();
        }

        [Fact]
        public void TorrentWithCreatedByShouldBeParsed()
        {
            var file = File.OpenRead("TestFiles//UbuntuTestTorrent.torrent");

            var metadata = new Metadata();

            metadata.Load(file).Should().BeTrue();

            file.Close();

            metadata.CreatedBy.Should().Be("qBittorrent v4.5.5");
        }

        [Fact]
        public void TorrentWithMultipleFilesShouldHaveMultipleFiles()
        {
            var file = File.OpenRead("TestFiles//UbuntuTestTorrent.torrent");

            var metadata = new Metadata();

            metadata.Load(file).Should().BeTrue();

            file.Close();

            metadata.GetFiles().Should().HaveCount(2);
        }

        [Fact]
        public void ValidMagnetDataCreatesValidMetadata()
        {
            var magnet = MagnetLink.Resolve(magnetLink);
            var metadata = new Metadata();

            metadata.Load(magnet).Should().BeTrue();

            metadata.HashString.Should().Be("C1463792A1FF36A237E3A0F68BADEB0D3764E9BB");
            metadata.Name.Should().Be("ubuntu-23.10-live-server-amd64.iso");
        }

        [Fact]
        public void ValidMagnetDataCreatesValidMetadata2()
        {
            var metadata = new Metadata(MagnetLink.Resolve(magnetLink));

            metadata.HashString.Should().Be("C1463792A1FF36A237E3A0F68BADEB0D3764E9BB");
            metadata.Name.Should().Be("ubuntu-23.10-live-server-amd64.iso");
        }

        [Fact]
        public void InvalidMagnetData()
        {
            var metadata = new Metadata();

            metadata.Load(MagnetLink.Resolve(magnetLinkWithNoHash)).Should().BeFalse();
        }
    }
}
