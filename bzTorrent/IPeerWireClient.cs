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
        int Timeout { get; }
        bool[] PeerBitField { get; set; }
        bool KeepConnectionAlive { get; set; }
        
        string LocalPeerID { get; set; }
        string RemotePeerID { get; }
        string Hash { get; set; }

        event Action<IPeerWireClient> DroppedConnection;
        event Action<IPeerWireClient> NoData;
        event Action<IPeerWireClient> HandshakeComplete;
        event Action<IPeerWireClient> KeepAlive;
        event Action<IPeerWireClient> Choke;
        event Action<IPeerWireClient> UnChoke;
        event Action<IPeerWireClient> Interested;
        event Action<IPeerWireClient> NotInterested;
        event Action<IPeerWireClient, int> Have;
        event Action<IPeerWireClient, int, bool[]> BitField;
        event Action<IPeerWireClient, int, int, int> Request;
        event Action<IPeerWireClient, int, int, byte[]> Piece;
        event Action<IPeerWireClient, int, int, int> Cancel;

        void Connect(IPEndPoint endPoint);
        void Connect(string ipHost, int port);
        void Disconnect();

        bool Handshake();
        bool Handshake(string hash, string peerId);

        void ProcessAsync();
        void StopProcessAsync();
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
