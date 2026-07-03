using System;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using bzTorrent.Helpers;

namespace bzTorrent.IO
{
	/// <summary>
	/// Pure crypto/encoding utilities used by the MSE/PE handshake. Split out of the former
	/// MessageStreamEncryption god class so the handshake state machine, the socket framing
	/// and these stateless helpers are separate concerns.
	/// </summary>
	internal static class MseCrypto
	{
		public static byte[] Ascii(string s)
		{
			return Encoding.ASCII.GetBytes(s);
		}

		public static byte[] Concat(params byte[][] parts)
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

		public static byte[] Hash(params byte[][] parts)
		{
			using (var sha1 = SHA1.Create())
			{
				return sha1.ComputeHash(Concat(parts));
			}
		}

		public static byte[] Xor(byte[] a, byte[] b)
		{
			var result = new byte[a.Length];
			for (var i = 0; i < a.Length; i++)
			{
				result[i] = (byte)(a[i] ^ b[i]);
			}

			return result;
		}

		public static bool BytesEqual(byte[] a, byte[] b)
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

		// Interprets big-endian bytes as a non-negative BigInteger.
		public static BigInteger ParseBigEndian(string hex)
		{
			return ParseBigEndian(HexCodec.Decode(hex));
		}

		public static BigInteger ParseBigEndian(byte[] bigEndian)
		{
			var littleEndian = new byte[bigEndian.Length + 1];   // extra 0 byte forces a positive sign
			for (var i = 0; i < bigEndian.Length; i++)
			{
				littleEndian[i] = bigEndian[bigEndian.Length - 1 - i];
			}

			return new BigInteger(littleEndian);
		}

		// Encodes a non-negative BigInteger as fixed-length big-endian, left zero-padded.
		public static byte[] ToBigEndian(BigInteger value, int length)
		{
			var littleEndian = value.ToByteArray();   // two's-complement little-endian, maybe a trailing 0 sign byte
			var result = new byte[length];
			for (var i = 0; i < length && i < littleEndian.Length; i++)
			{
				result[length - 1 - i] = littleEndian[i];
			}

			return result;
		}

		public static byte[] RandomBytes(int length)
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

		public static int RandomInt(int exclusiveMax)
		{
			if (exclusiveMax <= 1)
			{
				return 0;
			}

			var value = BitConverter.ToUInt32(RandomBytes(4), 0);
			return (int)(value % (uint)exclusiveMax);
		}
	}
}
