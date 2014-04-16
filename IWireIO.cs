using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace System.Net.Torrent
{
	public interface IWireIO
	{
		int Timeout { get; set; }
		bool Connected { get; }

		void Connect(IPEndPoint endPoint);
		void Disconnect();
		int Send(byte[] bytes);
		int Receive(ref byte[] bytes);
		IAsyncResult BeginReceive(byte[] buffer, int offset, int size, AsyncCallback callback, object state);
		int EndReceive(IAsyncResult asyncResult);
	}
}
