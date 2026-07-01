using System;
using bzTorrent;
using bzTorrent.Data;
using bzTorrent.DHT;
using bzTorrent.IO;
using System.Text;
using System.Threading;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using bzTorrent.ProtocolExtensions;
using System.Threading.Tasks;

namespace Demo
{
    class Program
    {
        const int MaxConnections = 10;
        const int MaxRequest = 16 * 1024;
        const int MaxInflight = 10;
        // How long to wait for a peer's handshake before giving up the connection slot. A
        // peer that connects but never handshakes (e.g. it requires encryption, or is just
        // unresponsive) must not pin a scarce connection slot for long, or the handful of
        // connectable peers get starved. Reachable peers handshake in well under a second.
        const int HandshakeTimeoutSeconds = 8;

        static string peerId = "-bz2200-";
        static string downloadDirectory;
        static Metadata downloadMetadata;
        static PeerEncryptionMode encryptionMode = PeerEncryptionMode.PreferEncryption;

        static readonly ConcurrentQueue<IPEndPoint> peerQueue = new();
        static readonly HashSet<string> seenPeers = new();
        static readonly object seenPeersLock = new();
        static readonly object metadataLock = new();

        static int currentPiece = 0;
        static int activeConnections = 0;

        static long PieceSizeFor(int pieceIndex)
        {
            var totalBytes = downloadMetadata.GetFileInfos().Sum(f => f.FileSize);
            if (pieceIndex == downloadMetadata.PieceHashes.Count - 1)
            {
                var remainder = totalBytes % downloadMetadata.PieceSize;
                return remainder == 0 ? downloadMetadata.PieceSize : remainder;
            }
            return downloadMetadata.PieceSize;
        }

        static void WritePieceData(int pieceIndex, int offset, byte[] data)
        {
            var absoluteStart = (long)pieceIndex * downloadMetadata.PieceSize + offset;
            var absoluteEnd = absoluteStart + data.Length;

            foreach (var fileInfo in downloadMetadata.GetFileInfos())
            {
                var fileEnd = fileInfo.FileStartByte + fileInfo.FileSize;
                if (absoluteEnd <= fileInfo.FileStartByte || absoluteStart >= fileEnd)
                    continue;

                var fileOffset = Math.Max(absoluteStart - fileInfo.FileStartByte, 0);
                var dataOffset = (int)Math.Max(fileInfo.FileStartByte - absoluteStart, 0);
                var writeLength = (int)Math.Min(data.Length - dataOffset, fileInfo.FileSize - fileOffset);

                if (writeLength <= 0)
                    continue;

                var fullPath = Path.GetFullPath(fileInfo.Filename, downloadDirectory);
                using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                fs.Seek(fileOffset, SeekOrigin.Begin);
                fs.Write(data, dataOffset, writeLength);
            }
        }

        static void PreAllocateFiles()
        {
            foreach (var file in downloadMetadata.GetFileInfos())
            {
                var fullFileName = Path.GetFullPath(file.Filename, downloadDirectory);
                var fullPathName = Path.GetDirectoryName(fullFileName);

                if (!Directory.Exists(fullPathName))
                    Directory.CreateDirectory(fullPathName);

                if (!File.Exists(fullFileName))
                {
                    Console.WriteLine("Preallocating {0} in {1}", file.Filename, downloadDirectory);
                    using var fileStream = File.Create(fullFileName);
                    fileStream.SetLength(file.FileSize);
                }
            }
        }

        static void AddPeers(IEnumerable<IPEndPoint> peers)
        {
            foreach (var peer in peers)
            {
                if (!IsUsablePeerEndpoint(peer))
                    continue;

                var key = $"{peer.Address}:{peer.Port}";
                lock (seenPeersLock)
                {
                    if (!seenPeers.Add(key))
                        continue;
                }
                Console.WriteLine($"Discovered peer {key}");
                peerQueue.Enqueue(peer);
            }
        }

        static bool IsUsablePeerEndpoint(IPEndPoint peer)
        {
            if (peer == null || peer.Port <= 0 || peer.Port > 65535)
                return false;

            var address = peer.Address;
            if (address.AddressFamily != AddressFamily.InterNetwork)
                return false;

            if (IPAddress.Any.Equals(address) || IPAddress.Broadcast.Equals(address) || IPAddress.None.Equals(address))
                return false;

            var bytes = address.GetAddressBytes();
            return bytes[0] != 0 && bytes[0] < 224;
        }

        static void PollTracker(string tracker)
        {
            ITrackerClient trackerClient;
            if (tracker.StartsWith("http"))
                trackerClient = new HTTPTrackerClient(10);
            else if (tracker.StartsWith("udp"))
                trackerClient = new UDPTrackerClient(10);
            else
            {
                Console.WriteLine($"Unsupported tracker protocol: {tracker}");
                return;
            }

            while (downloadMetadata.PieceHashes.Count == 0 || currentPiece < downloadMetadata.PieceHashes.Count)
            {
                try
                {
                    Console.WriteLine($"Requesting peers from {tracker}");
                    var announceInfo = trackerClient.Announce(
                        tracker,
                        downloadMetadata.HashString,
                        peerId,
                        0,
                        downloadMetadata.PieceHashes.Count * downloadMetadata.PieceSize,
                        0, 0, 0, 256, 12345, 0);

                    if (announceInfo != null)
                    {
                        var peers = announceInfo.Peers.ToArray();
                        AddPeers(peers);
                        Console.WriteLine($"Tracker {tracker}: {announceInfo.Seeders} seeders, {announceInfo.Leechers} leechers, {peers.Length} peers");

                        var interval = Math.Max(announceInfo.WaitTime, 30);
                        Thread.Sleep(TimeSpan.FromSeconds(interval));
                    }
                    else
                    {
                        Console.WriteLine($"Error announcing to {tracker}, retrying in 60s");
                        Thread.Sleep(TimeSpan.FromSeconds(60));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Tracker {tracker} error: {ex.Message}, retrying in 60s");
                    Thread.Sleep(TimeSpan.FromSeconds(60));
                }
            }
        }

        static void RunPeerConnection(IPEndPoint peer)
        {
            bool choked = true;
            int inflightPieces = 0;
            var pieceQueue = new Queue<int>();
            var connectedDateTime = DateTime.UtcNow;

            var socket = new PeerWireConnection<TCPSocket>
            {
                Timeout = 30,
                EncryptionMode = encryptionMode
            };
            var client = new PeerWireClient(socket) { KeepConnectionAlive = true };

            var fastExt = new FastExtensions();
            fastExt.AllowedFast += (pwc, index) =>
            {
                Console.WriteLine($"> AllowedFast: {index}");
                pieceQueue.Enqueue(index);
            };
            fastExt.HaveAll += (pwc) =>
            {
                Console.WriteLine("> HaveAll");
                pwc.SendInterested();
            };
            fastExt.HaveNone += (pwc) => Console.WriteLine("> HaveNone");
            fastExt.SuggestPiece += (pwc, index) => Console.WriteLine($"> SuggestPiece: {index}");

            var dhtPortExt = new DHTPortExtension();
            dhtPortExt.Port += (pwc, port) => Console.WriteLine($"> DHT Port: {port}");

            var pex = new UTPeerExchange();
            pex.Added += (pwc, ext, endpoint, flags) =>
            {
                Console.WriteLine($"> PEX peer: {endpoint}");
                AddPeers([endpoint]);
            };

            var trackerExchange = new LTTrackerExchange();
            trackerExchange.TrackerAdded += (pwc, ext, newTracker) =>
            {
                Console.WriteLine($"> Tracker exchange: {newTracker}");
                Task.Run(() => PollTracker(newTracker));
            };

            var utMetadata = new UTMetadata();
            utMetadata.MetaDataReceived += (pwc, ext, infoDict) =>
            {
                lock (metadataLock)
                {
                    if (downloadMetadata.PieceHashes.Count > 0) return;

                    Console.WriteLine($"Metadata received from {peer}, loading...");
                    downloadMetadata.Load(infoDict);
                    Console.WriteLine($"Name: {downloadMetadata.Name}");
                    Console.WriteLine($"Pieces: {downloadMetadata.PieceHashes.Count} x {downloadMetadata.PieceSize / 1024} kb");
                    PreAllocateFiles();
                }
            };

            var extProtocol = new ExtendedProtocolExtensions();
            extProtocol.RegisterProtocolExtension(client, pex);
            extProtocol.RegisterProtocolExtension(client, trackerExchange);
            extProtocol.RegisterProtocolExtension(client, utMetadata);

            client.RegisterBTExtension(fastExt);
            client.RegisterBTExtension(dhtPortExt);
            client.RegisterBTExtension(extProtocol);

            client.HandshakeComplete += (pwc) =>
            {
                Console.WriteLine($"> HandshakeComplete [{peer}]");
                pwc.SendInterested();
            };
            client.BitField += (pwc, bitFieldLength, bitField) =>
            {
                Console.WriteLine($"> BitField [{peer}]");
                pwc.SendInterested();
            };
            client.UnChoke += (pwc) =>
            {
                Console.WriteLine($"> UnChoke [{peer}]");
                choked = false;
            };
            client.Choke += (pwc) =>
            {
                Console.WriteLine($"> Choke [{peer}]");
                choked = true;
            };
            client.Have += (pwc, index) =>
            {
                Console.WriteLine($"> Have {index} [{peer}]");
                pieceQueue.Enqueue(index);
            };
            client.Piece += (pwc, index, start, buffer) =>
            {
                var enc = pwc.IsEncrypted ? "enc" : "plain";
                Console.WriteLine($"{enc}> Piece: {index} # {start} [{peer}]");
                inflightPieces = Math.Max(0, inflightPieces - 1);
                WritePieceData(index, start, buffer);
            };
            client.DroppedConnection += (pwc) => Console.WriteLine($"> DroppedConnection [{peer}]");
            client.Cancel += (pwc, a, b, c) => Console.WriteLine($"> Cancel [{peer}]");
            client.Request += (pwc, a, b, c) => Console.WriteLine($"> Request [{peer}]");
            client.Interested += (pwc) => Console.WriteLine($"> Interested [{peer}]");
            client.NotInterested += (pwc) => Console.WriteLine($"> NotInterested [{peer}]");
            client.NoData += (pwc) => { };

            try
            {
                Console.WriteLine($"< Connecting to {peer}");
                client.Connect(peer);
                connectedDateTime = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"< Failed to connect to {peer}: {ex.Message}");
                return;
            }

            try
            {
                client.Handshake(downloadMetadata.HashString, peerId);
                Thread.Sleep(200);

                int keepAliveCounter = 0;
                while (client.Process())
                {
                    if (!client.ReceivedHandshake && connectedDateTime < DateTime.UtcNow.AddSeconds(-HandshakeTimeoutSeconds))
                    {
                        Console.WriteLine($"< No handshake from {peer}, disconnecting");
                        client.Disconnect();
                        break;
                    }

                    // Only check piece completion and request data once we have metadata
                    if (downloadMetadata.PieceHashes.Count > 0)
                    {
                        if (currentPiece >= downloadMetadata.PieceHashes.Count)
                        {
                            client.Disconnect();
                            break;
                        }

                        if (!choked && inflightPieces < MaxInflight)
                        {
                            int piece;
                            if (pieceQueue.Count > 0)
                            {
                                piece = pieceQueue.Dequeue();
                            }
                            else
                            {
                                piece = Interlocked.Increment(ref currentPiece) - 1;
                                if (piece >= downloadMetadata.PieceHashes.Count)
                                    break;
                            }

                            var pieceSize = PieceSizeFor(piece);
                            var ceil = (int)Math.Ceiling(pieceSize / (float)MaxRequest);
                            for (int b = 0; b < ceil; b++)
                            {
                                var offset = b * MaxRequest;
                                var requesting = (int)Math.Min(pieceSize - offset, MaxRequest);
                                Console.WriteLine($"< Request({piece}, {offset}, {requesting}) [{peer}]");
                                client.SendRequest((uint)piece, (uint)offset, (uint)requesting);
                                inflightPieces++;
                            }
                        }
                    }

                    if (++keepAliveCounter > 1000)
                    {
                        keepAliveCounter = 0;
                        client.SendKeepAlive();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"< Error with {peer}: {ex.Message}");
            }
        }

        static void Main(string[] args)
        {
            var lpd = new LocalPeerDiscovery<UDPSocket>();
            lpd.NewPeer += (address, port, infoHash) =>
            {
                Console.WriteLine($"Found new local peer {address}:{port}");
                AddPeers([new IPEndPoint(address, port)]);
            };
            lpd.Open();

            GeneratePeerId();

            string inputFilename = null;
            string magnetLink = null;
            var manualPeers = new List<IPEndPoint>();

            for (var x = 0; x < args.Length; x++)
            {
                if (args[x] == "-file") { x++; inputFilename = args[x]; }
                else if (args[x] == "-magnet") { x++; magnetLink = args[x]; }
                else if (args[x] == "-output") { x++; downloadDirectory = Path.GetFullPath(args[x]); }
                else if (args[x] == "-peer")
                {
                    x++;
                    var parts = args[x].Split(':');
                    if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var peerAddress) || !int.TryParse(parts[1], out var peerPort))
                    {
                        Console.WriteLine($"Invalid -peer value '{args[x]}', expected format ip:port");
                        return;
                    }
                    manualPeers.Add(new IPEndPoint(peerAddress, peerPort));
                }
                else if (args[x] == "-require-encryption") { encryptionMode = PeerEncryptionMode.RequireEncryption; }
            }

            if (inputFilename == null && magnetLink == null)
            {
                Console.WriteLine("Usage: Demo -file <torrent> | -magnet <link> -output <directory> [-peer ip:port ...]");
                return;
            }

            if (inputFilename != null && !File.Exists(inputFilename))
            {
                Console.WriteLine("{0} does not exist", inputFilename);
                return;
            }

            if (downloadDirectory == null)
            {
                Console.WriteLine("No output directory specified (-output)");
                return;
            }

            if (!Directory.Exists(downloadDirectory))
                Directory.CreateDirectory(downloadDirectory);

            if (magnetLink != null)
            {
                if (!MagnetLink.IsMagnetLink(magnetLink))
                {
                    Console.WriteLine("Invalid magnet link");
                    return;
                }
                downloadMetadata = new Metadata(MagnetLink.Resolve(magnetLink));
                Console.WriteLine("Magnet link: {0}", downloadMetadata.HashString);
                Console.WriteLine("Fetching metadata from peers...");
            }
            else
            {
                downloadMetadata = (Metadata)Metadata.FromFile(inputFilename);
                Console.WriteLine("Downloading: {0} to {1}", downloadMetadata.Name, downloadDirectory);
                Console.WriteLine("Hash: {0}", downloadMetadata.HashString);
                Console.WriteLine("Pieces: {0} x {1} kb", downloadMetadata.PieceHashes.Count, downloadMetadata.PieceSize / 1024);
                PreAllocateFiles();
            }

            if (manualPeers.Count > 0)
                AddPeers(manualPeers);

            foreach (var tracker in downloadMetadata.AnnounceList)
            {
                var t = tracker;
                Task.Run(() => PollTracker(t));
            }

            var dht = new DHTClient();
            dht.PeerFound += endpoint =>
            {
                Console.WriteLine($"DHT peer: {endpoint}");
                AddPeers([endpoint]);
            };
            dht.Start();
            // Start the search immediately; it waits internally until the routing table has
            // entries. Decoupling it from bootstrap means a bootstrap error can never prevent
            // the search from running once nodes are available.
            dht.StartSearch(downloadMetadata.Hash);
            Task.Run(async () =>
            {
                try
                {
                    var bootstrapNodes = ResolveBootstrapNodes();
                    Console.WriteLine($"DHT bootstrapping from {bootstrapNodes.Length} nodes");
                    if (bootstrapNodes.Length == 0)
                    {
                        Console.WriteLine("DHT: no bootstrap nodes resolved (DNS failure?) — DHT will stay idle");
                        return;
                    }

                    await dht.BootstrapAsync(bootstrapNodes);
                    Console.WriteLine($"DHT routing table: {dht.NodeCount} nodes");
                    if (dht.NodeCount == 0)
                    {
                        Console.WriteLine("DHT: bootstrap produced no nodes (UDP blocked or nodes unreachable?)");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"DHT bootstrap failed: {ex.Message}");
                }
            });

            // For magnet links, keep looping until we have metadata AND all pieces are requested.
            // For torrent files, loop until all pieces are requested.
            while (downloadMetadata.PieceHashes.Count == 0 || currentPiece < downloadMetadata.PieceHashes.Count)
            {
                if (activeConnections >= MaxConnections)
                {
                    Thread.Sleep(100);
                    continue;
                }

                if (!peerQueue.TryDequeue(out var peer))
                {
                    if (activeConnections == 0)
                        Console.WriteLine("Waiting for peers...");
                    Thread.Sleep(500);
                    continue;
                }

                Interlocked.Increment(ref activeConnections);
                var p = peer;
                Task.Run(() =>
                {
                    try { RunPeerConnection(p); }
                    finally { Interlocked.Decrement(ref activeConnections); }
                });
            }

            while (activeConnections > 0)
                Thread.Sleep(200);

            dht.Dispose();
            Console.WriteLine("Download complete.");
        }

        static IPEndPoint[] ResolveBootstrapNodes()
        {
            var hosts = new[]
            {
                ("router.bittorrent.com", 6881),
                ("router.utorrent.com", 6881),
                ("dht.transmissionbt.com", 6881),
            };

            var endpoints = new List<IPEndPoint>();
            foreach (var (host, port) in hosts)
            {
                try
                {
                    var addresses = Dns.GetHostAddresses(host);
                    var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                    if (ipv4 != null)
                        endpoints.Add(new IPEndPoint(ipv4, port));
                }
                catch { }
            }
            return endpoints.ToArray();
        }

        static void GeneratePeerId()
        {
            var sb = new StringBuilder();
            var rand = new Random();
            for (var x = 0; x < 12; x++)
                sb.Append(rand.Next(0, 9));
            peerId = "-bz2200-" + sb.ToString();
        }
    }
}
