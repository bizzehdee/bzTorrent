using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace System.Net.Torrent.Extensions
{
    public class UTMetadata : IBTExtension
    {
        public string Protocol
        {
            get { return "ut_metadata"; }
        }
        public void OnExtendedMessage(PeerWireClient peerWireClient, byte[] bytes)
        {
            
        }
    }
}
