namespace System.Net.Torrent
{
    public interface IPeerWireClient
    {
        Int32 Timeout { get; }
        bool[] PeerBitField { get; set; }
        bool KeepConnectionAlive { get; set; }
        
        bool RemoteUsesDHT { get; }
        String LocalPeerID { get; set; }
        String RemotePeerID { get; }
        String Hash { get; set; }

        event Action<IPeerWireClient> DroppedConnection;
        event Action<IPeerWireClient> NoData;
        event Action<IPeerWireClient> HandshakeComplete;
        event Action<IPeerWireClient> KeepAlive;
        event Action<IPeerWireClient> Choke;
        event Action<IPeerWireClient> UnChoke;
        event Action<IPeerWireClient> Interested;
        event Action<IPeerWireClient> NotInterested;
        event Action<IPeerWireClient, Int32> Have;
        event Action<IPeerWireClient, Int32, bool[]> BitField;
        event Action<IPeerWireClient, Int32, Int32, Int32> Request;
        event Action<IPeerWireClient, Int32, Int32, byte[]> Piece;
        event Action<IPeerWireClient, Int32, Int32, Int32> Cancel;

        void Connect(IPEndPoint endPoint);
        void Connect(String ipHost, Int32 port);
        void Disconnect();

        bool Handshake();
        bool Handshake(String hash, String peerId);
        bool Handshake(byte[] hash, byte[] peerId);

        void ProcessAsync();
        void StopProcessAsync();
        bool Process();

        bool SendKeepAlive();
        bool SendChoke();
        bool SendUnChoke();
        bool SendInterested();
        bool SendNotInterested();
        bool SendHave(UInt32 index);
        void SendBitField(bool[] bitField);
        bool SendBitField(bool[] bitField, bool obsf);
        bool SendRequest(UInt32 index, UInt32 start, UInt32 length);
        bool SendPiece(UInt32 index, UInt32 start, byte[] data);
        bool SendCancel(UInt32 index, UInt32 start, UInt32 length);

        bool SendBytes(byte[] bytes);

        void RegisterBTExtension(IProtocolExtension extension);
        void UnregisterBTExtension(IProtocolExtension extension);
    }
}