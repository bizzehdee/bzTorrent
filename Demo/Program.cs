using System;
using bzTorrent;
using bzTorrent.Data;
using bzTorrent.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net;

namespace Demo
{

    class Program
    {
        static string peerId = "-bz2200-";
        static string inputFilename;
        static string downloadDirectory;
        static IMetadata downloadMetadata;
        static List<IPEndPoint> knownPeers = new();

        static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            GeneratePeerId();

            Console.Title = "bzTorrent Demo";

            for (var x = 0; x < args.Length; x++)
            {
                if (args[x] == "-file")
                {
                    x++;
                    inputFilename = args[x];
                }
                else if (args[x] == "-output")
                {
                    x++;
                    downloadDirectory = Path.GetFullPath(args[x]);
                }
            }

            if (!File.Exists(inputFilename))
            {
                Console.WriteLine("{0} does not exist", inputFilename);
                return;
            }

            if (!Directory.Exists(downloadDirectory))
            {
                Directory.CreateDirectory(downloadDirectory);
            }

            downloadMetadata = Metadata.FromFile(inputFilename);

            Console.Title = string.Format("bzTorrent Demo - {0}", downloadMetadata.Name);

            Console.WriteLine("Downloading: {0} to {1}", downloadMetadata.Name, downloadDirectory);
            Console.WriteLine("Hash: {0}", downloadMetadata.HashString);
            Console.WriteLine("Pieces: {0} x {1} kb", downloadMetadata.PieceHashes.Count, downloadMetadata.PieceSize / 1024);

            foreach (var tracker in downloadMetadata.AnnounceList)
            {
                Console.WriteLine("Requesting Peers from {0} for {1}", tracker, downloadMetadata.HashString);

                ITrackerClient trackerClient = null;
                if (tracker.StartsWith("http"))
                {
                    trackerClient = new HTTPTrackerClient(5);
                }
                else if (tracker.StartsWith("udp"))
                {
                    trackerClient = new UDPTrackerClient(5);
                }

                var announceInfo = trackerClient.Announce(tracker, downloadMetadata.HashString, peerId, 0, downloadMetadata.PieceHashes.Count() * downloadMetadata.PieceSize, 0, 0, 0, 256, 12345, 0);
                if (announceInfo == null)
                {
                    Console.WriteLine("Error announcing to {0}", tracker);
                    continue;
                }

                var peerArray = announceInfo.Peers.ToArray();
                knownPeers.AddRange(peerArray);

                Console.WriteLine("Found {0} seeders, {1} leachers and {2} total peers", announceInfo.Seeders, announceInfo.Leechers, peerArray.Length);

                if (knownPeers.Count >= 200)
                {
                    break;
                }
            }

            if (knownPeers.Count == 0)
            {
                Console.WriteLine("No peers found on trackers");
                return;
            }

            foreach (var file in downloadMetadata.GetFileInfos())
            {
                var fullFileName = Path.GetFullPath(file.Filename, downloadDirectory);
                var fullPathName = Path.GetDirectoryName(fullFileName);

                if (!Directory.Exists(fullPathName))
                {
                    Directory.CreateDirectory(fullPathName);
                }

                if (!File.Exists(fullFileName))
                {
                    Console.WriteLine("Preallocating {0} in {1}", file.Filename, downloadDirectory);

                    var fileStream = File.Create(fullFileName);
                    fileStream.SetLength(file.FileSize);
                    fileStream.Close();
                }
            }

            var socket = new PeerWireuTPConnection();
            socket.Timeout = 5;
            var client = new PeerWireClient(socket)
            {
                KeepConnectionAlive = true
            };

            //var peer = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 6881); //rappid testing against local install of qBittorrent

            foreach (var peer in knownPeers)
            {
                try
                {
                    Console.WriteLine("Attempting to connect to utp://{0}:{1}", peer.Address.ToString(), peer.Port);
                    client.Connect(peer);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to connect: {0}", ex.Message);
                    continue;
                }

                Console.WriteLine("Connected");
                client.Handshake(downloadMetadata.HashString, peerId);

                while (client.Process())
                {
                    Thread.Sleep(100);
                }
            }
        }

        private static void GeneratePeerId()
        {
            var sb = new StringBuilder();
            var rand = new Random();
            for (var x = 0; x < 12; x++)
            {
                sb.Append(rand.Next(0, 9));
            }
            peerId = "-bz2200-" + sb.ToString();
        }
    }
}