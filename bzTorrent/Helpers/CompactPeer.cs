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
using System.Net;

namespace bzTorrent.Helpers
{
	/// <summary>
	/// The single codec for BitTorrent "compact" peer and node lists. Previously
	/// this stride/offset logic was re-implemented in the HTTP and UDP tracker
	/// clients, the DHT client (six methods), and ut_pex — with copies that had
	/// already drifted (e.g. ut_pex wrote the dropped-peer port at the wrong
	/// offset).
	///
	/// Layout:
	///   peer  v4 = 4-byte addr + 2-byte big-endian port  (6 bytes)
	///   peer  v6 = 16-byte addr + 2-byte big-endian port (18 bytes)
	///   node  v4 = 20-byte id + 4-byte addr + 2-byte port (26 bytes)
	///   node  v6 = 20-byte id + 16-byte addr + 2-byte port (38 bytes)
	/// </summary>
	public static class CompactPeer
	{
		/// <summary>Length in bytes of a DHT / info node id.</summary>
		public const int NodeIdLength = 20;

		private const int PortSize = 2;

		private static int AddressSize(bool isV6) => isV6 ? 16 : 4;

		/// <summary>Byte size of one compact peer entry.</summary>
		public static int PeerEntrySize(bool isV6) => AddressSize(isV6) + PortSize;

		/// <summary>Byte size of one compact node entry.</summary>
		public static int NodeEntrySize(bool isV6) => NodeIdLength + AddressSize(isV6) + PortSize;

		/// <summary>
		/// Parses a single compact peer entry. Returns null when the entry is null,
		/// too short, or (when <paramref name="skipZeroPort"/> is set) has port 0.
		/// </summary>
		public static IPEndPoint ParsePeer(byte[] entry, bool isV6, bool skipZeroPort = false)
		{
			var addrSize = AddressSize(isV6);
			if (entry == null || entry.Length < addrSize + PortSize)
			{
				return null;
			}

			return BuildEndPoint(entry, 0, addrSize, skipZeroPort);
		}

		/// <summary>
		/// Parses a packed run of compact peer entries starting at
		/// <paramref name="start"/>.
		/// </summary>
		public static IEnumerable<IPEndPoint> ParsePeers(byte[] data, bool isV6, int start = 0, bool skipZeroPort = false)
		{
			if (data == null)
			{
				yield break;
			}

			var addrSize = AddressSize(isV6);
			var stride = addrSize + PortSize;
			for (var i = start; i + stride <= data.Length; i += stride)
			{
				var endPoint = BuildEndPoint(data, i, addrSize, skipZeroPort);
				if (endPoint != null)
				{
					yield return endPoint;
				}
			}
		}

		/// <summary>
		/// Parses a sequence of individually-framed compact peer entries (as found
		/// in a DHT "values" list, where each item is one peer).
		/// </summary>
		public static IEnumerable<IPEndPoint> ParsePeerEntries(IEnumerable<byte[]> entries, bool isV6, bool skipZeroPort = false)
		{
			if (entries == null)
			{
				yield break;
			}

			foreach (var entry in entries)
			{
				var endPoint = ParsePeer(entry, isV6, skipZeroPort);
				if (endPoint != null)
				{
					yield return endPoint;
				}
			}
		}

		/// <summary>
		/// Packs endpoints into a compact peer byte array (port big-endian).
		/// </summary>
		public static byte[] WritePeers(IReadOnlyList<IPEndPoint> peers, bool isV6)
		{
			if (peers == null)
			{
				return Array.Empty<byte>();
			}

			var addrSize = AddressSize(isV6);
			var stride = addrSize + PortSize;
			var result = new byte[peers.Count * stride];
			for (var i = 0; i < peers.Count; i++)
			{
				WriteEntry(result, i * stride, addrSize, peers[i]);
			}

			return result;
		}

		/// <summary>
		/// Parses a packed run of compact node entries (id + endpoint). Entries with
		/// port 0 are skipped, matching DHT semantics.
		/// </summary>
		public static IEnumerable<(byte[] Id, IPEndPoint EndPoint)> ParseNodes(byte[] data, bool isV6)
		{
			if (data == null)
			{
				yield break;
			}

			var addrSize = AddressSize(isV6);
			var stride = NodeIdLength + addrSize + PortSize;
			for (var i = 0; i + stride <= data.Length; i += stride)
			{
				var id = data.GetBytes(i, NodeIdLength);
				var endPoint = BuildEndPoint(data, i + NodeIdLength, addrSize, skipZeroPort: true);
				if (endPoint != null)
				{
					yield return (id, endPoint);
				}
			}
		}

		/// <summary>
		/// Packs id/endpoint pairs into a compact node byte array (port big-endian).
		/// </summary>
		public static byte[] WriteNodes(IReadOnlyList<(byte[] Id, IPEndPoint EndPoint)> nodes, bool isV6)
		{
			if (nodes == null)
			{
				return Array.Empty<byte>();
			}

			var addrSize = AddressSize(isV6);
			var stride = NodeIdLength + addrSize + PortSize;
			var result = new byte[nodes.Count * stride];
			for (var i = 0; i < nodes.Count; i++)
			{
				Array.Copy(nodes[i].Id, 0, result, i * stride, NodeIdLength);
				WriteEntry(result, (i * stride) + NodeIdLength, addrSize, nodes[i].EndPoint);
			}

			return result;
		}

		private static IPEndPoint BuildEndPoint(byte[] data, int offset, int addrSize, bool skipZeroPort)
		{
			// Build the address from its raw network-order bytes directly, not via
			// IPAddress(long)/IPEndPoint(long,int) — those constructors reinterpret
			// the value through BitConverter on the host's endianness, which double
			// flips a big-endian-decoded value into the wrong byte order on
			// little-endian hosts.
			var address = new IPAddress(data.GetBytes(offset, addrSize));
			var port = (data[offset + addrSize] << 8) | data[offset + addrSize + 1];
			if (port == 0 && skipZeroPort)
			{
				return null;
			}

			return new IPEndPoint(address, port);
		}

		private static void WriteEntry(byte[] dest, int offset, int addrSize, IPEndPoint endPoint)
		{
			var addr = endPoint.Address.GetAddressBytes();
			if (addr.Length != addrSize)
			{
				throw new ArgumentException($"Endpoint address is {addr.Length} bytes but a {addrSize}-byte (compact {(addrSize == 16 ? "IPv6" : "IPv4")}) entry was expected.", nameof(endPoint));
			}

			Array.Copy(addr, 0, dest, offset, addrSize);
			dest[offset + addrSize] = (byte)((endPoint.Port >> 8) & 0xFF);
			dest[offset + addrSize + 1] = (byte)(endPoint.Port & 0xFF);
		}
	}
}
