using System.IO;
using bzTorrent.Data;
using FluentAssertions;
using Xunit;

namespace bzTorrent.Tests.Data
{
    public class MetadataWebSeedTests
    {
        [Fact]
        public void MultiFileTorrent_IsMultiFileIsTrue()
        {
            var file = File.OpenRead("TestFiles//UbuntuTestTorrent.torrent");
            var metadata = new Metadata();
            metadata.Load(file).Should().BeTrue();
            file.Close();

            metadata.IsMultiFile.Should().BeTrue();
        }

        [Fact]
        public void SingleFileTorrent_IsMultiFileIsFalse()
        {
            var file = File.OpenRead("TestFiles//ubuntu-23.10-live-server-amd64.iso.torrent");
            var metadata = new Metadata();
            metadata.Load(file).Should().BeTrue();
            file.Close();

            metadata.IsMultiFile.Should().BeFalse();
        }

        [Fact]
        public void TorrentWithNoUrlList_WebSeedsIsEmpty()
        {
            var file = File.OpenRead("TestFiles//ubuntu-23.10-live-server-amd64.iso.torrent");
            var metadata = new Metadata();
            metadata.Load(file).Should().BeTrue();
            file.Close();

            metadata.WebSeeds.Should().BeEmpty();
        }

        [Fact]
        public void SingleUrlListString_IsParsedAsOneWebSeed()
        {
            var bencoded = "d8:url-list28:https://example.com/file.iso4:infod6:lengthi10e4:name4:test6:pieces20:aaaaaaaaaaaaaaaaaaaa12:piece lengthi10eee";
            var metadata = (Metadata)Metadata.FromString(bencoded);

            metadata.WebSeeds.Should().ContainSingle().Which.Should().Be("https://example.com/file.iso");
        }

        [Fact]
        public void UrlListOfStrings_AreAllParsedAsWebSeeds()
        {
            var bencoded = "d8:url-listl21:https://a.example.com21:https://b.example.come4:infod6:lengthi10e4:name4:test6:pieces20:aaaaaaaaaaaaaaaaaaaa12:piece lengthi10eee";
            var metadata = (Metadata)Metadata.FromString(bencoded);

            metadata.WebSeeds.Should().BeEquivalentTo("https://a.example.com", "https://b.example.com");
        }
    }
}
