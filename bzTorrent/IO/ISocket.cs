/*
Copyright (c) 2023, Darren Horrocks
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
	/// <summary>
	/// Members common to every socket flavour: lifecycle, timeouts and options.
	/// </summary>
	public interface ISocketCore : IDisposable
	{
		bool Connected { get; }
		int ReceiveTimeout { get; set; }
		int SendTimeout { get; set; }
		bool NoDelay { get; set; }
		bool ExclusiveAddressUse { get; set; }

		void Bind(EndPoint localEP);
		void Disconnect(bool reuseSocket);

		void SetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, bool optionValue);
		void SetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, int optionValue);
		void SetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, object optionValue);
		void Close();
	}

	/// <summary>Connection-oriented (stream) socket operations.</summary>
	public interface IConnectableSocket : ISocketCore
	{
		void Connect(EndPoint remoteEP);
		int Send(byte[] buffer);

		// Synchronous receive used by the MSE/PE handshake (negotiated inline before the async
		// receive loop takes over).
		int Receive(byte[] buffer, int offset, int size);

		IAsyncResult BeginReceive(byte[] buffer, int offset, int size, SocketFlags socketFlags, AsyncCallback callback, object state);
		int EndReceive(IAsyncResult asyncResult);
	}

	/// <summary>Connectionless (datagram) socket operations.</summary>
	public interface IDatagramSocket : ISocketCore
	{
		int SendTo(byte[] buffer, EndPoint remoteEP);
		IAsyncResult BeginReceiveFrom(byte[] buffer, int offset, int size, SocketFlags socketFlags, ref EndPoint remoteEP, AsyncCallback callback, object state);
		int EndReceiveFrom(IAsyncResult asyncResult, ref EndPoint endPoint);
	}

	/// <summary>Passive-open (listening) socket operations.</summary>
	public interface IListenerSocket : ISocketCore
	{
		void Listen(int backlog);
		ISocket Accept();
		IAsyncResult BeginAccept(AsyncCallback callback, object state);
		ISocket EndAccept(IAsyncResult ar);
	}

	/// <summary>
	/// The full socket surface. Composed of the three capability interfaces so a consumer can
	/// depend on just the one it needs (e.g. the MSE handshake takes an
	/// <see cref="IConnectableSocket"/>); this remains the type the generic connection/listener
	/// and the concrete BaseSocket implementations use.
	/// </summary>
	public interface ISocket : IConnectableSocket, IDatagramSocket, IListenerSocket
	{
	}
}
