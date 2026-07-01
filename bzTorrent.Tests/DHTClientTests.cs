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
	}
}
