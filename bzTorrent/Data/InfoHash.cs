/*
Copyright (c) 2021, Darren Horrocks
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
using bzTorrent.Helpers;

namespace bzTorrent.Data
{
	/// <summary>
	/// A torrent info hash. Owns the single hex &lt;-&gt; bytes conversion that
	/// <see cref="Metadata"/> and <see cref="MagnetLink"/> previously each duplicated in their
	/// own HashString property. A reference type so an unset hash is naturally null.
	/// </summary>
	public sealed class InfoHash : IEquatable<InfoHash>
	{
		public byte[] Bytes { get; }

		/// <summary>Upper-case hex representation.</summary>
		public string Hex => HexCodec.Encode(Bytes);

		public InfoHash(byte[] bytes)
		{
			Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
		}

		public static InfoHash FromHex(string hex) => new InfoHash(HexCodec.Decode(hex));

		// An info hash is its bytes; allow it to flow into the many byte[]-typed APIs
		// (DHT search, handshake, ...) without ceremony. Null-safe for an unset hash.
		public static implicit operator byte[](InfoHash hash) => hash?.Bytes;

		public override string ToString() => Hex;

		public bool Equals(InfoHash other)
		{
			if (other is null)
			{
				return false;
			}

			if (Bytes.Length != other.Bytes.Length)
			{
				return false;
			}

			for (var i = 0; i < Bytes.Length; i++)
			{
				if (Bytes[i] != other.Bytes[i])
				{
					return false;
				}
			}

			return true;
		}

		public override bool Equals(object obj) => Equals(obj as InfoHash);

		public override int GetHashCode()
		{
			var hash = 17;
			foreach (var b in Bytes)
			{
				hash = (hash * 31) + b;
			}

			return hash;
		}
	}
}
