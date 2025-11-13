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

using System.Collections.Generic;
using bzTorrent.IO;
using bzTorrent.Helpers;
using bzTorrent.Data;
using bzTorrent.Protocol.Handlers;
using System;
using System.Net;
using static bzTorrent.IPeerWireClient;

namespace bzTorrent
{
	public class PeerWireClient : IPeerWireClient
	{
		public bool ReceivedHandshake { get; private set; } = false;
		private DateTime lastKeepAliveSent;

		private readonly IPeerConnection peerConnection;
		private readonly List<IProtocolExtension> _btProtocolExtensions;
		private readonly MessageDispatcher _messageDispatcher;

		public int Timeout { get => peerConnection.Timeout; }
		public bool[] PeerBitField { get; set; }
		public bool KeepConnectionAlive { get; set; }

		public string LocalPeerID { get; set; }
		public string RemotePeerID { get; private set; }
		public string Hash { get; set; }

		public event DroppedConnectionDelegate DroppedConnection;
		public event NoDataDelegate NoData;
		public event HandshakeCompleteDelegate HandshakeComplete;
		public event KeepAliveDelegate KeepAlive;
		public event ChokeDelegate Choke;
		public event UnChokeDelegate UnChoke;
		public event InterestedDelegate Interested;
		public event NotInterestedDelegate NotInterested;
		public event HaveDelegate Have;
		public event BitFieldDelegate BitField;
		public event RequestDelegate Request;
		public event PieceDelegate Piece;
		public event CancelDelegate Cancel;
		/// <summary>
		/// Return true to stop built in processing of this command
		/// </summary>
		public event CommandDelegate Command;

		public PeerWireClient(IPeerConnection io)
		{
			peerConnection = io;
			_btProtocolExtensions = new List<IProtocolExtension>();
			_messageDispatcher = new MessageDispatcher();
		}

		#region Internal event raisers (for handlers)
		/// <summary>Raise the Have event. Called by HaveHandler.</summary>
		public void RaiseHave(int pieceIndex)
		{
			Have?.Invoke(this, pieceIndex);
		}

		/// <summary>Raise the BitField event. Called by BitfieldHandler.</summary>
		public void RaiseBitField(int size, bool[] bitField)
		{
			BitField?.Invoke(this, size, bitField);
		}

		/// <summary>Raise the Request event. Called by RequestHandler.</summary>
		public void RaiseRequest(int index, int begin, int length)
		{
			Request?.Invoke(this, index, begin, length);
		}

		/// <summary>Raise the Piece event. Called by PieceHandler.</summary>
		public void RaisePiece(int index, int begin, byte[] bytes)
		{
			Piece?.Invoke(this, index, begin, bytes);
		}

		/// <summary>Raise the Cancel event. Called by RequestHandler.</summary>
		public void RaiseCancel(int index, int begin, int length)
		{
			Cancel?.Invoke(this, index, begin, length);
		}
		#endregion

		public void Connect(IPEndPoint endPoint)
		{
			ReceivedHandshake = false;
			peerConnection.Connect(endPoint);
		}

		public void Connect(string ipHost, int port)
		{
			ReceivedHandshake = false;
			peerConnection.Connect(new IPEndPoint(IPAddress.Parse(ipHost), port));
		}

		public void Disconnect()
		{
			peerConnection.Disconnect();
		}

		public bool Handshake()
		{
			return Handshake(Hash, LocalPeerID);
		}


		public bool Handshake(string hash, string peerId)
		{
			if (hash == null)
			{
				throw new ArgumentNullException(nameof(hash), "Hash cannot be null");
			}

			if (peerId == null)
			{
				throw new ArgumentNullException(nameof(peerId), "Peer ID cannot be null");
			}

			if (hash.Length != 40)
			{
				throw new ArgumentOutOfRangeException(nameof(hash), "hash must be 40 bytes exactly");
			}

			if (peerId.Length != 20)
			{
				throw new ArgumentOutOfRangeException(nameof(peerId), "Peer ID must be 20 bytes exactly");
			}

			byte[] reservedBytes = { 0, 0, 0, 0, 0, 0, 0, 0 };

			foreach (var extension in _btProtocolExtensions)
			{
				for (var x = 0; x < 8; x++)
				{
					reservedBytes[x] |= extension.ByteMask[x];
				}
			}

			var handshake = new PeerClientHandshake
			{
				InfoHash = hash,
				PeerId = peerId,
				ReservedBytes = reservedBytes
			};

			peerConnection.Handshake(handshake);

			foreach (var extension in _btProtocolExtensions)
			{
				extension.OnHandshake(this);
			}

			return true;
		}

		public bool Process()
		{
			var returnVal = InternalProcess();

			if (returnVal)
			{
				return true;
			}

			DroppedConnection?.Invoke(this);

			return false;
		}

		private bool InternalProcess()
		{
			if ((lastKeepAliveSent < DateTime.UtcNow.AddMinutes(-1)) && ReceivedHandshake)
			{
				lastKeepAliveSent = DateTime.UtcNow;
				peerConnection.Send(new PeerWirePacket { Command = PeerClientCommands.KeepAlive });
			}

			try
			{
				peerConnection.Process();
			}
			catch
			{
				peerConnection.Disconnect();
				return false;
			}

			if (ReceivedHandshake == false && peerConnection.RemoteHandshake != null)
			{
				ReceivedHandshake = true;

				RemotePeerID = peerConnection.RemoteHandshake.PeerId;

				HandshakeComplete?.Invoke(this);
			}


			if (peerConnection.HasPackets() == false)
			{
				NoData?.Invoke(this);
			}
			else
			{
				while (peerConnection.HasPackets())
				{
					var command = peerConnection.Receive();
					ProcessCommand(command);
				}
			}

			return peerConnection.Connected;
		}

		private void ProcessCommand(PeerWirePacket command)
		{
			if (Command?.Invoke(this, (int)command.CommandLength, (byte)command.Command, command.Payload) ?? false)
			{
				return;
			}

			switch (command.Command)
			{
				case PeerClientCommands.KeepAlive:
					KeepAlive?.Invoke(this);
					break;
				case PeerClientCommands.Choke:
					Choke?.Invoke(this);
					break;
				case PeerClientCommands.Unchoke:
					UnChoke?.Invoke(this);
					break;
				case PeerClientCommands.Interested:
					Interested?.Invoke(this);
					break;
				case PeerClientCommands.NotInterested:
					NotInterested?.Invoke(this);
					break;
				default:
					// Try built-in handlers (Have, Bitfield, Request, Piece, Cancel)
					if (_messageDispatcher.Dispatch(this, command))
					{
						return;
					}

					// Fall through to extension handlers
					foreach (var extension in _btProtocolExtensions)
					{
						if (!extension.CommandIDs.Contains(b => b == (byte)command.Command))
						{
							continue;
						}

						if (extension.OnCommand(this, (int)command.CommandLength, (byte)command.Command, command.Payload))
						{
							break;
						}
					}
					break;
			}
		}

		public bool SendKeepAlive()
		{
			peerConnection.Send(new PeerMessageBuilder(128).Message());

			return true;
		}

		public bool SendChoke()
		{
			peerConnection.Send(new PeerMessageBuilder(0).Message());

			return true;
		}

		public bool SendUnChoke()
		{
			peerConnection.Send(new PeerMessageBuilder(1).Message());

			return true;
		}

		public bool SendInterested()
		{
			peerConnection.Send(new PeerMessageBuilder(2).Message());

			return true;
		}

		public bool SendNotInterested()
		{
			peerConnection.Send(new PeerMessageBuilder(3).Message());

			return true;
		}

		public bool SendHave(uint index)
		{
			peerConnection.Send(new PeerMessageBuilder(4).Add(index).Message());

			return true;
		}

		public void SendBitField(bool[] bitField)
		{
			SendBitField(bitField, false);
		}

		public bool SendBitField(bool[] bitField, bool obsf)
		{
			var obsfIDs = Array.Empty<uint>();

			if (obsf && bitField.Length > 32)
			{
				var rand = new Random();
				var obsfCount = (uint)Math.Min(16, bitField.Length / 16);
				var distObsf = 0;
				obsfIDs = new uint[obsfCount];

				while (distObsf < obsfCount)
				{
					var piece = (uint)rand.Next(0, bitField.Length);
					if (obsfIDs.Contains(piece))
					{
						continue;
					}

					obsfIDs[distObsf] = piece;
					distObsf++;
				}
			}

			var bytes = new byte[bitField.Length / 8];

			for (uint i = 0; i < bitField.Length; i++)
			{
				if (obsfIDs.Contains(i))
				{
					continue;
				}

				var x = (int)Math.Floor((double)i / 8);
				var p = (ushort)(i % 8);

				if (bitField[i])
				{
					bytes[x] = bytes[x].SetBit(p);
				}
			}

			peerConnection.Send(new PeerMessageBuilder(5).Add(bytes).Message());

			if (obsfIDs.Length <= 0)
			{
				return true;
			}

			foreach (var obsfID in obsfIDs)
			{
				SendHave(obsfID);
			}

			return true;
		}

		public bool SendRequest(uint index, uint start, uint length)
		{
			peerConnection.Send(new PeerMessageBuilder(6).Add(index).Add(start).Add(length).Message());

			return true;
		}

		public bool SendPiece(uint index, uint start, byte[] data)
		{
			peerConnection.Send(new PeerMessageBuilder(7).Add(index).Add(start).Add(data).Message());

			return true;
		}

		public bool SendCancel(uint index, uint start, uint length)
		{
			peerConnection.Send(new PeerMessageBuilder(8).Add(index).Add(start).Add(length).Message());

			return true;
		}

		#region Processors
		// Processors moved to Protocol.Handlers.* classes
		// - ProcessHave -> HaveHandler
		// - ProcessBitfield -> BitfieldHandler
		// - ProcessRequest -> RequestHandler
		// - ProcessPiece -> PieceHandler
		#endregion

		#region Event Dispatchers
		// Event dispatcher methods (OnHave, OnBitField, OnRequest, OnPiece, OnCancel) are no longer needed.
		// Events are now invoked directly from the handler classes.
		#endregion

		public void RegisterBTExtension(IProtocolExtension extension)
		{
			_btProtocolExtensions.Add(extension);
		}

		public void UnregisterBTExtension(IProtocolExtension extension)
		{
			_btProtocolExtensions.Remove(extension);
		}

		public bool SendPacket(PeerWirePacket packet)
		{
			peerConnection.Send(packet);

			return true;
		}
	}
}
