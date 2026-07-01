using System;
using System.Collections.Generic;
using System.Net;
using bzTorrent.Data;
using bzTorrent.IO;

namespace bzTorrent.Tests.Helpers
{
    /// <summary>
    /// Simple in-memory test double for IPeerConnection used by unit tests.
    /// Allows enqueuing inbound <see cref="PeerWirePacket"/> messages and
    /// captures outbound sends and handshakes.
    /// </summary>
    public class FakePeerConnection : IPeerConnection
    {
        private readonly Queue<PeerWirePacket> _inbound = new Queue<PeerWirePacket>();

        public List<PeerWirePacket> Outbound { get; } = new List<PeerWirePacket>();

        public PeerClientHandshake RemoteHandshake { get; set; }
        public PeerClientHandshake LastHandshakeSent { get; private set; }

        public bool Connected { get; private set; } = true;
        public int Timeout { get; set; } = 10000;
        public PeerEncryptionMode EncryptionMode { get; set; } = PeerEncryptionMode.PlainText;
        public PeerEncryptionOptions EncryptionOptions { get; } = new PeerEncryptionOptions();
        public bool IsEncrypted { get; set; }

        public void Connect(IPEndPoint endPoint) => Connected = true;

        public void Disconnect() => Connected = false;

        public void Listen(EndPoint ep) { /* not needed for tests */ }

        public IPeerConnection Accept() => this;

        public IAsyncResult BeginAccept(AsyncCallback callback) => throw new NotSupportedException();

        public ISocket EndAccept(IAsyncResult ar) => throw new NotSupportedException();

        /// <summary>Process loop hook; test double does nothing here.</summary>
        public bool Process() => true;

        /// <summary>Called by the client to send a handshake; record it so tests can assert.</summary>
        public void Handshake(PeerClientHandshake handshake)
        {
            LastHandshakeSent = handshake;
        }

        public bool HasPackets() => _inbound.Count > 0;

        public PeerWirePacket Receive() => _inbound.Dequeue();

        public void Send(PeerWirePacket packet) => Outbound.Add(packet);

        /// <summary>Enqueue a packet to be returned by <see cref="Receive"/>.</summary>
        public void EnqueuePacket(PeerWirePacket packet) => _inbound.Enqueue(packet);
    }
}
