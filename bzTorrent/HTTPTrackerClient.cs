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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;
using System;
using bzBencode;
using bzTorrent.Helpers;

namespace bzTorrent
{
	public class HTTPTrackerClient : BaseScraper, ITrackerClient
	{
		private const string UserAgent = "bzTorrent";

		public HTTPTrackerClient()
			: base()
		{ 

		}

		public HTTPTrackerClient(int timeout)
			: base(timeout)
		{

		}

		/// <summary>
		/// Builds an announce URL with query parameters.
		/// </summary>
		private static string BuildAnnounceUrl(string url, string hash, string peerId, long bytesDownloaded, 
			long bytesLeft, long bytesUploaded, int numWant, int listenPort)
		{
			var urlBuilder = new StringBuilder(url.Replace("scrape", "announce"));
			urlBuilder.Append("?info_hash=");
			urlBuilder.Append(UrlEncodeHexString(hash));
			urlBuilder.Append("&peer_id=");
			urlBuilder.Append(UrlEncodeBytes(Encoding.ASCII.GetBytes(peerId)));
			urlBuilder.Append("&port=").Append(listenPort);
			urlBuilder.Append("&uploaded=").Append(bytesUploaded);
			urlBuilder.Append("&downloaded=").Append(bytesDownloaded);
			urlBuilder.Append("&left=").Append(bytesLeft);
			urlBuilder.Append("&numWant=").Append(numWant);
			urlBuilder.Append("&event=started");
			urlBuilder.Append("&compact=1");

			return urlBuilder.ToString();
		}

		/// <summary>
		/// Reads the complete response from an HTTP stream.
		/// </summary>
		private static byte[] ReadHttpResponse(Stream stream)
		{
			if (stream == null)
			{
				return null;
			}

			var bytes = new List<byte>();
			using (var binaryReader = new BinaryReader(stream))
			{
				try
				{
					while (true)
					{
						bytes.Add(binaryReader.ReadByte());
					}
				}
				catch (EndOfStreamException)
				{
					// Expected when stream ends
				}
			}

			return bytes.ToArray();
		}

		/// <summary>
		/// Extracts peer information from bencode response.
		/// </summary>
		private static IEnumerable<IPEndPoint> GetPeers(byte[] peerData)
		{
			for (var i = 0; i < peerData.Length; i += 6)
			{
				long addr = UnpackHelper.UInt32(peerData, i, UnpackHelper.Endianness.Little);
				var port = UnpackHelper.UInt16(peerData, i + 4, UnpackHelper.Endianness.Big);

				yield return new IPEndPoint(addr, port);
			}
		}

		/// <summary>
		/// Parses an announce response from bencode format.
		/// </summary>
		private static AnnounceInfo ParseAnnounceResponse(BDict decoded)
		{
			if (decoded?.Count == 0 || !decoded.ContainsKey("peers"))
			{
				return null;
			}

			if (!(decoded["peers"] is BString))
			{
				throw new NotSupportedException("Dictionary based peers not supported");
			}

			var waitTime = decoded.ContainsKey("interval") ? (int)(BInt)decoded["interval"] : 0;
			var seeders = decoded.ContainsKey("complete") ? (int)(BInt)decoded["complete"] : 0;
			var leechers = decoded.ContainsKey("incomplete") ? (int)(BInt)decoded["incomplete"] : 0;

			var peerBinary = (BString)decoded["peers"];
			return new AnnounceInfo(GetPeers(peerBinary.ByteValue), waitTime, seeders, leechers);
		}

		/// <summary>
		/// Parses a scrape response from bencode format.
		/// </summary>
		private static Dictionary<string, ScrapeInfo> ParseScrapeResponse(BDict decoded)
		{
			var result = new Dictionary<string, ScrapeInfo>();

			if (decoded?.Count == 0 || !decoded.ContainsKey("files"))
			{
				return result;
			}

			var filesDict = (BDict)decoded["files"];
			foreach (var key in filesDict.Keys)
			{
				var fileInfo = (BDict)filesDict[key];

				if (!fileInfo.ContainsKey("complete") || !fileInfo.ContainsKey("downloaded") || 
					!fileInfo.ContainsKey("incomplete"))
				{
					continue;
				}

				var hash = UnpackHelper.Hex(BencodingUtils.ExtendedASCIIEncoding.GetBytes(key));
				var scrapeInfo = new ScrapeInfo(
					(uint)((BInt)fileInfo["complete"]).Value,
					(uint)((BInt)fileInfo["downloaded"]).Value,
					(uint)((BInt)fileInfo["incomplete"]).Value,
					ScraperType.HTTP);
				result.Add(hash, scrapeInfo);
			}

			return result;
		}

		public IDictionary<string, AnnounceInfo> Announce(string url, string[] hashes, string peerId)
		{
			return hashes.ToDictionary(hash => hash, hash => Announce(url, hash, peerId));
		}

		public AnnounceInfo Announce(string url, string hash, string peerId)
		{
			return Announce(url, hash, peerId, 0, 0, 0, 2, 0, -1, 12345, 0);
		}

		public AnnounceInfo Announce(string url, string hash, string peerId, long bytesDownloaded, 
			long bytesLeft, long bytesUploaded, int eventTypeFilter, int ipAddress, int numWant, 
			int listenPort, int extensions)
		{
			var announceUrl = BuildAnnounceUrl(url, hash, peerId, bytesDownloaded, bytesLeft, 
				bytesUploaded, numWant, listenPort);

			try
			{
				var webRequest = (HttpWebRequest)WebRequest.Create(announceUrl);
				webRequest.Accept = "*/*";
				webRequest.UserAgent = UserAgent;

				using (var webResponse = (HttpWebResponse)webRequest.GetResponse())
				using (var stream = webResponse.GetResponseStream())
				{
					var responseBytes = ReadHttpResponse(stream);
					if (responseBytes == null || responseBytes.Length == 0)
					{
						return null;
					}

					var decoded = (BDict)BencodingUtils.Decode(responseBytes);
					return ParseAnnounceResponse(decoded);
				}
			}
			catch (Exception)
			{
				return null;
			}
		}

		public IDictionary<string, ScrapeInfo> Scrape(string url, string[] hashes)
		{
			var scrapeUrl = new StringBuilder(url.Replace("announce", "scrape")).Append("?");

			foreach (var hash in hashes)
			{
				scrapeUrl.Append("info_hash=").Append(UrlEncodeHexString(hash)).Append("&");
			}

			try
			{
				var webRequest = (HttpWebRequest)WebRequest.Create(scrapeUrl.ToString());
				webRequest.Accept = "*/*";
				webRequest.UserAgent = UserAgent;
				webRequest.Timeout = Timeout * 1000;

				using (var webResponse = (HttpWebResponse)webRequest.GetResponse())
				using (var stream = webResponse.GetResponseStream())
				{
					var responseBytes = ReadHttpResponse(stream);
					if (responseBytes == null || responseBytes.Length == 0)
					{
						return new Dictionary<string, ScrapeInfo>();
					}

					var decoded = (BDict)BencodingUtils.Decode(responseBytes);
					return ParseScrapeResponse(decoded);
				}
			}
			catch (Exception)
			{
				return new Dictionary<string, ScrapeInfo>();
			}
		}
	}
}
