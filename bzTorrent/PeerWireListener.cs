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
using System.Net;
using System.Net.Sockets;
using bzTorrent.IO;

namespace bzTorrent
{
	public class PeerWireListener<T> where T : IPeerConnection, new()
	{

		private readonly T peerConnection;
		private readonly Func<ISocket, IPeerConnection> connectionFactory;
		private volatile bool stopped;

		public delegate void NewPeerDelegate(IPeerWireClient peerWireClient);

		public int Port { get; init; }
		public event NewPeerDelegate NewPeer;

		// Applied to every connection accepted by this listener (see Callback).
		public PeerEncryptionMode EncryptionMode
		{
			get => peerConnection.EncryptionMode;
			set => peerConnection.EncryptionMode = value;
		}
		public PeerEncryptionOptions EncryptionOptions => peerConnection.EncryptionOptions;

		public PeerWireListener() : this(0, null)
		{
		}

		public PeerWireListener(int port) : this(port, null)
		{
		}

		/// <summary>
		/// <paramref name="connectionFactory"/> wraps each accepted socket in a connection.
		/// Pass one (e.g. <c>s =&gt; new PeerWireConnection&lt;TCPSocket&gt;(s)</c>) to avoid the
		/// reflection-based default.
		/// </summary>
		public PeerWireListener(int port, Func<ISocket, IPeerConnection> connectionFactory)
		{
			Port = port;
			peerConnection = new T();
			this.connectionFactory = connectionFactory ?? DefaultConnectionFactory;
		}

		// Wraps an accepted socket in T via its (ISocket) constructor. Prefer an explicit factory.
		private static IPeerConnection DefaultConnectionFactory(ISocket socket)
		{
			var constructorInfo = typeof(T).GetConstructor(new[] { typeof(ISocket) });
			if (constructorInfo == null)
			{
				throw new InvalidOperationException(
					$"{typeof(T).Name} has no public (ISocket) constructor; pass a connectionFactory to PeerWireListener instead.");
			}

			return (IPeerConnection)constructorInfo.Invoke(new object[] { socket });
		}

		public void StartListening()
		{
			stopped = false;
			peerConnection.Listen(new IPEndPoint(IPAddress.Any, Port));
			peerConnection.BeginAccept(Callback);
		}

		public void StopListening()
		{
			// Stop surfacing new peers: the accept loop below stops re-arming. (The old code
			// called EndAccept on the pending accept, which completes -- not stops -- it.)
			stopped = true;
		}

		private void Callback(IAsyncResult ar)
		{
			if (stopped)
			{
				return;
			}

			var socket = peerConnection.EndAccept(ar);
			var connection = connectionFactory(socket);

			connection.EncryptionMode = peerConnection.EncryptionMode;
			foreach (var infoHash in peerConnection.EncryptionOptions.GetKnownInfoHashes())
			{
				connection.EncryptionOptions.AddKnownInfoHash(infoHash);
			}
			connection.EncryptionOptions.SupportedTypes = peerConnection.EncryptionOptions.SupportedTypes;
			connection.EncryptionOptions.MaxPaddingBytes = peerConnection.EncryptionOptions.MaxPaddingBytes;

			NewPeer?.Invoke(new PeerWireClient(connection));

			if (!stopped)
			{
				peerConnection.BeginAccept(Callback);
			}
		}

	}
}
