using System;
using System.Numerics;
using bzTorrent.Helpers;

namespace bzTorrent.IO
{
	/// <summary>
	/// Message Stream Encryption / Protocol Encryption (MSE/PE) handshake state machine.
	///
	/// Wire protocol (initiator A, receiver B):
	///   1. A-&gt;B: Ya, PadA
	///   2. B-&gt;A: Yb, PadB
	///   3. A-&gt;B: HASH('req1',S), HASH('req2',SKEY) xor HASH('req3',S),
	///            ENCRYPT(VC, crypto_provide, len(PadC), PadC, len(IA)), ENCRYPT(IA)
	///   4. B-&gt;A: ENCRYPT(VC, crypto_select, len(PadD), PadD), ENCRYPT2(IB)
	///   5. A-&gt;B: ENCRYPT2(payload...)
	///
	/// Ya/Yb are 96-byte big-endian Diffie-Hellman public keys; S is the 96-byte shared
	/// secret; SKEY is the 20-byte infohash; VC is eight zero bytes. ENCRYPT() is RC4 keyed
	/// with HASH('keyA'|'keyB', S, SKEY) (A encrypts with keyA, B with keyB) after discarding
	/// the first 1024 keystream bytes. ENCRYPT2 is the negotiated payload method (continue RC4,
	/// or plaintext). All multi-byte integers are big-endian.
	/// </summary>
	internal sealed class MseHandshake : IMseHandshake
	{
		private const int KeyLength = 96;         // 768-bit DH key / shared secret
		private const int HashLength = 20;        // SHA-1 digest
		private const int Rc4Discard = 1024;      // keystream bytes discarded per RC4 engine
		private const int VcLength = 8;           // verification constant (all zero)
		private const int PrivateKeyBytes = 20;   // 160-bit exponent

		// MSE's 768-bit DH prime, generator 2. NOT the standard RFC 2409 Oakley Group 1 prime —
		// MSE's P differs from it in the low 68 bits. Every real client (libtorrent, uTorrent,
		// etc.) uses this exact constant; using the textbook RFC 2409 prime instead makes the
		// shared secret silently diverge from every real peer's, breaking MSE against them while
		// still working self-to-self or against a peer seeded with the same wrong constant.
		private static readonly BigInteger Prime = MseCrypto.ParseBigEndian(
			"FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD1" +
			"29024E088A67CC74020BBEA63B139B22514A08798E3404DD" +
			"EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245" +
			"E485B576625E7EC6F44C42E9A63A36210000000000090563");
		private static readonly BigInteger Generator = new BigInteger(2);

		public byte[] CreateLocalPublicKey(out BigInteger privateKey)
		{
			privateKey = MseCrypto.ParseBigEndian(MseCrypto.RandomBytes(PrivateKeyBytes));
			return MseCrypto.ToBigEndian(BigInteger.ModPow(Generator, privateKey, Prime), KeyLength);
		}

		/// <summary>Random plaintext padding (0..MaxPaddingBytes) sent after a DH key (PadA/PadB).</summary>
		public byte[] CreatePadding(PeerEncryptionOptions options)
		{
			return MseCrypto.RandomBytes(MseCrypto.RandomInt(options.MaxPaddingBytes + 1));
		}

		/// <summary>
		/// Runs the initiator half. <paramref name="remotePublicKey"/> is Yb (already read from
		/// the socket by the caller); this method sends step 3 and reads step 4. On return the
		/// out ciphers are the live streams for subsequent traffic, or null if a plaintext
		/// payload was negotiated.
		/// </summary>
		public void CompleteOutgoing(
			ISocket socket,
			byte[] remotePublicKey,
			BigInteger privateKey,
			string infoHash,
			PeerEncryptionOptions options,
			PeerEncryptionType cryptoProvide,
			out RC4Cipher sendCipher,
			out RC4Cipher receiveCipher)
		{
			var skey = HexCodec.Decode(infoHash);
			var secret = ComputeSharedSecret(remotePublicKey, privateKey);

			var keyA = CreateCipher("keyA", secret, skey);

			// Step 3: req1 and (req2 xor req3) are plaintext; the rest is RC4(keyA).
			var req1 = MseCrypto.Hash(MseCrypto.Ascii("req1"), secret);
			var req2 = MseCrypto.Xor(MseCrypto.Hash(MseCrypto.Ascii("req2"), skey), MseCrypto.Hash(MseCrypto.Ascii("req3"), secret));
			var padC = CreatePadding(options);
			var negotiation = MseCrypto.Concat(
				new byte[VcLength],
				PackHelper.UInt32((uint)cryptoProvide),
				PackHelper.UInt16((ushort)padC.Length),
				padC,
				PackHelper.UInt16(0));                 // len(IA) = 0; the BitTorrent handshake follows as ENCRYPT2
			socket.Send(MseCrypto.Concat(req1, req2, keyA.Process(negotiation)));

			// Step 4: locate B's ENCRYPT(VC). PadB is plaintext of unknown length, so scan the
			// raw stream for the byte pattern that keyB produces for the 8 zero VC bytes.
			var keyB = CreateCipher("keyB", secret, skey);
			var encryptedVc = keyB.Process(new byte[VcLength]);
			MseFraming.ScanForNeedle(() => MseFraming.ReadExact(socket, 1)[0], encryptedVc, options.MaxPaddingBytes, "MSE verification constant not found");

			// Fresh keyB stream positioned immediately after the VC we just consumed raw.
			receiveCipher = CreateCipher("keyB", secret, skey);
			receiveCipher.Skip(VcLength);

			var head = receiveCipher.Process(MseFraming.ReadExact(socket, 6));   // crypto_select(4) + len(PadD)(2)
			var selected = (PeerEncryptionType)UnpackHelper.UInt32(head, 0, Endianness.Big);
			var padDLength = UnpackHelper.UInt16(head, 4, Endianness.Big);
			if (padDLength > 0)
			{
				receiveCipher.Process(MseFraming.ReadExact(socket, padDLength)); // consume PadD to stay aligned
			}

			if ((selected & PeerEncryptionType.RC4) == PeerEncryptionType.RC4)
			{
				sendCipher = keyA;                 // ENCRYPT2 continues the RC4 streams
			}
			else if ((selected & PeerEncryptionType.PlainText) == PeerEncryptionType.PlainText
				&& (cryptoProvide & PeerEncryptionType.PlainText) == PeerEncryptionType.PlainText)
			{
				sendCipher = null;                 // payload is plaintext; framing was still RC4
				receiveCipher = null;
			}
			else
			{
				throw new InvalidOperationException("Peer selected an unsupported MSE crypto method");
			}
		}

		/// <summary>
		/// Runs the receiver half. Reads Ya (from <paramref name="initialBuffer"/> plus the
		/// socket), sends Yb, processes step 3 and sends step 4. Returns the decrypted initial
		/// payload (IA plus any buffered ENCRYPT2 bytes); out ciphers are null if plaintext was
		/// negotiated.
		/// </summary>
		public byte[] CompleteIncoming(
			ISocket socket,
			byte[] initialBuffer,
			PeerEncryptionOptions options,
			bool requireEncryption,
			out string infoHash,
			out RC4Cipher sendCipher,
			out RC4Cipher receiveCipher)
		{
			var knownInfoHashes = options.GetKnownInfoHashes();
			if (knownInfoHashes.Count == 0)
			{
				throw new InvalidOperationException("KnownInfoHashes must contain at least one infohash for incoming MSE");
			}

			var stream = new MseFraming.StreamBuffer(initialBuffer);

			var remotePublicKey = stream.Read(socket, KeyLength);                       // Ya
			var localPublicKey = CreateLocalPublicKey(out var privateKey);
			socket.Send(MseCrypto.Concat(localPublicKey, CreatePadding(options)));       // step 2: Yb, PadB

			var secret = ComputeSharedSecret(remotePublicKey, privateKey);

			// Skip PadA by scanning the stream for the plaintext HASH('req1', S).
			MseFraming.ScanForNeedle(() => stream.Read(socket, 1)[0], MseCrypto.Hash(MseCrypto.Ascii("req1"), secret), options.MaxPaddingBytes, "MSE req1 marker not found");

			var maskedHash = stream.Read(socket, HashLength);                            // HASH('req2',SKEY) xor HASH('req3',S)
			var req3 = MseCrypto.Hash(MseCrypto.Ascii("req3"), secret);
			var wantedReq2 = MseCrypto.Xor(maskedHash, req3);                            // == HASH('req2', SKEY)

			infoHash = null;
			byte[] skey = null;
			foreach (var candidate in knownInfoHashes)
			{
				var candidateBytes = HexCodec.Decode(candidate);
				if (MseCrypto.BytesEqual(wantedReq2, MseCrypto.Hash(MseCrypto.Ascii("req2"), candidateBytes)))
				{
					infoHash = candidate;
					skey = candidateBytes;
					break;
				}
			}

			if (infoHash == null)
			{
				throw new InvalidOperationException("Incoming MSE handshake did not match a known infohash");
			}

			var keyA = CreateCipher("keyA", secret, skey);   // decrypts the initiator's data
			var keyB = CreateCipher("keyB", secret, skey);   // encrypts our data

			var negotiation = keyA.Process(stream.Read(socket, VcLength + 6));           // VC(8)+provide(4)+len(PadC)(2)
			for (var i = 0; i < VcLength; i++)
			{
				if (negotiation[i] != 0)
				{
					throw new InvalidOperationException("Invalid MSE verification constant");
				}
			}

			var provided = (PeerEncryptionType)UnpackHelper.UInt32(negotiation, VcLength, Endianness.Big);
			var padCLength = UnpackHelper.UInt16(negotiation, VcLength + 4, Endianness.Big);
			if (padCLength > 0)
			{
				keyA.Process(stream.Read(socket, padCLength));
			}

			var iaLength = UnpackHelper.UInt16(keyA.Process(stream.Read(socket, 2)), 0, Endianness.Big);   // len(IA)
			var initialPayload = iaLength == 0
				? Array.Empty<byte>()
				: keyA.Process(stream.Read(socket, iaLength));                           // ENCRYPT(IA), keyA always

			PeerEncryptionType selected;
			if ((provided & PeerEncryptionType.RC4) == PeerEncryptionType.RC4)
			{
				selected = PeerEncryptionType.RC4;
			}
			else if (!requireEncryption && (provided & PeerEncryptionType.PlainText) == PeerEncryptionType.PlainText)
			{
				selected = PeerEncryptionType.PlainText;
			}
			else
			{
				throw new InvalidOperationException("No mutually supported MSE crypto method");
			}

			// Step 4 framing is always RC4(keyB); only the payload after it honours crypto_select.
			socket.Send(keyB.Process(MseCrypto.Concat(new byte[VcLength], PackHelper.UInt32((uint)selected), PackHelper.UInt16(0))));

			if (selected == PeerEncryptionType.RC4)
			{
				sendCipher = keyB;
				receiveCipher = keyA;
				return MseCrypto.Concat(initialPayload, keyA.Process(stream.DrainBuffered()));
			}

			// Plaintext payload: bytes after IA are in the clear.
			sendCipher = null;
			receiveCipher = null;
			return MseCrypto.Concat(initialPayload, stream.DrainBuffered());
		}

		// The 96-byte DH shared secret S. Extracted because both handshake halves compute it
		// identically.
		private static byte[] ComputeSharedSecret(byte[] remotePublicKey, BigInteger privateKey)
		{
			return MseCrypto.ToBigEndian(BigInteger.ModPow(MseCrypto.ParseBigEndian(remotePublicKey), privateKey, Prime), KeyLength);
		}

		private static RC4Cipher CreateCipher(string label, byte[] secret, byte[] skey)
		{
			var cipher = new RC4Cipher(MseCrypto.Hash(MseCrypto.Ascii(label), secret, skey));
			cipher.Skip(Rc4Discard);
			return cipher;
		}
	}
}
