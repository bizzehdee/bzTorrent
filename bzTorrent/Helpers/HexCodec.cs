/*
Copyright (c) 2013, Darren Horrocks
All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice, this
  list of conditions and the following disclaimer in the documentation and/or
  other materials provided with the distribution.

* Neither the name of Darren Horrocks nor the names of its
  contributors may be used to endorse or promote products derived from
  this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

using System;
using System.Text;

namespace bzTorrent.Helpers
{
	/// <summary>
	/// The single hex &lt;-&gt; byte codec for the library. Previously this
	/// conversion was implemented four separate ways (PackHelper.Hex,
	/// UnpackHelper.Hex, BaseScraper.UrlEncodeBytes and
	/// MessageStreamEncryption.HexToBytes).
	/// </summary>
	public static class HexCodec
	{
		/// <summary>
		/// Converts a hex string to bytes. An odd-length input is padded with a
		/// trailing '0' (e.g. "ABC" -&gt; { 0xAB, 0xC0 }), preserving the
		/// historical behaviour of <c>PackHelper.Hex</c>.
		/// </summary>
		public static byte[] Decode(string str)
		{
			if (str == null)
			{
				throw new ArgumentNullException(nameof(str));
			}

			if ((str.Length % 2) == 1)
			{
				str += '0';
			}

			var bytes = new byte[str.Length / 2];
			for (var i = 0; i < str.Length; i += 2)
			{
				bytes[i / 2] = Convert.ToByte(str.Substring(i, 2), 16);
			}

			return bytes;
		}

		/// <summary>
		/// Converts bytes to an upper-case hex string.
		/// </summary>
		public static string Encode(byte[] bytes)
		{
			if (bytes == null)
			{
				throw new ArgumentNullException(nameof(bytes));
			}

			var sb = new StringBuilder(bytes.Length * 2);
			foreach (var b in bytes)
			{
				sb.Append(b.ToString("X2"));
			}

			return sb.ToString();
		}
	}
}
