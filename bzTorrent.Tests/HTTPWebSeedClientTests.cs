using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using bzTorrent.Data;
using FluentAssertions;
using Xunit;

namespace bzTorrent.Tests
{
    public class HTTPWebSeedClientTests : IDisposable
    {
        private readonly string tempDir = Path.Combine(Path.GetTempPath(), "bzTorrentWebSeedTests_" + Guid.NewGuid());

        public HTTPWebSeedClientTests()
        {
            Directory.CreateDirectory(tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }

        // A minimal loopback-only HTTP/1.1 range server: real socket I/O (per project convention
        // — no external network in tests), but not a full HttpListener so it works the same on
        // every platform/CI without needing URL ACL / elevated-permission setup.
        private sealed class RangeServer : IDisposable
        {
            private readonly System.Net.Sockets.TcpListener listener;
            private readonly Func<string, byte[]> resolveContent;
            private readonly CancellationTokenSource cts = new();
            private readonly Task acceptLoop;

            public int Port { get; }

            public RangeServer(Func<string, byte[]> resolveContent)
            {
                this.resolveContent = resolveContent;
                listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                acceptLoop = Task.Run(AcceptLoopAsync);
            }

            private async Task AcceptLoopAsync()
            {
                while (!cts.IsCancellationRequested)
                {
                    System.Net.Sockets.TcpClient client;
                    try
                    {
                        client = await listener.AcceptTcpClientAsync();
                    }
                    catch
                    {
                        return;
                    }

                    _ = Task.Run(() => HandleClient(client));
                }
            }

            private void HandleClient(System.Net.Sockets.TcpClient client)
            {
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream))
                {
                    var requestLine = reader.ReadLine();
                    if (requestLine == null) return;

                    var path = requestLine.Split(' ')[1];
                    string rangeHeader = null;
                    string line;
                    while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                    {
                        if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
                        {
                            rangeHeader = line.Substring("Range:".Length).Trim();
                        }
                    }

                    var content = resolveContent(Uri.UnescapeDataString(path));
                    var (start, end) = ParseRange(rangeHeader, content.Length);
                    var slice = content[start..(end + 1)];

                    var header = $"HTTP/1.1 206 Partial Content\r\nContent-Range: bytes {start}-{end}/{content.Length}\r\nContent-Length: {slice.Length}\r\nConnection: close\r\n\r\n";
                    var headerBytes = System.Text.Encoding.ASCII.GetBytes(header);
                    stream.Write(headerBytes, 0, headerBytes.Length);
                    stream.Write(slice, 0, slice.Length);
                    stream.Flush();
                }
            }

            private static (int Start, int End) ParseRange(string rangeHeader, int contentLength)
            {
                if (rangeHeader == null)
                {
                    return (0, contentLength - 1);
                }

                var spec = rangeHeader.Replace("bytes=", string.Empty).Split('-');
                var start = int.Parse(spec[0]);
                var end = spec.Length > 1 && spec[1].Length > 0 ? int.Parse(spec[1]) : contentLength - 1;
                return (start, end);
            }

            public void Dispose()
            {
                cts.Cancel();
                listener.Stop();
            }
        }

        [Fact]
        public void GetPieceBlock_SingleFileTorrent_FetchesExactByteRange()
        {
            var filePath = Path.Combine(tempDir, "file.bin");
            var content = new byte[10_000];
            new Random(42).NextBytes(content);
            File.WriteAllBytes(filePath, content);

            var metadata = Metadata.CreateFromPath(filePath, pieceSize: 1024);

            using var server = new RangeServer(_ => content);
            var client = new HTTPWebSeedClient(timeout: 5);

            var pieceIndex = 2L;
            var offset = 100L;
            var length = 256L;
            var expected = content[(int)(pieceIndex * 1024 + offset)..(int)(pieceIndex * 1024 + offset + length)];

            var result = client.GetPieceBlock($"http://127.0.0.1:{server.Port}/file.bin", metadata, pieceIndex, offset, length);

            result.Should().BeEquivalentTo(expected, o => o.WithStrictOrdering());
        }

        [Fact]
        public void GetPieceBlock_MultiFileTorrent_BuildsDirectoryStyleUrlAndFetchesRange()
        {
            var dir = Path.Combine(tempDir, "content");
            Directory.CreateDirectory(dir);
            var fileAContent = new byte[3000];
            var fileBContent = new byte[3000];
            new Random(1).NextBytes(fileAContent);
            new Random(2).NextBytes(fileBContent);
            File.WriteAllBytes(Path.Combine(dir, "a.bin"), fileAContent);
            File.WriteAllBytes(Path.Combine(dir, "b.bin"), fileBContent);

            var metadata = Metadata.CreateFromPath(dir, pieceSize: 512);
            metadata.IsMultiFile.Should().BeTrue();

            byte[] Resolve(string path)
            {
                // Expect /content/a.bin or /content/b.bin per BEP-19's directory-style URL.
                if (path.EndsWith("a.bin", StringComparison.Ordinal)) return fileAContent;
                if (path.EndsWith("b.bin", StringComparison.Ordinal)) return fileBContent;
                throw new InvalidOperationException($"Unexpected path: {path}");
            }

            using var server = new RangeServer(Resolve);
            var client = new HTTPWebSeedClient(timeout: 5);

            // Piece 0, offset 0, length spanning entirely inside a.bin (3000 bytes, piece size 512).
            var result = client.GetPieceBlock($"http://127.0.0.1:{server.Port}/", metadata, pieceIndex: 0, offset: 10, length: 100);

            result.Should().BeEquivalentTo(fileAContent[10..110], o => o.WithStrictOrdering());
        }

        [Fact]
        public void GetPieceBlock_RequestSpanningTwoFiles_FetchesFromBoth()
        {
            var dir = Path.Combine(tempDir, "content2");
            Directory.CreateDirectory(dir);
            var fileAContent = new byte[100];
            var fileBContent = new byte[100];
            for (var i = 0; i < 100; i++) { fileAContent[i] = (byte)i; fileBContent[i] = (byte)(200 + i); }
            File.WriteAllBytes(Path.Combine(dir, "a.bin"), fileAContent);
            File.WriteAllBytes(Path.Combine(dir, "b.bin"), fileBContent);

            var metadata = Metadata.CreateFromPath(dir, pieceSize: 256);

            byte[] Resolve(string path)
            {
                if (path.EndsWith("a.bin", StringComparison.Ordinal)) return fileAContent;
                if (path.EndsWith("b.bin", StringComparison.Ordinal)) return fileBContent;
                throw new InvalidOperationException($"Unexpected path: {path}");
            }

            using var server = new RangeServer(Resolve);
            var client = new HTTPWebSeedClient(timeout: 5);

            // Whole piece 0 (256 bytes) spans a.bin (100 bytes) + b.bin (100 bytes) + padding
            // beyond both — request only the 200 bytes that exist across the two files.
            var result = client.GetPieceBlock($"http://127.0.0.1:{server.Port}/", metadata, pieceIndex: 0, offset: 0, length: 200);

            var expected = new byte[200];
            Array.Copy(fileAContent, 0, expected, 0, 100);
            Array.Copy(fileBContent, 0, expected, 100, 100);
            result.Should().BeEquivalentTo(expected, o => o.WithStrictOrdering());
        }
    }
}
