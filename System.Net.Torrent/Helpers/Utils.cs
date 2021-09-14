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
    public static class Utils
    {
        public static bool GetBit(this byte t, ushort n)
        {
            return (t & (1 << n)) != 0;
        }

        public static byte SetBit(this byte t, ushort n)
        {
            return (byte)(t | (1 << n));
        }

        public static byte[] GetBytes(this byte[] bytes, int start, int length = -1)
        {
            var l = length;
            if (l == -1)
            {
                l = bytes.Length - start;
            }

            var intBytes = new byte[l];

            for (var i = 0; i < l; i++)
            {
                intBytes[i] = bytes[start + i];
            }

            return intBytes;
        }

        public static byte[] Cat(this byte[] first, byte[] second)
        {
            var returnBytes = new byte[first.Length + second.Length];

            first.CopyTo(returnBytes, 0);
            second.CopyTo(returnBytes, first.Length);
            
            return returnBytes;
        }

        public static bool Contains<T>(this T[] ar, T o)
        {
            foreach (var t in ar)
            {
                if (Equals(t, o))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool Contains<T>(this T[] ar, Func<T, bool> expr)
        {
            foreach (var t in ar)
            {
                if (expr != null && expr(t))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
