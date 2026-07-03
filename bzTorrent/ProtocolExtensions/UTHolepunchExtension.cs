/*
Copyright (c) 2026, Darren Horrocks
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
using bzTorrent.Helpers;

namespace bzTorrent.ProtocolExtensions
{
	public enum UTHolepunchMessageType : byte
	{
		Rendezvous = 0,
		Connect = 1,
		Error = 2
	}

	public enum UTHolepunchErrorCode : uint
	{
		NoSuchPeer = 1,
		NotConnected = 2,
		NoSupport = 3,
		NoSelf = 4
	}

	/// <summary>
	/// BEP-55 holepunch extension ("ut_holepunch"): lets a peer (A), unable to connect directly
	/// to another peer (C) it learned about via PEX, ask a peer it IS connected to (B, if also
	/// connected to C) to relay a NAT hole-punch — B tells C to expect and send a connection
	/// attempt to A, and tells A to retry connecting to C.
	///
	/// This class only implements the wire format and the events/send methods a caller drives —
	/// same scope as the other IBTExtension implementations (UTPeerExchange etc): it does not
	/// automatically decide when to rendezvous, track which peers are connected to whom, or retry
	/// connections. That orchestration is the consumer's job.
	///
	/// Wire format (payload of the ut_holepunch extended message), to the best of the BEP-55 spec
	/// as implemented here — verify against the published BEP text before relying on this for
	/// interop with other clients, the same caveat as this library's other experimental pieces:
	///   msg_type (1 byte), addr_type (1 byte: 0x04 = IPv4, 0x06 = IPv6),
	///   addr (4 or 16 bytes per addr_type), port (2 bytes, big-endian),
	///   error_code (4 bytes, big-endian — only present when msg_type == Error).
	/// </summary>
	public class UTHolepunchExtension : IBTExtension
	{
		private IExtensionHost _parent;

		public string Protocol => "ut_holepunch";

		public delegate void RendezvousDelegate(IPeerWireClient client, IBTExtension extension, IPEndPoint target);
		public delegate void ConnectDelegate(IPeerWireClient client, IBTExtension extension, IPEndPoint target);
		public delegate void ErrorDelegate(IPeerWireClient client, IBTExtension extension, IPEndPoint target, UTHolepunchErrorCode errorCode);

		public event RendezvousDelegate Rendezvous;
		public event ConnectDelegate Connect;
		public event ErrorDelegate Error;

		public void Init(IExtensionHost parent)
		{
			_parent = parent;
		}

		public void Deinit()
		{
		}

		public void OnHandshake(IPeerWireClient peerWireClient, byte[] handshake)
		{
		}

		public void OnExtendedMessage(IPeerWireClient peerWireClient, byte[] bytes)
		{
			if (bytes == null || bytes.Length < 2)
			{
				return;
			}

			var messageType = (UTHolepunchMessageType)bytes[0];
			var addrType = bytes[1];
			var addrLength = addrType == 0x06 ? 16 : 4;

			if (bytes.Length < 2 + addrLength + 2)
			{
				return;
			}

			var addressBytes = bytes.GetBytes(2, addrLength);
			var port = UnpackHelper.UInt16(bytes, 2 + addrLength, Endianness.Big);
			var endpoint = new IPEndPoint(new IPAddress(addressBytes), port);

			switch (messageType)
			{
				case UTHolepunchMessageType.Rendezvous:
					Rendezvous?.Invoke(peerWireClient, this, endpoint);
					break;

				case UTHolepunchMessageType.Connect:
					Connect?.Invoke(peerWireClient, this, endpoint);
					break;

				case UTHolepunchMessageType.Error:
					if (bytes.Length >= 2 + addrLength + 2 + 4)
					{
						var errorCode = (UTHolepunchErrorCode)UnpackHelper.UInt32(bytes, 2 + addrLength + 2, Endianness.Big);
						Error?.Invoke(peerWireClient, this, endpoint, errorCode);
					}
					break;
			}
		}

		public void SendRendezvous(IPeerWireClient peerWireClient, IPEndPoint target)
		{
			Send(peerWireClient, UTHolepunchMessageType.Rendezvous, target, null);
		}

		public void SendConnect(IPeerWireClient peerWireClient, IPEndPoint target)
		{
			Send(peerWireClient, UTHolepunchMessageType.Connect, target, null);
		}

		public void SendError(IPeerWireClient peerWireClient, IPEndPoint target, UTHolepunchErrorCode errorCode)
		{
			Send(peerWireClient, UTHolepunchMessageType.Error, target, errorCode);
		}

		private void Send(IPeerWireClient peerWireClient, UTHolepunchMessageType messageType, IPEndPoint target, UTHolepunchErrorCode? errorCode)
		{
			var addressBytes = target.Address.GetAddressBytes();
			var addrType = target.AddressFamily == AddressFamily.InterNetworkV6 ? (byte)0x06 : (byte)0x04;

			var payload = new byte[] { (byte)messageType, addrType }
				.Cat(addressBytes)
				.Cat(PackHelper.UInt16((ushort)target.Port));

			if (errorCode.HasValue)
			{
				payload = payload.Cat(PackHelper.UInt32((uint)errorCode.Value));
			}

			_parent.SendExtended(peerWireClient, _parent.GetIncomingMessageID(peerWireClient, this), payload);
		}
	}
}
