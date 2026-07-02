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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
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
	/// WARNING: experimental. Retransmission (RTO-based) and out-of-order/reordering delivery
	/// are implemented (see the "reliability" region below), and Receive() is now a real
	/// synchronous byte-stream pump so MSE/PE works over uTP too. There is no passive-open
	/// (listening) support, though — Listen/Accept don't work for uTP, so this can only initiate
	/// connections.
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

		private class CompletedAsyncResult : IAsyncResult
		{
			public CompletedAsyncResult(object state) { AsyncState = state; }
			public object AsyncState { get; }
			public WaitHandle AsyncWaitHandle => null;
			public bool CompletedSynchronously => true;
			public bool IsCompleted => true;
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

		private readonly UTPCongestionControl congestionControl = new(socketBufferSize);

		#region Reliability (retransmission + reordering)

		// Guards retransmissionTracker, reorderBuffer and deliverableQueue. A single lock keeps
		// this simple: the retransmit timer callback and the socket receive callback both touch
		// this state and must never interleave.
		private readonly object stateLock = new();

		private readonly UTPRetransmissionTracker retransmissionTracker = new();
		private readonly UTPReorderBuffer reorderBuffer = new();

		// In-order payloads that couldn't be handed to the caller yet because BeginReceive only
		// delivers one payload per call; drained on subsequent BeginReceive calls before touching
		// the socket again.
		private readonly Queue<byte[]> deliverableQueue = new();

		private int pendingDeliveredLength;

		private Timer retransmitTimer;
		private const int RetransmitPollIntervalMs = 100;

		#endregion

		private uint BytesInFlight
		{
			get { lock (stateLock) { return retransmissionTracker.BytesInFlight; } }
		}

		private ushort ConnectionIdRemote
		{
			get => (ushort)(ConnectionIdLocal + 1);
		}

		public override bool Connected { get => isConnected; }
		public override bool NoDelay { get; set; }

		public IPEndPoint LocalEndPoint => (IPEndPoint)_socket.LocalEndPoint;


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

			retransmitTimer = new Timer(_ => RetransmitTick(), null, RetransmitPollIntervalMs, RetransmitPollIntervalMs);
		}

		public override void Disconnect(bool reuseSocket)
		{
			SenduTPData(PacketType.STFin, null);
			isConnected = false;
			retransmitTimer?.Dispose();
			retransmitTimer = null;
		}

		public override void Dispose()
		{
			retransmitTimer?.Dispose();
			retransmitTimer = null;
			base.Dispose();
		}

		public override void Listen(int backlog)
		{
			_socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.ReuseAddress, true);
			_socket.Bind(endPoint);
		}

		public override int Send(byte[] buffer)
		{
			WaitForSendWindow(buffer?.Length ?? 0);
			return SenduTPData(PacketType.STData, buffer);
		}

		// Blocks (briefly) until the LEDBAT send window has room for dataLength bytes, so a
		// caller pushing a lot of data doesn't blow straight through max_window. This is a
		// best-effort throttle, not a queue: after maxWaitMs we send anyway rather than stalling
		// the caller indefinitely.
		private void WaitForSendWindow(int dataLength)
		{
			const int pollIntervalMs = 5;
			const int maxWaitMs = 2000;
			var waited = 0;

			while (BytesInFlight + dataLength > congestionControl.MaxWindow && waited < maxWaitMs)
			{
				Thread.Sleep(pollIntervalMs);
				waited += pollIntervalMs;
			}
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

		private int SendPacket(PacketType packetType, byte[] data, ushort seq)
		{
			var sendData = data ?? Array.Empty<byte>();

			var connectionId = packetType == PacketType.STSyn ? ConnectionIdLocal : ConnectionIdRemote;
			var typeAndVersion = new byte[] { (byte)(((byte)packetType << 4) | 1) };
			var extension = new byte[] { 0 };
			var timestamp = TimestampMicro();

			var header = typeAndVersion.Cat(extension).Cat(PackHelper.UInt16(connectionId))
				.Cat(PackHelper.UInt32(timestamp))
				.Cat(PackHelper.UInt32(LastTimestampReceivedDiff))
				.Cat(PackHelper.UInt32(congestionControl.MaxWindow))
				.Cat(PackHelper.UInt16(seq)).Cat(PackHelper.UInt16(AckNumber));

			return _socket.SendTo(header.Cat(sendData), SocketFlags.None, endPoint);
		}

		private int SenduTPData(PacketType packetType, byte[] data, bool expectState = true)
		{
			if (packetType != PacketType.STState)
			{
				SeqNumber++;
			}

			var seq = SeqNumber;
			var sendData = data ?? Array.Empty<byte>();
			var sent = SendPacket(packetType, sendData, seq);

			if (packetType != PacketType.STState)
			{
				lock (stateLock)
				{
					retransmissionTracker.OnSent(seq, packetType, sendData, DateTime.UtcNow);
				}
			}

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

		#region Reliability (retransmission + reordering)

		// Fires on a fixed poll interval so a fully lost packet (peer never sends anything back)
		// still gets retransmitted, not just packets sitting behind a request/response exchange.
		private void RetransmitTick()
		{
			if (!isConnected)
			{
				return;
			}

			var now = DateTime.UtcNow;
			IReadOnlyList<UTPRetransmissionTracker.TrackedPacket> due;

			lock (stateLock)
			{
				if (retransmissionTracker.HasExceededMaxRetransmits(now))
				{
					// Peer is unreachable — give up on the connection rather than
					// retransmitting forever.
					isConnected = false;
					return;
				}

				due = retransmissionTracker.GetDueForRetransmit(now);
			}

			foreach (var packet in due)
			{
				SendPacket(packet.Type, packet.Payload, packet.Seq);
				lock (stateLock)
				{
					retransmissionTracker.MarkRetransmitted(packet, DateTime.UtcNow);
				}
			}
		}

		#endregion

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
			byte[] queuedPayload = null;
			lock (stateLock)
			{
				if (deliverableQueue.Count > 0)
				{
					queuedPayload = deliverableQueue.Dequeue();
				}
			}

			if (queuedPayload != null)
			{
				// Already-reassembled in-order data waiting from a previous datagram; hand it
				// off without touching the socket. Dispatched via the thread pool to preserve
				// the async (not synchronously-recursive) calling contract.
				ThreadPool.QueueUserWorkItem(_ => DeliverPayload(queuedPayload, buffer, offset, callback, state));
				return new CompletedAsyncResult(state);
			}

			return _socket.BeginReceiveFrom(buffer, offset, size, socketFlags, ref endPoint, (IAsyncResult ar) => {
				var dataLength = EndReceiveInternal(ar);

				var internalBuffer = new byte[dataLength];
				Array.Copy(buffer, internalBuffer, dataLength);

				var timestampRecvd = TimestampMicro();

				if (dataLength < 20)
				{
					DeliverPayload(Array.Empty<byte>(), buffer, offset, callback, state);
					return;
				}

				var currentPacketHeader = new UTPPacketHeader();
				currentPacketHeader.Parse(internalBuffer);

				LastTimestampReceived = currentPacketHeader.TimestampRecvd;
				LastTimestampReceivedDiff = LastTimestampReceived - timestampRecvd;

				congestionControl.OnDelaySample(currentPacketHeader.TimestampDiffRecvd);

				uint bytesAcked;
				lock (stateLock)
				{
					bytesAcked = retransmissionTracker.OnAck(currentPacketHeader.AckNumberRecvd, DateTime.UtcNow);
				}
				congestionControl.OnAck(bytesAcked, timestampRecvd);

				byte[] readyPayload = null;

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
					if (currentPacketHeader.PacketType == PacketType.STData)
					{
						var payload = internalBuffer.GetBytes(20, dataLength - 20);

						UTPReorderBuffer.Result result;
						lock (stateLock)
						{
							result = reorderBuffer.Accept(currentPacketHeader.SeqNumberRecvd, AckNumber, payload);
						}

						AckNumber = result.AckNumber;

						// result.DeliverablePayloads is the packet just received (if it filled
						// the next expected sequence) plus any previously-buffered packets a
						// filled gap makes deliverable too, in order. Duplicate/old packets and
						// packets still ahead of an outstanding gap yield no deliverable payloads
						// here — the peer's RTO/backoff will resend the missing packet.
						if (result.DeliverablePayloads.Count > 0)
						{
							readyPayload = result.DeliverablePayloads[0];
							lock (stateLock)
							{
								for (var i = 1; i < result.DeliverablePayloads.Count; i++)
								{
									deliverableQueue.Enqueue(result.DeliverablePayloads[i]);
								}
							}
						}
					}
					else
					{
						AckNumber = currentPacketHeader.SeqNumberRecvd;
					}

					if (currentPacketHeader.PacketType == PacketType.STFin)
					{
						isConnected = false;
						return;
					}

					if (currentPacketHeader.PacketType != PacketType.STState)
					{
						SendAck();
					}
				}

				DeliverPayload(readyPayload ?? Array.Empty<byte>(), buffer, offset, callback, state);
			}, state);
		}

		private void DeliverPayload(byte[] payload, byte[] buffer, int offset, AsyncCallback callback, object state)
		{
			pendingDeliveredLength = payload.Length;
			if (payload.Length > 0)
			{
				Array.Copy(payload, 0, buffer, offset, payload.Length);
			}

			callback?.Invoke(new CompletedAsyncResult(state));
		}

		private int EndReceiveInternal(IAsyncResult asyncResult)
		{
			return _socket.EndReceiveFrom(asyncResult, ref endPoint);
		}

		public override int EndReceive(IAsyncResult asyncResult)
		{
			return pendingDeliveredLength;
		}

		// Synchronous byte-stream receive over uTP's async, packet-oriented BeginReceive/
		// EndReceive: pumps them on a blocking wait, looping past deliveries that carry no
		// application payload (pure ST_STATE acks, or ST_DATA still buffered behind a reorder
		// gap — see UTPReorderBuffer) so this never returns 0 bytes for a still-open connection,
		// matching the blocking-read contract MSE/PE's handshake (ReadExact, etc.) relies on.
		public override int Receive(byte[] buffer, int offset, int size)
		{
			var timeoutMs = ReceiveTimeout > 0 ? ReceiveTimeout : Timeout.Infinite;

			while (isConnected)
			{
				using var completed = new ManualResetEventSlim(false);
				var received = 0;
				Exception error = null;

				BeginReceive(buffer, offset, size, SocketFlags.None, ar =>
				{
					try { received = EndReceive(ar); }
					catch (Exception ex) { error = ex; }
					completed.Set();
				}, null);

				if (!completed.Wait(timeoutMs))
				{
					throw new SocketException((int)SocketError.TimedOut);
				}

				if (error != null)
				{
					throw error;
				}

				if (received > 0)
				{
					return received;
				}
			}

			return 0;
		}
	}
}
