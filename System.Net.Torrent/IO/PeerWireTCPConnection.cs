using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net.Torrent.Data;
using System.Net.Torrent.Helpers;
using System.Text;

namespace System.Net.Torrent.IO
{
	public class PeerWireTCPConnection : IPeerConnection
	{
		private readonly Socket socket;
		private bool receiving = false;
		private byte[] currentPacketBuffer = null;
		private const int socketBufferSize = 16 * 1024;
		private readonly byte[] socketBuffer = new byte[socketBufferSize];
		private readonly Queue<PeerWirePacket> receiveQueue = new();
		private readonly Queue<PeerWirePacket> sendQueue = new();
		private PeerClientHandshake incomingHandshake = null;

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
			get => socket.Connected && incomingHandshake != null;
		}

		public PeerClientHandshake RemoteHandshake { get => incomingHandshake; }

		public PeerWireTCPConnection()
		{
			socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			socket.NoDelay = true;
		}

		public PeerWireTCPConnection(Socket socket)
		{
			this.socket = socket;

			if (socket.AddressFamily != AddressFamily.InterNetwork)
			{
				throw new ArgumentException("AddressFamily of socket must be InterNetwork");
			}

			if (socket.SocketType != SocketType.Stream)
			{
				throw new ArgumentException("SocketType of socket must be Stream");
			}

			if (socket.ProtocolType != ProtocolType.Tcp)
			{
				throw new ArgumentException("ProtocolType of socket must be Tcp");
			}

			socket.NoDelay = true;
		}

		public void Connect(IPEndPoint endPoint)
		{
			socket.Connect(endPoint);
		}

		public void Disconnect()
		{
			if (socket.Connected)
			{
				socket.Disconnect(true);
			}
		}

		public bool Process()
		{
			if (receiving == false)
			{
				receiving = true;
				socket.BeginReceive(socketBuffer, 0, socketBufferSize, SocketFlags.None, ReceiveCallback, this);
			}

			foreach (var packet in sendQueue)
			{
				
			}

			return Connected;
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
			socket.Send(sendBuf);
		}

		private void ReceiveCallback(IAsyncResult asyncResult)
		{
			var dataLength = socket.EndReceive(asyncResult);

			if(incomingHandshake == null)
			{
				if (dataLength == 0)
				{
					receiving = false;
					return;
				}

				var protocolStrLen = socketBuffer[0];
				var protocolStrBytes = socketBuffer.GetBytes(1, protocolStrLen);
				var reservedBytes = socketBuffer.GetBytes(1 + protocolStrLen, 8);
				var infoHashBytes = socketBuffer.GetBytes(1 + protocolStrLen + 8, 20);
				var peerIdBytes = socketBuffer.GetBytes(1 + protocolStrLen + 28, 20);

				var protocolStr = Encoding.ASCII.GetString(protocolStrBytes);

				if(protocolStr != "BitTorrent protocol")
				{
					//throw exception
				}

				incomingHandshake = new PeerClientHandshake
				{
					ReservedBytes = reservedBytes,
					ProtocolHeader = protocolStr,
					InfoHash = UnpackHelper.Hex(infoHashBytes),
					PeerId = Encoding.ASCII.GetString(peerIdBytes)
				};

				receiving = false;
				return;
			}

			if (currentPacketBuffer == null)
			{
				currentPacketBuffer = new byte[0];
			}

			currentPacketBuffer = currentPacketBuffer.Cat(socketBuffer.GetBytes(0, dataLength));

			var advanceBytes = ParsePackets(currentPacketBuffer);

			currentPacketBuffer = currentPacketBuffer.GetBytes(advanceBytes);

			receiving = false;
		}

		private int ParsePackets(byte[] currentPacketBuffer)
		{
			var parsedBytes = 0;
			PeerWirePacket packet;

			do
			{
				packet = ParsePacket(currentPacketBuffer);

				parsedBytes += packet.PacketByteLength;

				receiveQueue.Enqueue(packet);
			} while (packet != null);

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
	}
}
