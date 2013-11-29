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

* Neither the name of the {organization} nor the names of its
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

namespace System.Net.Torrent
{
    public static class Pack
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
            }

            return false;
        }

        public static byte[] Int16(Int16 i, Endianness e = Endianness.Machine)
        {
            byte[] bytes = BitConverter.GetBytes(i);

            if (NeedsFlipping(e)) Array.Reverse(bytes);

            return bytes;
        }

        public static byte[] Int32(Int32 i, Endianness e = Endianness.Machine)
        {
            byte[] bytes = BitConverter.GetBytes(i);

            if (NeedsFlipping(e)) Array.Reverse(bytes);

            return bytes;
        }

        public static byte[] Int64(Int64 i, Endianness e = Endianness.Machine)
        {
            byte[] bytes = BitConverter.GetBytes(i);

            if (NeedsFlipping(e)) Array.Reverse(bytes);

            return bytes;
        }

        public static byte[] UInt16(UInt16 i, Endianness e = Endianness.Machine)
        {
            byte[] bytes = BitConverter.GetBytes(i);

            if (NeedsFlipping(e)) Array.Reverse(bytes);

            return bytes;
        }

        public static byte[] UInt32(UInt32 i, Endianness e = Endianness.Machine)
        {
            byte[] bytes = BitConverter.GetBytes(i);

            if (NeedsFlipping(e)) Array.Reverse(bytes);

            return bytes;
        }

        public static byte[] UInt64(UInt64 i, Endianness e = Endianness.Machine)
        {
            byte[] bytes = BitConverter.GetBytes(i);

            if (NeedsFlipping(e)) Array.Reverse(bytes);

            return bytes;
        }

        public static byte[] Float(float f, Endianness e = Endianness.Machine)
        {
            return BitConverter.GetBytes(f);
        }

        public static byte[] Double(double f, Endianness e = Endianness.Machine)
        {
            return BitConverter.GetBytes(f);
        }

        public static byte[] Hex(String str, Endianness e = Endianness.Machine)
        {
            if ((str.Length % 2) == 1) str += '0';

            byte[] bytes = new byte[str.Length / 2];
            for (int i = 0; i < str.Length; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(str.Substring(NeedsFlipping(e) ? ((str.Length - (i*2)) - 2) : i, 2), 16);
            }

            return bytes;
        }
    }
}
