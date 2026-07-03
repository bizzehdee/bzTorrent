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

using bzBencode;

namespace bzTorrent.Data
{
	/// <summary>
	/// Guarded typed accessors for bencode dictionaries. Replace the repeated
	/// <c>dict.TryGetValue(key, out var v)</c> followed by an unchecked
	/// <c>((BString)v).ByteValue</c> / <c>(BDict)v</c> cast (which throws on a type
	/// mismatch) with a single call that returns false instead.
	/// </summary>
	internal static class BencodeExtensions
	{
		public static bool TryGetBytes(this BDict dict, string key, out byte[] value)
		{
			if (dict.TryGetValue(key, out var v) && v is BString s)
			{
				value = s.ByteValue;
				return true;
			}

			value = null;
			return false;
		}

		public static bool TryGetString(this BDict dict, string key, out string value)
		{
			if (dict.TryGetValue(key, out var v) && v is BString s)
			{
				value = s.Value;
				return true;
			}

			value = null;
			return false;
		}

		public static bool TryGetDict(this BDict dict, string key, out BDict value)
		{
			if (dict.TryGetValue(key, out var v) && v is BDict d)
			{
				value = d;
				return true;
			}

			value = null;
			return false;
		}

		public static bool TryGetList(this BDict dict, string key, out BList value)
		{
			if (dict.TryGetValue(key, out var v) && v is BList l)
			{
				value = l;
				return true;
			}

			value = null;
			return false;
		}
	}
}
