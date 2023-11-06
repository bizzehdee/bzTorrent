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
	public interface ISocket : IDisposable
	{
		bool Connected { get; }
		int ReceiveTimeout { get; set; }
		int SendTimeout { get; set; }
		 bool NoDelay { get; set; }
		bool ExclusiveAddressUse { get; set; }

		void Connect(EndPoint remoteEP);
		void Bind(EndPoint localEP);
		void Listen(int backlog);
		void Disconnect(bool reuseSocket);

		ISocket Accept();
		IAsyncResult BeginAccept(AsyncCallback callback, object state);
		ISocket EndAccept(IAsyncResult ar);

		IAsyncResult BeginReceive(byte[] buffer, int offset, int size, SocketFlags socketFlags, AsyncCallback callback, object state);
		int EndReceive(IAsyncResult asyncResult);
		IAsyncResult BeginReceiveFrom(byte[] buffer, int offset, int size, SocketFlags socketFlags, ref EndPoint remoteEP, AsyncCallback callback, object state);
		int EndReceiveFrom(IAsyncResult asyncResult, ref EndPoint endPoint);

		int Send(byte[] buffer);
		int SendTo(byte[] buffer, EndPoint remoteEP);

		void SetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, bool optionValue);
		void SetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, int optionValue);
		void SetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, object optionValue);
		void Close();
	}
}
