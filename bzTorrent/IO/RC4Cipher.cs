using System;

namespace bzTorrent.IO
{
	/// <summary>
	/// Standard ARC4/RC4 stream cipher (KSA + PRGA). Used by MSE/PE for the RC4
	/// obfuscation stream. Not thread-safe: one instance encrypts a single direction.
	/// </summary>
	internal sealed class RC4Cipher
	{
		private readonly byte[] s = new byte[256];
		private int x;
		private int y;

		public RC4Cipher(byte[] key)
		{
			if (key == null || key.Length == 0)
			{
				throw new ArgumentException("RC4 key must not be empty", nameof(key));
			}

			// Key-scheduling algorithm (KSA).
			for (var i = 0; i < 256; i++)
			{
				s[i] = (byte)i;
			}

			var j = 0;
			for (var i = 0; i < 256; i++)
			{
				j = (j + s[i] + key[i % key.Length]) & 0xff;
				(s[i], s[j]) = (s[j], s[i]);
			}
		}

		/// <summary>XORs <paramref name="data"/> with the next keystream bytes and returns the result.</summary>
		public byte[] Process(byte[] data)
		{
			var output = new byte[data.Length];
			for (var n = 0; n < data.Length; n++)
			{
				x = (x + 1) & 0xff;
				y = (y + s[x]) & 0xff;
				(s[x], s[y]) = (s[y], s[x]);
				var k = s[(s[x] + s[y]) & 0xff];
				output[n] = (byte)(data[n] ^ k);
			}

			return output;
		}

		/// <summary>Advances the keystream by <paramref name="count"/> bytes without producing output.</summary>
		public void Skip(int count)
		{
			for (var n = 0; n < count; n++)
			{
				x = (x + 1) & 0xff;
				y = (y + s[x]) & 0xff;
				(s[x], s[y]) = (s[y], s[x]);
			}
		}
	}
}
