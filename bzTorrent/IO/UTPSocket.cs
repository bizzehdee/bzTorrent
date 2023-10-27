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
	/*
	public class UTPSocket : BaseSocket, ISocket
	{
		
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

		public enum PacketType : byte
		{
			STData = 0,
			STFin = 1,
			STState = 2,
			STReset = 3,
			STSyn = 4
		}

		public UTPSocket(Socket socket) : 
			base(socket)
		{

		}

		public UTPSocket() :
			base(new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
		{

		}
		public override async Task Connect(EndPoint remoteEP)
		{
			isConnected = true;
			endPoint = remoteEP;

			ConnectionIdLocal = (ushort)rng.Next(0, ushort.MaxValue);

			await SenduTPData(PacketType.STSyn, null);

			var tempBuffer = new byte[0];
			await Receive(tempBuffer);
		}

		public override async Task Disconnect(bool reuseSocket)
		{
			await SenduTPData(PacketType.STFin, null);
			isConnected = false;
		}

		public override void Listen(int backlog)
		{
			_socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.ReuseAddress, true);
			_socket.Bind(endPoint);
		}

		public override async Task<int> Send(byte[] buffer)
		{
			return await SenduTPData(PacketType.STData, buffer);
		}

		public override async Task<int> Receive(byte[] buffer)
		{
			var internalBuffer = new byte[buffer.Length + 20];

			var result = await _socket.ReceiveFromAsync(new ArraySegment<byte>(internalBuffer), SocketFlags.None, endPoint);
			var dataLength = result.ReceivedBytes;

			var timestampRecvd = TimestampMicro();

			if (dataLength < 20)
			{
				//error
			}
			
			var verRecvd = (byte)(internalBuffer[0] & 0x0F);
			var typeRecvd = (PacketType)((internalBuffer[0] & 0xF0) >> 4);
			var connectionIdRecvd = UnpackHelper.UInt16(internalBuffer, 2, UnpackHelper.Endianness.Big);
			LastTimestampReceived = UnpackHelper.UInt32(internalBuffer, 4, UnpackHelper.Endianness.Big);
			var timestampDiffRecvd = UnpackHelper.UInt32(internalBuffer, 8, UnpackHelper.Endianness.Big);
			var wndSizeRecvd = UnpackHelper.UInt32(internalBuffer, 12, UnpackHelper.Endianness.Big);
			var seqNumberRecvd = UnpackHelper.UInt16(internalBuffer, 16, UnpackHelper.Endianness.Big);
			var ackNumberRecvd = UnpackHelper.UInt16(internalBuffer, 18, UnpackHelper.Endianness.Big);
			
			LastTimestampReceivedDiff = LastTimestampReceived - timestampRecvd;

			if (isFullyConnected == false)
			{
				//syn sent but not received status yet
				if (typeRecvd == PacketType.STState && connectionIdRecvd == ConnectionIdLocal)
				{
					isFullyConnected = true;
					AckNumber = (ushort)(seqNumberRecvd - 1);
				}
			}
			else
			{

				AckNumber = seqNumberRecvd;

				if (typeRecvd == PacketType.STState)
				{
					return -1;
				}

				if (typeRecvd == PacketType.STFin)
				{
					isConnected = false;
					return 0;
				}

				if (typeRecvd != PacketType.STState)
				{
					//send ack
					await SendAck();
				}
			}

			Array.Copy(internalBuffer.GetBytes(20, dataLength-20), buffer, dataLength - 20);

			return dataLength - 20;
		}

		public override async Task<ISocket> Accept()
		{
			try
			{
				return new UTPSocket(await _socket.AcceptAsync());
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

		private async Task<int> SenduTPData(PacketType packetType, byte[] data)
		{
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


			return await _socket.SendToAsync(new ArraySegment<byte>(header.Cat(sendData)), SocketFlags.None, endPoint);
		}

		private async Task SendAck()
		{
			await SenduTPData(PacketType.STState, null);
		}
	}
	*/
}
