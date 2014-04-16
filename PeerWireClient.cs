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

		internal readonly IWireIO Socket;
        private byte[] _internalBuffer; //async internal buffer
        private readonly List<IBTExtension> _protocolExtensions;
		private readonly Dictionary<String, byte> _extOutgoing = new Dictionary<string, byte>();
		private readonly Dictionary<byte, String> _extIncoming = new Dictionary<byte, String>();
	    private bool _handshakeComplete;
	    private IAsyncResult _async;

	    private int _dynamicBufferSize = 1024;
	    private int _maxBufferSize = 1024*256;

        public Int32 Timeout { get { return Socket.Timeout; } }
        public bool[] PeerBitField { get; set; }
        public bool KeepConnectionAlive { get; set; }
        public bool UseExtended { get; set; }
		public bool UseFast { get; set; }
		public bool UseDHT { get; set; }
        public bool RemoteUsesExtended { get; private set; }
        public bool RemoteUsesFast { get; private set; }
		public bool RemoteUsesDHT { get; private set; }
        public String LocalPeerID { get; set; }
        public String RemotePeerID { get; private set; }
        public String Hash { get; set; }

		public event Action<PeerWireClient> NoData;
	    public event Action<PeerWireClient> HandshakeComplete;
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

	    public PeerWireClient(IWireIO io)
	    {
		    Socket = io;

			_protocolExtensions = new List<IBTExtension>();

			_internalBuffer = new byte[0];
	    }

        public void Connect(IPEndPoint endPoint)
        {
            Socket.Connect(endPoint);
        }

        public void Connect(String ipHost, Int32 port)
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
            return Handshake(Pack.Hex(Hash), Encoding.ASCII.GetBytes(LocalPeerID));
        }

        public bool Handshake(String hash, String peerId)
        {
            LocalPeerID = peerId;
            Hash = hash;

            return Handshake();
        }

		public bool Handshake(byte[] hash, byte[] peerId)
        {
            if (hash == null) throw new ArgumentNullException("hash", "Hash cannot be null");
            if (peerId == null) throw new ArgumentNullException("peerId", "Peer ID cannot be null");

            if (hash.Length != 20) throw new ArgumentOutOfRangeException("hash", "hash must be 20 bytes exactly");
            if (peerId.Length != 20) throw new ArgumentOutOfRangeException("peerId", "Peer ID must be 20 bytes exactly");

            byte[] reservedBytes = {0, 0, 0, 0, 0, 0, 0, 0};
			reservedBytes[5] |= (byte)(UseExtended ? 0x10 : 0x00);
			reservedBytes[7] |= (byte)(UseFast ? 0x04 : 0x00);
			reservedBytes[7] |= (byte)(UseDHT ? 0x1 : 0x00);

            byte[] sendBuf = (new[] { (byte)_bitTorrentProtocolHeader.Length }).Concat(_bitTorrentProtocolHeader).Concat(reservedBytes).Concat(hash).Concat(peerId).ToArray();

            if (UseExtended)
            {
                BDict handshakeDict = new BDict();
                BDict mDict = new BDict();
				byte i = 1;
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
					return false;
				}
            }

			byte[] recBuffer = new byte[_dynamicBufferSize];
			_async = Socket.BeginReceive(recBuffer, 0, _dynamicBufferSize, OnReceived, recBuffer);

			return true;
        }

        public bool SendKeepAlive()
        {
            int sent = Socket.Send(Pack.Int32(0));

			return sent == 4;
        }

		public bool SendChoke()
        {
			int sent = Socket.Send(new PeerMessageBuilder(0).Message());

			return sent == 5;
        }

		public bool SendUnChoke()
        {
			int sent = Socket.Send(new PeerMessageBuilder(1).Message());

			return sent == 5;
        }

		public bool SendInterested()
		{
			int sent = Socket.Send(new PeerMessageBuilder(2).Message());

			return sent == 5;
        }

		public bool SendNotInterested()
        {
			int sent = Socket.Send(new PeerMessageBuilder(3).Message());

			return sent == 5;
        }

		public bool SendHave(UInt32 index)
        {
			int sent = Socket.Send(new PeerMessageBuilder(4).Add(index).Message());

			return sent == 9;
        }

        public void SendBitField(bool[] bitField)
        {
            SendBitField(bitField, false);
        }

        public bool SendBitField(bool[] bitField, bool obsf)
        {
			UInt32[] obsfIDs = new UInt32[0];

            if (obsf && bitField.Length > 32)
            {
				Random rand = new Random();
				UInt32 obsfCount = (UInt32)Math.Min(16, bitField.Length / 16);
				UInt32 distObsf = 0;
				obsfIDs = new UInt32[obsfCount];

				while (distObsf < obsfCount)
				{
					UInt32 piece = (UInt32)rand.Next(0, bitField.Length);
					if (obsfIDs.Contains(piece)) continue;

					obsfIDs[distObsf] = piece;
					distObsf++;
				}
            }

            byte[] bytes = new byte[bitField.Length / 8];

			for (UInt32 i = 0; i < bitField.Length; i++)
            {
                if (obsfIDs.Contains(i)) continue;

                int x = (int)Math.Floor((double)i/8);
                ushort p = (ushort) (i%8);

	            if (bitField[i])
	            {
		            bytes[x] = bytes[x].SetBit(p);
	            }
            }

			int sent = Socket.Send(new PeerMessageBuilder(5).Add(bytes).Message());

	        if (obsfIDs.Length > 0)
	        {
				foreach (UInt32 obsfID in obsfIDs)
		        {
			        SendHave(obsfID);
		        }
	        }

	        return sent == (5 + bitField.Length);
        }

		public bool SendRequest(UInt32 index, UInt32 start, UInt32 length)
        {
			int sent = Socket.Send(new PeerMessageBuilder(6).Add(index).Add(start).Add(length).Message());

	        return sent == 17;
        }

		public bool SendPiece(UInt32 index, UInt32 start, byte[] data)
		{
			int sent = Socket.Send(new PeerMessageBuilder(7).Add(index).Add(start).Add(data).Message());

			return (sent == 13 + data.Length);
		}

		public bool SendCancel(UInt32 index, UInt32 start, UInt32 length)
		{
			int sent = Socket.Send(new PeerMessageBuilder(8).Add(index).Add(start).Add(length).Message());

			return sent == 13;
		}

		public bool SendExtended(byte extMsgId, byte[] bytes)
		{
			Int32 length = 2 + bytes.Length;

			int sent = Socket.Send(new PeerMessageBuilder(20).Add(extMsgId).Add(bytes).Message());

			return sent == (4 + length);
		}

        public void OnReceived(IAsyncResult ar)
        {
            if (Socket == null) return;

            byte[] data = (byte[])ar.AsyncState;

            Int32 len = Socket.EndReceive(ar);

	        _async = null;

            lock (_locker)
            {
                _internalBuffer = _internalBuffer == null ? data : _internalBuffer.Concat(data.Take(len)).ToArray();
            }

	        if (_dynamicBufferSize < _internalBuffer.Length && _dynamicBufferSize < _maxBufferSize)
	        {
		        _dynamicBufferSize += 1024;
	        }

			byte[] recBuffer = new byte[_dynamicBufferSize];

	        if (Socket.Connected)
	        {
				_async = Socket.BeginReceive(recBuffer, 0, _dynamicBufferSize, OnReceived, recBuffer);
	        }
        }

        public bool Process()
        {
            if (_internalBuffer.Length < 4)
            {
	            OnNoData();

	            return Socket.Connected;
            }

	        if (!_handshakeComplete)
			{
				Int32 resLen = _internalBuffer[0];
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

				byte[] recReserved = _internalBuffer.Skip(20).Take(8).ToArray();
				RemoteUsesExtended = (recReserved[5] & 0x10) == 0x10;
				RemoteUsesFast = (recReserved[7] & 0x04) == 0x04;
				RemoteUsesDHT = (recReserved[7] & 0x1) == 0x1;

				byte[] remoteIdbytes = _internalBuffer.Skip(48).Take(20).ToArray();

				RemotePeerID = Encoding.ASCII.GetString(remoteIdbytes);

				lock (_locker)
				{
					_internalBuffer = _internalBuffer.Skip(68).ToArray();
				}

				OnHandshake();

				return true;
			}

            Int32 commandLength = Unpack.Int32(_internalBuffer, 0, Unpack.Endianness.Big);

	        if (commandLength > _internalBuffer.Length)
	        {
				//need more data first
		        return true;
	        }

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
			Int32 index = Unpack.Int32(_internalBuffer, 0, Unpack.Endianness.Big);
			Int32 begin = Unpack.Int32(_internalBuffer, 4, Unpack.Endianness.Big);

			lock (_locker)
            {
                _internalBuffer = _internalBuffer.Skip(8).ToArray();
            }

			byte[] buffer = _internalBuffer.Take(length-8).ToArray();

	        lock (_locker)
	        {
				_internalBuffer = _internalBuffer.Skip(length-8).ToArray();
	        }

			OnPiece(index, begin, buffer);
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
	        lock (_locker)
	        {
		        _internalBuffer = _internalBuffer.Skip(1).ToArray();
	        }
            
            byte[] buffer = _internalBuffer.Take(length-1).ToArray();
	        lock (_locker)
	        {
		        _internalBuffer = _internalBuffer.Skip(length - 1).ToArray();
	        }

            if (msgId == 0)
            {
                BDict extendedHandshake = (BDict) BencodingUtils.Decode(buffer);

                BDict mDict = (BDict)extendedHandshake["m"];
                foreach (KeyValuePair<string, IBencodingType> pair in mDict)
                {
                    BInt i = (BInt)pair.Value;
                    _extIncoming.Add((byte)i, pair.Key);

                    IBTExtension ext = _protocolExtensions.FirstOrDefault(f => f.Protocol == pair.Key);

                    if (ext != null)
                    {
                        ext.OnHandshake(this, buffer);
                    }
                }
            }
            else
            {
				KeyValuePair<byte, String> pair = _extIncoming.FirstOrDefault(f => f.Key == msgId);
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

	        lock (_locker)
	        {
		        _internalBuffer = _internalBuffer.Skip(4).ToArray();
	        }

            OnAllowFast(index);
        }

        #endregion

        #region Event Dispatchers

		private void OnNoData()
		{
			if (NoData != null)
			{
				NoData(this);
			}
		}

		private void OnHandshake()
		{
			if (HandshakeComplete != null)
			{
				HandshakeComplete(this);
			}
		}

        private void OnKeepAlive()
        {
	        if (KeepAlive != null)
	        {
		        KeepAlive(this);
	        }
        }

        private void OnChoke()
        {
	        if (Choke != null)
	        {
		        Choke(this);
	        }
        }

        private void OnUnChoke()
        {
	        if (UnChoke != null)
	        {
		        UnChoke(this);
	        }
        }

        private void OnInterested()
        {
	        if (Interested != null)
	        {
		        Interested(this);
	        }
        }

        private void OnNotInterested()
        {
	        if (NotInterested != null)
	        {
		        NotInterested(this);
	        }
        }

        private void OnHave(Int32 pieceIndex)
        {
	        if (Have != null)
	        {
		        Have(this, pieceIndex);
	        }
        }

        private void OnBitField(Int32 size, bool[] bitField)
        {
	        if (BitField != null)
	        {
		        BitField(this, size, bitField);
	        }
        }

        private void OnRequest(Int32 index, Int32 begin, Int32 length)
        {
	        if (Request != null)
	        {
		        Request(this, index, begin, length);
	        }
        }

        private void OnPiece(Int32 index, Int32 begin, byte[] bytes)
        {
	        if (Piece != null)
	        {
		        Piece(this, index, begin, bytes);
	        }
        }

		private void OnCancel(Int32 index, Int32 begin, Int32 length)
        {
			if (Cancel != null)
			{
				Cancel(this, index, begin, length);
			}
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
	        if (HaveAll != null)
	        {
		        HaveAll(this);
	        }
        }

		private void OnHaveNone()
		{
			if (HaveNone != null)
			{
				HaveNone(this);
			}
		}

		private void OnReject(Int32 index, Int32 begin, Int32 length)
		{
			if (Reject != null)
			{
				Reject(this, index, begin, length);
			}
		}

        private void OnAllowFast(Int32 pieceIndex)
        {
	        if (AllowedFast != null)
	        {
		        AllowedFast(this, pieceIndex);
	        }
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

        public byte GetOutgoingMessageID(IBTExtension extension)
        {
            if (_extOutgoing.ContainsKey(extension.Protocol))
            {
                return _extOutgoing[extension.Protocol];
            }

            return 0;
        }
    }
}
