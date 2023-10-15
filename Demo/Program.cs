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
using bzTorrent.ProtocolExtensions;

namespace Demo
{

    class Program
    {
        static string peerId = "-bz2200-";
        static string inputFilename;
        static string downloadDirectory;
        static IMetadata downloadMetadata;
        static readonly List<IPEndPoint> knownPeers = new();
        static bool choked = false;

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

                var announceInfo = trackerClient.Announce(tracker, downloadMetadata.HashString, peerId, 0, downloadMetadata.PieceHashes.Count * downloadMetadata.PieceSize, 0, 0, 0, 256, 12345, 0);
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

            var socket = new PeerWireTCPConnection
            {
                Timeout = 5
            };

            var client = new PeerWireClient(socket)
            {
                KeepConnectionAlive = true
            };

            var fastExt = new FastExtensions();
            fastExt.AllowedFast += FastExt_AllowedFast;
            fastExt.HaveAll += FastExt_HaveAll;
            fastExt.HaveNone += FastExt_HaveNone;
            fastExt.SuggestPiece += FastExt_SuggestPiece;

            client.RegisterBTExtension(fastExt);

            client.NoData += Client_NoData;
            client.BitField += Client_BitField;
            client.Cancel += Client_Cancel;
            client.Piece += Client_Piece;
            client.Choke += Client_Choke;
            client.UnChoke += Client_UnChoke;
            client.DroppedConnection += Client_DroppedConnection;
            client.HandshakeComplete += Client_HandshakeComplete;
            client.Have += Client_Have;
            client.Interested += Client_Interested;
            client.NotInterested += Client_NotInterested;
            client.Request += Client_Request;

            foreach (var peer in knownPeers)
            {
                try
                {
                    Console.WriteLine("Attempting to connect to {0}:{1}", peer.Address.ToString(), peer.Port);
                    client.Connect(peer);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to connect: {0}", ex.Message);
                    //continue;
                }

                Console.WriteLine("Connected");
                client.Handshake(downloadMetadata.HashString, peerId);
                Thread.Sleep(200);
                int x = 0, i=0;
                while (client.Process())
                {
                    if (choked == false)
                    {

                    }
                    else
                    {

                    }

                    if(x++ > 10)
                    {
                        x = 0;
                        client.SendKeepAlive();
                    }

                    Thread.Sleep(200);
                }
            }
        }

        private static void FastExt_SuggestPiece(IPeerWireClient arg1, int arg2)
        {
            Console.WriteLine("SuggestPiece");
        }

        private static void FastExt_HaveNone(IPeerWireClient obj)
        {
            Console.WriteLine("HaveNone");
        }

        private static void FastExt_HaveAll(IPeerWireClient obj)
        {
            Console.WriteLine("HaveAll");
            //obj.SendRequest(1, 0, (uint)downloadMetadata.PieceSize);
        }

        private static void FastExt_AllowedFast(IPeerWireClient arg1, int arg2)
        {
            Console.WriteLine("AllowedFast");
        }

        private static void Client_Request(IPeerWireClient arg1, int arg2, int arg3, int arg4)
        {
            Console.WriteLine("Request");
        }

        private static void Client_NotInterested(IPeerWireClient obj)
        {
            Console.WriteLine("NotInterested");
        }

        private static void Client_Interested(IPeerWireClient obj)
        {
            Console.WriteLine("Interested");
        }

        private static void Client_Have(IPeerWireClient arg1, int arg2)
        {
            Console.WriteLine("Have");
        }

        private static void Client_HandshakeComplete(IPeerWireClient obj)
        {
            Console.WriteLine("HandshakeComplete");
        }

        private static void Client_DroppedConnection(IPeerWireClient obj)
        {
            Console.WriteLine("DroppedConnection");
        }

        private static void Client_UnChoke(IPeerWireClient obj)
        {
            Console.WriteLine("UnChoke");
            choked = false;
        }

        private static void Client_Choke(IPeerWireClient obj)
        {
            Console.WriteLine("Choke");
            choked = true;
        }

        private static void Client_Piece(IPeerWireClient arg1, int index, int start, byte[] buffer)
        {
            Console.WriteLine("Piece");

        }

        private static void Client_Cancel(IPeerWireClient arg1, int arg2, int arg3, int arg4)
        {
            Console.WriteLine("Cancel");

        }

        private static void Client_BitField(IPeerWireClient arg1, int bitFieldLength, bool[] bitField)
        {
            Console.WriteLine("BitField");

        }

        private static void Client_NoData(IPeerWireClient obj)
        {
            Console.WriteLine("NoData");
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