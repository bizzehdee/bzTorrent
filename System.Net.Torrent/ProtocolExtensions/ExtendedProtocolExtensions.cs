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
using bzBencode;
using System.Net.Torrent.Helpers;
using System.Text;

namespace System.Net.Torrent.ProtocolExtensions
{
    public class ExtendedProtocolExtensions : IProtocolExtension
    {
        public class ClientProtocolIDMap
        {
            public ClientProtocolIDMap(IPeerWireClient client, string protocol, byte commandId)
            {
                Client = client;
                Protocol = protocol;
                CommandID = commandId;
            }

            public IPeerWireClient Client { get; set; }
            public string Protocol { get; set; }
            public byte CommandID { get; set; }
        }

        private readonly List<IBTExtension> _protocolExtensions;
        private readonly List<ClientProtocolIDMap> _extOutgoing;
        private readonly List<ClientProtocolIDMap> _extIncoming;

        public ExtendedProtocolExtensions()
        {
            _protocolExtensions = new List<IBTExtension>();
            _extOutgoing = new List<ClientProtocolIDMap>();
            _extIncoming = new List<ClientProtocolIDMap>();
        }

        public byte[] ByteMask
		{
			get => new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00 };
		}

		public byte[] CommandIDs
		{
			get => new byte[]
				{
					20 //extended protocol
                };
		}

		public bool OnHandshake(IPeerWireClient client)
        {
            var handshakeDict = new BDict();
            var mDict = new BDict();
            byte i = 1;
            foreach (var extension in _protocolExtensions)
            {
                _extOutgoing.Add(new ClientProtocolIDMap(client, extension.Protocol, i));
                mDict.Add(extension.Protocol, new BInt(i));
                i++;
            }

            handshakeDict.Add("m", mDict);

            var handshakeEncoded = BencodingUtils.EncodeString(handshakeDict);
            var handshakeBytes = Encoding.ASCII.GetBytes(handshakeEncoded);
            var length = 2 + handshakeBytes.Length;

            client.SendBytes((new byte[0]).Cat(PackHelper.Int32(length).Cat(new[] { (byte)20 }).Cat(new[] { (byte)0 }).Cat(handshakeBytes)));

            return true;
        }

        public bool OnCommand(IPeerWireClient client, int commandLength, byte commandId, byte[] payload)
        {
            if (commandId == 20)
            {
                ProcessExtended(client, commandLength, payload);
                return true;
            }

            return false;
        }

        public void RegisterProtocolExtension(IPeerWireClient client, IBTExtension extension)
        {
            _protocolExtensions.Add(extension);
            extension.Init(this);
        }

        public void UnregisterProtocolExtension(IPeerWireClient client, IBTExtension extension)
        {
            _protocolExtensions.Remove(extension);
            extension.Deinit();
        }

        public byte GetOutgoingMessageID(IPeerWireClient client, IBTExtension extension)
        {
            var map = _extOutgoing.Find(f => f.Client == client && f.Protocol == extension.Protocol);

            if (map != null)
            {
                return map.CommandID;
            }

            return 0;
        }

        public byte GetIncomingMessageID(IPeerWireClient client, IBTExtension extension)
        {
            var map = _extIncoming.Find(f => f.Client == client && f.Protocol == extension.Protocol);

            if (map != null)
            {
                return map.CommandID;
            }

            return 0;
        }

        public bool SendExtended(IPeerWireClient client, byte extMsgId, byte[] bytes)
        {
            return client.SendBytes(new PeerMessageBuilder(20).Add(extMsgId).Add(bytes).Message());
        }

        private IBTExtension FindIBTExtensionByProtocol(string protocol)
        {
            foreach (var protocolExtension in _protocolExtensions)
            {
                if (protocolExtension.Protocol == protocol)
                {
                    return protocolExtension;
                }
            }

            return null;
        }

        private string FindIBTProtocolByInternalMessageID(int messageId)
        {
            foreach (var map in _extOutgoing)
            {
                if (map.CommandID == messageId)
                {
                    return map.Protocol;
                }
            }

            return null;
        }

        private void ProcessExtended(IPeerWireClient client, int commandLength, byte[] payload)
        {
            var msgId = payload[0];

            var buffer = payload.GetBytes(1, commandLength - 2);

            if (msgId == 0)
            {
                var extendedHandshake = (BDict)BencodingUtils.Decode(buffer);

                var mDict = (BDict)extendedHandshake["m"];
                foreach (var pair in mDict)
                {
                    var i = (BInt)pair.Value;
                    _extIncoming.Add(new ClientProtocolIDMap(client, pair.Key, (byte)i));

                    var ext = FindIBTExtensionByProtocol(pair.Key);

                    if (ext != null)
                    {
                        ext.OnHandshake(client, buffer);
                    }
                }
            }
            else
            {
                var protocol = FindIBTProtocolByInternalMessageID(msgId);
                var ext = FindIBTExtensionByProtocol(protocol);

                if (ext != null)
                {
                    ext.OnExtendedMessage(client, buffer);
                }
            }
        }
    }
}
