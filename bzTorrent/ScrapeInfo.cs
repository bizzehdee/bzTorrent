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

namespace bzTorrent
{
	/// <summary>
	/// A tracker scrape response. Promoted from a nested type of <see cref="BaseScraper"/> to a
	/// top-level type owned by <see cref="ITrackerClient"/>. The old constructor switched on a
	/// <c>ScraperType</c> to decide which fields to populate (leaving the rest silently zero);
	/// the named factory methods make the transport-specific field sets explicit.
	/// </summary>
	public class ScrapeInfo
	{
		public uint Seeders { get; set; }
		public uint Complete { get; set; }
		public uint Leechers { get; set; }
		public uint Downloaded { get; set; }
		public uint Incomplete { get; set; }

		/// <summary>HTTP scrape reports complete / downloaded / incomplete.</summary>
		public static ScrapeInfo FromHttp(uint complete, uint downloaded, uint incomplete)
		{
			return new ScrapeInfo { Complete = complete, Downloaded = downloaded, Incomplete = incomplete };
		}

		/// <summary>UDP scrape reports seeders / completed / leechers.</summary>
		public static ScrapeInfo FromUdp(uint seeders, uint completed, uint leechers)
		{
			return new ScrapeInfo { Seeders = seeders, Complete = completed, Leechers = leechers };
		}
	}
}
