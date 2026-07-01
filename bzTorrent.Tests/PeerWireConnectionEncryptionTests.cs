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
	public class PeerWireConnectionEncryptionTests
	{
		private const string InfoHash = "C1463792A1FF36A237E3A0F68BADEB0D3764E9BB";
		private const string ClientPeerId = "B1463792A1FF36A237E3";
		private const string ServerPeerId = "S1463792A1FF36A237E3";

		[Theory]
		[InlineData(PeerEncryptionMode.RequireEncryption)]
		[InlineData(PeerEncryptionMode.PreferEncryption)]
		public async Task MseHandshakeExchangesPeerHandshakeEncrypted(PeerEncryptionMode mode)
		{
			var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();

			try
			{
				var serverTask = Task.Run(() =>
				{
					var server = new PeerWireConnection<TCPSocket>(new TCPSocket(listener.AcceptSocket()))
					{
						EncryptionMode = mode
					};
					server.EncryptionOptions.AddKnownInfoHash(InfoHash);

					SpinUntil(() =>
					{
						server.Process();
						return server.RemoteHandshake != null;
					});

					server.Handshake(new PeerClientHandshake
					{
						InfoHash = InfoHash,
						PeerId = ServerPeerId,
						ReservedBytes = new byte[8]
					});
					server.Process();

					return server;
				});

				var client = new PeerWireConnection<TCPSocket> { EncryptionMode = mode };
				client.Connect((IPEndPoint)listener.LocalEndpoint);
				client.Handshake(new PeerClientHandshake
				{
					InfoHash = InfoHash,
					PeerId = ClientPeerId,
					ReservedBytes = new byte[8]
				});

				SpinUntil(() =>
				{
					client.Process();
					return client.RemoteHandshake != null;
				});

				var server = await serverTask;

				// Both peers negotiate RC4 (both at least prefer encryption), so the payload
				// is encrypted and the BitTorrent handshake round-trips intact.
				client.IsEncrypted.Should().BeTrue();
				server.IsEncrypted.Should().BeTrue();
				client.RemoteHandshake.InfoHash.Should().Be(InfoHash);
				client.RemoteHandshake.PeerId.Should().Be(ServerPeerId);
				server.RemoteHandshake.InfoHash.Should().Be(InfoHash);
				server.RemoteHandshake.PeerId.Should().Be(ClientPeerId);
			}
			finally
			{
				listener.Stop();
			}
		}

		[Fact]
		public void RequireEncryptionRejectsPlaintextIncomingHandshake()
		{
			// A RequireEncryption listener must never accept a plaintext BitTorrent handshake.
			var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();

			try
			{
				var serverTask = Task.Run(() =>
				{
					// A short timeout bounds the synchronous MSE read: the plaintext handshake is
					// too short to be a valid DH key, so the read fails instead of blocking forever.
					var server = new PeerWireConnection<TCPSocket>(new TCPSocket(listener.AcceptSocket()))
					{
						EncryptionMode = PeerEncryptionMode.RequireEncryption,
						Timeout = 1
					};
					server.EncryptionOptions.AddKnownInfoHash(InfoHash);

					var deadline = DateTime.UtcNow.AddSeconds(4);
					while (DateTime.UtcNow < deadline)
					{
						server.Process();
						if (server.RemoteHandshake != null)
						{
							break;
						}

						Thread.Sleep(10);
					}

					return server;
				});

				// A plaintext initiator (encryption disabled) sends the raw BitTorrent handshake.
				var client = new PeerWireConnection<TCPSocket> { EncryptionMode = PeerEncryptionMode.PlainText };
				client.Connect((IPEndPoint)listener.LocalEndpoint);
				client.Handshake(new PeerClientHandshake
				{
					InfoHash = InfoHash,
					PeerId = ClientPeerId,
					ReservedBytes = new byte[8]
				});

				var server = serverTask.GetAwaiter().GetResult();
				server.RemoteHandshake.Should().BeNull("a RequireEncryption listener must reject a plaintext handshake");
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

			throw new TimeoutException("Timed out waiting for encrypted peer handshake");
		}
	}
}
