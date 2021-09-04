/*
Copyright (c) 2013, Darren Horrocks
All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice, this
  list of conditions and the following disclaimer in the documentation and/or
  other materials provided with the distribution.

* Neither the name of Darren Horrocks nor the names of its
  contributors may be used to endorse or promote products derived from
  this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE. 
*/

namespace System.Net.Torrent
{
    using System.Diagnostics;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;

    public class LocalPeerDiscovery : IDisposable, ILocalPeerDiscovery
    {
        private const string lpdMulticastAddress = "239.192.152.143";
        private const int lpdMulticastPort = 6771;

        private readonly Socket udpReaderSocket;
        private readonly Socket udpSenderSocket;

        private Thread thread;
        private bool _killSwitch;

        public delegate void NewPeerCB(IPAddress address, int port, string infoHash);
        public event NewPeerCB NewPeer;

        private int ttl = 8;

        public int TTL
        {
            get => this.ttl;
            set
            {
                this.ttl = value;

                this.udpSenderSocket.SetSocketOption(SocketOptionLevel.IP,
                SocketOptionName.MulticastTimeToLive,
                this.ttl);
            }
        }

        public LocalPeerDiscovery()
        {
            this.udpReaderSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            this.udpSenderSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        }

        public LocalPeerDiscovery(Socket receive, Socket send)
        {
            this.ValidateSocket(receive, "receive");
            this.ValidateSocket(send, "send");

            this.udpReaderSocket = receive;
            this.udpSenderSocket = send;
        }

        [DebuggerHidden, DebuggerStepThrough]
        private void ValidateSocket(Socket socket, string name)
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
            this.SetupReaderSocket(address, lpdMulticastPort);
            this.SetupSenderSocket(address, lpdMulticastPort);

            this.thread = new Thread(this.Process);
            this.thread.Start();
        }

        private void SetupReaderSocket(IPAddress address, int port)
        {
            var endPoint = new IPEndPoint(IPAddress.Any, port);

            this.udpReaderSocket.ExclusiveAddressUse = false;
            this.udpReaderSocket.SetSocketOption(SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress,
                true);

            this.udpReaderSocket.Bind(endPoint);

            this.udpReaderSocket.SetSocketOption(SocketOptionLevel.IP,
                SocketOptionName.AddMembership,
                new MulticastOption(address, IPAddress.Any));
        }

        private void SetupSenderSocket(IPAddress address, int port)
        {
            var endPoint = new IPEndPoint(address, port);

            this.udpSenderSocket.ExclusiveAddressUse = false;
            this.udpSenderSocket.SetSocketOption(SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress,
                true);
            this.udpSenderSocket.SetSocketOption(SocketOptionLevel.IP, 
                SocketOptionName.AddMembership, 
                new MulticastOption(address));
            this.udpSenderSocket.SetSocketOption(SocketOptionLevel.IP, 
                SocketOptionName.MulticastTimeToLive,
                this.TTL);

            this.udpSenderSocket.Connect(endPoint);
        }

        public void Close()
        {
            this._killSwitch = true;
            this.thread.Abort();

            this.udpReaderSocket.Close();
            this.udpSenderSocket.Close();
        }

        private void Process()
        {
            byte[] buffer = new byte[200];
            while (!this._killSwitch)
            {
                EndPoint endPoint = new IPEndPoint(0,0);
                this.udpReaderSocket.ReceiveFrom(buffer, ref endPoint);

                IPAddress remoteAddress = ((IPEndPoint) endPoint).Address;
                int remotePort = 0;
                string remoteHash = "";

                string packet = Encoding.ASCII.GetString(buffer).Trim();

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

        public void Announce(int listeningPort, string infoHash)
        {
            string message = String.Format("BT-SEARCH * HTTP/1.1\r\n" +
                                           "Host: {2}:{3}\r\n" +
                                           "Port: {0}\r\n" +
                                           "Infohash: {1}\r\n" +
                                           "\r\n\r\n", 
                                           listeningPort, 
                                           infoHash,
                                           lpdMulticastAddress,
                                           lpdMulticastPort);

            byte[] buffer = Encoding.ASCII.GetBytes(message);

            this.udpSenderSocket.Send(buffer);
        }

        private bool isDisposed;
        public void Dispose()
        {
            if (!this.isDisposed)
            {
                this.isDisposed = true;

                try
                {
                    this.Close();
                    this.udpReaderSocket.Dispose();
                    this.udpSenderSocket.Dispose();
                }
                catch (Exception)
                {

                }
            }
        }
    }
}
