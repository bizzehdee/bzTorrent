using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using bzTorrent.IO;
using FluentAssertions;
using Xunit;

namespace bzTorrent.Tests
{
	public class PeerWireListenerTests
	{
		private const string InfoHash = "C1463792A1FF36A237E3A0F68BADEB0D3764E9BB";
		private const string ClientPeerId = "B1463792A1FF36A237E3";

		[Fact]
		public void AcceptedConnectionsInheritListenerEncryptionSettings()
		{
			var port = GetFreePort();
			var listener = new PeerWireListener<PeerWireConnection<TCPSocket>>(port)
			{
				EncryptionMode = PeerEncryptionMode.RequireEncryption
			};
			listener.EncryptionOptions.AddKnownInfoHash(InfoHash);

			IPeerWireClient accepted = null;
			var acceptedHandshakeComplete = false;

			listener.NewPeer += pwc =>
			{
				accepted = pwc;
				pwc.HandshakeComplete += _ => acceptedHandshakeComplete = true;

				// The client's Handshake() call below blocks synchronously waiting for this
				// side's MSE reply, so the accepted connection must be pumped concurrently
				// or both sides deadlock waiting on each other.
				Task.Run(() =>
				{
					var deadline = DateTime.UtcNow.AddSeconds(5);
					while (DateTime.UtcNow < deadline && !acceptedHandshakeComplete)
					{
						pwc.Process();
						Thread.Sleep(5);
					}
				});
			};
			listener.StartListening();

			try
			{
				var client = new PeerWireClient(new PeerWireConnection<TCPSocket>
				{
					EncryptionMode = PeerEncryptionMode.RequireEncryption,
					Timeout = 5
				});
				client.Connect(new IPEndPoint(IPAddress.Loopback, port));
				client.Handshake(InfoHash, ClientPeerId);

				SpinUntil(() =>
				{
					client.Process();
					return acceptedHandshakeComplete;
				});

				// The listener's EncryptionMode/EncryptionOptions must reach the connection
				// PeerWireListener hands out via NewPeer, not just connections made through
				// PeerWireConnection.Accept() directly.
				accepted.Should().NotBeNull();
				accepted.EncryptionMode.Should().Be(PeerEncryptionMode.RequireEncryption,
					"PeerWireListener must propagate its configured EncryptionMode to accepted connections");
				accepted.IsEncrypted.Should().BeTrue();
				client.IsEncrypted.Should().BeTrue();
			}
			finally
			{
				listener.StopListening();
			}
		}

		private static int GetFreePort()
		{
			var l = new TcpListener(IPAddress.Loopback, 0);
			l.Start();
			var port = ((IPEndPoint)l.LocalEndpoint).Port;
			l.Stop();
			return port;
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

			throw new TimeoutException("Timed out waiting for condition");
		}
	}
}
