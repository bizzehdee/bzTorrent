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
    public static class PackHelper
    {
        public enum Endianness
        {
            Machine,
            Big,
            Little
        }

        private static bool NeedsFlipping(Endianness e)
        {
			switch (e)
			{
				case Endianness.Big:
					return BitConverter.IsLittleEndian;
				case Endianness.Little:
					return !BitConverter.IsLittleEndian;
				case Endianness.Machine:
					break;
			}

			return false;
		}

		public static byte[] Int16(short i)
        {
            return BitConverter.GetBytes(IPAddress.HostToNetworkOrder(i));
        }

        public static byte[] Int32(int i)
        {
            return BitConverter.GetBytes(IPAddress.HostToNetworkOrder(i));
        }

        public static byte[] Int64(long i)
        {
            return BitConverter.GetBytes(IPAddress.HostToNetworkOrder(i));
        }

        public static byte[] UInt16(ushort value)
        {
            var bytes = BitConverter.GetBytes(value);

            var byteMarker = bytes.Length;
            ushort result = 0;
            for (var i = 0; i < bytes.Length; i++)
            {
                byteMarker--;
                result = (ushort)(result | bytes[i] << (byteMarker * 8));
            }

            return BitConverter.GetBytes(result);
        }

        public static byte[] UInt32(uint value)
        {
            var byte1 = (value >> 0) & 0xff;
            var byte2 = (value >> 8) & 0xff;
            var byte3 = (value >> 16) & 0xff;
            var byte4 = (value >> 24) & 0xff;

            return BitConverter.GetBytes(byte1 << 24 | byte2 << 16 | byte3 << 8 | byte4 << 0);
        }

        public static byte[] UInt64(ulong value)
        {
            var byte1 = (value >> 0) & 0xff;
            var byte2 = (value >> 8) & 0xff;
            var byte3 = (value >> 16) & 0xff;
            var byte4 = (value >> 24) & 0xff;
            var byte5 = (value >> 32) & 0xff;
            var byte6 = (value >> 40) & 0xff;
            var byte7 = (value >> 48) & 0xff;
            var byte8 = (value >> 56) & 0xff;

            return BitConverter.GetBytes(byte1 << 56 | byte2 << 48 | byte3 << 40 | byte4 << 32 | byte5 << 24 | byte6 << 16 | byte7 << 8 | byte8 << 0);
        }

        // No refs
        public static byte[] Float(float f, Endianness e = Endianness.Machine)
        {
            return BitConverter.GetBytes(f);
        }

        // No refs
        public static byte[] Double(double f, Endianness e = Endianness.Machine)
        {
            return BitConverter.GetBytes(f);
        }

        // All refs specify machine
        public static byte[] Hex(string str, Endianness e = Endianness.Machine)
        {
            if ((str.Length % 2) == 1)
            {
                str += '0';
            }

            var bytes = new byte[str.Length / 2];
            for (var i = 0; i < str.Length/2; i++)
            {
                var startByte = NeedsFlipping(e) == false ? i : (str.Length - (i * 2))-2;

                bytes[i] = Convert.ToByte(str.Substring(startByte, 2), 16);
            }

            return bytes;
        }
	}
}
