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
using System.Net.Torrent.Misc;

namespace System.Net.Torrent
{
    public class PeerMessageBuilder : IDisposable
    {
        public UInt32 PacketLength { get { return (UInt32)(5 + MessagePayload.Count); } }
        public UInt32 MessageLength { get { return (UInt32)(1 + MessagePayload.Count); } }
        public byte MessageID { get; private set; }
        public List<byte> MessagePayload { get; private set; }

        public PeerMessageBuilder(byte msgId)
        {
            MessageID = msgId;

            MessagePayload = new List<byte>();
        }

        public PeerMessageBuilder Add(byte b)
        {
            MessagePayload.Add(b);

            return this;
        }

        public PeerMessageBuilder Add(byte[] bytes)
        {
            MessagePayload.AddRange(bytes);

            return this;
        }

        public PeerMessageBuilder Add(UInt32 n, Pack.Endianness endianness = Pack.Endianness.Big)
        {
            MessagePayload.AddRange(Pack.UInt32(n, endianness));

            return this;
        }

        public PeerMessageBuilder Add(String str)
        {
            MessagePayload.AddRange(Pack.Hex(str));

            return this;
        }

        public byte[] Message()
        {
            byte[] messageBytes = new byte[PacketLength];
            byte[] lengthBytes = Pack.UInt32(MessageLength, Pack.Endianness.Big);

            lengthBytes.CopyTo(messageBytes, 0);

            messageBytes[4] = MessageID;

            MessagePayload.CopyTo(messageBytes, 5);

            return messageBytes;
        }

        public void Dispose()
        {
            MessagePayload.Clear();
        }
    }
}
