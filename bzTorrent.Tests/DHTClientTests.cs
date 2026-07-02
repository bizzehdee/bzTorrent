using System;
using System.Net;
using System.Threading.Tasks;
using bzTorrent.DHT;
using FluentAssertions;
using Xunit;

namespace bzTorrent.Tests
{
	public class DHTClientTests
	{
		[Fact]
		public async Task BootstrapPopulatesRoutingTableFromRespondingNode()
		{
			// Runs entirely over loopback UDP (no external network). Exercises the send/receive
			// loop, find_node query + response, transaction-id matching and routing-table seeding.
			// Regression guard: bootstrap must record the responding node even when that node
			// returns no additional compact nodes (a fresh peer's table is empty).
			using var responder = new DHTClient();
			responder.Start();

			using var client = new DHTClient();
			client.Start();

			await client.BootstrapAsync(new[] { new IPEndPoint(IPAddress.Loopback, responder.LocalPort) });

			client.NodeCount.Should().BeGreaterThan(0,
				"the responding bootstrap node must be added to the routing table so the search loop can start");
		}

		[Fact]
		public async Task BootstrapWithNoNodesDoesNotThrow()
		{
			// When DNS resolves no bootstrap nodes, bootstrap must return cleanly rather than
			// throwing out of the caller's fire-and-forget task (which would otherwise swallow
			// the exception and silently prevent the search from ever running).
			using var client = new DHTClient();
			client.Start();

			var act = async () => await client.BootstrapAsync(Array.Empty<IPEndPoint>());

			await act.Should().NotThrowAsync();
			client.NodeCount.Should().Be(0);
		}

		[Fact]
		public async Task BootstrapOverIPv6PopulatesRoutingTableFromRespondingNode()
		{
			// BEP-32: the dual-stack socket must work identically over IPv6 loopback, not just
			// IPv4 — same scenario as the IPv4 bootstrap test above, just addressed via ::1.
			using var responder = new DHTClient();
			responder.Start();

			using var client = new DHTClient();
			client.Start();

			await client.BootstrapAsync(new[] { new IPEndPoint(IPAddress.IPv6Loopback, responder.LocalPort) });

			client.NodeCount.Should().BeGreaterThan(0,
				"the responding bootstrap node must be added to the routing table even when reached over IPv6");
		}

		[Fact]
		public async Task BootstrapAgainstMixedIPv4AndIPv6Responders_PopulatesFromBoth()
		{
			// Regression guard for the CompactNodes/CompactNodes6 family-filtering fix: a client
			// whose routing table (and thus a find_node response's closest-nodes set) contains
			// both address families must encode each into the right compact list rather than
			// truncating/corrupting a 16-byte IPv6 address into a 4-byte slot.
			using var responderV4 = new DHTClient();
			responderV4.Start();

			using var responderV6 = new DHTClient();
			responderV6.Start();

			using var client = new DHTClient();
			client.Start();

			await client.BootstrapAsync(new[]
			{
				new IPEndPoint(IPAddress.Loopback, responderV4.LocalPort),
				new IPEndPoint(IPAddress.IPv6Loopback, responderV6.LocalPort)
			});

			client.NodeCount.Should().BeGreaterOrEqualTo(2,
				"both the IPv4 and the IPv6 responder should have been recorded");
		}
	}
}
