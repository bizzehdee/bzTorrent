/*
Copyright (c) 2021, Darren Horrocks
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

namespace bzTorrent.IO
{
	public abstract class BaseSocket : ISocket
	{
		protected Socket _socket;

		public virtual bool Connected { get => _socket.Connected; }
		public virtual int ReceiveTimeout { get => _socket.ReceiveTimeout; set => _socket.ReceiveTimeout = value; }
		public virtual int SendTimeout { get => _socket.SendTimeout; set => _socket.SendTimeout = value; }
		public virtual bool NoDelay { get => _socket.NoDelay; set => _socket.NoDelay = value; }

		public BaseSocket(Socket socket)
		{
			_socket = socket;
		}

		public abstract ISocket Accept();

		public virtual void Bind(EndPoint localEP)
		{
			_socket.Bind(localEP);
		}

		public virtual void Connect(EndPoint remoteEP)
		{
			_socket.Connect(remoteEP);
		}

		public virtual void Disconnect(bool reuseSocket)
		{
			_socket.Disconnect(reuseSocket);
		}

		public virtual void Dispose()
		{
			_socket.Dispose();
		}

		public virtual void Listen(int backlog)
		{
			_socket.Listen(backlog);
		}

		public virtual int Receive(byte[] buffer)
		{
			return _socket.Receive(buffer);
		}

		public virtual int Send(byte[] buffer)
		{
			return _socket.Send(buffer);
		}

		public virtual void SetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, bool optionValue)
		{
			_socket.SetSocketOption(optionLevel, optionName, optionValue);
		}

		public IAsyncResult BeginAccept(AsyncCallback callback, object state)
		{
			return _socket.BeginAccept(callback, state);
		}

		public abstract ISocket EndAccept(IAsyncResult ar);

		public virtual IAsyncResult BeginReceive(byte[] buffer, int offset, int size, SocketFlags socketFlags, AsyncCallback callback, object state)
		{
			return _socket.BeginReceive(buffer, offset, size, socketFlags, callback, state);
		}

		public virtual int EndReceive(IAsyncResult asyncResult)
		{
			return _socket.EndReceive(asyncResult);
		}

		public virtual IAsyncResult BeginReceiveFrom(byte[] buffer, int offset, int size, SocketFlags socketFlags, ref EndPoint remoteEP, AsyncCallback callback, object state)
		{
			return _socket.BeginReceiveFrom(buffer, offset, size, socketFlags, ref remoteEP, callback, state);
		}

		public virtual int EndReceiveFrom(IAsyncResult asyncResult, ref EndPoint endPoint)
		{
			return _socket.EndReceiveFrom(asyncResult, ref endPoint);
		}

		public virtual int SendTo(byte[] buffer, EndPoint remoteEP)
		{
			return _socket.SendTo(buffer, remoteEP);
		}
	}
}
