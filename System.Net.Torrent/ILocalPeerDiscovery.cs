namespace System.Net.Torrent
{
    public interface ILocalPeerDiscovery
    {
        event LocalPeerDiscovery.NewPeerCB NewPeer;
        int TTL { get; set; }
        void Open();
        void Close();
        void Announce(int listeningPort, String infoHash);
    }
}