using System.Net.Torrent.bencode;

namespace System.Net.Torrent
{
    public interface IBTExtension
    {
        String Protocol { get; }
        void OnExtendedMessage(PeerWireClient peerWireClient, byte[] bytes);
    }
}