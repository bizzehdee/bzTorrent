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

			// Background so a running LPD listener never keeps the process alive after the app
			// asks to exit (the receive loop can be parked in a blocking socket read that Stop's
			// bounded Join won't unstick).
			thread = new Thread(Process) { IsBackground = true };
			thread.Start();
		}

		// Both the reader and sender sockets share the multicast group, so both must allow the
		// address to be reused (port sharing) rather than claimed exclusively.
		private static void EnableAddressReuse(ISocket socket)
		{
			socket.ExclusiveAddressUse = false;
			socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
		}

		private void SetupReaderSocket(IPAddress address, int port)
		{
			var endPoint = new IPEndPoint(IPAddress.Any, port);

			EnableAddressReuse(udpReaderSocket);

			udpReaderSocket.Bind(endPoint);

			udpReaderSocket.SetSocketOption(SocketOptionLevel.IP,
				SocketOptionName.AddMembership,
				new MulticastOption(address, IPAddress.Any));
		}

		private void SetupSenderSocket(IPAddress address, int port)
		{
			var endPoint = new IPEndPoint(address, port);

			EnableAddressReuse(udpSenderSocket);
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
			// Cooperative shutdown: the Process loop polls _killSwitch. Thread.Abort
			// is obsolete and throws PlatformNotSupportedException on modern .NET.
			_killSwitch = true;

			udpReaderSocket.Close();
			udpSenderSocket.Close();

			thread?.Join(TimeSpan.FromSeconds(1));
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

					try
					{
						EndPoint endPoint = new IPEndPoint(0, 0);
						udpReaderSocket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref endPoint, (cb) =>
						{
							// This callback runs on a thread-pool thread, outside Close()/Dispose()'s
							// own try/catch — Close() closing the socket while a receive is pending
							// makes EndReceiveFrom throw (ObjectDisposedException or SocketException),
							// and an unhandled exception on a thread-pool thread crashes the whole
							// process, not just this component. Must always be swallowed here.
							try
							{
								EndPoint internalRemoteEP = new IPEndPoint(0, 0);

								var length = udpReaderSocket.EndReceiveFrom(cb, ref internalRemoteEP);

								if (length <= 0)
								{
									return;
								}

								var remoteAddress = ((IPEndPoint)internalRemoteEP).Address;

								// Decode only the bytes actually received, not the whole 16 KB buffer.
								var packet = Encoding.ASCII.GetString(buffer, 0, length).Trim();

								if (TryParseSearchPacket(packet, out var remotePort, out var remoteHash))
								{
									NewPeer?.Invoke(remoteAddress, remotePort, remoteHash);
								}
							}
							catch (ObjectDisposedException)
							{
								// Socket was closed while this receive was in flight — normal during shutdown.
							}
							catch (SocketException)
							{
								// Receive aborted (e.g. socket closed during shutdown) — normal during shutdown.
							}
							finally
							{
								isReceiving = false;
							}
						}, null);
					}
					catch (ObjectDisposedException)
					{
						// BeginReceiveFrom itself can throw synchronously, on this loop's own
						// thread, if Close() disposed the socket concurrently between the
						// while-condition check above and this call — not just inside the async
						// callback above. _killSwitch is set right before Close() does that, so
						// the loop condition will pick it up on the next iteration; nothing else
						// to do here but stop trying to receive on this pass.
						return;
					}
					catch (SocketException)
					{
						return;
					}
				}
			}
		}

		// Parses an LPD BT-SEARCH announce. Returns true only when both a non-zero port and a
		// 40-char infohash were found. Extracted from the receive callback so it's a pure,
		// testable function — and guards the infohash length (the old inline Substring(10, 40)
		// threw on a short/malformed line).
		internal static bool TryParseSearchPacket(string packet, out int port, out string infoHash)
		{
			port = 0;
			infoHash = null;

			if (string.IsNullOrEmpty(packet) || !packet.StartsWith("BT-SEARCH"))
			{
				return false;
			}

			const string portPrefix = "Port:";
			const string infoHashPrefix = "Infohash:";
			const int infoHashLength = 40;

			foreach (var line in packet.Split('\n'))
			{
				if (line.StartsWith(portPrefix))
				{
					int.TryParse(line.Substring(portPrefix.Length).Trim(), out port);
				}
				else if (line.StartsWith(infoHashPrefix))
				{
					var value = line.Substring(infoHashPrefix.Length).Trim();
					if (value.Length >= infoHashLength)
					{
						infoHash = value.Substring(0, infoHashLength);
					}
				}
			}

			return !string.IsNullOrEmpty(infoHash) && port != 0;
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
