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
using System.Reflection;

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
        static int maxRequest = 16 * 1024;
        static DateTime connectedDateTime;

        static Queue<int> PieceQueue = new Queue<int>();

        static int currentPiece = 0;
        static int inflightPieces = 0;

        static void RequestPieceInParts(PeerWireClient client, int piece, int maxBufferSize, long pieceSize)
        {

            var ceil = Math.Ceiling(pieceSize / (float)maxBufferSize);
            for (int b = 0; b < ceil; b++)
            {
                var n = b * maxBufferSize;

                var requesting = Math.Min(pieceSize - n, maxBufferSize);

                var m = n + requesting;

                Console.WriteLine($"< Request({piece}, {n}, {requesting})");

                client.SendRequest((uint)piece, (uint)n, (uint)requesting);


                inflightPieces++;
            }
        }

        static void Main(string[] args)
        {
            var lpd = new LocalPeerDiscovery();
            lpd.NewPeer += Lpd_NewPeer;
            lpd.Open();

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

            var socket = new PeerWireConnection<UTPSocket>
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

            var dhtPortExt = new DHTPortExtension();
            dhtPortExt.Port += DhtPortExt_Port;

            client.RegisterBTExtension(fastExt);
            client.RegisterBTExtension(dhtPortExt);

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

            var peer = new IPEndPoint(IPAddress.Parse("192.168.0.42"), 6881);
            //foreach (var peer in knownPeers)
            {
                try
                {
                    Console.WriteLine("< Attempting to connect to {0}:{1}", peer.Address.ToString(), peer.Port);
                    client.Connect(peer);
                    connectedDateTime = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to connect: {0}", ex.Message);
                    //continue;
                }

                Console.WriteLine("< Connected");
                client.Handshake(downloadMetadata.HashString, peerId);
                Thread.Sleep(200);
                int x = 0, i=0;
                while (client.Process())
                {
                    if(connectedDateTime < DateTime.UtcNow.AddSeconds(-30) && client.ReceivedHandshake == false)
                    {
                        Console.WriteLine("< Disconnecting");
                        client.Disconnect();
                        continue;
                    }

                    if (choked == false && inflightPieces < 10)
                    {
                        if (PieceQueue.Count > 0)
                        {
                            while (PieceQueue.Count > 0)
                            {
                                var piece = PieceQueue.Dequeue();

                                RequestPieceInParts(client, piece, maxRequest, downloadMetadata.PieceSize);
                            }
                        }
                        else
                        {

                            RequestPieceInParts(client, currentPiece, maxRequest, downloadMetadata.PieceSize);
                            currentPiece++;
                        }
                    }
                    else
                    {

                    }

                    if(x++ > 1000)
                    {
                        x = 0;
                        client.SendKeepAlive();
                        Console.WriteLine("< KeepAlive");
                    }

                    Thread.Sleep(1);
                }
            }
        }

        private static void DhtPortExt_Port(IPeerWireClient pwc, ushort port)
        {
            Console.WriteLine($"> DHT Port: {port}");
        }

        private static void Lpd_NewPeer(IPAddress address, int port, string infoHash)
        {
            Console.WriteLine("Found new local peer");
        }

        private static void FastExt_SuggestPiece(IPeerWireClient pwc, int index)
        {
            Console.WriteLine($"> SuggestPiece: {index}");
        }

        private static void FastExt_HaveNone(IPeerWireClient pwc)
        {
            Console.WriteLine("> HaveNone");
        }

        private static void FastExt_HaveAll(IPeerWireClient pwc)
        {
            Console.WriteLine("> HaveAll");
            pwc.SendInterested();
        }

        private static void FastExt_AllowedFast(IPeerWireClient pwc, int index)
        {
            Console.WriteLine($"> AllowedFast: {index}");
            PieceQueue.Enqueue(index);
        }

        private static void Client_Request(IPeerWireClient pwc, int arg2, int arg3, int arg4)
        {
            Console.WriteLine("> Request");
        }

        private static void Client_NotInterested(IPeerWireClient pwc)
        {
            Console.WriteLine("> NotInterested");
        }

        private static void Client_Interested(IPeerWireClient pwc)
        {
            Console.WriteLine("> Interested");
        }

        private static void Client_Have(IPeerWireClient pwc, int index)
        {
            Console.WriteLine($"> Have {index}");
            PieceQueue.Enqueue(index);
        }

        private static void Client_HandshakeComplete(IPeerWireClient pwc)
        {
            Console.WriteLine("> HandshakeComplete");
        }

        private static void Client_DroppedConnection(IPeerWireClient pwc)
        {
            Console.WriteLine("> DroppedConnection");
        }

        private static void Client_UnChoke(IPeerWireClient pwc)
        {
            Console.WriteLine("> UnChoke");
            choked = false;
        }

        private static void Client_Choke(IPeerWireClient pwc)
        {
            Console.WriteLine("> Choke");
            choked = true;
        }

        private static void Client_Piece(IPeerWireClient pwc, int index, int start, byte[] buffer)
        {
            Console.WriteLine($"> Piece: {index} # {start}");
            inflightPieces--;
            if(inflightPieces < 0)
            {
                inflightPieces = 0;
            }

        }

        private static void Client_Cancel(IPeerWireClient pwc, int arg2, int arg3, int arg4)
        {
            Console.WriteLine("> Cancel");

        }

        private static void Client_BitField(IPeerWireClient pwc, int bitFieldLength, bool[] bitField)
        {
            Console.WriteLine("> BitField");

        }

        private static void Client_NoData(IPeerWireClient pwc)
        {
            Console.WriteLine("> NoData");
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