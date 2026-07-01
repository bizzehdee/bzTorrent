using System;

namespace bzTorrent.IO
{
	/// <summary>
	/// MSE/PE crypto_provide / crypto_select bitfield values, as sent on the wire.
	/// </summary>
	[Flags]
	public enum PeerEncryptionType : uint
	{
		PlainText = 0x01,
		RC4 = 0x02
	}
}
