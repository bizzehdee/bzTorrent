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
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Net;
using System;
using bzTorrent.Helpers;

namespace bzTorrent
{
	public class UDPTrackerClient : BaseScraper, ITrackerClient
	{
		private byte[] _currentConnectionId;

		public UDPTrackerClient()
			: base()
		{
			_currentConnectionId = BaseCurrentConnectionId;
		}

		public UDPTrackerClient(int timeout)
			: base(timeout)
		{
			_currentConnectionId = BaseCurrentConnectionId;
		}

		/// <summary>
		/// Creates and configures a UDP client with appropriate timeouts.
		/// </summary>
		private UdpClient CreateConfiguredUdpClient(bool dontFragment = false)
		{
			var udpClient = new UdpClient(Tracker, Port)
			{
				DontFragment = dontFragment,
				Client =
				{
					SendTimeout = Timeout * 1000,
					ReceiveTimeout = Timeout * 1000
				}
			};
			return udpClient;
		}

		/// <summary>
		/// Sends a connection request and returns the new connection ID.
		/// </summary>
		private byte[] EstablishConnection(UdpClient udpClient, int transactionId)
		{
			var sendBuf = _currentConnectionId
				.Concat(PackHelper.Int32(0))  // action: connect
				.Concat(PackHelper.Int32(transactionId))
				.ToArray();

			udpClient.Send(sendBuf, sendBuf.Length);

			IPEndPoint endPoint = null;
			var recBuf = udpClient.Receive(ref endPoint);

			ValidateUdpResponse(recBuf, 0, transactionId);
			return CopyBytes(recBuf, 8, 8);
		}

		/// <summary>
		/// Validates a UDP response for action, transaction ID, and minimum length.
		/// </summary>
		private static void ValidateUdpResponse(byte[] response, uint expectedAction, int expectedTransactionId, int minLength = 16)
		{
			if (response == null)
			{
				throw new InvalidOperationException("UDP client failed to receive response");
			}

			if (response.Length < minLength)
			{
				throw new InvalidOperationException("UDP client did not receive complete response");
			}

			var action = UnpackHelper.UInt32(response, 0, UnpackHelper.Endianness.Big);
			var transactionId = UnpackHelper.UInt32(response, 4, UnpackHelper.Endianness.Big);

			if (action != expectedAction || transactionId != expectedTransactionId)
			{
				throw new InvalidOperationException("Invalid response from tracker");
			}
		}

		/// <summary>
		/// Parses scrape response data into ScrapeInfo objects.
		/// </summary>
		private static Dictionary<string, ScrapeInfo> ParseScrapeResponse(byte[] response, string[] hashes, int offset = 8)
		{
			var result = new Dictionary<string, ScrapeInfo>();
			var startIndex = offset;

			foreach (var hash in hashes)
			{
				var seeders = UnpackHelper.UInt32(response, startIndex, UnpackHelper.Endianness.Big);
				var completed = UnpackHelper.UInt32(response, startIndex + 4, UnpackHelper.Endianness.Big);
				var leechers = UnpackHelper.UInt32(response, startIndex + 8, UnpackHelper.Endianness.Big);

				result.Add(hash, new ScrapeInfo(seeders, completed, leechers, ScraperType.UDP));
				startIndex += 12;
			}

			return result;
		}

		/// <summary>
		/// Parses announce response data into peer list and statistics.
		/// </summary>
		private static List<IPEndPoint> ParseAnnouncePeers(byte[] response, out int waitTime, out int leechers, out int seeders)
		{
			var peers = new List<IPEndPoint>();
			waitTime = (int)UnpackHelper.UInt32(response, 8, UnpackHelper.Endianness.Big);
			leechers = (int)UnpackHelper.UInt32(response, 12, UnpackHelper.Endianness.Big);
			seeders = (int)UnpackHelper.UInt32(response, 16, UnpackHelper.Endianness.Big);

			for (var i = 20; i < response.Length; i += 6)
			{
				var ip = UnpackHelper.UInt32(response, i, UnpackHelper.Endianness.Little);
				var port = UnpackHelper.UInt16(response, i + 4, UnpackHelper.Endianness.Big);
				peers.Add(new IPEndPoint(ip, port));
			}

			return peers;
		}

		public IDictionary<string, ScrapeInfo> Scrape(string url, string[] hashes)
		{
			ValidateInput(url, hashes, ScraperType.UDP);

			var transactionId = Random.Next(0, 65535);

			try
			{
				using (var udpClient = CreateConfiguredUdpClient())
				{
					// Establish connection
					_currentConnectionId = EstablishConnection(udpClient, transactionId);

					// Build scrape request
					var hashBytes = hashes.Aggregate(Array.Empty<byte>(), 
						(current, hash) => current.Concat(PackHelper.Hex(hash)).ToArray());

					var sendBuf = _currentConnectionId
						.Concat(PackHelper.Int32(2))  // action: scrape
						.Concat(PackHelper.Int32(transactionId))
						.Concat(hashBytes)
						.ToArray();

					udpClient.Send(sendBuf, sendBuf.Length);

					// Receive and validate response
					IPEndPoint endPoint = null;
					var recBuf = udpClient.Receive(ref endPoint);

					var expectedLength = 8 + (12 * hashes.Length);
					ValidateUdpResponse(recBuf, 2, transactionId, expectedLength);

					_currentConnectionId = CopyBytes(recBuf, 8, 8);

					return ParseScrapeResponse(recBuf, hashes);
				}
			}
			catch (Exception)
			{
				return new Dictionary<string, ScrapeInfo>();
			}
		}

		public AnnounceInfo Announce(string url, string hash, string peerId)
		{
			return Announce(url, hash, peerId, 0, 0, 0, 2, 0, -1, 12345, 0);
		}

		public AnnounceInfo Announce(string url, string hash, string peerId, long bytesDownloaded, 
			long bytesLeft, long bytesUploaded, int eventTypeFilter, int ipAddress, int numWant, 
			int listenPort, int extensions)
		{
			ValidateInput(url, new[] { hash }, ScraperType.UDP);

			var transactionId = Random.Next(0, 65535);

			try
			{
				using (var udpClient = CreateConfiguredUdpClient(dontFragment: true))
				{
					// Establish connection
					_currentConnectionId = EstablishConnection(udpClient, transactionId);

					// Build announce request
					var hashBytes = PackHelper.Hex(hash).ToArray();
					var key = Random.Next(0, 65535);

					var sendBuf = _currentConnectionId          /*connection id*/
						.Concat(PackHelper.Int32(1))           /*action: announce*/
						.Concat(PackHelper.Int32(transactionId)) /*transaction id*/
						.Concat(hashBytes)                      /*info hash*/
						.Concat(Encoding.ASCII.GetBytes(peerId)) /*peer id*/
						.Concat(PackHelper.Int64(bytesDownloaded)) /*bytes downloaded*/
						.Concat(PackHelper.Int64(bytesLeft))     /*bytes left*/
						.Concat(PackHelper.Int64(bytesUploaded)) /*bytes uploaded*/
						.Concat(PackHelper.Int32(eventTypeFilter)) /*event*/
						.Concat(PackHelper.Int32(ipAddress))    /*ip address*/
						.Concat(PackHelper.Int32(key))          /*key*/
						.Concat(PackHelper.Int32(numWant))      /*num want*/
						.Concat(PackHelper.Int32(listenPort))   /*port*/
						.Concat(PackHelper.Int32(extensions))   /*extensions*/
						.ToArray();

					udpClient.Send(sendBuf, sendBuf.Length);

					// Receive and validate response
					IPEndPoint endPoint = null;
					var recBuf = udpClient.Receive(ref endPoint);

					ValidateUdpResponse(recBuf, 1, transactionId, 20);

					var peers = ParseAnnouncePeers(recBuf, out int waitTime, out int leechers, out int seeders);
					return new AnnounceInfo(peers, waitTime, seeders, leechers);
				}
			}
			catch (Exception)
			{
				return null;
			}
		}

		public IDictionary<string, AnnounceInfo> Announce(string url, string[] hashes, string peerId)
		{
			ValidateInput(url, hashes, ScraperType.UDP);
			return hashes.ToDictionary(hash => hash, hash => Announce(url, hash, peerId));
		}
	}
}
