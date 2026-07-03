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
			ConnectionIdRecvd = UnpackHelper.UInt16(buffer, 2, Endianness.Big);
			TimestampRecvd = UnpackHelper.UInt32(buffer, 4, Endianness.Big);
			TimestampDiffRecvd = UnpackHelper.UInt32(buffer, 8, Endianness.Big);
			WndSizeRecvd = UnpackHelper.UInt32(buffer, 12, Endianness.Big);
			SeqNumberRecvd = UnpackHelper.UInt16(buffer, 16, Endianness.Big);
			AckNumberRecvd = UnpackHelper.UInt16(buffer, 18, Endianness.Big);
		}
	}

	/// <summary>
	/// WARNING: experimental against real-world/foreign uTP implementations (wire-format
	/// compatibility is unverified), but internally self-consistent and tested end-to-end:
	/// retransmission (RTO-based) and out-of-order/reordering delivery are implemented (see the
	/// "reliability" region below), Receive() is a real synchronous byte-stream pump so MSE/PE
	/// works over uTP too, and passive-open (Listen/Accept) works — verified with two real
	/// UTPSocket instances (and PeerWireListener/PeerWireConnection built on them) completing a
	/// full handshake and exchanging data bidirectionally over real loopback UDP.
	///
	/// Passive-open: since UDP has no OS-level "listen/accept", all accepted connections share
	/// the single bound Socket with the listening UTPSocket, which runs a demultiplex loop keyed
	/// by remote endpoint — an ST_SYN from an unrecognized endpoint creates a new accepted
	/// UTPSocket (see the "passive open" region below), everything else is routed to the matching
	/// existing connection. This is a simplification of the full BEP-29 connection-id-based demux
	/// (a second connection attempt from the same (address, port) while one is already tracked
	/// routes to the existing one rather than starting a new connection).
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

		private class AcceptAsyncResult : IAsyncResult
		{
			public AcceptAsyncResult(object state) { AsyncState = state; }
			public object AsyncState { get; }
			public UTPSocket AcceptedSocket { get; set; }
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

		// Guards retransmissionTracker, reorderBuffer, deliverableQueue and the waiting-
		// receive/accept registrations below. A single lock keeps this simple: the retransmit
		// timer callback, this socket's own receive callback, and (for an accepted child) the
		// listening parent's demux thread all touch this state and must never interleave.
		private readonly object stateLock = new();

		private readonly UTPRetransmissionTracker retransmissionTracker = new();
		private readonly UTPReorderBuffer reorderBuffer = new();

		// In-order payloads that couldn't be handed to the caller yet because BeginReceive only
		// delivers one payload per call; drained on subsequent BeginReceive calls before touching
		// the socket (or, for an accepted child, before registering as a waiting receiver) again.
		private readonly Queue<byte[]> deliverableQueue = new();

		private int pendingDeliveredLength;

		private Timer retransmitTimer;
		private const int RetransmitPollIntervalMs = 100;

		#endregion

		#region Passive open (Listen/Accept)

		// True for a UTPSocket returned by Accept()/EndAccept(): it shares the listening socket's
		// _socket rather than owning one, so it must never itself call BeginReceiveFrom (the
		// listener's demux loop owns all reads on that socket) and must never Dispose() it.
		private bool isServerChild;
		private UTPSocket listenerParent;
		private string childKey;

		private bool isListening;
		private readonly object childrenLock = new();
		private readonly Dictionary<string, UTPSocket> activeChildren = new();
		private readonly Queue<UTPSocket> pendingAccepts = new();
		private AsyncCallback waitingAcceptCallback;
		private object waitingAcceptState;

		// A child's single outstanding BeginReceive registration, completed by the listening
		// parent's demux thread when a packet for this connection arrives (or immediately, from
		// deliverableQueue, if one is already buffered).
		private AsyncCallback waitingReceiveCallback;
		private byte[] waitingReceiveBuffer;
		private int waitingReceiveOffset;
		private object waitingReceiveState;

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

		// Used internally by the listening socket's demux loop to construct an accepted
		// connection sharing its Socket. ConnectionIdLocal is derived so that this socket's
		// ConnectionIdRemote (used for every non-SYN send) equals the SYN's ConnectionIdRecvd —
		// the value the initiator is listening for its ST_STATE reply on.
		private UTPSocket(Socket sharedSocket, IPEndPoint remote, UTPPacketHeader synHeader, UTPSocket parent) :
			base(sharedSocket)
		{
			isServerChild = true;
			listenerParent = parent;
			endPoint = remote;
			childKey = remote.ToString();
			ConnectionIdLocal = (ushort)(synHeader.ConnectionIdRecvd - 1);
			AckNumber = synHeader.SeqNumberRecvd;
			isConnected = true;
			isFullyConnected = true;

			retransmitTimer = new Timer(_ => RetransmitTick(), null, RetransmitPollIntervalMs, RetransmitPollIntervalMs);
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
			listenerParent?.RemoveChild(this);
		}

		public override void Dispose()
		{
			retransmitTimer?.Dispose();
			retransmitTimer = null;
			isListening = false;

			// A child shares the listening socket's _socket — only the listener that actually
			// owns it may dispose it, or every other accepted connection breaks too.
			if (!isServerChild)
			{
				base.Dispose();
			}
		}

		public override void Listen(int backlog)
		{
			isListening = true;
			BeginDemuxReceive();
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
			using var completed = new ManualResetEventSlim(false);
			UTPSocket accepted = null;

			BeginAccept(ar =>
			{
				accepted = (UTPSocket)EndAccept(ar);
				completed.Set();
			}, null);

			completed.Wait();
			return accepted;
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

		// Does NOT arm a receive after sending. An earlier version did (to let a bare Connect()
		// call alone observe the ST_STATE reply), but that self-armed receive competes with
		// whatever the consumer's own receive loop (PeerWireConnection.Process(), or a direct
		// Receive()/BeginReceive call) is doing — with multiple sends in quick succession (e.g.
		// Connect() immediately followed by a Send()), several such receives can be outstanding
		// at once, each racing to consume the next incoming datagram, and a reply meant for the
		// consumer's own receive loop can be silently stolen by one of these no-op internal ones.
		// The OS still buffers an incoming datagram even with no receive currently pending, so
		// removing this doesn't lose anything — whichever BeginReceive call comes next (from
		// anywhere) picks it up.
		private int SenduTPData(PacketType packetType, byte[] data)
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

			return sent;
		}

		private void SendAck()
		{
			SenduTPData(PacketType.STState, null);
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
					listenerParent?.RemoveChild(this);
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

		#region Passive open (Listen/Accept)

		public override IAsyncResult BeginAccept(AsyncCallback callback, object state)
		{
			lock (stateLock)
			{
				if (pendingAccepts.Count > 0)
				{
					var child = pendingAccepts.Dequeue();
					var ar = new AcceptAsyncResult(state) { AcceptedSocket = child };
					ThreadPool.QueueUserWorkItem(_ => callback?.Invoke(ar));
					return ar;
				}

				waitingAcceptCallback = callback;
				waitingAcceptState = state;
				return new AcceptAsyncResult(state);
			}
		}

		public override ISocket EndAccept(IAsyncResult ar)
		{
			return (ar as AcceptAsyncResult)?.AcceptedSocket;
		}

		private void RemoveChild(UTPSocket child)
		{
			lock (childrenLock)
			{
				activeChildren.Remove(child.childKey);
			}
		}

		// Continuously reads datagrams on the listening socket and routes each one to the
		// matching accepted connection (by remote endpoint), or — for an ST_SYN from an
		// unrecognized endpoint — creates and completes a new accepted connection.
		private void BeginDemuxReceive()
		{
			if (!isListening)
			{
				return;
			}

			var buffer = new byte[socketBufferSize];
			EndPoint from = new IPEndPoint(
				_socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);

			try
			{
				_socket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref from, ar =>
				{
					EndPoint remote = from;
					int dataLength;
					try
					{
						dataLength = _socket.EndReceiveFrom(ar, ref remote);
					}
					catch (ObjectDisposedException)
					{
						return;
					}
					catch (SocketException)
					{
						BeginDemuxReceive();
						return;
					}

					var internalBuffer = new byte[dataLength];
					Array.Copy(buffer, internalBuffer, dataLength);

					DispatchIncoming(internalBuffer, dataLength, (IPEndPoint)remote);

					BeginDemuxReceive();
				}, null);
			}
			catch (ObjectDisposedException)
			{
			}
		}

		private void DispatchIncoming(byte[] internalBuffer, int dataLength, IPEndPoint remote)
		{
			if (dataLength < 20)
			{
				return;
			}

			var header = new UTPPacketHeader();
			header.Parse(internalBuffer);

			var key = remote.ToString();

			UTPSocket child;
			lock (childrenLock)
			{
				activeChildren.TryGetValue(key, out child);
			}

			if (child != null)
			{
				child.HandleIncomingPacket(internalBuffer, dataLength);
				return;
			}

			if (header.PacketType != PacketType.STSyn)
			{
				return;
			}

			var accepted = new UTPSocket(_socket, remote, header, this);

			lock (childrenLock)
			{
				activeChildren[key] = accepted;
			}

			// Completes the handshake: acks the SYN's sequence number, which the initiator uses
			// to seed its own AckNumber baseline for our (the acceptor's) subsequent data.
			accepted.SendAck();

			AsyncCallback callback;
			object state;
			lock (stateLock)
			{
				if (waitingAcceptCallback == null)
				{
					pendingAccepts.Enqueue(accepted);
					return;
				}

				callback = waitingAcceptCallback;
				state = waitingAcceptState;
				waitingAcceptCallback = null;
			}

			var ar = new AcceptAsyncResult(state) { AcceptedSocket = accepted };
			callback?.Invoke(ar);
		}

		// Called by the listening parent's demux thread (never by this instance's own code) to
		// feed this accepted connection a datagram addressed to it.
		private void HandleIncomingPacket(byte[] internalBuffer, int dataLength)
		{
			var readyPayload = ProcessIncomingPacket(internalBuffer, dataLength) ?? Array.Empty<byte>();

			AsyncCallback callback;
			byte[] buffer;
			int offset;
			object state;
			lock (stateLock)
			{
				if (waitingReceiveCallback == null)
				{
					deliverableQueue.Enqueue(readyPayload);
					return;
				}

				callback = waitingReceiveCallback;
				buffer = waitingReceiveBuffer;
				offset = waitingReceiveOffset;
				state = waitingReceiveState;
				waitingReceiveCallback = null;
			}

			DeliverPayload(readyPayload, buffer, offset, callback, state);
		}

		#endregion

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

			if (isServerChild)
			{
				// This connection doesn't own a receive loop — the listening parent's demux
				// thread delivers datagrams via HandleIncomingPacket. Register to be completed
				// then, rather than touching the (shared, parent-owned) socket ourselves.
				lock (stateLock)
				{
					waitingReceiveCallback = callback;
					waitingReceiveBuffer = buffer;
					waitingReceiveOffset = offset;
					waitingReceiveState = state;
				}
				return new CompletedAsyncResult(state);
			}

			return _socket.BeginReceiveFrom(buffer, offset, size, socketFlags, ref endPoint, (IAsyncResult ar) => {
				int dataLength;
				try
				{
					dataLength = EndReceiveInternal(ar);
				}
				catch (ObjectDisposedException)
				{
					// The socket was disposed while this receive was outstanding (e.g. the
					// caller tore down the connection) — nothing left to deliver to.
					return;
				}
				catch (SocketException)
				{
					// A pending receive is cancelled (e.g. ECANCELED/OperationAborted) when the
					// underlying socket is disposed out from under it. Same as above: the
					// connection is going away, there's nothing to deliver.
					return;
				}

				var internalBuffer = new byte[dataLength];
				Array.Copy(buffer, internalBuffer, dataLength);

				var readyPayload = ProcessIncomingPacket(internalBuffer, dataLength);

				DeliverPayload(readyPayload ?? Array.Empty<byte>(), buffer, offset, callback, state);
			}, state);
		}

		// Header parse, ack/RTT bookkeeping, connection-state transitions and reorder-buffer
		// handling for one received datagram. Shared by this socket's own receive callback (the
		// initiating/client role) and HandleIncomingPacket (the accepted/server role) — the same
		// logic applies to both, just driven from different places.
		private byte[] ProcessIncomingPacket(byte[] internalBuffer, int dataLength)
		{
			if (dataLength < 20)
			{
				return null;
			}

			var timestampRecvd = TimestampMicro();

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
					// The acceptor's ST_STATE reply carries its own (unincremented) current
					// seq_nr; its first real ST_DATA send increments from there. So our "next
					// expected" baseline is exactly that seq_nr, not seq_nr-1 — matches how an
					// accepted connection seeds AckNumber from the SYN's seq_nr (see the private
					// accept constructor above).
					AckNumber = currentPacketHeader.SeqNumberRecvd;
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
					listenerParent?.RemoveChild(this);
					return null;
				}

				if (currentPacketHeader.PacketType != PacketType.STState)
				{
					SendAck();
				}
			}

			return readyPayload;
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
