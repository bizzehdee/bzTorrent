using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace System.Net.Torrent
{
    public class LocalPeerDiscovery : IDisposable, ILocalPeerDiscovery
    {
        private const string lpdMulticastAddress = "239.192.152.143";
        private const int lpdMulticastPort = 6771;

        private readonly Socket udpReaderSocket;
        private readonly Socket udpSenderSocket;

        private Thread thread;
        private bool _killSwitch;

        public delegate void NewPeerCB(IPAddress address, int port, String infoHash);
        public event NewPeerCB NewPeer;

        private int ttl = 8;

        public int TTL
        {
            get { return ttl; }
            set
            {
                ttl = value;

                udpSenderSocket.SetSocketOption(SocketOptionLevel.IP,
                SocketOptionName.MulticastTimeToLive,
                ttl);
            }
        }

        public LocalPeerDiscovery()
        {
            udpReaderSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udpSenderSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        }

        public LocalPeerDiscovery(Socket receive, Socket send)
        {
            ValidateSocket(receive, "receive");
            ValidateSocket(send, "send");

            udpReaderSocket = receive;
            udpSenderSocket = send;
        }

        [DebuggerHidden, DebuggerStepThrough]
        private void ValidateSocket(Socket socket, String name)
        {
            if (socket == null)
            {
                throw new ArgumentNullException(name);
            }

            if (socket.ProtocolType != ProtocolType.Udp)
            {
                throw new ArgumentException("socket must be a UDP socket", name);
            }

            if (socket.SocketType != SocketType.Dgram)
            {
                throw new ArgumentException("socket must be a datagram socket", name);
            }
        }

        public void Open()
        {
            IPAddress address = IPAddress.Parse(lpdMulticastAddress);
            SetupReaderSocket(address, lpdMulticastPort);
            SetupSenderSocket(address, lpdMulticastPort);

            thread = new Thread(Process);
            thread.Start();
        }

        private void SetupReaderSocket(IPAddress address, int port)
        {
            var endPoint = new IPEndPoint(IPAddress.Any, port);

            udpReaderSocket.ExclusiveAddressUse = false;
            udpReaderSocket.SetSocketOption(SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress,
                true);

            udpReaderSocket.Bind(endPoint);

            udpReaderSocket.SetSocketOption(SocketOptionLevel.IP,
                SocketOptionName.AddMembership,
                new MulticastOption(address, IPAddress.Any));
        }

        private void SetupSenderSocket(IPAddress address, int port)
        {
            var endPoint = new IPEndPoint(address, port);

            udpSenderSocket.ExclusiveAddressUse = false;
            udpSenderSocket.SetSocketOption(SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress,
                true);
            udpSenderSocket.SetSocketOption(SocketOptionLevel.IP, 
                SocketOptionName.AddMembership, 
                new MulticastOption(address));
            udpSenderSocket.SetSocketOption(SocketOptionLevel.IP, 
                SocketOptionName.MulticastTimeToLive, 
                TTL);

            udpSenderSocket.Connect(endPoint);
        }

        public void Close()
        {
            _killSwitch = true;
            thread.Abort();

            udpReaderSocket.Close();
            udpSenderSocket.Close();
        }

        private void Process()
        {
            byte[] buffer = new byte[200];
            while (!_killSwitch)
            {
                EndPoint endPoint = new IPEndPoint(0,0);
                udpReaderSocket.ReceiveFrom(buffer, ref endPoint);

                IPAddress remoteAddress = ((IPEndPoint) endPoint).Address;
                int remotePort = 0;
                string remoteHash = "";

                String packet = Encoding.ASCII.GetString(buffer).Trim();

                if (!packet.StartsWith("BT-SEARCH"))
                {
                    continue;
                }

                String[] packetLines = packet.Split('\n');

                foreach (string line in packetLines)
                {
                    if (line.StartsWith("Port:"))
                    {
                        string portStr = line.Substring(5).Trim();
                        int.TryParse(portStr, out remotePort);
                    }
                    if (line.StartsWith("Infohash:"))
                    {
                        remoteHash = line.Substring(10, 40);
                    }
                }

                if (!String.IsNullOrEmpty(remoteHash) && remotePort != 0)
                {
                    if (NewPeer != null)
                    {
                        NewPeer(remoteAddress, remotePort, remoteHash);
                    }
                }
            }
        }

        public void Announce(int listeningPort, String infoHash)
        {
            String message = String.Format("BT-SEARCH * HTTP/1.1\r\n" +
                                           "Host: {2}:{3}\r\n" +
                                           "Port: {0}\r\n" +
                                           "Infohash: {1}\r\n" +
                                           "\r\n\r\n", 
                                           listeningPort, 
                                           infoHash,
                                           lpdMulticastAddress,
                                           lpdMulticastPort);

            byte[] buffer = Encoding.ASCII.GetBytes(message);

            udpSenderSocket.Send(buffer);
        }

        private bool isDisposed;
        public void Dispose()
        {
            if (!isDisposed)
            {
                isDisposed = true;

                try
                {
                    Close();
                    udpReaderSocket.Dispose();
                    udpSenderSocket.Dispose();
                }
                catch (Exception)
                {

                }
            }
        }
    }
}
