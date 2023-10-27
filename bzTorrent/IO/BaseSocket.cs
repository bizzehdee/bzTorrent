using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

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

		public IAsyncResult BeginReceive(byte[] buffer, int offset, int size, SocketFlags socketFlags, AsyncCallback callback, object state)
		{
			return _socket.BeginReceive(buffer, offset, size, socketFlags, callback, state);
		}

		public int EndReceive(IAsyncResult asyncResult)
		{
			return _socket.EndReceive(asyncResult);
		}

		public IAsyncResult BeginReceiveFrom(byte[] buffer, int offset, int size, SocketFlags socketFlags, ref EndPoint remoteEP, AsyncCallback callback, object state)
		{
			return _socket.BeginReceiveFrom(buffer, offset, size, socketFlags, ref remoteEP, callback, state);
		}

		public int EndReceiveFrom(IAsyncResult asyncResult, ref EndPoint endPoint)
		{
			return _socket.EndReceiveFrom(asyncResult, ref endPoint);
		}

		public int SendTo(byte[] buffer, EndPoint remoteEP)
		{
			return _socket.SendTo(buffer, remoteEP);
		}
	}
}
