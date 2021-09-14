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

namespace System.Net.Torrent.Helpers
{
    public static class UnpackHelper
    {
        public enum Endianness
        {
            Machine,
            Big,
            Little
        }

        public static short Int16(byte[] bytes, int start, Endianness e = Endianness.Machine)
        {
            var intBytes = Utils.GetBytes(bytes, start, 2);

            if (NeedsFlipping(e))
            {
                Array.Reverse(intBytes);
            }

            return BitConverter.ToInt16(intBytes, 0);
        }

        public static int Int32(byte[] bytes, int start, Endianness e = Endianness.Machine)
        {
            var intBytes = Utils.GetBytes(bytes, start, 4);

            if (NeedsFlipping(e))
            {
                Array.Reverse(intBytes);
            }

            return BitConverter.ToInt32(intBytes, 0);
        }

        public static long Int64(byte[] bytes, int start, Endianness e = Endianness.Machine)
        {
            var intBytes = Utils.GetBytes(bytes, start, 8);

            if (NeedsFlipping(e))
            {
                Array.Reverse(intBytes);
            }

            return BitConverter.ToInt64(intBytes, 0);
        }

        public static ushort UInt16(byte[] bytes, int start, Endianness e = Endianness.Machine)
        {
            var intBytes = Utils.GetBytes(bytes, start, 2);

            if (NeedsFlipping(e))
            {
                Array.Reverse(intBytes);
            }

            return BitConverter.ToUInt16(intBytes, 0);
        }

        public static uint UInt32(byte[] bytes, int start, Endianness e = Endianness.Machine)
        {
            var intBytes = Utils.GetBytes(bytes, start, 4);

            if (NeedsFlipping(e))
            {
                Array.Reverse(intBytes);
            }

            return BitConverter.ToUInt32(intBytes, 0);
        }

        public static ulong UInt64(byte[] bytes, int start, Endianness e = Endianness.Machine)
        {
            var intBytes = Utils.GetBytes(bytes, start, 8);

            if (NeedsFlipping(e))
            {
                Array.Reverse(intBytes);
            }

            return BitConverter.ToUInt64(intBytes, 0);
        }

        private static bool NeedsFlipping(Endianness e)
        {
			switch (e)
			{
				case Endianness.Big:
					return BitConverter.IsLittleEndian;
				case Endianness.Little:
					return !BitConverter.IsLittleEndian;
				default:
					break;
			}

			return false;
		}
		public static string Hex(byte[] bytes, Endianness e = Endianness.Machine)
        {
            var str = "";

            foreach (var b in bytes)
            {
                str += string.Format("{0:X2}", b);
            }

            return str;
        }
	}
}
