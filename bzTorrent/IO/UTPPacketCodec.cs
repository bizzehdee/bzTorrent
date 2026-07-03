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
using bzTorrent.Helpers;

namespace bzTorrent.IO
{
	/// <summary>
	/// Parsed fields of a uTP (BEP-29) packet header. A plain data holder; the wire
	/// (de)serialization lives in <see cref="UTPPacketCodec"/>.
	/// </summary>
	public class UTPPacketHeader
	{
		public byte Version { get; internal set; }
		public UTPPacketType PacketType { get; internal set; }
		public ushort ConnectionIdRecvd { get; internal set; }
		public uint TimestampRecvd { get; internal set; }
		public uint TimestampDiffRecvd { get; internal set; }
		public uint WndSizeRecvd { get; internal set; }
		public ushort SeqNumberRecvd { get; internal set; }
		public ushort AckNumberRecvd { get; internal set; }
	}

	/// <summary>
	/// The single (de)serializer for uTP packet headers. Previously the parse lived on
	/// <c>UTPPacketHeader</c> and the build was inlined in <c>UTPSocket.SendPacket</c>;
	/// keeping both here removes the wire-format responsibility from the socket and lets
	/// the header layout be unit-tested in isolation.
	/// </summary>
	public static class UTPPacketCodec
	{
		/// <summary>Fixed uTP header size in bytes (no extensions).</summary>
		public const int HeaderSize = 20;

		/// <summary>Protocol version encoded in the low nibble of byte 0.</summary>
		private const byte ProtocolVersion = 1;

		public static UTPPacketHeader Parse(byte[] buffer)
		{
			if (buffer == null)
			{
				throw new ArgumentNullException(nameof(buffer));
			}

			if (buffer.Length < HeaderSize)
			{
				throw new ArgumentException($"uTP header requires at least {HeaderSize} bytes.", nameof(buffer));
			}

			return new UTPPacketHeader
			{
				Version = (byte)(buffer[0] & 0x0F),
				PacketType = (UTPPacketType)((buffer[0] & 0xF0) >> 4),
				ConnectionIdRecvd = UnpackHelper.UInt16(buffer, 2, Endianness.Big),
				TimestampRecvd = UnpackHelper.UInt32(buffer, 4, Endianness.Big),
				TimestampDiffRecvd = UnpackHelper.UInt32(buffer, 8, Endianness.Big),
				WndSizeRecvd = UnpackHelper.UInt32(buffer, 12, Endianness.Big),
				SeqNumberRecvd = UnpackHelper.UInt16(buffer, 16, Endianness.Big),
				AckNumberRecvd = UnpackHelper.UInt16(buffer, 18, Endianness.Big)
			};
		}

		/// <summary>
		/// Builds a complete uTP datagram (20-byte header + payload).
		/// </summary>
		public static byte[] BuildPacket(UTPPacketType packetType, ushort connectionId, uint timestamp, uint timestampDiff, uint wndSize, ushort seq, ushort ack, byte[] payload)
		{
			var typeAndVersion = new byte[] { (byte)(((byte)packetType << 4) | ProtocolVersion) };
			var extension = new byte[] { 0 };

			var header = typeAndVersion.Cat(extension)
				.Cat(PackHelper.UInt16(connectionId))
				.Cat(PackHelper.UInt32(timestamp))
				.Cat(PackHelper.UInt32(timestampDiff))
				.Cat(PackHelper.UInt32(wndSize))
				.Cat(PackHelper.UInt16(seq))
				.Cat(PackHelper.UInt16(ack));

			return header.Cat(payload ?? Array.Empty<byte>());
		}
	}
}
