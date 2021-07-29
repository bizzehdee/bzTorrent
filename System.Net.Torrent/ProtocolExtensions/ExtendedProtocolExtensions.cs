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

namespace System.Net.Torrent.ProtocolExtensions
{
    using System.Collections.Generic;
    using System.Net.Torrent.BEncode;
    using System.Net.Torrent.Helpers;
    using System.Text;

    public class ExtendedProtocolExtensions : IProtocolExtension
    {
        public class ClientProtocolIDMap
        {
            public ClientProtocolIDMap(IPeerWireClient client, string protocol, byte commandId)
            {
                this.Client = client;
                this.Protocol = protocol;
                this.CommandID = commandId;
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
            this._protocolExtensions = new List<IBTExtension>();
            this._extOutgoing = new List<ClientProtocolIDMap>();
            this._extIncoming = new List<ClientProtocolIDMap>();
        }

        public byte[] ByteMask
        {
            get { return new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00 }; }
        }

        public byte[] CommandIDs
        {
            get { 
                return new byte[]
                {
                    20 //extended protocol
                }; 
            }
        }

        public bool OnHandshake(IPeerWireClient client)
        {
            BDict handshakeDict = new BDict();
            BDict mDict = new BDict();
            byte i = 1;
            foreach (IBTExtension extension in this._protocolExtensions)
            {
                this._extOutgoing.Add(new ClientProtocolIDMap(client, extension.Protocol, i));
                mDict.Add(extension.Protocol, new BInt(i));
                i++;
            }

            handshakeDict.Add("m", mDict);

            string handshakeEncoded = BencodingUtils.EncodeString(handshakeDict);
            byte[] handshakeBytes = Encoding.ASCII.GetBytes(handshakeEncoded);
            Int32 length = 2 + handshakeBytes.Length;

            client.SendBytes((new byte[0]).Cat(PackHelper.Int32(length).Cat(new[] { (byte)20 }).Cat(new[] { (byte)0 }).Cat(handshakeBytes)));

            return true;
        }

        public bool OnCommand(IPeerWireClient client, int commandLength, byte commandId, byte[] payload)
        {
            if (commandId == 20)
            {
                this.ProcessExtended(client, commandLength, payload);
                return true;
            }

            return false;
        }

        public void RegisterProtocolExtension(IPeerWireClient client, IBTExtension extension)
        {
            this._protocolExtensions.Add(extension);
            extension.Init(this);
        }

        public void UnregisterProtocolExtension(IPeerWireClient client, IBTExtension extension)
        {
            this._protocolExtensions.Remove(extension);
            extension.Deinit();
        }

        public byte GetOutgoingMessageID(IPeerWireClient client, IBTExtension extension)
        {
            ClientProtocolIDMap map = this._extOutgoing.Find(f => f.Client == client && f.Protocol == extension.Protocol);

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
            foreach (IBTExtension protocolExtension in this._protocolExtensions)
            {
                if (protocolExtension.Protocol == protocol)
                {
                    return protocolExtension;
                }
            }

            return null;
        }

        private string FindIBTProtocolByMessageID(int messageId)
        {
            foreach (ClientProtocolIDMap map in this._extIncoming)
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
            Int32 msgId = payload[0];

            byte[] buffer = payload.GetBytes(1, commandLength - 1);

            if (msgId == 0)
            {
                BDict extendedHandshake = (BDict)BencodingUtils.Decode(buffer);

                BDict mDict = (BDict)extendedHandshake["m"];
                foreach (KeyValuePair<string, IBencodingType> pair in mDict)
                {
                    BInt i = (BInt)pair.Value;
                    this._extIncoming.Add(new ClientProtocolIDMap(client, pair.Key, (byte)i));

                    IBTExtension ext = this.FindIBTExtensionByProtocol(pair.Key);

                    if (ext != null)
                    {
                        ext.OnHandshake(client, buffer);
                    }
                }
            }
            else
            {
                string protocol = this.FindIBTProtocolByMessageID(msgId);
                IBTExtension ext = this.FindIBTExtensionByProtocol(protocol);

                if (ext != null)
                {
                    ext.OnExtendedMessage(client, buffer);
                }
            }
        }
    }
}
