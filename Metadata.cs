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

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Torrent.BEncode;
using System.Net.Torrent.Misc;
using System.Security.Cryptography;
using System.Text;

namespace System.Net.Torrent
{
    public class Metadata
    {
        private IBencodingType _root;

        public byte[] Hash { get; set; }

        public String HashString
        {
            get { return Unpack.Hex(Hash); }
            set { Hash = Pack.Hex(value); }
        }

        public String Comment { get; set; }
        public String Announce { get; set; }
        public ICollection<String> AnnounceList { get; set; }
        public String CreatedBy { get; set; }
        public DateTime CreationDate { get; set; }
        public String Name { get; set; }
        public Int64 PieceSize { get; set; }
        public ICollection<byte[]> PieceHashes { get; set; }
        public bool Private { get; set; }
        private IDictionary<String, Int64> Files { get; set; }

        public Metadata()
        {
            Init();
        }

        public Metadata(Stream stream)
        {
            Init();

            Load(stream);
        }

        public Metadata(MagnetLink magnetLink)
        {
            Init();

            Load(magnetLink);
        }

        private void Init()
        {
            AnnounceList = new Collection<string>();
            PieceHashes = new Collection<byte[]>();
            Files = new Dictionary<string, long>();
        }

        public bool Load(MagnetLink magnetLink)
        {
            if (magnetLink == null) return false;
            if (magnetLink.Hash == null) return false;

            HashString = magnetLink.HashString;

            if (magnetLink.Trackers != null)
            {
                foreach (string tracker in magnetLink.Trackers)
                {
                    AnnounceList.Add(tracker);
                }
            }

            return true;
        }

        public bool Load(Stream stream)
        {
            _root = BencodingUtils.Decode(stream);
            if (_root == null) return false;

            BDict dictRoot = (_root as BDict);
            if (dictRoot == null) return false;

            if (dictRoot.ContainsKey("announce"))
            {
                Announce = (BString)dictRoot["announce"];
            }

            if (dictRoot.ContainsKey("announce-list"))
            {
                BList announceList = (BList)dictRoot["announce-list"];
                foreach (IBencodingType type in announceList)
                {
                    if (type is BString)
                    {
                        AnnounceList.Add(type as BString);
                    }
                    else
                    {
                        BList list = type as BList;
                        if (list == null) continue;

                        BList listType = list;
                        foreach (IBencodingType bencodingType in listType)
                        {
                            BString s = (BString)bencodingType;
                            AnnounceList.Add(s);
                        }
                    }
                }
            }

            if (dictRoot.ContainsKey("comment"))
            {
                Comment = (BString)dictRoot["comment"];
            }

            if (dictRoot.ContainsKey("created by"))
            {
                CreatedBy = (BString)dictRoot["created by"];
            }

            if (dictRoot.ContainsKey("creation date"))
            {
                long ts = (BInt)dictRoot["creation date"];
                CreationDate = new DateTime(1970, 1, 1).AddSeconds(ts);
            }

            if (dictRoot.ContainsKey("info"))
            {
                BDict infoDict = (BDict)dictRoot["info"];

                using (SHA1Managed sha1 = new SHA1Managed())
                {
                    byte[] str = BencodingUtils.EncodeBytes(infoDict);
                    Hash = sha1.ComputeHash(str);
                }                

                if (infoDict.ContainsKey("files"))
                {
                    //multi file mode
                    BList fileList = (BList)infoDict["files"];
                    foreach (IBencodingType bencodingType in fileList)
                    {
                        BDict fileDict = (BDict)bencodingType;
                        
                        String filename = string.Empty;
                        Int64 filesize = default(Int64);

                        if (fileDict.ContainsKey("path"))
                        {
                            BList filenameList = (BList)fileDict["path"];
                            foreach (IBencodingType type in filenameList)
                            {
                                filename += (BString)type;
                                filename += "\\";
                            }
                            filename = filename.Trim('\\');
                        }

                        if (fileDict.ContainsKey("length"))
                        {
                            filesize = (BInt)fileDict["length"];
                        }

                        Files.Add(filename, filesize);
                    }
                }

                if (infoDict.ContainsKey("name"))
                {
                    Name = (BString)infoDict["name"];
                    if (Files.Count == 0 && infoDict.ContainsKey("length"))
                    {
                        Files.Add(Name, (BInt)infoDict["length"]);
                    }
                }

                if (infoDict.ContainsKey("private"))
                {
                    BInt isPrivate = (BInt)infoDict["private"];
                    Private = isPrivate != 0;
                }

                if (infoDict.ContainsKey("pieces"))
                {
                    BString pieces = (BString)infoDict["pieces"];
                    for (int x = 0; x < pieces.ByteValue.Length; x += 20)
                    {
                        byte[] hash = pieces.ByteValue.GetBytes(x, 20);
                        PieceHashes.Add(hash);
                    }
                }

                if (infoDict.ContainsKey("piece length"))
                {
                    PieceSize = (BInt)infoDict["piece length"];
                }
            }

            return true;
        }

        #region Static Helpers
        public static Metadata FromString(String metadata)
        {
            return FromBuffer(Encoding.ASCII.GetBytes(metadata));
        }

        public static Metadata FromBuffer(byte[] metadata)
        {
            using (MemoryStream ms = new MemoryStream(metadata))
            {
                return new Metadata(ms);
            }
        }

        public static Metadata FromFile(String filename)
        {
            using (FileStream fs = File.OpenRead(filename))
            {
                return new Metadata(fs);
            }
        }
        #endregion
    }
}
