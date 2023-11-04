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

using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Tasks;
using bzTorrent.Helpers;

namespace bzTorrent.IO
{
	public class UTPPacketHeader
	{
		public byte Version { get; private set; }
		public UTPSocket.PacketType PacketType { get; private set; }
		public ushort ConnectionIdRecvd { get; private set; }
		public uint TimestampRecvd { get; private set; }
		public uint TimestampDiffRecvd { get; private set; }
		public uint WndSizeRecvd { get; private set; }
		public ushort SeqNumberRecvd { get; private set; }
		public ushort AckNumberRecvd { get; private set; }

		public void Parse(byte[] buffer)
		{
			Version = (byte)(buffer[0] & 0x0F);
			PacketType = (UTPSocket.PacketType)((buffer[0] & 0xF0) >> 4);
			ConnectionIdRecvd = UnpackHelper.UInt16(buffer, 2, UnpackHelper.Endianness.Big);
			TimestampRecvd = UnpackHelper.UInt32(buffer, 4, UnpackHelper.Endianness.Big);
			TimestampDiffRecvd = UnpackHelper.UInt32(buffer, 8, UnpackHelper.Endianness.Big);
			WndSizeRecvd = UnpackHelper.UInt32(buffer, 12, UnpackHelper.Endianness.Big);
			SeqNumberRecvd = UnpackHelper.UInt16(buffer, 16, UnpackHelper.Endianness.Big);
			AckNumberRecvd = UnpackHelper.UInt16(buffer, 18, UnpackHelper.Endianness.Big);
		}
	}

	/// <summary>
	/// WARNING, This is an incomplete implementation, and is quite buggy, and needs a lot of work to fix the issues. use the TCPSocket for a standard BT connection for now
	/// </summary>
	public class UTPSocket : BaseSocket, ISocket
	{
		public enum PacketType : byte
		{
			STData = 0,
			STFin = 1,
			STState = 2,
			STReset = 3,
			STSyn = 4
		}

		private readonly Random rng = new();
		private const int socketBufferSize = 16 * 1024;
		private static readonly byte[] socketBuffer = new byte[socketBufferSize];

		private EndPoint endPoint;
		private bool isConnected = false;
		private bool isFullyConnected = false;
		private uint LastTimestampReceived;
		private uint LastTimestampReceivedDiff;
		private ushort SeqNumber = 0;
		private ushort AckNumber = 0;
		private ushort ConnectionIdLocal;
		private readonly uint MaxWindow = socketBufferSize;

		private ushort ConnectionIdRemote
		{
			get => (ushort)(ConnectionIdLocal + 1);
		}

		public override bool Connected { get => isConnected; }
		public override bool NoDelay { get; set; }


		public UTPSocket(Socket socket) : 
			base(socket)
		{

		}

		public UTPSocket() :
			base(new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
		{

		}

		public override void Connect(EndPoint remoteEP)
		{
			isConnected = true;
			endPoint = remoteEP;

			ConnectionIdLocal = (ushort)rng.Next(0, ushort.MaxValue);

			SenduTPData(PacketType.STSyn, null);
		}

		public override void Disconnect(bool reuseSocket)
		{
			SenduTPData(PacketType.STFin, null);
			isConnected = false;
		}

		public override void Listen(int backlog)
		{
			_socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.ReuseAddress, true);
			_socket.Bind(endPoint);
		}

		public override int Send(byte[] buffer)
		{
			return SenduTPData(PacketType.STData, buffer);
		}

		public override ISocket Accept()
		{
			try
			{
				return new UTPSocket(_socket.Accept());
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static uint TimestampMicro()
		{
			return (uint)(DateTime.UtcNow.Ticks / (TimeSpan.TicksPerMillisecond / 1000));
		}

		private int SenduTPData(PacketType packetType, byte[] data, bool expectState = true)
		{
			Console.WriteLine("PACKET SENT: {0}", packetType.ToString());

			if (packetType != PacketType.STState)
			{
				SeqNumber++;
			}

			var sendData = data ?? (Array.Empty<byte>());

			var connectionId = packetType == PacketType.STSyn ? ConnectionIdLocal : ConnectionIdRemote;
			var typeAndVersion = new byte[] { (byte)(((byte)packetType << 4) | 1) };
			var extension = new byte[] { 0 };
			var timestamp = TimestampMicro();

			var header = typeAndVersion.Cat(extension).Cat(PackHelper.UInt16(connectionId))
				.Cat(PackHelper.UInt32(timestamp))
				.Cat(PackHelper.UInt32(LastTimestampReceivedDiff))
				.Cat(PackHelper.UInt32(MaxWindow))
				.Cat(PackHelper.UInt16(SeqNumber)).Cat(PackHelper.UInt16(AckNumber));

			var sent = _socket.SendTo(header.Cat(sendData), SocketFlags.None, endPoint);

			if (expectState)
			{
				var internalBuffer = new byte[1024];

				BeginReceive(internalBuffer, 0, 1024, SocketFlags.None, (IAsyncResult ar) => {
				}, this);

			}
			return sent;
		}

		private void SendAck()
		{
			SenduTPData(PacketType.STState, null, false);
		}

		public override ISocket EndAccept(IAsyncResult ar)
		{
			try
			{
				return new UTPSocket(_socket.EndAccept(ar));
			}
			catch (Exception)
			{
				return null;
			}
		}

		public override IAsyncResult BeginReceive(byte[] buffer, int offset, int size, SocketFlags socketFlags, AsyncCallback callback, object state)
		{
			return _socket.BeginReceiveFrom(buffer, offset, size, socketFlags, ref endPoint, (IAsyncResult ar) => {
				var dataLength = EndReceiveInternal(ar);

				var internalBuffer = new byte[dataLength];
				Array.Copy(buffer, internalBuffer, dataLength);

				var timestampRecvd = TimestampMicro();

				if (dataLength < 20)
				{
					//error
				}

				var currentPacketHeader = new UTPPacketHeader();
				currentPacketHeader.Parse(internalBuffer);

				Console.WriteLine("PACKET RECV: {0}", currentPacketHeader.PacketType.ToString());

				LastTimestampReceived = currentPacketHeader.TimestampRecvd;
				LastTimestampReceivedDiff = LastTimestampReceived - timestampRecvd;

				if (isFullyConnected == false)
				{
					//syn sent but not received status yet
					if (currentPacketHeader.PacketType == PacketType.STState && currentPacketHeader.ConnectionIdRecvd == ConnectionIdLocal)
					{
						isFullyConnected = true;
						AckNumber = (ushort)(currentPacketHeader.SeqNumberRecvd - 1);
					}
				}
				else
				{
					AckNumber = currentPacketHeader.SeqNumberRecvd;

					if (currentPacketHeader.PacketType == PacketType.STFin)
					{
						isConnected = false;
						return;
					}

					if (currentPacketHeader.PacketType == PacketType.STState)
					{
						if (dataLength > 20)
						{
							Console.WriteLine("Received state and {0} extra data", dataLength - 20);
						}
					}

					if (currentPacketHeader.PacketType != PacketType.STState)
					{
						SendAck();
					}
				}

				Array.Clear(buffer, 0, dataLength);
				Array.Copy(internalBuffer.GetBytes(20, dataLength - 20), buffer, dataLength - 20);
				
				callback?.Invoke(ar);
			}, state);
		}

		private int EndReceiveInternal(IAsyncResult asyncResult)
		{
			return _socket.EndReceiveFrom(asyncResult, ref endPoint);
		}

		public override int EndReceive(IAsyncResult asyncResult)
		{
			return EndReceiveInternal(asyncResult)-20;
		}
	}
}
