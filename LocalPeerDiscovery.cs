using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace System.Net.Torrent
{
	public class LocalPeerDiscovery
	{
		private const string lpdMulticastAddress = "239.192.152.143";
		private const int lpdMulticastPort = 6771;

		private readonly Socket udpReaderSocket;

		private Thread thread;
		private bool _killSwitch;

		public delegate void NewPeerCB(IPAddress address, int port, String infoHash);

		public event NewPeerCB NewPeer;

		public LocalPeerDiscovery()
		{
			udpReaderSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
		}

		public LocalPeerDiscovery(Socket socket)
		{
			if (socket == null)
			{
				throw new ArgumentNullException("socket");
			}

			if (socket.ProtocolType != ProtocolType.Udp)
			{
				throw new ArgumentException("socket must be a UDP socket", "socket");
			}

			if (socket.SocketType != SocketType.Dgram)
			{
				throw new ArgumentException("socket must be a datagram socket", "socket");
			}

			udpReaderSocket = socket;
		}

		public void Open()
		{
			udpReaderSocket.ExclusiveAddressUse = false;
			udpReaderSocket.SetSocketOption(SocketOptionLevel.Socket,
				SocketOptionName.ReuseAddress,
				true);

			IPAddress address = IPAddress.Parse(lpdMulticastAddress);

			var endPoint = new IPEndPoint(IPAddress.Any, lpdMulticastPort);
			udpReaderSocket.Bind(endPoint);

			udpReaderSocket.SetSocketOption(SocketOptionLevel.IP, 
				SocketOptionName.AddMembership, 
				new MulticastOption(address, IPAddress.Any));

			thread = new Thread(Process);
			thread.Start();
		}

		public void Close()
		{
			_killSwitch = true;
			thread.Abort();

			udpReaderSocket.Close();
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
	}
}
