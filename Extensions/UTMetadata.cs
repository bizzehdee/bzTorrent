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
		private Int64 _piecesReceived;
	    private byte[] _metadataBuffer;

        public string Protocol
        {
            get { return "ut_metadata"; }
        }

		public event Action<PeerWireClient, IBTExtension, BDict> MetaDataReceived;

        public void Init(PeerWireClient peerWireClient)
        {
			_metadataBuffer = new byte[0];
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
            Int32 startAt = 0;
			BDict dict = (BDict)BencodingUtils.Decode(bytes, ref startAt);
	        _piecesReceived += 1;

	        if (_pieceCount >= _piecesReceived)
	        {
				_metadataBuffer = _metadataBuffer.Concat(bytes.Skip(startAt)).ToArray();
	        }

	        if (_pieceCount == _piecesReceived)
	        {
				BDict metadata = (BDict)BencodingUtils.Decode(_metadataBuffer);

		        if (MetaDataReceived != null)
		        {
			        MetaDataReceived(peerWireClient, this, metadata);
		        }
	        }
        }

        public void RequestMetaData()
        {
			byte[] sendBuffer = new byte[0];

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

	            sendBuffer = sendBuffer.Concat(buffer).ToArray();
            }

			_peerWireClient.Socket.Send(sendBuffer);
        }

    }
}
