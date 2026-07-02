/*
Copyright (c) 2026, Darren Horrocks
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

using bzTorrent.Data;

namespace bzTorrent
{
	/// <summary>
	/// BEP-19 WebSeed client: fetches a piece block over HTTP(S)/FTP from a URL listed in a
	/// torrent's "url-list", as an alternative peer-less data source.
	/// </summary>
	public interface IWebSeedClient
	{
		int Timeout { get; set; }

		/// <summary>
		/// Fetches a block of piece data. Maps the requested (pieceIndex, offset, length) — the
		/// same coordinates used by PeerWireClient.SendRequest — onto whichever file(s) in
		/// <paramref name="metadata"/> it falls within, issuing one ranged HTTP request per file
		/// per BEP-19 (a single-file torrent's URL addresses the file directly; a multi-file
		/// torrent's URL is treated as a directory and has the torrent name and file path
		/// appended).
		/// </summary>
		byte[] GetPieceBlock(string webSeedUrl, IMetadata metadata, long pieceIndex, long offset, long length);
	}
}
