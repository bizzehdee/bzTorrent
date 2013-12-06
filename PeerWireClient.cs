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

* Neither the name of the {organization} nor the names of its
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
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Net.Torrent.bencode;
using System.Text;
using System.Threading;

namespace System.Net.Torrent
{
    public class PeerWireClient
    {
        private readonly byte[] _bitTorrentProtocolHeader = { 0x42, 0x69, 0x74, 0x54, 0x6F, 0x72, 0x72, 0x65, 0x6E, 0x74, 0x20, 0x70, 0x72, 0x6F, 0x74, 0x6F, 0x63, 0x6F, 0x6C };

        private readonly TcpClient _client;
        private byte[] _internalBuffer;

        public Int32 Timeout { get; private set; }
        public bool[] PieceBitFild { get; set; }
        public bool KeepAlive { get; set; }

        public bool UseExtended { get; set; }
        public bool UseFast { get; set; }

        public BDict ExtendedHandshake { get; private set; }

        public event Action<PeerWireClient> Choke;
        public event Action<PeerWireClient> UnChoke;
        public event Action<PeerWireClient> Interested;
        public event Action<PeerWireClient> NotInterested;
        public event Action<PeerWireClient, Int32> Have;
        public event Action<PeerWireClient, Int32, bool[]> BitField;
        public event Action<PeerWireClient, Int32, Int32, Int32> Request;
        public event Action<PeerWireClient, Int32, Int32, Int32> Cancel;
        public event Action<PeerWireClient> HaveAll;
        public event Action<PeerWireClient> HaveNone;
        public event Action<PeerWireClient, Int32> AllowedFast;

        public PeerWireClient(Int32 timeout)
        {
            Timeout = timeout;
            _client = new TcpClient
            {
                Client =
                {
                    ReceiveTimeout = timeout*1000,
                    SendTimeout = timeout*1000
                }
            };
            _internalBuffer = new byte[0];
            KeepAlive = true;
        }

        public void Connect(IPEndPoint endPoint)
        {
            _client.Connect(endPoint);
        }

        public void Connect(String ipHost, Int32 port)
        {
            _client.Connect(ipHost, port);
        }

        public void Disconnect()
        {
            _client.Close();
        }

        public void Handshake(String hash, String peerId)
        {
            Handshake(Pack.Hex(hash), Encoding.ASCII.GetBytes(peerId));
        }

        public void Handshake(byte[] hash, byte[] peerId)
        {
            if (hash == null) throw new ArgumentNullException("hash", "Hash cannot be null");
            if (peerId == null) throw new ArgumentNullException("peerId", "Peer ID cannot be null");

            if (hash.Length != 20) throw new ArgumentOutOfRangeException("hash", "hash must be 20 bytes exactly");
            if (peerId.Length != 20) throw new ArgumentOutOfRangeException("peerId", "Peer ID must be 20 bytes exactly");

            byte[] reservedBytes = {0, 0, 0, 0, 0, 0, 0, 0};
            if(UseExtended) reservedBytes[5] |= 0x10;
            if(UseFast) reservedBytes[7] |= 0x04;

            byte[] sendBuf = (new[] { (byte)_bitTorrentProtocolHeader.Length }).Concat(_bitTorrentProtocolHeader).Concat(reservedBytes).Concat(hash).Concat(peerId).ToArray();

            _client.Client.Send(sendBuf);

            byte[] readBuf = new byte[68];
            _client.Client.Receive(readBuf);

            Int32 resLen = readBuf[0];
            if (resLen != 19)
            {
                _client.Close();
                throw new InvalidProgramException("Invalid response received from peer");
            }

            //byte[] recBuffer = new byte[128];
            //_client.Client.BeginReceive(recBuffer, 0, 128, SocketFlags.None, OnReceived, recBuffer);
        }

        public void SendChoke()
        {
            _client.Client.Send(Pack.Int32(1, Pack.Endianness.Big).Concat(new byte[] { 0 }).ToArray());
        }

        public void SendUnChoke()
        {
            _client.Client.Send(Pack.Int32(1, Pack.Endianness.Big).Concat(new byte[] { 1 }).ToArray());
        }

        public void SendInterested()
        {
            _client.Client.Send(Pack.Int32(1, Pack.Endianness.Big).Concat(new byte[] { 2 }).ToArray());
        }

        public void SendNotInterested()
        {
            _client.Client.Send(Pack.Int32(1, Pack.Endianness.Big).Concat(new byte[] { 3 }).ToArray());
        }

        public void SendHave(Int32 index)
        {
            _client.Client.Send(Pack.Int32(5, Pack.Endianness.Big).Concat(new byte[] { 4 }).Concat(Pack.Int32(index)).ToArray());
        }

        public void OnReceived(IAsyncResult ar)
        {
            if (_client.Client == null) return;

            byte[] data = (byte[])ar.AsyncState;

            Int32 len = _client.Client.EndReceive(ar);

            /*lock (_internalBuffer) */_internalBuffer = _internalBuffer == null ? data : _internalBuffer.Concat(data.Take(len)).ToArray();

            byte[] recBuffer = new byte[128];
            if (_client.Client.Connected) _client.Client.BeginReceive(recBuffer, 0, 128, SocketFlags.None, OnReceived, recBuffer);
        }

        public bool Process()
        {
            Thread.Sleep(10);

            if (_client.Client.Connected && _client.Client.Available > 0)
            {
                byte[] recBuffer = new byte[_client.Client.Available];
                _client.Client.Receive(recBuffer);

                _internalBuffer = _internalBuffer == null ? recBuffer : _internalBuffer.Concat(recBuffer).ToArray();
            }

            if (_internalBuffer.Length < 4)
            {
                if (!_client.Connected) return false;

                Thread.Sleep(10);
                return true;
            }

            Int32 commandLength = Unpack.Int32(_internalBuffer, 0, Unpack.Endianness.Big);

            /*lock (_internalBuffer) */_internalBuffer = _internalBuffer.Skip(4).ToArray();

            if (commandLength == 0)
            {
                if (KeepAlive) _client.Client.Send(Pack.Int32(0));
                return true;
            }

            Int32 commandId = _internalBuffer[0];

            /*lock (_internalBuffer) */_internalBuffer = _internalBuffer.Skip(1).ToArray();

            switch (commandId)
            {
                case 0:
                    //choke
                    OnChoke();
                    break;
                case 1:
                    //unchoke
                    OnUnChoke();
                    break;
                case 2:
                    //interested
                    OnInterested();
                    break;
                case 3:
                    //not interested
                    OnNotInterested();
                    break;
                case 4:
                    //have
                    ProcessHave();
                    break;
                case 5:
                    //bitfield
                    ProcessBitfield(commandLength-1);
                    break;
                case 6:
                    //request
                    ProcessRequest(false);
                    break;
                case 7:
                    //piece
                    _internalBuffer = _internalBuffer.Skip(commandLength-9).ToArray();
                    break;
                case 8:
                    //cancel
                    ProcessRequest(true);
                    break;
                case 9:
                    //port
                    _internalBuffer = _internalBuffer.Skip(2).ToArray();
                    break;
                case 13:
                    //Suggest Piece
                    _internalBuffer = _internalBuffer.Skip(4).ToArray();
                    break;
                case 14:
                    //have all
                    OnHaveAll();
                    break;
                case 15:
                    //have none
                    OnHaveNone();
                    break;
                case 16:
                    //Reject Request
                    _internalBuffer = _internalBuffer.Skip(12).ToArray();
                    break;
                case 17:
                    //Allowed Fast
                    ProcessAllowFast();
                    break;
                case 20:
                    //ext protocol
                    ProcessExtended(commandLength - 1);
                    break;
                default:
                    break;
            }

            return true;
        }

        #region Processors

        private void ProcessBitfield(Int32 length)
        {
            PieceBitFild = new bool[length * 8];
            for (int i = 0; i < length; i++)
            {
                byte b = 0;

                try
                {
                    b = _internalBuffer[0];
                }
                catch
                {
                    
                }

                PieceBitFild[(i * 8) + 0] = b.GetBit(0);
                PieceBitFild[(i * 8) + 1] = b.GetBit(1);
                PieceBitFild[(i * 8) + 2] = b.GetBit(2);
                PieceBitFild[(i * 8) + 3] = b.GetBit(3);
                PieceBitFild[(i * 8) + 4] = b.GetBit(4);
                PieceBitFild[(i * 8) + 5] = b.GetBit(5);
                PieceBitFild[(i * 8) + 6] = b.GetBit(6);
                PieceBitFild[(i * 8) + 7] = b.GetBit(7);

                /*lock (_internalBuffer) */_internalBuffer = _internalBuffer.Skip(1).ToArray();
            }

            OnBitField(length*8, PieceBitFild);
        }

        private void ProcessHave()
        {
            Int32 pieceIndex = Unpack.Int32(_internalBuffer, 0, Unpack.Endianness.Big);
            
            /*lock (_internalBuffer)
            {*/
                _internalBuffer = _internalBuffer.Skip(4).ToArray();
            /*}*/

            PieceBitFild[pieceIndex] = true;
            OnHave(pieceIndex);
        }

        private void ProcessRequest(bool cancel)
        {
            Int32 index = Unpack.Int32(_internalBuffer, 0, Unpack.Endianness.Big);
            _internalBuffer = _internalBuffer.Skip(4).ToArray();
            Int32 begin = Unpack.Int32(_internalBuffer, 0, Unpack.Endianness.Big);
            _internalBuffer = _internalBuffer.Skip(4).ToArray();
            Int32 length = Unpack.Int32(_internalBuffer, 0, Unpack.Endianness.Big);
            _internalBuffer = _internalBuffer.Skip(4).ToArray();

            if (!cancel)
            {
                OnRequest(index, begin, length);
            }
            else
            {
                OnCancel(index, begin, length);
            }
        }

        private void ProcessExtended(Int32 length)
        {
            Int32 msgId = _internalBuffer[0];
            _internalBuffer = _internalBuffer.Skip(1).ToArray();
            byte[] buffer = _internalBuffer.Take(length-1).ToArray();
            _internalBuffer = _internalBuffer.Skip(length-1).ToArray();

            if (msgId == 0) ExtendedHandshake = (BDict)BencodingUtils.Decode(buffer);
        }

        private void ProcessAllowFast()
        {
            Int32 index = Unpack.Int32(_internalBuffer, 0, Unpack.Endianness.Big);
            _internalBuffer = _internalBuffer.Skip(4).ToArray();

            OnAllowFast(index);
        }

        #endregion

        #region Event Dispatchers
        private void OnChoke()
        {
            if (Choke != null) Choke(this);
        }

        private void OnUnChoke()
        {
            if (UnChoke != null) UnChoke(this);
        }

        private void OnInterested()
        {
            if (Interested != null) Interested(this);
        }

        private void OnNotInterested()
        {
            if (NotInterested != null) NotInterested(this);
        }

        private void OnHave(Int32 pieceIndex)
        {
            if (Have != null) Have(this, pieceIndex);
        }

        private void OnBitField(Int32 size, bool[] bitField)
        {
            if (BitField != null) BitField(this, size, bitField);
        }

        private void OnRequest(Int32 index, Int32 begin, Int32 length)
        {
            if (Request != null) Request(this, index, begin, length);
        }

        private void OnCancel(Int32 index, Int32 begin, Int32 length)
        {
            if (Cancel != null) Cancel(this, index, begin, length);
        }

        private void OnHaveAll()
        {
            if (HaveAll != null) HaveAll(this);
        }

        private void OnHaveNone()
        {
            if (HaveNone != null) HaveNone(this);
        }

        private void OnAllowFast(Int32 pieceIndex)
        {
            if (AllowedFast != null) AllowedFast(this, pieceIndex);
        }
        #endregion
    }
}
