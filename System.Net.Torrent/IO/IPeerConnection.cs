using System;
using System.Collections.Generic;
using System.Net.Torrent.Data;
using System.Text;

namespace System.Net.Torrent.IO
{
	public interface IPeerConnection
	{
		bool Connected { get; }
		int Timeout { get; set; }

		void Connect(IPEndPoint endPoint);
		void Disconnect();

		bool Process();

		void Handshake(PeerClientHandshake handshake);

		PeerWirePacket Receive();
		void Send(PeerWirePacket packet);
	}
}
