/*
Copyright (c) 2013, Darren Horrocks
All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice, this
  list of conditions and the following disclaimer in the documentation and/or
  other materials provided with the distribution.

* Neither the name of Darren Horrocks nor the names of its
  contributors may be used to endorse or promote products derived from
  this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE. 
*/

using System;
using System.Net;
using System.Net.Sockets;
using bzTorrent.IO;

namespace bzTorrent
{
	public class PeerWireListener<T> where T : IPeerConnection, new()
	{

		private readonly T peerConnection;
		private readonly int port;
		private IAsyncResult asyncResult = null;

		public event Action<PeerWireClient> NewPeer;

		public PeerWireListener(int port)
		{
			this.port = port;
			peerConnection = new T();
		}

		public void StartListening()
		{
			peerConnection.Listen(new IPEndPoint(IPAddress.Any, port));
			asyncResult = peerConnection.BeginAccept(Callback);
		}


		public void StopListening()
		{
			if (asyncResult != null)
			{
				peerConnection.EndAccept(asyncResult);
			}
		}

		private void Callback(IAsyncResult ar)
		{
			var socket = peerConnection.EndAccept(ar);

			var constructorInfo = typeof(T).GetConstructor(new[] { typeof(Socket) });

			NewPeer?.Invoke(new PeerWireClient((IPeerConnection)constructorInfo.Invoke(new object[] { socket })));

			peerConnection.BeginAccept(Callback);
		}

	}
}
