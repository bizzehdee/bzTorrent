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
using bzTorrent.Data;

namespace bzTorrent
{
	public interface IPeerWireClient
	{
		public delegate void DroppedConnectionDelegate(IPeerWireClient client);
		public delegate void NoDataDelegate(IPeerWireClient client);
		public delegate void HandshakeCompleteDelegate(IPeerWireClient client);
		public delegate void KeepAliveDelegate(IPeerWireClient client);
		public delegate void ChokeDelegate(IPeerWireClient client);
		public delegate void UnChokeDelegate(IPeerWireClient client);
		public delegate void InterestedDelegate(IPeerWireClient client);
		public delegate void NotInterestedDelegate(IPeerWireClient client);
		public delegate void HaveDelegate(IPeerWireClient client, int pieceIdx);
		public delegate void BitFieldDelegate(IPeerWireClient client, int size, bool[] bitfield);
		public delegate void RequestDelegate(IPeerWireClient client, int pieceIdx, int start, int length);
		public delegate void PieceDelegate(IPeerWireClient client, int pieceIdx, int start, byte[] buffer);
		public delegate void CancelDelegate(IPeerWireClient client, int pieceIdx, int start, int length);
		public delegate bool CommandDelegate(IPeerWireClient client, int commandLength, byte commandId, byte[] payload);

		int Timeout { get; }
		bool[] PeerBitField { get; set; }
		bool KeepConnectionAlive { get; set; }

		string LocalPeerID { get; set; }
		string RemotePeerID { get; }
		string Hash { get; set; }

		event DroppedConnectionDelegate DroppedConnection;
		event NoDataDelegate NoData;
		event HandshakeCompleteDelegate HandshakeComplete;
		event KeepAliveDelegate KeepAlive;
		event ChokeDelegate Choke;
		event UnChokeDelegate UnChoke;
		event InterestedDelegate Interested;
		event NotInterestedDelegate NotInterested;
		event HaveDelegate Have;
		event BitFieldDelegate BitField;
		event RequestDelegate Request;
		event PieceDelegate Piece;
		event CancelDelegate Cancel;

		void Connect(IPEndPoint endPoint);
		void Connect(string ipHost, int port);
		void Disconnect();

		bool Handshake();
		bool Handshake(string hash, string peerId);

		bool Process();

		bool SendKeepAlive();
		bool SendChoke();
		bool SendUnChoke();
		bool SendInterested();
		bool SendNotInterested();
		bool SendHave(uint index);
		void SendBitField(bool[] bitField);
		bool SendBitField(bool[] bitField, bool obsf);
		bool SendRequest(uint index, uint start, uint length);
		bool SendPiece(uint index, uint start, byte[] data);
		bool SendCancel(uint index, uint start, uint length);

		bool SendPacket(PeerWirePacket packet);

		void RegisterBTExtension(IProtocolExtension extension);
		void UnregisterBTExtension(IProtocolExtension extension);
	}
}
