using System.Numerics;

namespace bzTorrent.IO
{
	/// <summary>
	/// The MSE/PE handshake state machine as an abstraction, so PeerWireConnection depends on
	/// this rather than a concrete static class (and the handshake can be substituted in tests).
	/// </summary>
	internal interface IMseHandshake
	{
		byte[] CreateLocalPublicKey(out BigInteger privateKey);

		byte[] CreatePadding(PeerEncryptionOptions options);

		void CompleteOutgoing(
			ISocket socket,
			byte[] remotePublicKey,
			BigInteger privateKey,
			string infoHash,
			PeerEncryptionOptions options,
			PeerEncryptionType cryptoProvide,
			out RC4Cipher sendCipher,
			out RC4Cipher receiveCipher);

		byte[] CompleteIncoming(
			ISocket socket,
			byte[] initialBuffer,
			PeerEncryptionOptions options,
			bool requireEncryption,
			out string infoHash,
			out RC4Cipher sendCipher,
			out RC4Cipher receiveCipher);
	}
}
