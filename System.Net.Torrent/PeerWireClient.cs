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
using System.Diagnostics;
using System.Net.Sockets;
using System.Net.Torrent.IO;
using System.Net.Torrent.Helpers;
using System.Text;
using System.Threading;

namespace System.Net.Torrent
{
    public class PeerWireClient : IPeerWireClient
    {
        private readonly object _locker = new();
        private bool _asyncContinue = true;
        private readonly byte[] _bitTorrentProtocolHeader = { 0x42, 0x69, 0x74, 0x54, 0x6F, 0x72, 0x72, 0x65, 0x6E, 0x74, 0x20, 0x70, 0x72, 0x6F, 0x74, 0x6F, 0x63, 0x6F, 0x6C };

        internal readonly IWireIO Socket;
        private byte[] _internalBuffer; //async internal buffer
        private readonly List<IProtocolExtension> _btProtocolExtensions;
        private bool _handshakeSent;
        private bool _handshakeComplete;
        private bool _receiving;
        private IAsyncResult _async;

        private const int MinBufferSize = 1024;
        private const int MaxBufferSize = 1024 * 256;
        private int _dynamicBufferSize = 1024*16;
        

        public int Timeout { get => Socket.Timeout; }
        public bool[] PeerBitField { get; set; }
        public bool KeepConnectionAlive { get; set; }
        
        public bool RemoteUsesDHT { get; private set; }
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
        

        public PeerWireClient(IWireIO io)
        {
            Socket = io;

            _btProtocolExtensions = new List<IProtocolExtension>();

            _internalBuffer = new byte[0];
        }

        public void Connect(IPEndPoint endPoint)
        {
            Socket.Connect(endPoint);
        }

        public void Connect(string ipHost, int port)
        {
            Socket.Connect(new IPEndPoint(IPAddress.Parse(ipHost), port));
        }

        public void Disconnect()
        {
            if(_async != null)
            {
                Socket.EndReceive(_async);
            }

            Socket.Disconnect();
        }

        public bool Handshake()
        {
            return Handshake(PackHelper.Hex(Hash), Encoding.ASCII.GetBytes(LocalPeerID));
        }

        public bool Handshake(string hash, string peerId)
        {
            LocalPeerID = peerId;
            Hash = hash;

            return Handshake();
        }

        public bool Handshake(byte[] hash, byte[] peerId)
        {
            if (hash == null)
            {
                throw new ArgumentNullException("hash", "Hash cannot be null");
            }

            if (peerId == null)
            {
                throw new ArgumentNullException("peerId", "Peer ID cannot be null");
            }

            if (hash.Length != 20)
            {
                throw new ArgumentOutOfRangeException("hash", "hash must be 20 bytes exactly");
            }

            if (peerId.Length != 20)
            {
                throw new ArgumentOutOfRangeException("peerId", "Peer ID must be 20 bytes exactly");
            }

            byte[] reservedBytes = {0, 0, 0, 0, 0, 0, 0, 0};

            foreach (var extension in _btProtocolExtensions)
            {
                for (var x = 0; x < 8; x++)
                {
                    reservedBytes[x] |= extension.ByteMask[x];
                }
            }

            var sendBuf = (new[] { (byte)_bitTorrentProtocolHeader.Length }).Cat(_bitTorrentProtocolHeader).Cat(reservedBytes).Cat(hash).Cat(peerId);

            try
            {
                var len = Socket.Send(sendBuf);
                if (len != sendBuf.Length)
                {
                    throw new Exception("Didnt sent entire handshake");
                }
            }
            catch (SocketException ex)
            {
                Trace.TraceInformation(ex.Message);
                return false;
            }

            foreach (var extension in _btProtocolExtensions)
            {
                extension.OnHandshake(this);
            }

            _handshakeSent = true;

            return true;
        }

        public void ProcessAsync()
        {
            _asyncContinue = true;

            (new Thread(o =>
            {
                var client = (PeerWireClient) o;
                while (client.Process() && _asyncContinue)
                {
                    Thread.Sleep(10);
                }
            })).Start(this);
        }

        public void StopProcessAsync()
        {
            _asyncContinue = false;
        }

        public bool Process()
        {
            var returnVal = InternalProcess();

            if (returnVal)
            {
                return true;
            }

            if (Socket.Connected)
            {
                Socket.Disconnect();
            }

            DroppedConnection?.Invoke(this);

            return false;
        }

        private bool InternalProcess()
        {
            if (!_receiving)
            {
                var recBuffer = new byte[_dynamicBufferSize];
                try
                {
                    _receiving = true;

                    _async = Socket.BeginReceive(recBuffer, 0, _dynamicBufferSize, OnReceived, recBuffer);
                }
                catch(Exception ex)
                {
                    Trace.TraceInformation(ex.Message);
                    return false;
                }


            }

            if (_internalBuffer.Length < 4)
            {
                OnNoData();

                return Socket.Connected;
            }

            if (!_handshakeComplete)
            {
                int resLen = _internalBuffer[0];
                if (resLen != 19)
                {
                    if (resLen == 0)
                    {
                        // keep alive?
                        Thread.Sleep(100);

                        Disconnect();
                        return false;
                    }
                }

                _handshakeComplete = true;

                var recReserved = _internalBuffer.GetBytes(20, 8);
                RemoteUsesDHT = (recReserved[7] & 0x1) == 0x1;

                var remoteHashBytes = _internalBuffer.GetBytes(28, 20);
                if (string.IsNullOrEmpty(Hash))
                {
                    var remoteHash = UnpackHelper.Hex(remoteHashBytes);
                    Hash = remoteHash;
                }

                var remoteIdbytes = _internalBuffer.GetBytes(48, 20);

                RemotePeerID = Encoding.ASCII.GetString(remoteIdbytes);

                lock (_locker)
                {
                    _internalBuffer = _internalBuffer.GetBytes(68);
                }

                OnHandshake();

                if (_handshakeSent)
                {
                    return true;
                }

                Handshake();

                return true;
            }

            var commandLength = UnpackHelper.Int32(_internalBuffer, 0, UnpackHelper.Endianness.Big);

            if (commandLength > (_internalBuffer.Length - 4))
            {
                //need more data first
                return true;
            }

            lock (_locker)
            {
                _internalBuffer = _internalBuffer.GetBytes(4);
            }

            if (commandLength == 0)
            {
                if (!KeepConnectionAlive)
                {
                    return true;
                }

                SendKeepAlive();
                OnKeepAlive();

                return true;
            }

            int commandId = _internalBuffer[0];

            lock (_locker)
            {
                _internalBuffer = _internalBuffer.GetBytes(1);
            }

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
                    ProcessBitfield(commandLength - 1);
                    break;
                case 6:
                    //request
                    ProcessRequest(false);
                    break;
                case 7:
                    //piece
                    ProcessPiece(commandLength - 1);
                    break;
                case 8:
                    //cancel
                    ProcessRequest(true);
                    break;
                default:
                {
                    foreach (var extension in _btProtocolExtensions)
                    {
                        if (!extension.CommandIDs.Contains(b => b == commandId))
                        {
                            continue;
                        }

                        if (extension.OnCommand(this, commandLength, (byte)commandId, _internalBuffer))
                        {
                            break;
                        }
                    }

                    lock (_locker)
                    {
                            _internalBuffer = _internalBuffer.GetBytes(commandLength - 1);
                    }
                }
                    break;
            }

            return true;
        }

        private void OnReceived(IAsyncResult ar)
        {
            if (Socket == null)
            {
                return;
            }

            var data = (byte[])ar.AsyncState;

            var len = Socket.EndReceive(ar);

            _async = null;

            if (len > 0)
            {
                lock (_locker)
                {
                    _internalBuffer = _internalBuffer == null ? data : _internalBuffer.Cat(data.GetBytes(0, len));
                }

                #region Automatically alter the buffer size
                if (_internalBuffer.Length > _dynamicBufferSize && (_dynamicBufferSize - 1024) >= MinBufferSize)
                {
                    _dynamicBufferSize -= 1024;
                }

                if (_internalBuffer.Length < _dynamicBufferSize && (_dynamicBufferSize + 1024) <= MaxBufferSize)
                {
                    _dynamicBufferSize += 1024;
                }
                #endregion
            }

            _receiving = false;

        }

        public bool SendKeepAlive()
        {
            var sent = Socket.Send(PackHelper.Int32(0));

            return sent == 4;
        }

        public bool SendChoke()
        {
            var sent = Socket.Send(new PeerMessageBuilder(0).Message());

            return sent == 5;
        }

        public bool SendUnChoke()
        {
            var sent = Socket.Send(new PeerMessageBuilder(1).Message());

            return sent == 5;
        }

        public bool SendInterested()
        {
            var sent = Socket.Send(new PeerMessageBuilder(2).Message());

            return sent == 5;
        }

        public bool SendNotInterested()
        {
            var sent = Socket.Send(new PeerMessageBuilder(3).Message());

            return sent == 5;
        }

        public bool SendHave(uint index)
        {
            var sent = Socket.Send(new PeerMessageBuilder(4).Add(index).Message());

            return sent == 9;
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

            var sent = Socket.Send(new PeerMessageBuilder(5).Add(bytes).Message());

            if (obsfIDs.Length > 0)
            {
                foreach (var obsfID in obsfIDs)
                {
                    SendHave(obsfID);
                }
            }

            return sent == (5 + bitField.Length);
        }

        public bool SendRequest(uint index, uint start, uint length)
        {
            var sent = Socket.Send(new PeerMessageBuilder(6).Add(index).Add(start).Add(length).Message());

            return sent == 17;
        }

        public bool SendPiece(uint index, uint start, byte[] data)
        {
            var sent = Socket.Send(new PeerMessageBuilder(7).Add(index).Add(start).Add(data).Message());

            return (sent == 13 + data.Length);
        }

        public bool SendCancel(uint index, uint start, uint length)
        {
            var sent = Socket.Send(new PeerMessageBuilder(8).Add(index).Add(start).Add(length).Message());

            return sent == 13;
        }

        public bool SendBytes(byte[] bytes)
        {
            var sent = Socket.Send(bytes);

            return sent == bytes.Length;
        }

        #region Processors
        private void ProcessHave()
        {
            var pieceIndex = UnpackHelper.Int32(_internalBuffer, 0, UnpackHelper.Endianness.Big);

            lock (_locker)
            {
                _internalBuffer = _internalBuffer.GetBytes(4);
            }

            OnHave(pieceIndex);
        }

        private void ProcessBitfield(int length)
        {
            if (_internalBuffer.Length < length)
            {
                //not sent entire bitfield, kill the connection
                Disconnect();
                return;
            }

            PeerBitField = new bool[length * 8];
            for (var i = 0; i < length; i++)
            {
                var b = _internalBuffer[0];

                PeerBitField[(i * 8) + 0] = b.GetBit(0);
                PeerBitField[(i * 8) + 1] = b.GetBit(1);
                PeerBitField[(i * 8) + 2] = b.GetBit(2);
                PeerBitField[(i * 8) + 3] = b.GetBit(3);
                PeerBitField[(i * 8) + 4] = b.GetBit(4);
                PeerBitField[(i * 8) + 5] = b.GetBit(5);
                PeerBitField[(i * 8) + 6] = b.GetBit(6);
                PeerBitField[(i * 8) + 7] = b.GetBit(7);

                lock (_locker)
                {
                    _internalBuffer = _internalBuffer.GetBytes(1);
                }
            }

            OnBitField(length*8, PeerBitField);
        }


        private void ProcessRequest(bool cancel)
        {
            var index = UnpackHelper.Int32(_internalBuffer, 0, UnpackHelper.Endianness.Big);
            var begin = UnpackHelper.Int32(_internalBuffer, 4, UnpackHelper.Endianness.Big);
            var length = UnpackHelper.Int32(_internalBuffer, 8, UnpackHelper.Endianness.Big);

            lock (_locker)
            {
                _internalBuffer = _internalBuffer.GetBytes(12);
            }

            if (!cancel)
            {
                OnRequest(index, begin, length);
            }
            else
            {
                OnCancel(index, begin, length);
            }
        }

        private void ProcessPiece(int length)
        {
            var index = UnpackHelper.Int32(_internalBuffer, 0, UnpackHelper.Endianness.Big);
            var begin = UnpackHelper.Int32(_internalBuffer, 4, UnpackHelper.Endianness.Big);

            lock (_locker)
            {
                _internalBuffer = _internalBuffer.GetBytes(8);
            }

            var buffer = _internalBuffer.GetBytes(0, length - 8);

            lock (_locker)
            {
                _internalBuffer = _internalBuffer.GetBytes(length - 8);
            }

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
    }
}
