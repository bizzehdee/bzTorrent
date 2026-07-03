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
using System;
using System.Net;
using System.Collections.Concurrent;

namespace bzTorrent.IO
{
	public class PeerWireConnection<T> : IPeerConnection where T : ISocket, new()
	{
		private ISocket socket;
		private IPEndPoint _lastEndPoint;
		private volatile bool receiving = false;
		private byte[] currentPacketBuffer = null;
		private const int socketBufferSize = 16 * 1024;
		private const int DhPublicKeyLength = 96;   // MSE Diffie-Hellman public key (Yb)
		private readonly IMseHandshake mseHandshake = new MseHandshake();
		private readonly byte[] socketBuffer = new byte[socketBufferSize];
		private readonly ConcurrentQueue<PeerWirePacket> receiveQueue = new();
		private readonly ConcurrentQueue<PeerWirePacket> sendQueue = new();
		private PeerClientHandshake incomingHandshake = null;
		private byte[] handshakeBuffer = Array.Empty<byte>();
		private volatile RC4Cipher sendCipher;
		private volatile RC4Cipher receiveCipher;
		private volatile bool encryptionHandshakeCompleted;
		private volatile bool encryptionNegotiated;

		public PeerEncryptionMode EncryptionMode { get; set; } = PeerEncryptionMode.PlainText;
		public bool IsEncrypted { get => encryptionHandshakeCompleted; }
		public PeerEncryptionOptions EncryptionOptions { get; } = new PeerEncryptionOptions();

		private int _timeout;
		public int Timeout
		{
			get => _timeout;
			set
			{
				_timeout = value;
				// Apply to an already-created socket too (e.g. an accepted connection whose
				// Timeout is set via an object initializer after construction). Otherwise the
				// synchronous MSE read timeout would never take effect on that socket.
				if (socket != null)
				{
					socket.ReceiveTimeout = value * 1000;
					socket.SendTimeout = value * 1000;
				}
			}
		}

		// "Connected" reflects the liveness of the underlying socket only. It must NOT also
		// require the peer handshake to have arrived: callers drive the connection with
		// `while (Process())`, and Process() returns this value. Gating it on the inbound
		// handshake made the loop exit before the peer's reply could arrive (it only "worked"
		// when the reply was already buffered, e.g. low-latency peers). Handshake completion
		// is observed separately via RemoteHandshake / the HandshakeComplete event.
		public bool Connected
		{
			get => socket != null && socket.Connected;
		}

		public PeerClientHandshake RemoteHandshake { get => incomingHandshake; }

		public PeerWireConnection()
		{

		}

		public PeerWireConnection(ISocket _socket)
		{
			socket = _socket;
			ConfigureSocket(socket);
		}

		// Common socket setup applied wherever a socket is (re)created for this connection.
		private void ConfigureSocket(ISocket target)
		{
			target.NoDelay = true;
			target.ReceiveTimeout = Timeout * 1000;
			target.SendTimeout = Timeout * 1000;
		}

		// Clears the MSE/PE negotiation state shared by connect/disconnect teardown.
		private void ResetEncryptionState()
		{
			handshakeBuffer = Array.Empty<byte>();
			sendCipher = null;
			receiveCipher = null;
			encryptionHandshakeCompleted = false;
			encryptionNegotiated = false;
		}

		public void Connect(IPEndPoint endPoint)
		{
			_lastEndPoint = endPoint;
			socket = new T();
			ConfigureSocket(socket);

			incomingHandshake = null;
			ResetEncryptionState();

			socket.Connect(endPoint);
		}

		public void Disconnect()
		{
			socket.Disconnect(true);
			socket = null;
			receiving = false;
			ResetEncryptionState();
		}

		public void Listen(EndPoint ep)
		{
			socket ??= new T();
			socket.Bind(ep);
			socket.Listen(10);
		}

		public IPeerConnection Accept()
		{
			var connection = new PeerWireConnection<T>(socket.Accept())
			{
				Timeout = Timeout,
				EncryptionMode = EncryptionMode
			};

			foreach (var infoHash in EncryptionOptions.GetKnownInfoHashes())
			{
				connection.EncryptionOptions.AddKnownInfoHash(infoHash);
			}

			connection.EncryptionOptions.SupportedTypes = EncryptionOptions.SupportedTypes;
			connection.EncryptionOptions.MaxPaddingBytes = EncryptionOptions.MaxPaddingBytes;

			return connection;
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
					socket.Send(EncodeOutgoing(packet.GetBytes()));
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
			if (EncryptionMode != PeerEncryptionMode.PlainText && !encryptionHandshakeCompleted)
			{
				if (EncryptionMode == PeerEncryptionMode.PreferEncryption && _lastEndPoint != null)
				{
					try
					{
						BeginOutgoingEncryption(handshake.InfoHash);
					}
					catch
					{
						// MSE failed — the TCP stream is corrupted from the peer's
						// perspective. Reconnect cleanly and fall back to plaintext.
						sendCipher = null;
						receiveCipher = null;
						encryptionHandshakeCompleted = false;
						ReconnectSocket();
					}
				}
				else
				{
					BeginOutgoingEncryption(handshake.InfoHash);
				}
			}

			socket.Send(EncodeOutgoing(PeerHandshakeCodec.Build(handshake)));
		}

		private void ReconnectSocket()
		{
			try { socket.Disconnect(true); } catch { }
			socket = new T();
			ConfigureSocket(socket);
			socket.Connect(_lastEndPoint);
		}

		private void BeginOutgoingEncryption(string infoHash)
		{
			if ((EncryptionOptions.SupportedTypes & PeerEncryptionType.RC4) != PeerEncryptionType.RC4)
			{
				throw new InvalidOperationException("MSE/PE currently requires RC4 support");
			}

			// In PreferEncryption mode we additionally offer plaintext, so the peer may
			// negotiate an unencrypted payload instead of dropping the connection.
			var cryptoProvide = EncryptionOptions.SupportedTypes;
			if (EncryptionMode == PeerEncryptionMode.PreferEncryption)
			{
				cryptoProvide |= PeerEncryptionType.PlainText;
			}

			var localPublicKey = mseHandshake.CreateLocalPublicKey(out var privateKey);
			socket.Send(localPublicKey.Cat(mseHandshake.CreatePadding(EncryptionOptions)));

			var remotePublicKey = MseFraming.ReadExact(socket, DhPublicKeyLength);

			mseHandshake.CompleteOutgoing(socket, remotePublicKey, privateKey, infoHash, EncryptionOptions, cryptoProvide, out var outSend, out var outReceive);
			sendCipher = outSend;
			receiveCipher = outReceive;
			encryptionNegotiated = true;
			// Null ciphers mean the peer negotiated a plaintext payload; the connection is
			// not encrypted but the MSE exchange itself completed successfully.
			encryptionHandshakeCompleted = outSend != null;
		}

		private void ReceiveCallback(IAsyncResult asyncResult)
		{
			var localSocket = socket;
			if (localSocket == null)
			{
				receiving = false;
				return;
			}

			int dataLength;
			try
			{
				dataLength = localSocket.EndReceive(asyncResult);
			}
			catch (SocketException)
			{
				receiving = false;
				return;
			}
			catch (ObjectDisposedException)
			{
				receiving = false;
				return;
			}

			try
			{
				var socketBufferCopy = socketBuffer.GetBytes(0, dataLength);

				if (incomingHandshake == null)
				{
					if (dataLength == 0)
					{
						receiving = false;
						return;
					}

					if (receiveCipher != null && encryptionHandshakeCompleted)
					{
						socketBufferCopy = receiveCipher.Process(socketBufferCopy);
					}
					else if (!encryptionNegotiated && handshakeBuffer.Length == 0
						&& (EncryptionMode == PeerEncryptionMode.RequireEncryption || socketBufferCopy[0] != 19))
					{
						// A leading 0x13 (19) normally signals a plaintext BitTorrent handshake,
						// but an MSE handshake begins with the peer's DH public key whose first
						// byte is ~uniformly random and is 0x13 roughly 1 in 256 times. In
						// RequireEncryption mode plaintext is not permitted, so a 0x13 first byte
						// must be treated as an MSE key rather than dropped. The guards ensure
						// MSE detection only runs on the very first chunk of a connection.
						if (EncryptionMode == PeerEncryptionMode.PlainText)
						{
							receiving = false;
							return;
						}

						socketBufferCopy = mseHandshake.CompleteIncoming(socket, socketBufferCopy, EncryptionOptions, EncryptionMode == PeerEncryptionMode.RequireEncryption, out _, out var inSend, out var inReceive);
						sendCipher = inSend;
						receiveCipher = inReceive;
						encryptionNegotiated = true;
						// Null ciphers mean a plaintext payload was negotiated.
						encryptionHandshakeCompleted = inReceive != null;
					}

					// Accumulate decrypted/plaintext handshake bytes until the full
					// fixed-length BitTorrent handshake has arrived. Decrypting a partial
					// handshake and then discarding it on a parse failure would advance and
					// irrecoverably desync the stream cipher.
					handshakeBuffer = handshakeBuffer.Cat(socketBufferCopy);

					if (!PeerHandshakeCodec.TryParse(handshakeBuffer, out var parsedHandshake, out var consumed))
					{
						// Not a full valid handshake yet (need more bytes, or protocol mismatch);
						// keep the accumulated bytes and wait for the next receive.
						receiving = false;
						return;
					}

					incomingHandshake = parsedHandshake;

					socketBufferCopy = handshakeBuffer.GetBytes(consumed);
					handshakeBuffer = Array.Empty<byte>();
					dataLength = socketBufferCopy.Length;
				}
				else if (receiveCipher != null)
				{
					socketBufferCopy = receiveCipher.Process(socketBufferCopy);
					dataLength = socketBufferCopy.Length;
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
			}
			catch (Exception)
			{
				// Malformed data from peer — stop receiving on this connection
			}
			finally
			{
				receiving = false;
			}
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

		private byte[] EncodeOutgoing(byte[] bytes)
		{
			return sendCipher == null ? bytes : sendCipher.Process(bytes);
		}
	}
}
