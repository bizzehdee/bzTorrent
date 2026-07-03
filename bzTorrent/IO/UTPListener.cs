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

namespace bzTorrent.IO
{
	/// <summary>
	/// The passive-open (Listen/Accept) half of what used to be the UTPSocket god class.
	///
	/// Since UDP has no OS-level "listen/accept", all accepted connections share this
	/// listener's single bound Socket. The listener runs a demultiplex loop keyed by remote
	/// endpoint — an ST_SYN from an unrecognized endpoint creates a new accepted
	/// <see cref="UTPConnection"/>, everything else is routed to the matching existing
	/// connection via its <see cref="UTPConnection.HandleIncomingPacket"/>. This is a
	/// simplification of the full BEP-29 connection-id-based demux (a second connection
	/// attempt from the same (address, port) while one is already tracked routes to the
	/// existing one rather than starting a new connection).
	/// </summary>
	public class UTPListener : BaseSocket, ISocket
	{
		private class AcceptAsyncResult : IAsyncResult
		{
			public AcceptAsyncResult(object state) { AsyncState = state; }
			public object AsyncState { get; }
			public UTPConnection AcceptedConnection { get; set; }
			public WaitHandle AsyncWaitHandle => null;
			public bool CompletedSynchronously => true;
			public bool IsCompleted => true;
		}

		private const int socketBufferSize = 16 * 1024;

		private bool isListening;
		private readonly object childrenLock = new();
		private readonly Dictionary<string, UTPConnection> activeChildren = new();

		// Guards the accept-waiter registration below.
		private readonly object acceptLock = new();
		private readonly Queue<UTPConnection> pendingAccepts = new();
		private AsyncCallback waitingAcceptCallback;
		private object waitingAcceptState;

		public IPEndPoint LocalEndPoint => (IPEndPoint)_socket.LocalEndPoint;

		public UTPListener(Socket socket) :
			base(socket)
		{

		}

		public UTPListener() :
			base(new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
		{

		}

		public override void Dispose()
		{
			isListening = false;
			base.Dispose();
		}

		public override void Listen(int backlog)
		{
			isListening = true;
			BeginDemuxReceive();
		}

		public override ISocket Accept()
		{
			using var completed = new ManualResetEventSlim(false);
			ISocket accepted = null;

			BeginAccept(ar =>
			{
				accepted = EndAccept(ar);
				completed.Set();
			}, null);

			completed.Wait();
			return accepted;
		}

		public override IAsyncResult BeginAccept(AsyncCallback callback, object state)
		{
			lock (acceptLock)
			{
				if (pendingAccepts.Count > 0)
				{
					var child = pendingAccepts.Dequeue();
					var ar = new AcceptAsyncResult(state) { AcceptedConnection = child };
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
			return (ar as AcceptAsyncResult)?.AcceptedConnection;
		}

		internal void RemoveChild(UTPConnection child)
		{
			lock (childrenLock)
			{
				activeChildren.Remove(child.ChildKey);
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
			if (dataLength < UTPPacketCodec.HeaderSize)
			{
				return;
			}

			var header = UTPPacketCodec.Parse(internalBuffer);

			var key = remote.ToString();

			UTPConnection child;
			lock (childrenLock)
			{
				activeChildren.TryGetValue(key, out child);
			}

			if (child != null)
			{
				child.HandleIncomingPacket(internalBuffer, dataLength);
				return;
			}

			if (header.PacketType != UTPPacketType.STSyn)
			{
				return;
			}

			var accepted = new UTPConnection(_socket, remote, header, this);

			lock (childrenLock)
			{
				activeChildren[key] = accepted;
			}

			// Completes the handshake: acks the SYN's sequence number, which the initiator uses
			// to seed its own AckNumber baseline for our (the acceptor's) subsequent data.
			accepted.SendAck();

			AsyncCallback callback;
			object state;
			lock (acceptLock)
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

			var ar = new AcceptAsyncResult(state) { AcceptedConnection = accepted };
			callback?.Invoke(ar);
		}
	}
}
