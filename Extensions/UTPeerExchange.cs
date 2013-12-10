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

        public void OnExtendedMessage(PeerWireClient peerWireClient, byte[] bytes)
        {
            BDict d = (BDict) BencodingUtils.Decode(bytes);
        }
    }
}
