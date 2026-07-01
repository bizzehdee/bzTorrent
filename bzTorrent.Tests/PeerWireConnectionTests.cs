using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using bzTorrent.Data;
using bzTorrent.IO;
using FluentAssertions;
using Xunit;

namespace bzTorrent.Tests
{
	public class PeerWireConnectionTests
	{
		[Fact]
		public void ProcessLoopSurvivesUntilDelayedPeerHandshakeArrives()
		{
			// Regression: callers drive a connection with `while (client.Process())`. Process()
			// returns Connected, which must stay true while we await the peer's handshake reply.
			// Previously Connected also required the inbound handshake, so the loop exited on the
			// first iteration and the connection was dropped whenever the peer's reply did not
			// arrive before the loop's first check (i.e. any real network latency).
			var infoHash = "C1463792A1FF36A237E3A0F68BADEB0D3764E9BB";
			var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();

			try
			{
				var clientEnteredLoop = new ManualResetEventSlim(false);

				var serverTask = Task.Run(() =>
				{
					var serverConnection = new PeerWireConnection<TCPSocket>(new TCPSocket(listener.AcceptSocket()));

					SpinUntil(() =>
					{
						serverConnection.Process();
						return serverConnection.RemoteHandshake != null;
					});

					// Emulate network latency: only reply once the client is already spinning in
					// its Process() loop. Pre-fix the client has by then already given up.
					clientEnteredLoop.Wait(TimeSpan.FromSeconds(2));
					Thread.Sleep(150);

					serverConnection.Handshake(new PeerClientHandshake
					{
						InfoHash = infoHash,
						PeerId = "S1463792A1FF36A237E3",
						ReservedBytes = new byte[8]
					});
					serverConnection.Process();
					Thread.Sleep(100);
				});

				var clientConnection = new PeerWireConnection<TCPSocket>();
				clientConnection.Connect((IPEndPoint)listener.LocalEndpoint);
				clientConnection.Handshake(new PeerClientHandshake
				{
					InfoHash = infoHash,
					PeerId = "B1463792A1FF36A237E3",
					ReservedBytes = new byte[8]
				});

				var iterations = 0;
				var processReturnedFalse = false;
				var sawHandshakeWhileLooping = false;
				var deadline = DateTime.UtcNow.AddSeconds(5);
				// Drive purely off Process()'s return value, exactly as documented usage does.
				// If Process() reports the still-alive connection as disconnected, the caller's
				// loop terminates and the peer is abandoned — that is the bug being guarded.
				while (true)
				{
					if (!clientConnection.Process())
					{
						processReturnedFalse = true;
						break;
					}

					if (++iterations == 3)
					{
						clientEnteredLoop.Set();
					}

					if (clientConnection.RemoteHandshake != null)
					{
						sawHandshakeWhileLooping = true;
						break;
					}

					if (DateTime.UtcNow > deadline)
					{
						break;
					}

					Thread.Sleep(1);
				}

				clientEnteredLoop.Set();
				serverTask.Wait(TimeSpan.FromSeconds(5));

				processReturnedFalse.Should().BeFalse(
					"Process() must not report a live connection as disconnected while awaiting the peer handshake");
				sawHandshakeWhileLooping.Should().BeTrue(
					"the Process() loop must keep running until the delayed peer handshake arrives");
				clientConnection.RemoteHandshake.PeerId.Should().Be("S1463792A1FF36A237E3");
			}
			finally
			{
				listener.Stop();
			}
		}

		private static void SpinUntil(Func<bool> condition)
		{
			var deadline = DateTime.UtcNow.AddSeconds(5);
			while (DateTime.UtcNow < deadline)
			{
				if (condition())
				{
					return;
				}

				Thread.Sleep(10);
			}

			throw new TimeoutException("Timed out waiting for peer handshake");
		}
	}
}
