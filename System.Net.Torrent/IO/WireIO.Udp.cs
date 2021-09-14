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

using System.Net.Sockets;

namespace System.Net.Torrent.IO
{
    public partial class WireIO
    {
        public class Udp : IWireIO
        {
            private readonly Socket _socket;
            private IPEndPoint _endPoint;

            public int Timeout
            {
                get => _socket.ReceiveTimeout / 1000;
                set
                {
                    _socket.ReceiveTimeout = value * 1000;
                    _socket.SendTimeout = value * 1000;
                }
            }

            public bool Connected
			{
				get => _socket.Connected;
			}

			public Udp()
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            }

            public void Connect(IPEndPoint endPoint)
            {
                _endPoint = endPoint;
            }

            public void Disconnect()
            {
                _socket.Disconnect(true);
            }

            public int Send(byte[] bytes)
            {
                return _socket.SendTo(bytes, _endPoint);
            }

            public int Receive(ref byte[] bytes)
            {
                EndPoint recFrom = new IPEndPoint(IPAddress.Any, _endPoint.Port);

                return _socket.ReceiveFrom(bytes, ref recFrom);
            }

            public IAsyncResult BeginReceive(byte[] buffer, int offset, int size, AsyncCallback callback, object state)
            {
                return _socket.BeginReceive(buffer, offset, size, SocketFlags.None, callback, state);
            }

            public int EndReceive(IAsyncResult asyncResult)
            {
                return _socket.EndReceive(asyncResult);
            }

            public void Listen(EndPoint ep)
            {
                throw new NotImplementedException();
            }

            public IWireIO Accept()
            {
                throw new NotImplementedException();
            }

            public IAsyncResult BeginAccept(AsyncCallback callback)
            {
                throw new NotImplementedException();
            }

            public IWireIO EndAccept(IAsyncResult ar)
            {
                throw new NotImplementedException();
            }
        }
    }
}
