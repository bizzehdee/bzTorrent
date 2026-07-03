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

namespace bzTorrent.Helpers
{
	public static class UnpackHelper
	{
		public static short Int16(byte[] bytes, int start, Endianness e = Endianness.Machine) => BitConverter.ToInt16(Slice(bytes, start, 2, e), 0);

		public static int Int32(byte[] bytes, int start, Endianness e = Endianness.Machine) => BitConverter.ToInt32(Slice(bytes, start, 4, e), 0);

		public static long Int64(byte[] bytes, int start, Endianness e = Endianness.Machine) => BitConverter.ToInt64(Slice(bytes, start, 8, e), 0);

		public static ushort UInt16(byte[] bytes, int start, Endianness e = Endianness.Machine) => BitConverter.ToUInt16(Slice(bytes, start, 2, e), 0);

		public static uint UInt32(byte[] bytes, int start, Endianness e = Endianness.Machine) => BitConverter.ToUInt32(Slice(bytes, start, 4, e), 0);

		public static ulong UInt64(byte[] bytes, int start, Endianness e = Endianness.Machine) => BitConverter.ToUInt64(Slice(bytes, start, 8, e), 0);

		public static string Hex(byte[] bytes) => HexCodec.Encode(bytes);

		// Extracts <paramref name="length"/> bytes at <paramref name="start"/> and
		// reorders them from the requested endianness into machine order ready for
		// BitConverter. Collapses the six near-identical integer readers above.
		private static byte[] Slice(byte[] bytes, int start, int length, Endianness e) => ByteOrder.Order(bytes.GetBytes(start, length), e);
	}
}
