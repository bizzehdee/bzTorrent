/*
Copyright (c) 2021, Darren Horrocks
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

using System.Collections.Generic;
using System.Net.Sockets;
using bzTorrent.Data;
using bzTorrent.Helpers;
using System.Text;
using System;
using System.Net;
using System.Collections.Concurrent;

namespace bzTorrent.IO
{
	public class PeerWireConnection<T> : IPeerConnection where T : ISocket, new()
	{
		private ISocket socket;
		private bool receiving = false;
		private byte[] currentPacketBuffer = null;
		private const int socketBufferSize = 16 * 1024;
		private readonly byte[] socketBuffer = new byte[socketBufferSize];
		private readonly ConcurrentQueue<PeerWirePacket> receiveQueue = new();
		private readonly ConcurrentQueue<PeerWirePacket> sendQueue = new();
		private PeerClientHandshake incomingHandshake = null;

		public int Timeout
		{
			get;
			set;
		}

		public bool Connected
		{
			get => socket.Connected && incomingHandshake != null;
		}

		public PeerClientHandshake RemoteHandshake { get => incomingHandshake; }

		public PeerWireConnection()
		{

		}

		public PeerWireConnection(ISocket _socket)
		{
			socket = _socket;
			socket.ReceiveTimeout = Timeout * 1000;
			socket.SendTimeout = Timeout * 1000;

			socket.NoDelay = true;
		}

		public void Connect(IPEndPoint endPoint)
		{
			socket = new T();
			socket.NoDelay = true;
			socket.ReceiveTimeout = Timeout * 1000;
			socket.SendTimeout = Timeout * 1000;

			incomingHandshake = null;

			socket.Connect(endPoint);
		}

		public void Disconnect()
		{
			//if (socket.Connected)
			{
				socket.Disconnect(true);
				socket = null;
				receiving = false;
			}
		}

		public void Listen(EndPoint ep)
		{
			socket.Bind(ep);
			socket.Listen(10);
		}

		public IPeerConnection Accept()
		{
			return new PeerWireConnection<T>(socket.Accept());
		}

		public IAsyncResult BeginAccept(AsyncCallback callback)
		{
			return socket.BeginAccept(callback, null);
		}

		public ISocket EndAccept(IAsyncResult ar)
		{
			return socket.EndAccept(ar);
		}

		public bool Process()
		{
			if (receiving == false)
			{
				receiving = true;
				Array.Clear(socketBuffer, 0, socketBufferSize);
				socket.BeginReceive(socketBuffer, 0, socketBufferSize, SocketFlags.None, ReceiveCallback, this);
			}

			while (sendQueue.Count > 0)
			{
				if (sendQueue.TryDequeue(out var packet)) 
				{
					socket.Send(packet.GetBytes());
				}
			}

			return Connected;
		}

		public void Send(PeerWirePacket packet)
		{
			sendQueue.Enqueue(packet);
		}

		public PeerWirePacket Receive()
		{
			if (receiveQueue.Count > 0 && receiveQueue.TryDequeue(out var packet))
			{
				return packet;
			}

			return null;
		}

		public void Handshake(PeerClientHandshake handshake)
		{
			var infoHashBytes = PackHelper.Hex(handshake.InfoHash);
			var protocolHeaderBytes = Encoding.ASCII.GetBytes(handshake.ProtocolHeader);
			var peerIdBytes = Encoding.ASCII.GetBytes(handshake.PeerId);

			var sendBuf = (new byte[] { (byte)protocolHeaderBytes.Length }).Cat(protocolHeaderBytes).Cat(handshake.ReservedBytes).Cat(infoHashBytes).Cat(peerIdBytes);
			socket.Send(sendBuf);
		}

		private void ReceiveCallback(IAsyncResult asyncResult)
		{
			var dataLength = socket.EndReceive(asyncResult);

			var socketBufferCopy = socketBuffer.GetBytes(0, dataLength);

			if (incomingHandshake == null)
			{
				if (dataLength == 0)
				{
					receiving = false;
					return;
				}

				var protocolStrLen = socketBufferCopy[0];
				var protocolStrBytes = socketBufferCopy.GetBytes(1, protocolStrLen);
				var reservedBytes = socketBufferCopy.GetBytes(1 + protocolStrLen, 8);
				var infoHashBytes = socketBufferCopy.GetBytes(1 + protocolStrLen + 8, 20);
				var peerIdBytes = socketBufferCopy.GetBytes(1 + protocolStrLen + 28, 20);

				var protocolStr = Encoding.ASCII.GetString(protocolStrBytes);

				if (protocolStr != "BitTorrent protocol")
				{
					throw new Exception(string.Format("Unsupported protocol: '{0}'", protocolStr));
				}

				incomingHandshake = new PeerClientHandshake
				{
					ReservedBytes = reservedBytes,
					ProtocolHeader = protocolStr,
					InfoHash = UnpackHelper.Hex(infoHashBytes),
					PeerId = Encoding.ASCII.GetString(peerIdBytes)
				};

				socketBufferCopy = socketBufferCopy.GetBytes(protocolStrLen + 49);
				dataLength -= (protocolStrLen + 49);
			}

			if (currentPacketBuffer == null)
			{
				currentPacketBuffer = Array.Empty<byte>();
			}

			currentPacketBuffer = currentPacketBuffer.Cat(socketBufferCopy.GetBytes(0, dataLength));

			if (dataLength > 0)
			{
				var parsedBytes = ParsePackets(currentPacketBuffer);

				currentPacketBuffer = currentPacketBuffer.GetBytes((int)parsedBytes);
			}

			receiving = false;
		}

		private uint ParsePackets(byte[] currentPacketBuffer)
		{
			uint parsedBytes = 0;
			PeerWirePacket packet;

			do
			{
				packet = ParsePacket(currentPacketBuffer);

				if (packet != null)
				{
					parsedBytes += packet.PacketByteLength;
					currentPacketBuffer = currentPacketBuffer.GetBytes((int)packet.PacketByteLength);
					receiveQueue.Enqueue(packet);
				}
			} while (packet != null && currentPacketBuffer.Length > 0);

			return parsedBytes;
		}

		private PeerWirePacket ParsePacket(byte[] currentPacketBuffer)
		{
			var newPacket = new PeerWirePacket();

			if (newPacket.Parse(currentPacketBuffer))
			{
				return newPacket;
			}

			return null;
		}

		public bool HasPackets()
		{
			return receiveQueue.Count > 0;
		}
	}
}
