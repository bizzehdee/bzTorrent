namespace bzTorrent.IO
{
	/// <summary>
	/// Controls how a connection negotiates MSE/PE encryption.
	/// </summary>
	public enum PeerEncryptionMode
	{
		/// <summary>No MSE; the BitTorrent handshake is sent/received in the clear.</summary>
		PlainText,

		/// <summary>Attempt MSE, but fall back to plaintext if the peer will not encrypt.</summary>
		PreferEncryption,

		/// <summary>Require MSE; refuse any connection that is not encrypted.</summary>
		RequireEncryption
	}
}
