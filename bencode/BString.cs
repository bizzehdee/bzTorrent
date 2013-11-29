using System;
using System.IO;

namespace System.Net.Torrent.bencode
{
    /// <summary>
    /// Represents a String object. It cannot contain a null value.
    /// </summary>
    public class BString : IEquatable<string>, IEquatable<BString>, IComparable<string>, IComparable<BString>, IBencodingType
    {
        internal BString()
        {

        }

        public BString(string value)
        {
            Value = value;
        }

        private byte[] _byteValue = null;
        private string _value = string.Empty;
        public string Value
        {
            get { return _value; }
            set
            {
                if (value != null)
                    _value = value;
            }
        }
        public byte[] ByteValue { get; set; }

        /// <summary>
        /// Decode the next token as a string.
        /// Assumes the next token is a string.
        /// </summary>
        /// <param name="inputStream"></param>
        /// <returns>Decoded string</returns>
        public static BString Decode(BinaryReader inputStream)
        {
            // Read up to ':'
            string numberLength = "";
            char ch;

            while ((ch = inputStream.ReadChar()) != ':')
            {
                numberLength += ch;
            }

            // Read chars out
            //char[] stringData = new char[int.Parse(numberLength)];
            //inputStream.Read(stringData, 0, stringData.Length);
            byte[] stringData = inputStream.ReadBytes(int.Parse(numberLength));
            // Return
            return new BString { Value = BencodingUtils.ExtendedASCIIEncoding.GetString(stringData), ByteValue = stringData };
        }

        public void Encode(BinaryWriter writer)
        {
            byte[] ascii = BencodingUtils.ExtendedASCIIEncoding.GetBytes(Value);

            // Write length
            writer.Write(BencodingUtils.ExtendedASCIIEncoding.GetBytes(ascii.Length.ToString()));

            // Write seperator
            writer.Write(':');

            // Write ASCII representation
            writer.Write(ascii);
        }

        public int CompareTo(string other)
        {
            return StringComparer.InvariantCulture.Compare(Value, other);
        }
        public int CompareTo(BString other)
        {
            if (other == null)
                throw new ArgumentNullException("other");

            return CompareTo(other.Value);
        }

        public override bool Equals(object obj)
        {
            BString other = obj as BString;

            if (other == null)
                return false;

            return Equals(other);
        }
        public bool Equals(BString other)
        {
            if (other == null)
                return false;

            if (other == this)
                return true;

            return Equals(other.Value, Value);
        }
        public bool Equals(string other)
        {
            if (other == null)
                return false;

            return Equals(Value, other);
        }
        public override int GetHashCode()
        {
            // Value should never be null
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value;
        }
    }
}
