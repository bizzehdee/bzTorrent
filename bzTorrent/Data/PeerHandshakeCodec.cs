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

using System.Text;
using bzTorrent.Helpers;

namespace bzTorrent.Data
{
	/// <summary>
	/// The single (de)serializer for the fixed-length BitTorrent peer handshake:
	///   [1 byte protocol-name length][protocol name][8 reserved][20 info hash][20 peer id]
	/// Previously the byte layout was written inline in <c>PeerWireConnection.Handshake</c>
	/// and re-implemented (with magic offsets 49/8/20/28 and the literal protocol string) in
	/// <c>PeerWireConnection.ReceiveCallback</c>.
	/// </summary>
	public static class PeerHandshakeCodec
	{
		public const string ProtocolName = "BitTorrent protocol";

		private const int ReservedLength = 8;
		private const int InfoHashLength = 20;
		private const int PeerIdLength = 20;

		/// <summary>Total handshake length for a given protocol-name length.</summary>
		public static int HandshakeLength(int protocolNameLength) =>
			1 + protocolNameLength + ReservedLength + InfoHashLength + PeerIdLength;

		public static byte[] Build(PeerClientHandshake handshake)
		{
			var protocolHeaderBytes = Encoding.ASCII.GetBytes(handshake.ProtocolHeader);
			var infoHashBytes = HexCodec.Decode(handshake.InfoHash);
			var peerIdBytes = Encoding.ASCII.GetBytes(handshake.PeerId);

			return (new byte[] { (byte)protocolHeaderBytes.Length })
				.Cat(protocolHeaderBytes)
				.Cat(handshake.ReservedBytes)
				.Cat(infoHashBytes)
				.Cat(peerIdBytes);
		}

		/// <summary>
		/// Attempts to parse a complete handshake from the front of <paramref name="buffer"/>.
		/// Returns false (leaving the buffer untouched) when more bytes are still needed or the
		/// protocol name does not match — callers keep accumulating and retry.
		/// </summary>
		public static bool TryParse(byte[] buffer, out PeerClientHandshake handshake, out int consumed)
		{
			handshake = null;
			consumed = 0;

			if (buffer == null || buffer.Length < 1)
			{
				return false;
			}

			var protocolStrLen = buffer[0];
			var handshakeLength = HandshakeLength(protocolStrLen);
			if (buffer.Length < handshakeLength)
			{
				return false;
			}

			var protocolStrBytes = buffer.GetBytes(1, protocolStrLen);
			var reservedBytes = buffer.GetBytes(1 + protocolStrLen, ReservedLength);
			var infoHashBytes = buffer.GetBytes(1 + protocolStrLen + ReservedLength, InfoHashLength);
			var peerIdBytes = buffer.GetBytes(1 + protocolStrLen + ReservedLength + InfoHashLength, PeerIdLength);

			var protocolStr = Encoding.ASCII.GetString(protocolStrBytes);
			if (protocolStr != ProtocolName)
			{
				return false;
			}

			handshake = new PeerClientHandshake
			{
				ReservedBytes = reservedBytes,
				ProtocolHeader = protocolStr,
				InfoHash = HexCodec.Encode(infoHashBytes),
				PeerId = Encoding.ASCII.GetString(peerIdBytes)
			};
			consumed = handshakeLength;
			return true;
		}
	}
}
