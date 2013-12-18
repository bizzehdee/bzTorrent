using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Torrent.bencode;
using System.Text;

namespace System.Net.Torrent.Extensions
{
    public class UTMetadata : IBTExtension
    {
        private PeerWireClient _peerWireClient;
        private Int64 _metadataSize;
        private Int64 _pieceCount;

        public string Protocol
        {
            get { return "ut_metadata"; }
        }

        public void Init(PeerWireClient peerWireClient)
        {
            _peerWireClient = peerWireClient;
        }

        public void Deinit(PeerWireClient peerWireClient)
        {

        }

        public void OnHandshake(PeerWireClient peerWireClient, byte[] handshake)
        {
            BDict dict = (BDict)BencodingUtils.Decode(handshake);
            if (dict.ContainsKey("metadata_size"))
            {
                BInt size = (BInt)dict["metadata_size"];
                _metadataSize = size;
                _pieceCount = (Int64)Math.Ceiling((double)_metadataSize / 16384);
            }

            RequestMetaData();
        }

        public void OnExtendedMessage(PeerWireClient peerWireClient, byte[] bytes)
        {
            Int64 startAt = 0;
            BDict dict = (BDict)BencodingUtils.Decode(bytes);

            if (dict.ContainsKey("total_size"))
            {
                startAt = bytes.Length - (BInt)dict["total_size"];
            }

            BDict metadata = (BDict) BencodingUtils.Decode(bytes.Skip((int) startAt).ToArray());
        }

        public void RequestMetaData()
        {
            for (Int32 i = 0; i < _pieceCount; i++)
            {
                BDict masterBDict = new BDict();
                masterBDict.Add("msg_type", (BInt)0);
                masterBDict.Add("piece", (BInt)i);
                String encoded = BencodingUtils.EncodeString(masterBDict);

                byte[] buffer = Pack.Int32(2 + encoded.Length, Pack.Endianness.Big);
                buffer = buffer.Concat(new byte[] {20}).ToArray();
                buffer = buffer.Concat(new byte[] {(byte) _peerWireClient.GetOutgoingMessageID(this)}).ToArray();
                buffer = buffer.Concat(Encoding.ASCII.GetBytes(encoded)).ToArray();

                _peerWireClient.Socket.Send(buffer);
            }
        }

    }
}
