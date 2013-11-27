using System;
using System.Collections.Generic;
using System.IO;

namespace System.Net.Torrent.bencode
{
    public class BDict : Dictionary<string, IBencodingType>, IEquatable<BDict>, IEquatable<Dictionary<string, IBencodingType>>, IBencodingType
    {
        /// <summary>
        /// Decode the next token as a dictionary.
        /// Assumes the next token is a dictionary.
        /// </summary>
        /// <param name="inputStream"></param>
        /// <returns>Decoded dictionary</returns>
        public static BDict Decode(BinaryReader inputStream)
        {
            // Get past 'd'
            char c = (char)inputStream.ReadByte();

            BDict res = new BDict();

            // Read elements till an 'e'
            while (inputStream.PeekChar() != 'e')
            {
                // Key
                BString key = BString.Decode(inputStream);

                // Value
                IBencodingType value = BencodingUtils.Decode(inputStream);

                res[key.Value] = value;
            }

            // Get past 'e'
            inputStream.Read();

            return res;
        }

        public void Encode(BinaryWriter writer)
        {
            // Write header
            writer.Write('d');

            // Write elements
            foreach (KeyValuePair<string, IBencodingType> item in this)
            {
                // Write key
                BString key = new BString();
                key.Value = item.Key;

                key.Encode(writer);

                // Write value
                item.Value.Encode(writer);
            }

            // Write footer
            writer.Write('e');
        }

        public bool Equals(BDict obj)
        {
            Dictionary<string, IBencodingType> other = obj as Dictionary<string, IBencodingType>;

            return Equals(other);
        }
        public bool Equals(Dictionary<string, IBencodingType> other)
        {
            if (other == null)
                return false;

            if (other.Count != Count)
                return false;

            foreach (string key in Keys)
            {
                if (!other.ContainsKey(key))
                    return false;

                // Dictionaries cannot have nulls
                if (!other[key].Equals(this[key]))
                {
                    // Not ok
                    return false;
                }
            }

            return true;
        }
        public override bool Equals(object obj)
        {
            BDict other = obj as BDict;

            return Equals(other);
        }

        /// <summary>
        /// Adds a specified value.
        /// Must not be null.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <exception cref="ArgumentNullException">If the value is null</exception>
        public new void Add(string key, IBencodingType value)
        {
            if (value == null)
                throw new ArgumentNullException("value");

            base.Add(key, value);
        }

        /// <summary>
        /// Gets or sets a value. 
        /// Values must not be null.
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">If the value is null</exception>
        public IBencodingType this[string index]
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
