using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Torrent.bencode;
using System.Text;

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

        public void SendMessage(PeerWireClient peerWireClient, IPEndPoint[] addedEndPoints, byte[] flags)
        {

        }
    }
}
