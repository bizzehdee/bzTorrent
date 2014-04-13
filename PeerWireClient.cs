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
using System.Linq;
using System.Net.Sockets;
using System.Net.Torrent.bencode;
using System.Text;
using System.Threading;

namespace System.Net.Torrent
{
    public class PeerWireClient
    {
        private readonly Object _locker = new Object();
        private readonly byte[] _bitTorrentProtocolHeader = { 0x42, 0x69, 0x74, 0x54, 0x6F, 0x72, 0x72, 0x65, 0x6E, 0x74, 0x20, 0x70, 0x72, 0x6F, 0x74, 0x6F, 0x63, 0x6F, 0x6C };

        internal readonly Socket Socket;
        private byte[] _internalBuffer; //async internal buffer
        private readonly List<IBTExtension> _protocolExtensions;
        private readonly Dictionary<String, Int64> _extOutgoing = new Dictionary<string, long>();
        private readonly Dictionary<Int64, String> _extIncoming = new Dictionary<Int64, String>();

        public Int32 Timeout { get; private set; }
        public bool[] PeerBitField { get; set; }
        public bool KeepConnectionAlive { get; set; }
        public bool UseExtended { get; set; }
        public bool RemoteUsesExtended { get; private set; }
        public bool RemoteUsesFast { get; private set; }
        public bool UseFast { get; set; }
        public String LocalPeerID { get; set; }
        public String RemotePeerID { get; private set; }
        public String Hash { get; set; }

        public event Action<PeerWireClient> KeepAlive;
        public event Action<PeerWireClient> Choke;
        public event Action<PeerWireClient> UnChoke;
        public event Action<PeerWireClient> Interested;
        public event Action<PeerWireClient> NotInterested;
        public event Action<PeerWireClient, Int32> Have;
        public event Action<PeerWireClient, Int32, bool[]> BitField;
        public event Action<PeerWireClient, Int32, Int32, Int32> Request;
        public event Action<PeerWireClient, Int32, Int32, byte[]> Piece;
        public event Action<PeerWireClient, Int32, Int32, Int32> Cancel;
		public event Action<PeerWireClient, UInt16> Port;
		public event Action<PeerWireClient, Int32> SuggestPiece;
		public event Action<PeerWireClient, Int32, Int32, Int32> Reject;
        public event Action<PeerWireClient> HaveAll;
        public event Action<PeerWireClient> HaveNone;
        public event Action<PeerWireClient, Int32> AllowedFast;

        public PeerWireClient(Int32 timeout)
        {
            _protocolExtensions = new List<IBTExtension>();

            Timeout = timeout;

            Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
	            ReceiveTimeout = timeout*1000,
	            SendTimeout = timeout*1000
            };

	        _internalBuffer = new byte[0];
        }

        public PeerWireClient(Int32 timeout, Socket socket)
        {
            _protocolExtensions = new List<IBTExtension>();

            Timeout = timeout;

            Socket = socket;
            Socket.ReceiveTimeout = timeout * 1000;
            Socket.SendTimeout = timeout * 1000;

            _internalBuffer = new byte[0];
        }

        public void Connect(IPEndPoint endPoint)
        {
            Socket.Connect(endPoint);
        }

        public void Connect(String ipHost, Int32 port)
        {
            Socket.Connect(ipHost, port);
        }

        public void Disconnect()
        {
            Socket.Disconnect(false);
            Socket.Close();
        }

        public void Handshake()
        {
            Handshake(Pack.Hex(Hash), Encoding.ASCII.GetBytes(LocalPeerID));
        }

        public void Handshake(String hash, String peerId)
        {
            LocalPeerID = peerId;
            Hash = hash;

            Handshake();
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

            if (UseExtended)
            {
                BDict handshakeDict = new BDict();
                BDict mDict = new BDict();
                Int32 i = 1;
                foreach (IBTExtension extension in _protocolExtensions)
                {
                    _extOutgoing.Add(extension.Protocol, i);
                    mDict.Add(extension.Protocol, new BInt(i));
                    i++;
                }

                handshakeDict.Add("m", mDict);

                String handshakeEncoded = BencodingUtils.EncodeString(handshakeDict);
	            byte[] handshakeBytes = Encoding.ASCII.GetBytes(handshakeEncoded);
				Int32 length = 2 + handshakeBytes.Length;
				sendBuf = sendBuf.Concat(Pack.Int32(length, Pack.Endianness.Big).Concat(new[] { (byte)20 }).Concat(new[] { (byte)0 }).Concat(handshakeBytes).ToArray()).ToArray();

                Socket.Send(sendBuf);
            }
            else
            {
				try
				{
					Socket.Send(sendBuf);
				}
				catch (SocketException ex)
				{
					Trace.TraceInformation(ex.Message);
					return;
				}
            }

            byte[] readBuf = new byte[68];
            try
            {
                Socket.Receive(readBuf);
            }
            catch (SocketException ex)
            {
				Trace.TraceInformation(ex.Message);
                return;
            }

            Int32 resLen = readBuf[0];
            if (resLen != 19)
            {
                Socket.Disconnect(false);
                Socket.Close();
                throw new InvalidProgramException("Invalid response received from peer");
            }

            byte[] recReserved = readBuf.Skip(20).Take(8).ToArray();
            RemoteUsesExtended = (recReserved[5] & 0x10) == 0x10;
            RemoteUsesFast = (recReserved[7] & 0x04) == 0x04;

            byte[] recBuffer = new byte[128];
            Socket.BeginReceive(recBuffer, 0, 128, SocketFlags.None, OnReceived, recBuffer);
        }

        public void SendKeepAlive()
        {
            Socket.Send(Pack.Int32(0));
        }

        public void SendChoke()
        {
            Socket.Send(Pack.Int32(1, Pack.Endianness.Big).Concat(new byte[] { 0 }).ToArray());
        }

        public void SendUnChoke()
        {
            Socket.Send(Pack.Int32(1, Pack.Endianness.Big).Concat(new byte[] { 1 }).ToArray());
        }

        public void SendInterested()
        {
            Socket.Send(Pack.Int32(1, Pack.Endianness.Big).Concat(new byte[] { 2 }).ToArray());
        }

        public void SendNotInterested()
        {
            Socket.Send(Pack.Int32(1, Pack.Endianness.Big).Concat(new byte[] { 3 }).ToArray());
        }

        public void SendHave(Int32 index)
        {
            Socket.Send(Pack.Int32(5, Pack.Endianness.Big).Concat(new byte[] { 4 }).Concat(Pack.Int32(index)).ToArray());
        }

        public void SendBitField(bool[] bitField)
        {
            SendBitField(bitField, false);
        }

        public void SendBitField(bool[] bitField, bool obsf)
        {
			int[] obsfIDs = new int[0];

            if (obsf && bitField.Length > 32)
            {
				Random rand = new Random();
	            int obsfCount = Math.Min(16, bitField.Length/16);
	            int distObsf = 0;
				obsfIDs = new int[obsfCount];

				while (distObsf < obsfCount)
				{
					int piece = rand.Next(0, bitField.Length);
					if (obsfIDs.Contains(piece)) continue;

					obsfIDs[distObsf] = piece;
					distObsf++;
				}
            }

            byte[] bytes = new byte[bitField.Length / 8];

            for (int i = 0; i < bitField.Length; i++)
            {
                if (obsfIDs.Contains(i)) continue;

                int x = (int)Math.Floor((double)i/8);
                ushort p = (ushort) (i%8);

                if(bitField[i]) bytes[x] = bytes[x].SetBit(p);
            }

            Socket.Send(Pack.Int32(1 + bitField.Length, Pack.Endianness.Big).Concat(new byte[] { 5 }).Concat(bytes).ToArray());

	        if (obsfIDs.Length > 0)
	        {
				foreach (int obsfID in obsfIDs)
		        {
			        SendHave(obsfID);
		        }
	        }
        }

		public void SendPiece(Int32 index, Int32 start, byte[] data)
		{
			Socket.Send(Pack.Int32(9 + data.Length, Pack.Endianness.Big).Concat(new byte[] { 7 }).Concat(Pack.Int32(index)).Concat(Pack.Int32(start)).Concat(data).ToArray());
		}

        public void SendRequest(Int32 index, Int32 start, Int32 length)
        {
            Socket.Send(Pack.Int32(13, Pack.Endianness.Big).Concat(new byte[] { 6 }).Concat(Pack.Int32(index)).Concat(Pack.Int32(start)).Concat(Pack.Int32(length)).ToArray());
        }

		public void SendCancel(Int32 index, Int32 start, Int32 length)
		{
			Socket.Send(Pack.Int32(13, Pack.Endianness.Big).Concat(new byte[] { 8 }).Concat(Pack.Int32(index)).Concat(Pack.Int32(start)).Concat(Pack.Int32(length)).ToArray());
		}

		public void SendExtended(Int32 extMsgId, Int32 start, Int32 length)
		{
			
		}

        public void OnReceived(IAsyncResult ar)
        {
            if (Socket == null) return;

            byte[] data = (byte[])ar.AsyncState;

            Int32 len = Socket.EndReceive(ar);

            lock (_locker)
            {
                _internalBuffer = _internalBuffer == null ? data : _internalBuffer.Concat(data.Take(len)).ToArray();
            }

            byte[] recBuffer = new byte[128];
            if (Socket.Connected) Socket.BeginReceive(recBuffer, 0, 128, SocketFlags.None, OnReceived, recBuffer);
        }

        public bool Process()
        {
            Thread.Sleep(10);

            /*if (_socket.Connected && _socket.Available > 0)
            {
                byte[] recBuffer = new byte[_socket.Available];
                _socket.Receive(recBuffer);

                _internalBuffer = _internalBuffer == null ? recBuffer : _internalBuffer.Concat(recBuffer).ToArray();
            }*/

            if (_internalBuffer.Length < 4)
            {
                if (!Socket.Connected) return false;

                Thread.Sleep(10);
                return true;
            }

            Int32 commandLength = Unpack.Int32(_internalBuffer, 0, Unpack.Endianness.Big);

            lock (_locker)
            {
                _internalBuffer = _internalBuffer.Skip(4).ToArray();
            }

            if (commandLength == 0)
            {
                if (KeepConnectionAlive)
                {
                    SendKeepAlive();
                    OnKeepAlive();
                }

                return true;
            }

            Int32 commandId = _internalBuffer[0];

            lock (_locker)
            {
                _internalBuffer = _internalBuffer.Skip(1).ToArray();
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
                    ProcessBitfield(commandLength-1);
                    break;
                case 6:
                    //request
                    ProcessRequest(false);
                    break;
                case 7:
                    //piece
                    ProcessPiece(commandLength-1);
                    break;
                case 8:
                    //cancel
                    ProcessRequest(true);
                    break;
                case 9:
                    //port
		            ProcessPort();
                    break;
                case 13:
                    //Suggest Piece
		            ProcessSuggest();
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
		            ProcessReject();
                    break;
                case 17:
                    //Allowed Fast
                    ProcessAllowFast();
                    break;
                case 20:
                    //ext protocol
                    ProcessExtended(commandLength - 1);
                    break;
            }

            return true;
        }

        #region Processors
		private void ProcessHave()
		{
			Int32 pieceIndex = Unpack.Int32(_internalBuffer, 0, Unpack.Endianness.Big);

			lock (_locker)
			{
				_internalBuffer = _internalBuffer.Skip(4).ToArray();
			}

			PeerBitField[pieceIndex] = true;
			OnHave(pieceIndex);
		}

        private void ProcessBitfield(Int32 length)
        {
            if (_internalBuffer.Length < length)
            {
                //not sent entire bitfield, kill the connection
                Disconnect();
                return;
            }

            PeerBitField = new bool[length * 8];
            for (int i = 0; i < length; i++)
            {
                byte b = _internalBuffer[0];

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
                    _internalBuffer = _internalBuffer.Skip(1).ToArray();
                }
            }

            OnBitField(length*8, PeerBitField);
        }


        private void ProcessRequest(bool cancel)
        {
            Int32 index = Unpack.Int32(_internalBuffer, 0, Unpack.Endianness.Big);
            Int32 begin = Unpack.Int32(_internalBuffer, 4, Unpack.Endianness.Big);
            Int32 length = Unpack.Int32(_internalBuffer, 8, Unpack.Endianness.Big);

            lock (_locker)
            {
                _internalBuffer = _internalBuffer.Skip(12).ToArray();
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

		private void ProcessPort()
		{
			UInt16 port = Unpack.UInt16(_internalBuffer, 0, Unpack.Endianness.Big);

			lock (_locker)
			{
				_internalBuffer = _internalBuffer.Skip(2).ToArray();
			}

			OnPort(port);
		}

		private void ProcessSuggest()
		{
			Int32 index = Unpack.Int32(_internalBuffer, 0, Unpack.Endianness.Big);

			lock (_locker)
			{
				_internalBuffer = _internalBuffer.Skip(4).ToArray();
			}

			OnSuggest(index);
		}

        private void ProcessPiece(Int32 length)
        {
            lock (_locker)
            {
                _internalBuffer = _internalBuffer.Skip(length - 8).ToArray();
            }

            OnPiece(0, 0, null);
        }

	    private void ProcessReject()
	    {
			Int32 index = Unpack.Int32(_internalBuffer, 0, Unpack.Endianness.Big);
			Int32 begin = Unpack.Int32(_internalBuffer, 4, Unpack.Endianness.Big);
			Int32 length = Unpack.Int32(_internalBuffer, 8, Unpack.Endianness.Big);

			lock (_locker)
			{
				_internalBuffer = _internalBuffer.Skip(12).ToArray();
			}

		    OnReject(index, begin, length);
	    }

        private void ProcessExtended(Int32 length)
        {
            Int32 msgId = _internalBuffer[0];
            lock (_locker) _internalBuffer = _internalBuffer.Skip(1).ToArray();
            
            byte[] buffer = _internalBuffer.Take(length-1).ToArray();
            lock (_locker) _internalBuffer = _internalBuffer.Skip(length - 1).ToArray();

            if (msgId == 0)
            {
                BDict extendedHandshake = (BDict) BencodingUtils.Decode(buffer);

                BDict mDict = (BDict)extendedHandshake["m"];
                foreach (KeyValuePair<string, IBencodingType> pair in mDict)
                {
                    BInt i = (BInt)pair.Value;
                    _extIncoming.Add(i, pair.Key);

                    IBTExtension ext = _protocolExtensions.FirstOrDefault(f => f.Protocol == pair.Key);

                    if (ext != null)
                    {
                        ext.OnHandshake(this, buffer);
                    }
                }
            }
            else
            {
                KeyValuePair<Int64, String> pair = _extIncoming.FirstOrDefault(f => f.Key == msgId);
                IBTExtension ext = _protocolExtensions.FirstOrDefault(f => f.Protocol == pair.Value);

                if (ext != null)
                {
                    ext.OnExtendedMessage(this, buffer);
                }
            }
        }

        private void ProcessAllowFast()
        {
            Int32 index = Unpack.Int32(_internalBuffer, 0, Unpack.Endianness.Big);

            lock (_locker) _internalBuffer = _internalBuffer.Skip(4).ToArray();

            OnAllowFast(index);
        }

        #endregion

        #region Event Dispatchers

        private void OnKeepAlive()
        {
            if (KeepAlive != null) KeepAlive(this);
        }

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

        private void OnPiece(Int32 index, Int32 begin, byte[] bytes)
        {
            if (Piece != null) Piece(this, index, begin, bytes);
        }

		private void OnCancel(Int32 index, Int32 begin, Int32 length)
        {
            if (Cancel != null) Cancel(this, index, begin, length);
        }

	    private void OnPort(UInt16 port)
	    {
			if (Port != null)
			{
				Port(this, port);
			}
	    }

	    private void OnSuggest(Int32 pieceIndex)
	    {
		    if (SuggestPiece != null)
		    {
			    SuggestPiece(this, pieceIndex);
		    }
	    }

        private void OnHaveAll()
        {
            if (HaveAll != null) HaveAll(this);
        }

		private void OnHaveNone()
		{
			if (HaveNone != null) HaveNone(this);
		}

		private void OnReject(Int32 index, Int32 begin, Int32 length)
		{
			if (Reject != null) Reject(this, index, begin, length);
		}

        private void OnAllowFast(Int32 pieceIndex)
        {
            if (AllowedFast != null) AllowedFast(this, pieceIndex);
        }
        #endregion

        public void RegisterProtocolExtension(IBTExtension extension)
        {
            _protocolExtensions.Add(extension);
            extension.Init(this);
        }

        public void UnregisterProtocolExtension(IBTExtension extension)
        {
            _protocolExtensions.Remove(extension);
            extension.Deinit(this);
        }

        public Int64 GetOutgoingMessageID(IBTExtension extension)
        {
            if (_extOutgoing.ContainsKey(extension.Protocol))
            {
                return _extOutgoing[extension.Protocol];
            }

            return -1;
        }
    }
}
