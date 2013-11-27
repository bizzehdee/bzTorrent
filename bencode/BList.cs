using System;
using System.Collections.Generic;
using System.IO;

namespace System.Net.Torrent.bencode
{
    public class BList : List<IBencodingType>, IEquatable<BList>, IEquatable<IList<IBencodingType>>, IBencodingType
    {
        /// <summary>
        /// Decode the next token as a list.
        /// Assumes the next token is a list.
        /// </summary>
        /// <param name="inputStream"></param>
        /// <returns>Decoded list</returns>
        public static BList Decode(BinaryReader inputStream)
        {
            // Get past 'l'
            inputStream.Read();

            BList res = new BList();

            // Read elements till an 'e'
            while (inputStream.PeekChar() != 'e')
            {
                res.Add(BencodingUtils.Decode(inputStream));
            }

            // Get past 'e'
            inputStream.Read();

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
            IList<IBencodingType> other = obj as IList<IBencodingType>;

            return Equals(other);
        }
        public bool Equals(IList<IBencodingType> other)
        {
            if (other == null)
                return false;

            if (other.Count != Count)
                return false;

            for (int i = 0; i < Count; i++)
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

            return Equals(other);
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
                throw new ArgumentNullException("value");

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
                throw new ArgumentNullException("values");

            foreach (IBencodingType value in values)
            {
                if (value == null)
                    throw new ArgumentNullException("values");

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
        public IBencodingType this[int index]
        {
            get { return base[index]; }
            set
            {
                if (value == null)
                    throw new ArgumentNullException("value");

                base[index] = value;
            }
        }
    }
}
