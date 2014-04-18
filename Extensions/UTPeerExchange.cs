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

using System.Net.Torrent.bencode;

namespace System.Net.Torrent.Extensions
{
    public class UTPeerExchange : IBTExtension
    {
        public string Protocol
        {
            get { return "ut_pex"; }
        }

        public event Action<PeerWireClient, IBTExtension, IPEndPoint, byte> Added;
        public event Action<PeerWireClient, IBTExtension, IPEndPoint> Dropped;

        public void Init(PeerWireClient peerWireClient)
        {
            
        }

        public void Deinit(PeerWireClient peerWireClient)
        {

        }

        public void OnHandshake(PeerWireClient peerWireClient, byte[] handshake)
        {
            BDict d = (BDict)BencodingUtils.Decode(handshake);
        }

        public void OnExtendedMessage(PeerWireClient peerWireClient, byte[] bytes)
        {
            BDict d = (BDict) BencodingUtils.Decode(bytes);
            if (d.ContainsKey("added") && d.ContainsKey("added.f"))
            {
                BString pexList = (BString)d["added"];
                BString pexFlags = (BString)d["added.f"];

                for (int i = 0; i < pexList.ByteValue.Length/6; i++)
                {
                    UInt32 ip = Unpack.UInt32(pexList.ByteValue, i*6, Unpack.Endianness.Little);
                    UInt16 port = Unpack.UInt16(pexList.ByteValue, (i * 6) + 4, Unpack.Endianness.Big);
                    byte flags = pexFlags.ByteValue[i];

                    IPEndPoint ipAddr = new IPEndPoint(ip, port);

                    if (Added != null)
                    {
                        Added(peerWireClient, this, ipAddr, flags);
                    }
                }
            }

            if (d.ContainsKey("dropped"))
            {
                BString pexList = (BString)d["dropped"];

                for (int i = 0; i < pexList.ByteValue.Length / 6; i++)
                {
                    UInt32 ip = Unpack.UInt32(pexList.ByteValue, i * 6, Unpack.Endianness.Little);
                    UInt16 port = Unpack.UInt16(pexList.ByteValue, (i * 6) + 4, Unpack.Endianness.Big);

                    IPEndPoint ipAddr = new IPEndPoint(ip, port);

                    if (Dropped != null)
                    {
                        Dropped(peerWireClient, this, ipAddr);
                    }
                }
            }
        }

		public void SendMessage(PeerWireClient peerWireClient, IPEndPoint[] addedEndPoints, byte[] flags, IPEndPoint[] droppedEndPoints)
		{
			if (addedEndPoints == null && droppedEndPoints == null) return;

			BDict d = new BDict();

			if (addedEndPoints != null)
			{
				byte[] added = new byte[addedEndPoints.Length * 6];
				for (int x = 0; x < addedEndPoints.Length; x++)
				{
					addedEndPoints[x].Address.GetAddressBytes().CopyTo(added, x * 6);
					BitConverter.GetBytes((ushort)addedEndPoints[x].Port).CopyTo(added, (x * 6)+4);
				}

				d.Add("added", new BString { ByteValue = added });
			}

			if (droppedEndPoints != null)
			{
				byte[] dropped = new byte[droppedEndPoints.Length * 6];
				for (int x = 0; x < droppedEndPoints.Length; x++)
				{
					droppedEndPoints[x].Address.GetAddressBytes().CopyTo(dropped, x * 6);

					dropped.SetValue((ushort)droppedEndPoints[x].Port, (x * 6) + 2);
				}

				d.Add("dropped", new BString { ByteValue = dropped });
			}

			peerWireClient.SendExtended(peerWireClient.GetOutgoingMessageID(this), BencodingUtils.EncodeBytes(d));
		}
    }
}
