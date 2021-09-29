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

namespace bzTorrent.IO
{
	public class PeerWireuTPConnection : IPeerConnection
	{
		private readonly Socket socket;
		private bool receiving = false;
		private byte[] currentPacketBuffer = null;
		private const int socketBufferSize = 16 * 1024;
		private readonly byte[] socketBuffer = new byte[socketBufferSize];
		private readonly Queue<PeerWirePacket> receiveQueue = new();
		private readonly Queue<PeerWirePacket> sendQueue = new();
		private PeerClientHandshake incomingHandshake = null;
		private ushort SeqNumber = 0;
		private ushort AckNumber = 0;
		private uint MaxWindow = socketBufferSize;
		private ushort ConnectionIdLocal;
		private ushort ConnectionIdRemote => (ushort)(ConnectionIdLocal + 1);
		private IPEndPoint remoteEndPoint;
		private bool isConnected = false;
		private uint LastTimestampReceived;
		private uint LastTimestampReceivedDiff;

		private Random rng = new();

		public enum PacketType : byte
		{
			STData = 0,
			STFin = 1,
			STState = 2,
			STReset = 3,
			STSyn = 4
		}

		public int Timeout
		{
			get => socket.ReceiveTimeout / 1000;
			set {
				socket.ReceiveTimeout = value * 1000;
				socket.SendTimeout = value * 1000;
			}
		}

		public bool Connected
		{
			get => isConnected;
		}

		public PeerClientHandshake RemoteHandshake { get => incomingHandshake; }

		public PeerWireuTPConnection()
		{
			socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
		}

		public PeerWireuTPConnection(Socket socket)
		{
			this.socket = socket;

			if (socket.AddressFamily != AddressFamily.InterNetwork)
			{
				throw new ArgumentException("AddressFamily of socket must be InterNetwork");
			}

			if (socket.SocketType != SocketType.Dgram)
			{
				throw new ArgumentException("SocketType of socket must be StDgramream");
			}

			if (socket.ProtocolType != ProtocolType.Udp)
			{
				throw new ArgumentException("ProtocolType of socket must be Udp");
			}
		}

		public void Connect(IPEndPoint endPoint)
		{
			ConnectionIdLocal = (ushort)rng.Next(0, ushort.MaxValue);

			remoteEndPoint = endPoint;
			socket.Connect(endPoint);

			SenduTPData(PacketType.STSyn, null);

			Process();
		}

		public void Disconnect()
		{
			if (socket.Connected)
			{
				socket.Disconnect(true);
			}
		}

		public void Listen(EndPoint ep)
		{

		}

		public IPeerConnection Accept()
		{
			return new PeerWireTCPConnection(socket.Accept());
		}

		public IAsyncResult BeginAccept(AsyncCallback callback)
		{
			return socket.BeginAccept(callback,  null);
		}

		public Socket EndAccept(IAsyncResult ar)
		{
			return socket.EndAccept(ar);
		}

		public bool Process()
		{
			if (receiving == false)
			{
				receiving = true;
				socket.BeginReceive(socketBuffer, 0, socketBufferSize, SocketFlags.None, ReceiveCallback, this);
			}

			while (sendQueue.TryDequeue(out var packet))
			{
				SendData(packet);
			}

			return true;
		}

		public void Send(PeerWirePacket packet)
		{
			sendQueue.Enqueue(packet);
		}

		public PeerWirePacket Receive()
		{
			if (receiveQueue.TryDequeue(out var packet))
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

			SenduTPData(PacketType.STData, sendBuf);
		}

		private void ReceiveCallback(IAsyncResult asyncResult)
		{
			var timestampRecvd = TimestampMicro();

			var dataLength = socket.EndReceive(asyncResult);
			var socketBufferCopy = socketBuffer.GetBytes(0, dataLength);

			if(dataLength < 20)
			{
				//error
			}

			var verRecvd = (byte)(socketBufferCopy[0] & 0x0F);
			var typeRecvd = (PacketType)((socketBufferCopy[0] & 0xF0) >> 4);
			var connectionIdRecvd = UnpackHelper.UInt16(socketBufferCopy, 2, UnpackHelper.Endianness.Big);
			LastTimestampReceived = UnpackHelper.UInt32(socketBufferCopy, 4, UnpackHelper.Endianness.Big);
			var timestampDiffRecvd = UnpackHelper.UInt32(socketBufferCopy, 8, UnpackHelper.Endianness.Big);
			var wndSizeRecvd = UnpackHelper.UInt32(socketBufferCopy, 12, UnpackHelper.Endianness.Big);
			var seqNumberRecvd = UnpackHelper.UInt16(socketBufferCopy, 16, UnpackHelper.Endianness.Big);
			var ackNumberRecvd = UnpackHelper.UInt16(socketBufferCopy, 18, UnpackHelper.Endianness.Big);

			Console.WriteLine("{0}#{1}", typeRecvd.ToString(), seqNumberRecvd);

			LastTimestampReceivedDiff = LastTimestampReceived - timestampRecvd;

			if (isConnected == false)
			{
				//syn sent but not received status yet
				if(typeRecvd == PacketType.STState && connectionIdRecvd == ConnectionIdLocal)
				{
					isConnected = true;
					AckNumber = (ushort)(seqNumberRecvd - 1);
					receiving = false;
					return;
				}
			}

			AckNumber = seqNumberRecvd;
			if (typeRecvd != PacketType.STState)
			{
				//send ack
				SendAck();
			}
			else if (typeRecvd == PacketType.STFin)
			{
				isConnected = false;
			}

			dataLength -= 20;
			socketBufferCopy = socketBufferCopy.GetBytes(20, dataLength);

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

				if(protocolStr != "BitTorrent protocol")
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

				dataLength -= (protocolStrLen + 49);
				socketBufferCopy = socketBufferCopy.GetBytes(protocolStrLen + 49, dataLength);
			}

			if (currentPacketBuffer == null)
			{
				currentPacketBuffer = new byte[0];
			}

			currentPacketBuffer = currentPacketBuffer.Cat(socketBufferCopy.GetBytes(0, dataLength));

			ParsePackets(currentPacketBuffer);

			receiving = false;
		}

		private void SendData(PeerWirePacket packet)
		{
			SenduTPData(PacketType.STData, packet.GetBytes());
		}

		private void SendAck()
		{
			SenduTPData(PacketType.STState, null);
		}

		private void SenduTPData(PacketType packetType, byte[] data)
		{
			if (packetType != PacketType.STState)
			{
				SeqNumber++;
			}

			var sendData = data != null ? data : new byte[0];

			var connectionId = packetType == PacketType.STSyn ? ConnectionIdLocal : ConnectionIdRemote;
			var typeAndVersion = new byte[] { (byte)(((byte)packetType << 4) | 1) };
			var extension = new byte[] { 0 };
			var timestamp = TimestampMicro();

			var header = typeAndVersion.Cat(extension).Cat(PackHelper.UInt16(connectionId))
				.Cat(PackHelper.UInt32(timestamp))
				.Cat(PackHelper.UInt32(LastTimestampReceivedDiff))
				.Cat(PackHelper.UInt32(MaxWindow))
				.Cat(PackHelper.UInt16(SeqNumber)).Cat(PackHelper.UInt16(AckNumber));

			socket.SendTo(header.Cat(sendData), remoteEndPoint);
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
				Console.WriteLine(newPacket.Command.ToString());

				return newPacket;
			}

			return null;
		}

		private uint TimestampMicro()
		{
			return (uint)(DateTime.UtcNow.Ticks / (TimeSpan.TicksPerMillisecond / 1000));
		}
	}
}
