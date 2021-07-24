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

namespace System.Net.Torrent.BEncode
{
    using System.Collections.Generic;
    using System.IO;

    public class BList : List<IBencodingType>, IEquatable<BList>, IEquatable<IList<IBencodingType>>, IBencodingType
    {
        public override int GetHashCode()
        {
            int i = 1;

            foreach (var item in this)
            {
                i ^= item.GetHashCode();
            }

            return i;
        }

        /// <summary>
        /// Decode the next token as a list.
        /// Assumes the next token is a list.
        /// </summary>
        /// <param name="inputStream"></param>
        /// <param name="bytesConsumed"></param>
        /// <returns>Decoded list</returns>
        public static BList Decode(BinaryReader inputStream, ref int bytesConsumed)
        {
            // Get past 'l'
            inputStream.Read();
            bytesConsumed++;

            BList res = new BList();

            // Read elements till an 'e'
            while (inputStream.PeekChar() != 'e')
            {
                res.Add(BencodingUtils.Decode(inputStream, ref bytesConsumed));
            }

            // Get past 'e'
            inputStream.Read();
            bytesConsumed++;

            return res;
        }

        public void Encode(BinaryWriter writer)
        {
            // Write header
            writer.Write('l');

            // Write elements
            foreach (IBencodingType item in this)
            {
                item.Encode(writer);
            }

            // Write footer
            writer.Write('e');
        }

        public bool Equals(BList obj)
        {
            IList<IBencodingType> other = obj;

            return this.Equals(other);
        }
        public bool Equals(IList<IBencodingType> other)
        {
            if (other == null)
            {
                return false;
            }

            if (other.Count != this.Count)
            {
                return false;
            }

            for (int i = 0; i < this.Count; i++)
            {
                // Lists cannot have nulls
                if (!other[i].Equals(this[i]))
                {
                    // Not ok
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            BList other = obj as BList;

            return this.Equals(other);
        }

        /// <summary>
        /// Adds a specified value.
        /// Must not be null.
        /// </summary>
        /// <param name="value"></param>
        /// <exception cref="ArgumentNullException">If the value is null</exception>
        public new void Add(IBencodingType value)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }

            base.Add(value);
        }

        /// <summary>
        /// Adds a range of specified values.
        /// None of them must be null.
        /// </summary>
        /// <param name="values"></param>
        /// <exception cref="ArgumentNullException">If any of the values is null</exception>
        public new void AddRange(IEnumerable<IBencodingType> values)
        {
            if (values == null)
            {
                throw new ArgumentNullException("values");
            }

            foreach (IBencodingType value in values)
            {
                if (value == null)
                {
                    throw new ArgumentNullException("values");
                }

                base.Add(value);
            }
        }

        /// <summary>
        /// Gets or sets a specified value.
        /// The value must not be null.
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">If the value is null</exception>
        public new IBencodingType this[int index]
        {
            get => base[index];
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException("value");
                }

                base[index] = value;
            }
        }
    }
}
