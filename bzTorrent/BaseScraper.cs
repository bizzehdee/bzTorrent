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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using bzTorrent.Helpers;

namespace bzTorrent
{
	public abstract class BaseScraper
	{
		private readonly Regex _hashRegex = new Regex("^[a-f0-9]{40}$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase);
		private readonly Regex _udpRegex = new Regex("udp://([^:/]*)(?::([0-9]*))?(?:/)?", RegexOptions.ECMAScript | RegexOptions.IgnoreCase);
		private readonly Regex _httpRegex = new Regex("(http://.*?/)announce?|scrape?([^/]*)$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase);
		protected readonly byte[] BaseCurrentConnectionId = { 0x00, 0x00, 0x04, 0x17, 0x27, 0x10, 0x19, 0x80 };
		protected readonly Random Random = new Random(DateTime.Now.Second);

		public int Timeout { get; init; }
		public string Tracker { get; private set; }
		public int Port { get; private set; }

		protected BaseScraper()
		{

		}

		protected BaseScraper(int timeout)
		{
			Timeout = timeout;
		}

		// The full announce, implemented per transport (HTTP/UDP).
		public abstract AnnounceInfo Announce(AnnounceRequest request);

		// Convenience announce with default request parameters — was duplicated verbatim in both
		// clients.
		public AnnounceInfo Announce(string url, string hash, string peerId)
		{
			return Announce(new AnnounceRequest { Url = url, Hash = hash, PeerId = peerId });
		}

		// Announce a set of hashes. Was duplicated in both clients (UDP additionally validates the
		// whole set up front — see its override).
		public virtual IDictionary<string, AnnounceInfo> Announce(string url, string[] hashes, string peerId)
		{
			return hashes.ToDictionary(hash => hash, hash => Announce(url, hash, peerId));
		}

		public enum ScraperType
		{
			UDP,
			HTTP
		}

		protected void ValidateInput(string url, string[] hashes, ScraperType type)
		{
			if (hashes.Length < 1)
			{
				throw new ArgumentOutOfRangeException(nameof(hashes), hashes, "Must have at least one hash when calling scrape");
			}

			if (hashes.Length > 74)
			{
				throw new ArgumentOutOfRangeException(nameof(hashes), hashes, "Must have a maximum of 74 hashes when calling scrape");
			}

			foreach (var hash in hashes)
			{
				if (!_hashRegex.IsMatch(hash))
				{
					throw new ArgumentOutOfRangeException(nameof(hashes), hash, "Hash is not valid");
				}
			}

			if (type == ScraperType.UDP)
			{
				var match = _udpRegex.Match(url);

				if (!match.Success)
				{
					throw new ArgumentOutOfRangeException(nameof(url), url, "URL is not a valid UDP tracker address");
				}

				Tracker = match.Groups[1].Value;
				Port = match.Groups[2].Success ? Convert.ToInt32(match.Groups[2].Value) : 80;
			}
			else if (type == ScraperType.HTTP)
			{
				var match = _httpRegex.Match(url);

				if (!match.Success)
				{
					throw new ArgumentOutOfRangeException(nameof(url), url, "URL is not a valid HTTP tracker address");
				}

				Tracker = match.Groups[0].Value;
			}
		}

		/// <summary>
		/// URL-encodes a byte array to a percent-encoded string.
		/// </summary>
		protected static string UrlEncodeBytes(byte[] bytes)
		{
			return bytes.Aggregate(new StringBuilder(), (sb, b) => sb.Append($"%{b:X2}"), sb => sb.ToString());
		}

		/// <summary>
		/// Converts a hex string to its URL-encoded representation.
		/// </summary>
		protected static string UrlEncodeHexString(string hexString)
		{
			var bytes = PackHelper.Hex(hexString);
			return UrlEncodeBytes(bytes);
		}

		/// <summary>
		/// Copies a portion of a byte array.
		/// </summary>
		protected static byte[] CopyBytes(byte[] sourceArray, int startIndex, int length)
		{
			return sourceArray.GetBytes(startIndex, length);
		}
	}
}
