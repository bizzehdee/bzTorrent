using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace bzTorrent.IO
{
	/// <summary>
	/// Message Stream Encryption / Protocol Encryption (MSE/PE) handshake.
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
	internal static class MessageStreamEncryption
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
		private static readonly BigInteger Prime = ParseBigEndian(
			"FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD1" +
			"29024E088A67CC74020BBEA63B139B22514A08798E3404DD" +
			"EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245" +
			"E485B576625E7EC6F44C42E9A63A36210000000000090563");
		private static readonly BigInteger Generator = new BigInteger(2);

		public static byte[] CreateLocalPublicKey(out BigInteger privateKey)
		{
			privateKey = ParseBigEndian(RandomBytes(PrivateKeyBytes));
			return ToBigEndian(BigInteger.ModPow(Generator, privateKey, Prime), KeyLength);
		}

		/// <summary>Random plaintext padding (0..MaxPaddingBytes) sent after a DH key (PadA/PadB).</summary>
		public static byte[] CreatePadding(PeerEncryptionOptions options)
		{
			return RandomBytes(RandomInt(options.MaxPaddingBytes + 1));
		}

		/// <summary>
		/// Runs the initiator half. <paramref name="remotePublicKey"/> is Yb (already read from
		/// the socket by the caller); this method sends step 3 and reads step 4. On return the
		/// out ciphers are the live streams for subsequent traffic, or null if a plaintext
		/// payload was negotiated.
		/// </summary>
		public static void CompleteOutgoing(
			ISocket socket,
			byte[] remotePublicKey,
			BigInteger privateKey,
			string infoHash,
			PeerEncryptionOptions options,
			PeerEncryptionType cryptoProvide,
			out RC4Cipher sendCipher,
			out RC4Cipher receiveCipher)
		{
			var skey = HexToBytes(infoHash);
			var secret = ToBigEndian(BigInteger.ModPow(ParseBigEndian(remotePublicKey), privateKey, Prime), KeyLength);

			var keyA = CreateCipher("keyA", secret, skey);

			// Step 3: req1 and (req2 xor req3) are plaintext; the rest is RC4(keyA).
			var req1 = Hash(Ascii("req1"), secret);
			var req2 = Xor(Hash(Ascii("req2"), skey), Hash(Ascii("req3"), secret));
			var padC = CreatePadding(options);
			var negotiation = Concat(
				new byte[VcLength],
				UInt32BE((uint)cryptoProvide),
				UInt16BE(padC.Length),
				padC,
				UInt16BE(0));                 // len(IA) = 0; the BitTorrent handshake follows as ENCRYPT2
			socket.Send(Concat(req1, req2, keyA.Process(negotiation)));

			// Step 4: locate B's ENCRYPT(VC). PadB is plaintext of unknown length, so scan the
			// raw stream for the byte pattern that keyB produces for the 8 zero VC bytes.
			var keyB = CreateCipher("keyB", secret, skey);
			var encryptedVc = keyB.Process(new byte[VcLength]);
			ScanRawForNeedle(socket, encryptedVc, options.MaxPaddingBytes);

			// Fresh keyB stream positioned immediately after the VC we just consumed raw.
			receiveCipher = CreateCipher("keyB", secret, skey);
			receiveCipher.Skip(VcLength);

			var head = receiveCipher.Process(ReadExact(socket, 6));   // crypto_select(4) + len(PadD)(2)
			var selected = (PeerEncryptionType)ReadUInt32BE(head, 0);
			var padDLength = ReadUInt16BE(head, 4);
			if (padDLength > 0)
			{
				receiveCipher.Process(ReadExact(socket, padDLength)); // consume PadD to stay aligned
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
		public static byte[] CompleteIncoming(
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

			var stream = new StreamBuffer(initialBuffer);

			var remotePublicKey = stream.Read(socket, KeyLength);                       // Ya
			var localPublicKey = CreateLocalPublicKey(out var privateKey);
			socket.Send(Concat(localPublicKey, CreatePadding(options)));                // step 2: Yb, PadB

			var secret = ToBigEndian(BigInteger.ModPow(ParseBigEndian(remotePublicKey), privateKey, Prime), KeyLength);

			// Skip PadA by scanning the stream for the plaintext HASH('req1', S).
			ScanBufferForNeedle(socket, stream, Hash(Ascii("req1"), secret), options.MaxPaddingBytes);

			var maskedHash = stream.Read(socket, HashLength);                            // HASH('req2',SKEY) xor HASH('req3',S)
			var req3 = Hash(Ascii("req3"), secret);
			var wantedReq2 = Xor(maskedHash, req3);                                      // == HASH('req2', SKEY)

			infoHash = null;
			byte[] skey = null;
			foreach (var candidate in knownInfoHashes)
			{
				var candidateBytes = HexToBytes(candidate);
				if (BytesEqual(wantedReq2, Hash(Ascii("req2"), candidateBytes)))
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

			var provided = (PeerEncryptionType)ReadUInt32BE(negotiation, VcLength);
			var padCLength = ReadUInt16BE(negotiation, VcLength + 4);
			if (padCLength > 0)
			{
				keyA.Process(stream.Read(socket, padCLength));
			}

			var iaLength = ReadUInt16BE(keyA.Process(stream.Read(socket, 2)), 0);        // len(IA)
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
			socket.Send(keyB.Process(Concat(new byte[VcLength], UInt32BE((uint)selected), UInt16BE(0))));

			if (selected == PeerEncryptionType.RC4)
			{
				sendCipher = keyB;
				receiveCipher = keyA;
				return Concat(initialPayload, keyA.Process(stream.DrainBuffered()));
			}

			// Plaintext payload: bytes after IA are in the clear.
			sendCipher = null;
			receiveCipher = null;
			return Concat(initialPayload, stream.DrainBuffered());
		}

		private static RC4Cipher CreateCipher(string label, byte[] secret, byte[] skey)
		{
			var cipher = new RC4Cipher(Hash(Ascii(label), secret, skey));
			cipher.Skip(Rc4Discard);
			return cipher;
		}

		// --- socket / buffering helpers -------------------------------------------------

		private static byte[] ReadExact(ISocket socket, int length)
		{
			var buffer = new byte[length];
			var offset = 0;
			while (offset < length)
			{
				var read = socket.Receive(buffer, offset, length - offset);
				if (read <= 0)
				{
					throw new InvalidOperationException("Socket closed during MSE handshake");
				}

				offset += read;
			}

			return buffer;
		}

		// Reads raw bytes, one at a time, until the last needle.Length bytes equal the needle.
		// Used by the initiator to skip PadB and land on B's ENCRYPT(VC).
		private static void ScanRawForNeedle(ISocket socket, byte[] needle, int maxPadding)
		{
			var window = new byte[needle.Length];
			var filled = 0;
			var scanned = 0;
			var limit = maxPadding + needle.Length;

			while (scanned < limit)
			{
				var b = ReadExact(socket, 1)[0];
				scanned++;

				if (filled < window.Length)
				{
					window[filled++] = b;
				}
				else
				{
					Array.Copy(window, 1, window, 0, window.Length - 1);
					window[window.Length - 1] = b;
				}

				if (filled == window.Length && BytesEqual(window, needle))
				{
					return;
				}
			}

			throw new InvalidOperationException("MSE verification constant not found");
		}

		// As above, but drawing bytes from a StreamBuffer (initial data + socket). Used by the
		// receiver to skip PadA and land on HASH('req1', S).
		private static void ScanBufferForNeedle(ISocket socket, StreamBuffer stream, byte[] needle, int maxPadding)
		{
			var window = new byte[needle.Length];
			var filled = 0;
			var scanned = 0;
			var limit = maxPadding + needle.Length;

			while (scanned < limit)
			{
				var b = stream.Read(socket, 1)[0];
				scanned++;

				if (filled < window.Length)
				{
					window[filled++] = b;
				}
				else
				{
					Array.Copy(window, 1, window, 0, window.Length - 1);
					window[window.Length - 1] = b;
				}

				if (filled == window.Length && BytesEqual(window, needle))
				{
					return;
				}
			}

			throw new InvalidOperationException("MSE req1 marker not found");
		}

		// --- crypto / encoding helpers --------------------------------------------------

		private static byte[] Hash(params byte[][] parts)
		{
			using (var sha1 = SHA1.Create())
			{
				var total = 0;
				foreach (var p in parts)
				{
					total += p.Length;
				}

				var buffer = new byte[total];
				var offset = 0;
				foreach (var p in parts)
				{
					Buffer.BlockCopy(p, 0, buffer, offset, p.Length);
					offset += p.Length;
				}

				return sha1.ComputeHash(buffer);
			}
		}

		private static byte[] Xor(byte[] a, byte[] b)
		{
			var result = new byte[a.Length];
			for (var i = 0; i < a.Length; i++)
			{
				result[i] = (byte)(a[i] ^ b[i]);
			}

			return result;
		}

		private static bool BytesEqual(byte[] a, byte[] b)
		{
			if (a.Length != b.Length)
			{
				return false;
			}

			for (var i = 0; i < a.Length; i++)
			{
				if (a[i] != b[i])
				{
					return false;
				}
			}

			return true;
		}

		private static byte[] Ascii(string s)
		{
			return Encoding.ASCII.GetBytes(s);
		}

		private static byte[] Concat(params byte[][] parts)
		{
			var total = 0;
			foreach (var p in parts)
			{
				total += p.Length;
			}

			var result = new byte[total];
			var offset = 0;
			foreach (var p in parts)
			{
				Buffer.BlockCopy(p, 0, result, offset, p.Length);
				offset += p.Length;
			}

			return result;
		}

		private static byte[] UInt16BE(int value)
		{
			return new[] { (byte)(value >> 8), (byte)value };
		}

		private static byte[] UInt32BE(uint value)
		{
			return new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
		}

		private static int ReadUInt16BE(byte[] data, int offset)
		{
			return (data[offset] << 8) | data[offset + 1];
		}

		private static uint ReadUInt32BE(byte[] data, int offset)
		{
			return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16)
				| ((uint)data[offset + 2] << 8) | data[offset + 3];
		}

		private static byte[] HexToBytes(string hex)
		{
			var bytes = new byte[hex.Length / 2];
			for (var i = 0; i < bytes.Length; i++)
			{
				bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
			}

			return bytes;
		}

		// Interprets big-endian bytes as a non-negative BigInteger.
		private static BigInteger ParseBigEndian(string hex)
		{
			return ParseBigEndian(HexToBytes(hex));
		}

		private static BigInteger ParseBigEndian(byte[] bigEndian)
		{
			var littleEndian = new byte[bigEndian.Length + 1];   // extra 0 byte forces a positive sign
			for (var i = 0; i < bigEndian.Length; i++)
			{
				littleEndian[i] = bigEndian[bigEndian.Length - 1 - i];
			}

			return new BigInteger(littleEndian);
		}

		// Encodes a non-negative BigInteger as fixed-length big-endian, left zero-padded.
		private static byte[] ToBigEndian(BigInteger value, int length)
		{
			var littleEndian = value.ToByteArray();   // two's-complement little-endian, maybe a trailing 0 sign byte
			var result = new byte[length];
			for (var i = 0; i < length && i < littleEndian.Length; i++)
			{
				result[length - 1 - i] = littleEndian[i];
			}

			return result;
		}

		private static byte[] RandomBytes(int length)
		{
			var bytes = new byte[length];
			if (length > 0)
			{
				using (var rng = RandomNumberGenerator.Create())
				{
					rng.GetBytes(bytes);
				}
			}

			return bytes;
		}

		private static int RandomInt(int exclusiveMax)
		{
			if (exclusiveMax <= 1)
			{
				return 0;
			}

			var value = BitConverter.ToUInt32(RandomBytes(4), 0);
			return (int)(value % (uint)exclusiveMax);
		}

		/// <summary>A FIFO byte buffer seeded with already-received bytes, topped up from the socket.</summary>
		private sealed class StreamBuffer
		{
			private readonly Queue<byte> buffer = new Queue<byte>();

			public StreamBuffer(byte[] initial)
			{
				foreach (var b in initial)
				{
					buffer.Enqueue(b);
				}
			}

			public byte[] Read(ISocket socket, int length)
			{
				while (buffer.Count < length)
				{
					foreach (var b in ReadExact(socket, length - buffer.Count))
					{
						buffer.Enqueue(b);
					}
				}

				var result = new byte[length];
				for (var i = 0; i < length; i++)
				{
					result[i] = buffer.Dequeue();
				}

				return result;
			}

			public byte[] DrainBuffered()
			{
				var result = new byte[buffer.Count];
				for (var i = 0; i < result.Length; i++)
				{
					result[i] = buffer.Dequeue();
				}

				return result;
			}
		}
	}
}
