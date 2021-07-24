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

namespace System.Net.Torrent.Extensions
{
    using System.Linq;
    using System.Net.Torrent.BEncode;
    using System.Net.Torrent.Helpers;
    using System.Net.Torrent.ProtocolExtensions;
    using System.Text;

    public class UTMetadata : IBTExtension
    {
        private Int64 _metadataSize;
        private Int64 _pieceCount;
        private Int64 _piecesReceived;
        private byte[] _metadataBuffer;
        private ExtendedProtocolExtensions _parent;

        public string Protocol
        {
            get { return "ut_metadata"; }
        }

        public event Action<IPeerWireClient, IBTExtension, BDict> MetaDataReceived;

        public void Init(ExtendedProtocolExtensions parent)
        {
            this._parent = parent;
            this._metadataBuffer = new byte[0];
        }

        public void Deinit()
        {

        }

        public void OnHandshake(IPeerWireClient peerWireClient, byte[] handshake)
        {
            BDict dict = (BDict)BencodingUtils.Decode(handshake);
            if (dict.ContainsKey("metadata_size"))
            {
                BInt size = (BInt)dict["metadata_size"];
                this._metadataSize = size;
                this._pieceCount = (Int64)Math.Ceiling((double)this._metadataSize / 16384);
            }

            this.RequestMetaData(peerWireClient);
        }

        public void OnExtendedMessage(IPeerWireClient peerWireClient, byte[] bytes)
        {
            Int32 startAt = 0;
            BencodingUtils.Decode(bytes, ref startAt);
            this._piecesReceived += 1;

            if (this._pieceCount >= this._piecesReceived)
            {
                this._metadataBuffer = this._metadataBuffer.Concat(bytes.Skip(startAt)).ToArray();
            }

            if (this._pieceCount == this._piecesReceived)
            {
                BDict metadata = (BDict)BencodingUtils.Decode(this._metadataBuffer);

                if (MetaDataReceived != null)
                {
                    MetaDataReceived(peerWireClient, this, metadata);
                }
            }
        }

        public void RequestMetaData(IPeerWireClient peerWireClient)
        {
            byte[] sendBuffer = new byte[0];

            for (Int32 i = 0; i < this._pieceCount; i++)
            {
                BDict masterBDict = new BDict
                {
                    {"msg_type", (BInt) 0}, 
                    {"piece", (BInt) i}
                };

                string encoded = BencodingUtils.EncodeString(masterBDict);

                byte[] buffer = PackHelper.Int32(2 + encoded.Length);
                buffer = buffer.Concat(new byte[] {20}).ToArray();
                buffer = buffer.Concat(new[] { this._parent.GetOutgoingMessageID(peerWireClient, this) }).ToArray();
                buffer = buffer.Concat(Encoding.GetEncoding(1252).GetBytes(encoded)).ToArray();

                sendBuffer = sendBuffer.Concat(buffer).ToArray();
            }

            peerWireClient.SendBytes(sendBuffer);
        }

    }
}
