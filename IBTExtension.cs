using System.Net.Torrent.bencode;

namespace System.Net.Torrent
{
    public interface IBTExtension
    {
        String Protocol { get; }
        void Init(PeerWireClient peerWireClient);
        void Deinit(PeerWireClient peerWireClient);
        void OnHandshake(PeerWireClient peerWireClient, byte[] handshake);
        void OnExtendedMessage(PeerWireClient peerWireClient, byte[] bytes);
    }
}