using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using bzBencode;
using bzTorrent.Data;
using bzTorrent.Helpers;

namespace bzTorrent.DHT
{
	/// <summary>
	/// The DHT server role: answers inbound queries (ping / find_node / get_peers /
	/// announce_peer) using the local node id and routing table, and writes compact node lists
	/// for the responses. Split out of DHTClient so the client (outbound search + transport)
	/// and server (inbound responses) responsibilities are separate.
	/// </summary>
	internal sealed class DHTQueryResponder
	{
		private readonly byte[] _nodeId;
		private readonly DHTRoutingTable _routingTable;
		private readonly int _k;
		private readonly Action<byte[], IPEndPoint> _send;

		public DHTQueryResponder(byte[] nodeId, DHTRoutingTable routingTable, int k, Action<byte[], IPEndPoint> send)
		{
			_nodeId = nodeId;
			_routingTable = routingTable;
			_k = k;
			_send = send;
		}

		public void Respond(IPEndPoint from, BDict message)
		{
			if (!message.TryGetString("q", out var q)) return;
			if (!message.TryGetBytes("t", out var txId)) return;
			if (!message.TryGetDict("a", out var a)) return;

			if (a.TryGetBytes("id", out var queryNodeId))
				_routingTable.AddOrUpdate(new DHTNode(queryNodeId, from));

			BDict responseArgs;
			switch (q)
			{
				case "ping":
					responseArgs = new BDict { { "id", new BString { ByteValue = _nodeId } } };
					break;

				case "find_node":
					if (!a.TryGetBytes("target", out var target)) return;
					responseArgs = AddCompactNodes(
						new BDict { { "id", new BString { ByteValue = _nodeId } } },
						_routingTable.GetClosest(target, _k));
					break;

				case "get_peers":
					if (!a.TryGetBytes("info_hash", out var infoHash)) return;
					responseArgs = AddCompactNodes(
						new BDict
						{
							{ "id", new BString { ByteValue = _nodeId } },
							{ "token", new BString { ByteValue = from.Address.GetAddressBytes() } }
						},
						_routingTable.GetClosest(infoHash, _k));
					break;

				case "announce_peer":
					responseArgs = new BDict { { "id", new BString { ByteValue = _nodeId } } };
					break;

				default:
					return;
			}

			var response = new BDict
			{
				{ "t", new BString { ByteValue = txId } },
				{ "y", new BString("r") },
				{ "r", responseArgs }
			};

			try
			{
				_send(BencodingUtils.EncodeBytes(response), from);
			}
			catch { }
		}

		// Adds "nodes" (IPv4, 26-byte entries) and/or "nodes6" (IPv6, 38-byte entries) to a
		// response dict, each only when the closest-nodes set actually contains that family, so a
		// mixed-family node list never gets truncated/corrupted into the wrong format.
		private static BDict AddCompactNodes(BDict responseArgs, IList<DHTNode> closest)
		{
			var nodes4 = CompactNodes(closest, isV6: false);
			if (nodes4.Length > 0)
			{
				responseArgs["nodes"] = new BString { ByteValue = nodes4 };
			}

			var nodes6 = CompactNodes(closest, isV6: true);
			if (nodes6.Length > 0)
			{
				responseArgs["nodes6"] = new BString { ByteValue = nodes6 };
			}

			return responseArgs;
		}

		private static byte[] CompactNodes(IList<DHTNode> nodes, bool isV6)
		{
			var family = isV6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
			var matching = nodes.Where(n => n.EndPoint.AddressFamily == family)
				.Select(n => (n.Id, n.EndPoint)).ToList();
			return CompactPeer.WriteNodes(matching, isV6);
		}
	}
}
