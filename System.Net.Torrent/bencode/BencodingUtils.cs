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

using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace System.Net.Torrent.BEncode
{
    public static class BencodingUtils
    {
        /// <summary>
        /// The encoding used by Bencoding files. As Strings are allowed to have binary 
        /// data, we need to have an encoding that will allow this. UTF encodings won't 
        /// work, as it may interpret several bytes as being a single character. Bencoding 
        /// itself is ASCII based, and uses the full 256 character set (called "Extended ASCII"). 
        /// This Encoding is also named "IBM 437", and is referred here for further use. 
        /// If you need to read strings and decode them, you should load them with this encoding.
        /// </summary>
        /// <example>
        /// To load a string from somewhere else, we need to specify that the source must be 
        /// interpreted as Extended ASCII. Else the string will get messed up, and 
        /// the parsing won't work.
        /// 
        /// <code>
        /// // Read a file using the extended ASCII encoding.
        /// string origTorrentString = File.ReadAllText(@"./torrentFile.torrent", BencodingUtils.ExtendedASCIIEncoding);
        /// 
        /// // Parse the string as a bencoded torrent
        /// IBencodingType torrent = BencodingUtils.Decode(origTorrentString);
        /// </code>
        /// </example>
        public static Encoding ExtendedASCIIEncoding { get; private set; }

        static BencodingUtils()
        {
            // Extended ASCII encoding - http://stackoverflow.com/questions/4623650/encode-to-single-byte-extended-ascii-values
            ExtendedASCIIEncoding = Encoding.GetEncoding(1252);
        }

        /// <summary>
        /// Read a file, and parse it a bencoded object.
        /// </summary>
        /// <param name="fileName">The path to the file.</param>
        /// <returns>A bencoded object.</returns>
        public static IBencodingType DecodeFile(string fileName)
        {
            using (FileStream fileStream = File.OpenRead(fileName))
            {
                return Decode(fileStream);
            }
        }

        /// <summary>
        /// Parse a bencoded object from a string. 
        /// Warning: Beware of encodings.
        /// </summary>
        /// <seealso cref="ExtendedASCIIEncoding"/>
        /// <param name="inputString">The bencoded string to parse.</param>
        /// <returns>A bencoded object.</returns>
        public static IBencodingType Decode(string inputString)
        {
            byte[] byteArray = ExtendedASCIIEncoding.GetBytes(inputString);

            return Decode(new MemoryStream(byteArray));
        }

        public static IBencodingType Decode(byte[] byteArray)
        {
            return Decode(new MemoryStream(byteArray));
        }

        public static IBencodingType Decode(byte[] byteArray, ref int bytesConsumed)
        {
            return Decode(new MemoryStream(byteArray), ref bytesConsumed);
        }

        /// <summary>
        /// Parse a bencoded stream (for example a file).
        /// </summary>
        /// <param name="inputStream">The bencoded stream to parse.</param>
        /// <returns>A bencoded object.</returns>
        public static IBencodingType Decode(Stream inputStream)
        {
            using (BinaryReader sr = new BinaryReader(inputStream, ExtendedASCIIEncoding))
            {
                int bytesConsumed = 0;
                return Decode(sr, ref bytesConsumed);
            }
        }

        public static IBencodingType Decode(Stream inputStream, ref int bytesConsumed)
        {
            using (BinaryReader sr = new BinaryReader(inputStream, ExtendedASCIIEncoding))
            {
                return Decode(sr, ref bytesConsumed);
            }
        }

        internal static IBencodingType Decode(BinaryReader inputStream, ref int bytesConsumed)
        {
            char next = (char)inputStream.PeekChar();

            switch (next)
            {
                case 'i':
                    // Integer
                    return BInt.Decode(inputStream, ref bytesConsumed);

                case 'l':
                    // List
                    return BList.Decode(inputStream, ref bytesConsumed);

                case 'd':
                    // List
                    return BDict.Decode(inputStream, ref bytesConsumed);

                case '0':
                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9':
                    // String
                    return BString.Decode(inputStream, ref bytesConsumed);
            }

            return null;
        }

        /// <summary>
        /// Encode the given object to a stream.
        /// </summary>
        /// <param name="bencode">The object to encode.</param>
        /// <param name="output">The stream to write to.</param>
        public static void Encode(IBencodingType bencode, Stream output)
        {
            BinaryWriter writer = new BinaryWriter(output, ExtendedASCIIEncoding);

            bencode.Encode(writer);

            writer.Flush();
        }

        /// <summary>
        /// Encode the given object to a string.
        /// Warning: Beware of encodings, take special care when using it further.
        /// </summary>
        /// <seealso cref="ExtendedASCIIEncoding"/>
        /// <param name="bencode">The bencode object to encode.</param>
        /// <returns>A bencoded string with the object.</returns>
        public static string EncodeString(IBencodingType bencode)
        {
            MemoryStream ms = new MemoryStream();
            Encode(bencode, ms);
            ms.Position = 0;

            return new StreamReader(ms, ExtendedASCIIEncoding).ReadToEnd();
        }

        /// <summary>
        /// Encode the given object to a series of bytes.
        /// </summary>
        /// <param name="bencode">The bencode object to encode.</param>
        /// <returns>A bencoded string of the object in Extended ASCII Encoding.</returns>
        public static byte[] EncodeBytes(IBencodingType bencode)
        {
            MemoryStream ms = new MemoryStream();
            Encode(bencode, ms);
            ms.Position = 0;

            return new BinaryReader(ms, ExtendedASCIIEncoding).ReadBytes((int)ms.Length);
        }

        /// <summary>
        /// Calculates the InfoHash from a torrent. You must supply the "info" dictionary from the torrent.
        /// </summary>
        /// <param name="torrentInfoDict">The "info" dictionary.</param>
        /// <example>
        /// This example, will load a torrent, take the "info" dictionary out of the root dictionary and hash this.
        /// <code>
        /// BDict torrent = BencodingUtils.DecodeFile(@"torrentFile.torrent") as BDict;
        /// byte[] infoHash = BencodingUtils.CalculateTorrentInfoHash(torrent["info"] as BDict);
        /// </code>
        /// 
        /// The "infoHash" byte array now contains 20 bytes with the SHA-1 infoHash.
        /// </example>
        /// <returns></returns>
        public static byte[] CalculateTorrentInfoHash(BDict torrentInfoDict)
        {
            // Take the "info" dictionary provided, and encode it
            byte[] infoBytes = EncodeBytes(torrentInfoDict);

            // Hash the encoded dictionary
            return new SHA1CryptoServiceProvider().ComputeHash(infoBytes);
        }
    }
}
