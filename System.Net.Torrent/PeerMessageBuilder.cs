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

namespace System.Net.Torrent
{
    using System.Collections.Generic;
    using System.Net.Torrent.Helpers;

    public class PeerMessageBuilder : IDisposable
    {
        public UInt32 PacketLength { get { return (UInt32)(5 + this.MessagePayload.Count); } }
        public UInt32 MessageLength { get { return (UInt32)(1 + this.MessagePayload.Count); } }
        public byte MessageID { get; private set; }
        public List<byte> MessagePayload { get; private set; }

        public PeerMessageBuilder(byte msgId)
        {
            this.MessageID = msgId;

            this.MessagePayload = new List<byte>();
        }

        public PeerMessageBuilder Add(byte b)
        {
            this.MessagePayload.Add(b);

            return this;
        }

        public PeerMessageBuilder Add(byte[] bytes)
        {
            this.MessagePayload.AddRange(bytes);

            return this;
        }

        public PeerMessageBuilder Add(UInt32 n, PackHelper.Endianness endianness = PackHelper.Endianness.Big)
        {
            this.MessagePayload.AddRange(PackHelper.UInt32(n));

            return this;
        }

        public PeerMessageBuilder Add(string str)
        {
            this.MessagePayload.AddRange(PackHelper.Hex(str));

            return this;
        }

        public byte[] Message()
        {
            byte[] messageBytes = new byte[this.PacketLength];
            byte[] lengthBytes = PackHelper.UInt32(this.MessageLength);

            lengthBytes.CopyTo(messageBytes, 0);

            messageBytes[4] = this.MessageID;

            this.MessagePayload.CopyTo(messageBytes, 5);

            return messageBytes;
        }

        public void Dispose()
        {
            this.MessagePayload.Clear();
        }
    }
}
