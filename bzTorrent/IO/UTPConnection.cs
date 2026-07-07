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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using bzTorrent.Helpers;

namespace bzTorrent.IO
{
	/// <summary>
	/// A single uTP (BEP-29) connection — the data-transfer half of what used to be the
	/// UTPSocket god class. It plays one of two roles decided at construction:
	///   * a client connection that owns its UDP socket and runs its own receive loop
	///     (created via the public constructors + <see cref="Connect"/>); or
	///   * an accepted child that shares the listening <see cref="UTPListener"/>'s socket and
	///     is fed datagrams by that listener's demux loop (created via the internal
	///     constructor; <see cref="HandleIncomingPacket"/> is the feed point).
	/// The passive-open/demux/accept responsibility lives in <see cref="UTPListener"/>.
	///
	/// WARNING: experimental against real-world/foreign uTP implementations (wire-format
	/// compatibility is unverified), but internally self-consistent and tested end-to-end
	/// over real loopback UDP: RTO-based retransmission and out-of-order reordering are
	/// implemented, and Receive() is a real synchronous byte-stream pump so MSE/PE works.
	/// </summary>
	public class UTPConnection : BaseSocket, ISocket
	{
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

		// Guards retransmissionTracker, reorderBuffer, deliverableQueue and the waiting-receive
		// registration below. A single lock keeps this simple: the retransmit timer callback,
		// this connection's own receive callback, and (for an accepted child) the listening
		// parent's demux thread all touch this state and must never interleave.
		private readonly object stateLock = new();

		private readonly UTPRetransmissionTracker retransmissionTracker = new();
		private readonly UTPReorderBuffer reorderBuffer = new();

		// In-order payloads that couldn't be handed to the caller yet because BeginReceive only
		// delivers one payload per call; drained on subsequent BeginReceive calls before touching
		// the socket (or, for an accepted child, before registering as a waiting receiver) again.
		private readonly Queue<byte[]> deliverableQueue = new();

		// Data the caller asked to Send() before the SYN was acked (connection not yet
		// established). Sent on an unestablished connection it would carry ack_nr=0, which peers
		// reject (they drop it and then FIN); so it's held here and flushed - with the correct
		// ack - the moment the ST_STATE arrives. Guarded by stateLock.
		private readonly List<byte[]> pendingSends = new();

		private int pendingDeliveredLength;

		private Timer retransmitTimer;
		private const int RetransmitPollIntervalMs = 100;

		#endregion

		// True for a connection returned by UTPListener.Accept(): it shares the listening
		// socket's _socket rather than owning one, so it must never itself call BeginReceiveFrom
		// (the listener's demux loop owns all reads on that socket) and must never Dispose() it.
		private readonly bool isServerChild;
		private readonly UTPListener listenerParent;
		internal string ChildKey { get; }

		// A child's single outstanding BeginReceive registration, completed by the listening
		// parent's demux thread when a packet for this connection arrives (or immediately, from
		// deliverableQueue, if one is already buffered).
		private AsyncCallback waitingReceiveCallback;
		private byte[] waitingReceiveBuffer;
		private int waitingReceiveOffset;
		private object waitingReceiveState;

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

		public UTPConnection(Socket socket) :
			base(socket)
		{

		}

		public UTPConnection() :
			base(new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
		{

		}

		// Used by UTPListener's demux loop to construct an accepted connection sharing its
		// Socket. ConnectionIdLocal is derived so that this connection's ConnectionIdRemote
		// (used for every non-SYN send) equals the SYN's ConnectionIdRecvd — the value the
		// initiator is listening for its ST_STATE reply on.
		internal UTPConnection(Socket sharedSocket, IPEndPoint remote, UTPPacketHeader synHeader, UTPListener parent) :
			base(sharedSocket)
		{
			isServerChild = true;
			listenerParent = parent;
			endPoint = remote;
			ChildKey = remote.ToString();
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

			SenduTPData(UTPPacketType.STSyn, null);

			retransmitTimer = new Timer(_ => RetransmitTick(), null, RetransmitPollIntervalMs, RetransmitPollIntervalMs);
		}

		public override void Disconnect(bool reuseSocket)
		{
			SenduTPData(UTPPacketType.STFin, null);
			isConnected = false;
			retransmitTimer?.Dispose();
			retransmitTimer = null;
			listenerParent?.RemoveChild(this);
		}

		public override void Dispose()
		{
			retransmitTimer?.Dispose();
			retransmitTimer = null;

			// A child shares the listening socket's _socket — only the listener that actually
			// owns it may dispose it, or every other accepted connection breaks too.
			if (!isServerChild)
			{
				base.Dispose();
			}
		}

		public override int Send(byte[] buffer)
		{
			lock (stateLock)
			{
				if (!isFullyConnected)
				{
					// SYN sent but not yet acked - hold this data and send it once the connection
					// is established (see FlushPendingSends), so it goes out with a valid ack_nr.
					pendingSends.Add(buffer ?? Array.Empty<byte>());
					return buffer?.Length ?? 0;
				}
			}

			WaitForSendWindow(buffer?.Length ?? 0);
			return SenduTPData(UTPPacketType.STData, buffer);
		}

		// Flushes data queued by Send() before the connection was established, in order, now that
		// it can be sent with a valid ack. Called once, when the ST_STATE establishes the connection.
		private void FlushPendingSends()
		{
			List<byte[]> toSend;
			lock (stateLock)
			{
				if (pendingSends.Count == 0)
				{
					return;
				}

				toSend = new List<byte[]>(pendingSends);
				pendingSends.Clear();
			}

			foreach (var data in toSend)
			{
				SenduTPData(UTPPacketType.STData, data);
			}
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

		private static uint TimestampMicro()
		{
			return (uint)(DateTime.UtcNow.Ticks / (TimeSpan.TicksPerMillisecond / 1000));
		}

		private int SendPacket(UTPPacketType packetType, byte[] data, ushort seq)
		{
			var connectionId = packetType == UTPPacketType.STSyn ? ConnectionIdLocal : ConnectionIdRemote;

			var packet = UTPPacketCodec.BuildPacket(packetType, connectionId, TimestampMicro(),
				LastTimestampReceivedDiff, congestionControl.MaxWindow, seq, AckNumber, data);

			return _socket.SendTo(packet, SocketFlags.None, endPoint);
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
		private int SenduTPData(UTPPacketType packetType, byte[] data)
		{
			if (packetType != UTPPacketType.STState)
			{
				SeqNumber++;
			}

			// A data/SYN/FIN packet consumes a sequence number and is sent under it. A state (ack)
			// packet consumes none, but libutp semantics require it to advertise the NEXT sequence
			// number we'll send - i.e. the seq our first following ST_DATA will carry - not the
			// current (already-used) one. That lets the peer baseline its ack_nr to seq-1 on the
			// establishing ST_STATE and treat our first real data packet as in-order. Advertising
			// the current seq instead would leave the peer's baseline one behind, making our first
			// data look like a duplicate and getting it dropped by its reorder buffer.
			var seq = packetType == UTPPacketType.STState ? (ushort)(SeqNumber + 1) : SeqNumber;
			var sendData = data ?? Array.Empty<byte>();
			var sent = SendPacket(packetType, sendData, seq);

			if (packetType != UTPPacketType.STState)
			{
				lock (stateLock)
				{
					retransmissionTracker.OnSent(seq, packetType, sendData, DateTime.UtcNow);
				}
			}

			return sent;
		}

		// Acks the SYN during accept (called by the listener) and every received data packet.
		internal void SendAck()
		{
			SenduTPData(UTPPacketType.STState, null);
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

		// Called by the listening parent's demux thread (never by this instance's own code) to
		// feed this accepted connection a datagram addressed to it.
		internal void HandleIncomingPacket(byte[] internalBuffer, int dataLength)
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
		// handling for one received datagram. Shared by this connection's own receive callback
		// (the initiating/client role) and HandleIncomingPacket (the accepted/server role) — the
		// same logic applies to both, just driven from different places.
		private byte[] ProcessIncomingPacket(byte[] internalBuffer, int dataLength)
		{
			if (dataLength < UTPPacketCodec.HeaderSize)
			{
				return null;
			}

			var timestampRecvd = TimestampMicro();

			var currentPacketHeader = UTPPacketCodec.Parse(internalBuffer);

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

			var justEstablished = false;
			if (isFullyConnected == false)
			{
				//syn sent but not received status yet
				if (currentPacketHeader.PacketType == UTPPacketType.STState && currentPacketHeader.ConnectionIdRecvd == ConnectionIdLocal)
				{
					isFullyConnected = true;
					justEstablished = true;
					// The acceptor's ST_STATE (a state packet) carries its next-to-send seq_nr
					// WITHOUT consuming it - its first real ST_DATA will reuse that same seq_nr.
					// So the last sequence we've actually "received" is seq_nr - 1, and that's the
					// ack_nr our packets must carry until that first data arrives. Acking seq_nr
					// itself acks a packet the peer hasn't sent yet, which real peers (e.g.
					// libutp/qBittorrent) reject - they silently drop our data and then FIN,
					// so the connection never gets past the handshake. (Differs from the accept
					// side, where the SYN *does* consume a seq, so there AckNumber = SYN seq_nr.)
					AckNumber = (ushort)(currentPacketHeader.SeqNumberRecvd - 1);
				}
			}
			else
			{
				if (currentPacketHeader.PacketType == UTPPacketType.STData)
				{
					var payload = internalBuffer.GetBytes(UTPPacketCodec.HeaderSize, dataLength - UTPPacketCodec.HeaderSize);

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
				// An ST_STATE (pure ack) must NOT advance AckNumber: it's a state packet whose
				// seq_nr is the sender's next-to-send and consumes no sequence number. Advancing
				// to it made the peer's first real ST_DATA (which reuses that same seq_nr) look
				// like an already-seen packet, so the reorder buffer dropped it and the BitTorrent
				// handshake never arrived. Only ST_DATA advances AckNumber (via the reorder buffer).

				if (currentPacketHeader.PacketType == UTPPacketType.STFin)
				{
					isConnected = false;
					listenerParent?.RemoveChild(this);
					return null;
				}

				if (currentPacketHeader.PacketType != UTPPacketType.STState)
				{
					SendAck();
				}
			}

			// The SYN was just acked: send anything the caller queued before establishment now
			// (with the ack this ST_STATE seeded), so its handshake goes out on a live connection.
			if (justEstablished)
			{
				FlushPendingSends();
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
