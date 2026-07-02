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

using System;
using System.IO;
using System.Linq;
using System.Net;
using bzTorrent.Data;

namespace bzTorrent
{
	/// <summary>
	/// BEP-19 WebSeed client using ranged HTTP GET requests ("GetRight" style — the URL scheme
	/// BEP-19 specifies).
	/// </summary>
	public class HTTPWebSeedClient : IWebSeedClient
	{
		private const string UserAgent = "bzTorrent";

		public int Timeout { get; set; }

		public HTTPWebSeedClient()
			: this(10)
		{
		}

		public HTTPWebSeedClient(int timeout)
		{
			Timeout = timeout;
		}

		public byte[] GetPieceBlock(string webSeedUrl, IMetadata metadata, long pieceIndex, long offset, long length)
		{
			if (string.IsNullOrEmpty(webSeedUrl))
			{
				throw new ArgumentNullException(nameof(webSeedUrl));
			}

			var absoluteStart = (pieceIndex * metadata.PieceSize) + offset;
			var absoluteEnd = absoluteStart + length;
			var result = new byte[length];

			foreach (var file in metadata.GetFileInfos())
			{
				var fileEnd = file.FileStartByte + file.FileSize;
				if (absoluteEnd <= file.FileStartByte || absoluteStart >= fileEnd)
				{
					continue;
				}

				var fileRangeStart = Math.Max(absoluteStart - file.FileStartByte, 0);
				var resultOffset = (int)Math.Max(file.FileStartByte - absoluteStart, 0);
				var rangeLength = (int)Math.Min(length - resultOffset, file.FileSize - fileRangeStart);

				if (rangeLength <= 0)
				{
					continue;
				}

				var fileUrl = BuildFileUrl(webSeedUrl, metadata, file);
				var fileBytes = FetchRange(fileUrl, fileRangeStart, rangeLength);
				Array.Copy(fileBytes, 0, result, resultOffset, rangeLength);
			}

			return result;
		}

		// BEP-19: a single-file torrent's URL addresses the file directly. A multi-file torrent's
		// URL is treated as a directory, with the torrent name and the file's path appended.
		private static string BuildFileUrl(string webSeedUrl, IMetadata metadata, MetadataFileInfo file)
		{
			if (!metadata.IsMultiFile)
			{
				return webSeedUrl;
			}

			var baseUrl = webSeedUrl.EndsWith("/", StringComparison.Ordinal) ? webSeedUrl : webSeedUrl + "/";
			var pathSegments = file.Filename.Split('\\').Select(Uri.EscapeDataString);

			return baseUrl + Uri.EscapeDataString(metadata.Name) + "/" + string.Join("/", pathSegments);
		}

		private byte[] FetchRange(string url, long start, int length)
		{
			var request = (HttpWebRequest)WebRequest.Create(url);
			request.UserAgent = UserAgent;
			request.Timeout = Timeout * 1000;
			request.AddRange(start, start + length - 1);

			using (var response = (HttpWebResponse)request.GetResponse())
			using (var stream = response.GetResponseStream())
			{
				return ReadExact(stream, length);
			}
		}

		private static byte[] ReadExact(Stream stream, int length)
		{
			var buffer = new byte[length];
			var readTotal = 0;

			while (readTotal < length)
			{
				var read = stream.Read(buffer, readTotal, length - readTotal);
				if (read <= 0)
				{
					throw new InvalidOperationException("WebSeed response ended before the requested range was fully read");
				}

				readTotal += read;
			}

			return buffer;
		}
	}
}
