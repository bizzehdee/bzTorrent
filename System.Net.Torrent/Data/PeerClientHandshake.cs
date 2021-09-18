using System;
using System.Collections.Generic;
using System.Text;

namespace System.Net.Torrent.Data
{
	public class PeerClientHandshake
	{
		public string ProtocolHeader { get; set; } = "BitTorrent protocol";
		public string PeerId { get; set; }
		public string InfoHash { get; set; }
		public byte[] ReservedBytes { get; set; }
	}
}
