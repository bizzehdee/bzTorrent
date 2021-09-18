using System;
using System.Collections.Generic;
using System.Text;

namespace System.Net.Torrent.Data
{
	public enum PeerClientCommands : byte
	{
		/*Base bt protocol commands*/
		Choke = 0,
		Unchoke = 1,
		Interested = 2,
		NotInterested = 3,
		Have = 4,
		Bitfield = 5,
		Request = 6,
		Piece = 7,
		Cancel = 8,

		/*Extension commands*/
		DHTPort = 9,

		SuggestPiece = 13,
		HaveAll = 14,
        HaveNone = 15,
        Reject = 16,
        AllowedFast = 17,

		ExtendedProtocol = 20,

		/*Keep alive command, not in the protocol, only here for ease*/
		KeepAlive = 128
	}
}
