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

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using bzTorrent.IO;

namespace bzTorrent
{
	public class LocalPeerDiscovery<T> : IDisposable, ILocalPeerDiscovery<T> where T : ISocket, new()
	{
		private const string lpdMulticastAddress = "239.192.152.143";
		private const int lpdMulticastPort = 6771;

		//TODO: replace with ISocket
		private readonly ISocket udpReaderSocket;
		private readonly ISocket udpSenderSocket;

		private Thread thread;
		private bool _killSwitch;

		public delegate void NewPeerCB(IPAddress address, int port, string infoHash);
		public event NewPeerCB NewPeer;

		private int ttl = 8;

		public int TTL
		{
			get => ttl;
			set {
				ttl = value;
				udpSenderSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, ttl);
			}
		}

		public LocalPeerDiscovery()
		{
			udpReaderSocket = new T();
			udpSenderSocket = new T();
		}

		public LocalPeerDiscovery(ISocket receive, ISocket send)
		{
			udpReaderSocket = receive;
			udpSenderSocket = send;
		}

		public void Open()
		{
			var address = IPAddress.Parse(lpdMulticastAddress);
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
			var isReceiving = false;

			var buffer = new byte[16*1024];
			while (!_killSwitch)
			{
				if (!isReceiving)
				{
					isReceiving = true;

					EndPoint endPoint = new IPEndPoint(0, 0);
					udpReaderSocket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref endPoint, (cb) => {
						EndPoint internalRemoteEP = new IPEndPoint(0, 0);

						var length = udpReaderSocket.EndReceiveFrom(cb, ref internalRemoteEP);

						if (length <= 0)
						{
							return;
						}

						var remoteAddress = ((IPEndPoint)endPoint).Address;
						var remotePort = 0;
						var remoteHash = "";

						var packet = Encoding.ASCII.GetString(buffer).Trim();

						if (!packet.StartsWith("BT-SEARCH"))
						{
							return;
						}

						var packetLines = packet.Split('\n');

						foreach (var line in packetLines)
						{
							if (line.StartsWith("Port:"))
							{
								var portStr = line.Substring(5).Trim();
								int.TryParse(portStr, out remotePort);
							}
							if (line.StartsWith("Infohash:"))
							{
								remoteHash = line.Substring(10, 40);
							}
						}

						if (!string.IsNullOrEmpty(remoteHash) && remotePort != 0)
						{
							NewPeer?.Invoke(remoteAddress, remotePort, remoteHash);
						}

						isReceiving = false;
					}, null);
				}
			}
		}

		public void Announce(int listeningPort, string infoHash)
		{
			var message = string.Format("BT-SEARCH * HTTP/1.1\r\n" +
										   "Host: {2}:{3}\r\n" +
										   "Port: {0}\r\n" +
										   "Infohash: {1}\r\n" +
										   "\r\n\r\n",
										   listeningPort,
										   infoHash,
										   lpdMulticastAddress,
										   lpdMulticastPort);

			var buffer = Encoding.ASCII.GetBytes(message);

			udpSenderSocket.Send(buffer);
		}

		private bool isDisposed;
		public void Dispose()
		{
			GC.SuppressFinalize(this);

			if (isDisposed)
			{
				return;
			}

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
