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
using System;
using System.Net;
using System.Threading.Tasks;

namespace bzTorrent
{
	public class PeerWireClient : IPeerWireClient
	{
		public bool ReceivedHandshake { get; private set; } = false;
		private DateTime lastKeepAliveSent;

		private readonly IPeerConnection peerConnection;
		private readonly List<IProtocolExtension> _btProtocolExtensions;

		public int Timeout { get => peerConnection.Timeout; }
		public bool[] PeerBitField { get; set; }
		public bool KeepConnectionAlive { get; set; }

		public string LocalPeerID { get; set; }
		public string RemotePeerID { get; private set; }
		public string Hash { get; set; }

		public event Action<IPeerWireClient> DroppedConnection;
		public event Action<IPeerWireClient> NoData;
		public event Action<IPeerWireClient> HandshakeComplete;
		public event Action<IPeerWireClient> KeepAlive;
		public event Action<IPeerWireClient> Choke;
		public event Action<IPeerWireClient> UnChoke;
		public event Action<IPeerWireClient> Interested;
		public event Action<IPeerWireClient> NotInterested;
		public event Action<IPeerWireClient, int> Have;
		public event Action<IPeerWireClient, int, bool[]> BitField;
		public event Action<IPeerWireClient, int, int, int> Request;
		public event Action<IPeerWireClient, int, int, byte[]> Piece;
		public event Action<IPeerWireClient, int, int, int> Cancel;


		public PeerWireClient(IPeerConnection io)
		{
			peerConnection = io;
			_btProtocolExtensions = new List<IProtocolExtension>();
		}

		public async Task Connect(IPEndPoint endPoint)
		{
			ReceivedHandshake = false;
			await peerConnection.Connect(endPoint);
		}

		public async Task Connect(string ipHost, int port)
		{
			ReceivedHandshake = false;
			await peerConnection.Connect(new IPEndPoint(IPAddress.Parse(ipHost), port));
		}

		public void Disconnect()
		{
			peerConnection.Disconnect();
		}

		public async Task<bool> Handshake()
		{
			return await Handshake(Hash, LocalPeerID);
		}


		public async Task<bool> Handshake(string hash, string peerId)
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

			await peerConnection.Handshake(handshake);

			foreach (var extension in _btProtocolExtensions)
			{
				extension.OnHandshake(this);
			}

			return true;
		}

		public async Task<bool> Process()
		{
			var returnVal = await InternalProcess();

			if (returnVal)
			{
				return true;
			}

			DroppedConnection?.Invoke(this);

			return false;
		}

		private async Task<bool> InternalProcess()
		{
			if ((lastKeepAliveSent == null || lastKeepAliveSent < DateTime.UtcNow.AddMinutes(-1)) && ReceivedHandshake)
			{
				lastKeepAliveSent = DateTime.UtcNow;
				peerConnection.Send(new PeerWirePacket { Command = PeerClientCommands.KeepAlive });
			}

			try
			{
				await peerConnection.Process();
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

				OnHandshake();
			}


			if (peerConnection.HasPackets() == false)
			{
				OnNoData();
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

			switch (command.Command)
			{
				case PeerClientCommands.KeepAlive:
					OnKeepAlive();
					break;
				case PeerClientCommands.Choke:
					//choke
					OnChoke();
					break;
				case PeerClientCommands.Unchoke:
					//unchoke
					OnUnChoke();
					break;
				case PeerClientCommands.Interested:
					//interested
					OnInterested();
					break;
				case PeerClientCommands.NotInterested:
					//not interested
					OnNotInterested();
					break;
				case PeerClientCommands.Have:
					//have
					ProcessHave(command);
					break;
				case PeerClientCommands.Bitfield:
					//bitfield
					ProcessBitfield(command);
					break;
				case PeerClientCommands.Request:
					//request
					ProcessRequest(command, false);
					break;
				case PeerClientCommands.Piece:
					//piece
					ProcessPiece(command);
					break;
				case PeerClientCommands.Cancel:
					//cancel
					ProcessRequest(command, true);
					break;
				default:
					{
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
			var obsfIDs = new uint[0];

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

			if (obsfIDs.Length > 0)
			{
				foreach (var obsfID in obsfIDs)
				{
					SendHave(obsfID);
				}
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
		private void ProcessHave(PeerWirePacket packet)
		{
			var pieceIndex = UnpackHelper.Int32(packet.Payload, 0, UnpackHelper.Endianness.Big);

			OnHave(pieceIndex);
		}

		private void ProcessBitfield(PeerWirePacket packet)
		{
			if (packet.Payload.Length < packet.CommandLength)
			{
				//not sent entire bitfield, kill the connection
				Disconnect();
				return;
			}

			var bitfieldLength = packet.Payload.Length;

			PeerBitField = new bool[bitfieldLength * 8];
			for (var i = 0; i < bitfieldLength; i++)
			{
				var b = packet.Payload[i];

				PeerBitField[(i * 8) + 0] = b.GetBit(0);
				PeerBitField[(i * 8) + 1] = b.GetBit(1);
				PeerBitField[(i * 8) + 2] = b.GetBit(2);
				PeerBitField[(i * 8) + 3] = b.GetBit(3);
				PeerBitField[(i * 8) + 4] = b.GetBit(4);
				PeerBitField[(i * 8) + 5] = b.GetBit(5);
				PeerBitField[(i * 8) + 6] = b.GetBit(6);
				PeerBitField[(i * 8) + 7] = b.GetBit(7);
			}

			OnBitField(bitfieldLength * 8, PeerBitField);
		}


		private void ProcessRequest(PeerWirePacket packet, bool cancel)
		{
			var index = UnpackHelper.Int32(packet.Payload, 0, UnpackHelper.Endianness.Big);
			var begin = UnpackHelper.Int32(packet.Payload, 4, UnpackHelper.Endianness.Big);
			var length = UnpackHelper.Int32(packet.Payload, 8, UnpackHelper.Endianness.Big);

			if (!cancel)
			{
				OnRequest(index, begin, length);
			}
			else
			{
				OnCancel(index, begin, length);
			}
		}

		private void ProcessPiece(PeerWirePacket packet)
		{
			var index = UnpackHelper.Int32(packet.Payload, 0, UnpackHelper.Endianness.Big);
			var begin = UnpackHelper.Int32(packet.Payload, 4, UnpackHelper.Endianness.Big);

			var buffer = packet.Payload.GetBytes(8);

			OnPiece(index, begin, buffer);
		}
		#endregion

		#region Event Dispatchers

		private void OnNoData()
		{
			NoData?.Invoke(this);
		}

		private void OnHandshake()
		{
			HandshakeComplete?.Invoke(this);
		}

		private void OnKeepAlive()
		{
			KeepAlive?.Invoke(this);
		}

		private void OnChoke()
		{
			Choke?.Invoke(this);
		}

		private void OnUnChoke()
		{
			UnChoke?.Invoke(this);
		}

		private void OnInterested()
		{
			Interested?.Invoke(this);
		}

		private void OnNotInterested()
		{
			NotInterested?.Invoke(this);
		}

		private void OnHave(int pieceIndex)
		{
			Have?.Invoke(this, pieceIndex);
		}

		private void OnBitField(int size, bool[] bitField)
		{
			BitField?.Invoke(this, size, bitField);
		}

		private void OnRequest(int index, int begin, int length)
		{
			Request?.Invoke(this, index, begin, length);
		}

		private void OnPiece(int index, int begin, byte[] bytes)
		{
			Piece?.Invoke(this, index, begin, bytes);
		}

		private void OnCancel(int index, int begin, int length)
		{
			Cancel?.Invoke(this, index, begin, length);
		}

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
