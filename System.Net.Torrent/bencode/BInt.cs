﻿/*
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

using System.Globalization;
using System.IO;

namespace System.Net.Torrent.BEncode
{
    public class BInt : IBencodingType, IComparable<long>, IEquatable<long>, IEquatable<BInt>, IComparable<BInt>
    {
        private BInt()
        {
            Value = 0;
        }
        public BInt(long value)
        {
            Value = value;
        }

        public long Value { get; set; }

        /// <summary>
        /// Decode the next token as a int.
        /// Assumes the next token is a int.
        /// </summary>
        /// <param name="inputStream"></param>
        /// <param name="bytesConsumed"></param>
        /// <returns>Decoded int</returns>
        public static BInt Decode(BinaryReader inputStream, ref int bytesConsumed)
        {
            // Get past 'i'
            inputStream.Read();
            bytesConsumed++;

            // Read numbers till an 'e'
            var number = "";
            char ch;

            while ((ch = inputStream.ReadChar()) != 'e')
            {
                number += ch;

                bytesConsumed++;
            }

            bytesConsumed++;

            var res = new BInt { Value = long.Parse(number) };

            return res;
        }

        public void Encode(BinaryWriter writer)
        {
            // Write header
            writer.Write('i');

            // Write value
            writer.Write(Value.ToString(CultureInfo.InvariantCulture).ToCharArray());

            // Write footer
            writer.Write('e');
        }

        public int CompareTo(long other)
        {
            if (Value < other)
            {
                return -1;
            }

            if (Value > other)
            {
                return 1;
            }

            return 0;
        }
        public int CompareTo(BInt other)
        {
            if (other == null)
            {
                throw new ArgumentNullException("other");
            }

            if (Value < other.Value)
            {
                return -1;
            }

            if (Value > other.Value)
            {
                return 1;
            }

            return 0;
        }

        public override bool Equals(object obj)
        {
            var other = obj as BInt;

            return Equals(other);
        }
        public bool Equals(BInt other)
        {
            if (other == null)
            {
                return false;
            }

            return Equals(other.Value);
        }
        public bool Equals(long other)
        {
            return Value == other;
        }
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return string.Format("{0}", Value);
        }

        public static implicit operator BInt(long x)
        {
            return new BInt(x);
        }

        public static implicit operator long(BInt x)
        {
            return x.Value;
        }

        public static implicit operator BInt(int x)
        {
            return new BInt(x);
        }

        public static implicit operator int(BInt x)
        {
            return (int)x.Value;
        }
    }
}
