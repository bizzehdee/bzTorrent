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
			urlBuilder.Append(TrackerUrlEncoder.EncodeHexString(hash));
			urlBuilder.Append("&peer_id=");
			urlBuilder.Append(TrackerUrlEncoder.EncodeBytes(Encoding.ASCII.GetBytes(peerId)));
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
		/// Extracts IPv4 peer information (6-byte compact entries) from bencode response.
		/// </summary>
		private static IEnumerable<IPEndPoint> GetPeers(byte[] peerData)
		{
			return CompactPeer.ParsePeers(peerData, isV6: false);
		}

		/// <summary>
		/// Extracts IPv6 peer information (18-byte compact entries, BEP-7's "peers6") from
		/// bencode response.
		/// </summary>
		private static IEnumerable<IPEndPoint> GetPeers6(byte[] peerData)
		{
			return CompactPeer.ParsePeers(peerData, isV6: true);
		}

		/// <summary>
		/// Parses an announce response from bencode format.
		/// </summary>
		private static AnnounceInfo ParseAnnounceResponse(BDict decoded)
		{
			if (decoded?.Count == 0 || (!decoded.ContainsKey("peers") && !decoded.ContainsKey("peers6")))
			{
				return null;
			}

			if (decoded.ContainsKey("peers") && !(decoded["peers"] is BString))
			{
				throw new NotSupportedException("Dictionary based peers not supported");
			}

			var waitTime = decoded.ContainsKey("interval") ? (int)(BInt)decoded["interval"] : 0;
			var seeders = decoded.ContainsKey("complete") ? (int)(BInt)decoded["complete"] : 0;
			var leechers = decoded.ContainsKey("incomplete") ? (int)(BInt)decoded["incomplete"] : 0;

			var peers = Enumerable.Empty<IPEndPoint>();
			if (decoded.ContainsKey("peers"))
			{
				peers = peers.Concat(GetPeers(((BString)decoded["peers"]).ByteValue));
			}

			if (decoded.ContainsKey("peers6") && decoded["peers6"] is BString peers6Binary)
			{
				peers = peers.Concat(GetPeers6(peers6Binary.ByteValue));
			}

			return new AnnounceInfo(peers, waitTime, seeders, leechers);
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
				var scrapeInfo = ScrapeInfo.FromHttp(
					(uint)((BInt)fileInfo["complete"]).Value,
					(uint)((BInt)fileInfo["downloaded"]).Value,
					(uint)((BInt)fileInfo["incomplete"]).Value);
				result.Add(hash, scrapeInfo);
			}

			return result;
		}

		public override AnnounceInfo Announce(AnnounceRequest request)
		{
			var announceUrl = BuildAnnounceUrl(request.Url, request.Hash, request.PeerId, request.BytesDownloaded,
				request.BytesLeft, request.BytesUploaded, request.NumWant, request.ListenPort);

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
				scrapeUrl.Append("info_hash=").Append(TrackerUrlEncoder.EncodeHexString(hash)).Append("&");
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
