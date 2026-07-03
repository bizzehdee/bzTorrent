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
	/// Parameters for a tracker announce. Replaces the former 11-positional-parameter
	/// <c>Announce(...)</c> overload — which was hard to call correctly and which the HTTP
	/// client silently ignored several parameters of.
	/// </summary>
	public class AnnounceRequest
	{
		public string Url { get; set; }
		public string Hash { get; set; }
		public string PeerId { get; set; }

		public long BytesDownloaded { get; set; }
		public long BytesLeft { get; set; }
		public long BytesUploaded { get; set; }

		/// <summary>Tracker event code (0 = none/periodic, per the announce protocol; default 2).</summary>
		public int Event { get; set; } = 2;
		public int IpAddress { get; set; }
		/// <summary>Number of peers wanted; -1 lets the tracker decide.</summary>
		public int NumWant { get; set; } = -1;
		public int ListenPort { get; set; } = 12345;
		public int Extensions { get; set; }
	}
}
