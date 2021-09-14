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

using System.Net.Torrent.IO;

namespace System.Net.Torrent
{
    public class PeerWireListener
    {
        private readonly IWireIO _socket;
        private readonly int _port;
        private IAsyncResult _ar = null;

        public event Action<PeerWireClient> NewPeer;

        public PeerWireListener(int port)
        {
            _port = port;
            _socket = new WireIO.Tcp();
        }

        public void StartListening()
        {
            _socket.Listen(new IPEndPoint(IPAddress.Any, _port));
            _ar = _socket.BeginAccept(Callback);
        }


        public void StopListening()
        {
            if (_ar != null)
            {
                _socket.EndAccept(_ar);
            }
        }

        private void Callback(IAsyncResult ar)
        {
            var socket = _socket.EndAccept(ar);

            NewPeer?.Invoke(new PeerWireClient(socket));

            _socket.BeginAccept(Callback);
        }
    }
}
