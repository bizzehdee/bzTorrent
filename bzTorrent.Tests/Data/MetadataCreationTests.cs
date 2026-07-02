using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using bzTorrent.Data;
using Xunit;

namespace bzTorrent.Tests.Data
{
    public class MetadataCreationTests : IDisposable
    {
        private readonly string tempDir;

        public MetadataCreationTests()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "bzTorrentTests_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void CreateFromPath_SingleFile_ProducesLoadableTorrentWithCorrectHash()
        {
            var filePath = Path.Combine(tempDir, "single.bin");
            var content = new byte[50_000];
            new Random(1).NextBytes(content);
            File.WriteAllBytes(filePath, content);

            var metadata = (Metadata)Metadata.CreateFromPath(filePath, new[] { "udp://tracker.example.com:80/announce" }, pieceSize: 16 * 1024, comment: "test torrent", isPrivate: true);

            metadata.Name.Should().Be("single.bin");
            metadata.PieceSize.Should().Be(16 * 1024);
            metadata.Private.Should().BeTrue();
            metadata.Comment.Should().Be("test torrent");
            metadata.Announce.Should().Be("udp://tracker.example.com:80/announce");
            metadata.PieceHashes.Should().HaveCount((int)Math.Ceiling(content.Length / (16.0 * 1024)));
            metadata.GetFileInfos().Should().HaveCount(1);
            metadata.GetFileInfos().First().FileSize.Should().Be(content.Length);

            using var stream = new MemoryStream();
            metadata.Save(stream);
            stream.Position = 0;

            var reloaded = (Metadata)Metadata.FromBuffer(stream.ToArray());
            reloaded.HashString.Should().Be(metadata.HashString);
            reloaded.Name.Should().Be("single.bin");
            reloaded.Private.Should().BeTrue();
            reloaded.PieceHashes.Should().BeEquivalentTo(metadata.PieceHashes, o => o.WithStrictOrdering());
        }

        [Fact]
        public void CreateFromPath_Directory_ProducesMultiFileTorrent()
        {
            var subDir = Path.Combine(tempDir, "content");
            Directory.CreateDirectory(Path.Combine(subDir, "nested"));
            File.WriteAllBytes(Path.Combine(subDir, "a.txt"), new byte[1234]);
            File.WriteAllBytes(Path.Combine(subDir, "nested", "b.txt"), new byte[5678]);

            var metadata = (Metadata)Metadata.CreateFromPath(subDir, pieceSize: 512);

            metadata.Name.Should().Be("content");
            metadata.GetFileInfos().Should().HaveCount(2);
            metadata.GetFileInfos().Sum(f => f.FileSize).Should().Be(1234 + 5678);
            metadata.GetFiles().Should().Contain(f => f.Contains("nested"));

            using var stream = new MemoryStream();
            metadata.Save(stream);
            stream.Position = 0;

            var reloaded = (Metadata)Metadata.FromBuffer(stream.ToArray());
            reloaded.HashString.Should().Be(metadata.HashString);
            reloaded.GetFileInfos().Should().HaveCount(2);
        }

        [Fact]
        public void SaveToFile_RoundTripsThroughFromFile()
        {
            var filePath = Path.Combine(tempDir, "roundtrip.bin");
            File.WriteAllBytes(filePath, new byte[100_000]);

            var metadata = Metadata.CreateFromPath(filePath, pieceSize: 32 * 1024);

            var torrentPath = Path.Combine(tempDir, "roundtrip.torrent");
            metadata.SaveToFile(torrentPath);

            var reloaded = (Metadata)Metadata.FromFile(torrentPath);
            reloaded.HashString.Should().Be(metadata.HashString);
            reloaded.PieceHashes.Should().HaveCount(metadata.PieceHashes.Count);
        }

        [Fact]
        public void Save_WithoutLoadedOrCreatedMetadata_Throws()
        {
            var metadata = new Metadata();

            Action act = () => metadata.Save(new MemoryStream());

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void CreateFromPath_MissingSource_Throws()
        {
            Action act = () => Metadata.CreateFromPath(Path.Combine(tempDir, "doesnotexist"));

            act.Should().Throw<FileNotFoundException>();
        }

        [Fact]
        public void CreateFromPath_DefaultPieceSize_IsPickedAutomatically()
        {
            var filePath = Path.Combine(tempDir, "auto.bin");
            File.WriteAllBytes(filePath, new byte[1_000_000]);

            var metadata = (Metadata)Metadata.CreateFromPath(filePath);

            metadata.PieceSize.Should().BeGreaterThan(0);
            metadata.PieceHashes.Should().HaveCount((int)Math.Ceiling(1_000_000.0 / metadata.PieceSize));
        }
    }
}
