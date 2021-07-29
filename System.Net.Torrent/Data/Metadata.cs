/*
Copyright (c) 2013, Darren Horrocks
Copyright (c) 2021, Russell Webster
All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice, this
  list of conditions and the following disclaimer in the documentation and/or
  other materials provided with the distribution.

* Neither the name of Darren Horrocks, Russell Webster nor the names of their
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

namespace System.Net.Torrent.Data
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.IO;
    using System.Linq;
    using System.Net.Torrent.BEncode;
    using System.Net.Torrent.Extensions;
    using System.Net.Torrent.Helpers;
    using System.Security.Cryptography;
    using System.Text;

    public class Metadata : IMetadata
    {
        private IBencodingType _root;
        private IDictionary<string, Int64> files;

        public byte[] Hash { get; private set; }

        public string HashString
        {
            get => UnpackHelper.Hex(this.Hash);
            private set => this.Hash = PackHelper.Hex(value);
        }

        public string Comment { get; private set; }

        public string Announce { get; private set; }

        public ICollection<string> AnnounceList { get; private set; }

        public string CreatedBy { get; private set; }

        public DateTime CreationDate { get; private set; }

        public string Name { get; private set; }

        public Int64 PieceSize { get; private set; }

        public ICollection<byte[]> PieceHashes { get; private set; }

        public bool Private { get; private set; }

        public Metadata()
        {
            this.Init();
        }

        public Metadata(Stream stream)
        {
            this.Init();

            this.Load(stream);
        }

        public Metadata(MagnetLink magnetLink)
        {
            this.Init();

            this.Load(magnetLink);
        }

        private void Init()
        {
            this.AnnounceList = new Collection<string>();
            this.PieceHashes = new Collection<byte[]>();
            this.files = new Dictionary<string, long>();
        }

        public bool Load(MagnetLink magnetLink)
        {
            if (magnetLink?.Hash == null)
            {
                return false;
            }

            this.HashString = magnetLink.HashString;

            if (magnetLink.Trackers != null)
            {
                foreach (string tracker in magnetLink.Trackers)
                {
                    this.AnnounceList.Add(tracker);
                }
            }

            return true;
        }

        public bool Load(Stream stream)
        {
            this._root = BencodingUtils.Decode(stream);
            if (this._root == null)
            {
                return false;
            }

            BDict dictRoot = (this._root as BDict);
            if (dictRoot == null)
            {
                return false;
            }

            if (dictRoot.ContainsKey("announce"))
            {
                this.Announce = (BString)dictRoot["announce"];
            }

            if (dictRoot.ContainsKey("announce-list"))
            {
                BList announceList = (BList)dictRoot["announce-list"];
                foreach (IBencodingType type in announceList)
                {
                    if (type is BString)
                    {
                        this.AnnounceList.Add(type as BString);
                    }
                    else
                    {
                        BList list = type as BList;
                        if (list == null)
                        {
                            continue;
                        }

                        BList listType = list;
                        foreach (IBencodingType bencodingType in listType)
                        {
                            BString s = (BString)bencodingType;
                            this.AnnounceList.Add(s);
                        }
                    }
                }
            }

            if (dictRoot.ContainsKey("comment"))
            {
                this.Comment = (BString)dictRoot["comment"];
            }

            if (dictRoot.ContainsKey("created by"))
            {
                this.CreatedBy = (BString)dictRoot["created by"];
            }

            if (dictRoot.ContainsKey("creation date"))
            {
                long ts = (BInt)dictRoot["creation date"];
                this.CreationDate = new DateTime(1970, 1, 1).AddSeconds(ts);
            }

            if (dictRoot.ContainsKey("info"))
            {
                BDict infoDict = (BDict)dictRoot["info"];

                using (SHA1Managed sha1 = new SHA1Managed())
                {
                    byte[] str = BencodingUtils.EncodeBytes(infoDict);
                    this.Hash = sha1.ComputeHash(str);
                }

                if (infoDict.ContainsKey("files"))
                {
                    //multi file mode
                    BList fileList = (BList)infoDict["files"];
                    foreach (IBencodingType bencodingType in fileList)
                    {
                        BDict fileDict = (BDict)bencodingType;

                        string filename = string.Empty;
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

                        this.files.Add(filename, filesize);
                    }
                }

                if (infoDict.ContainsKey("name"))
                {
                    this.Name = (BString)infoDict["name"];
                    if (this.files.Count == 0 && infoDict.ContainsKey("length"))
                    {
                        this.files.Add(this.Name, (BInt)infoDict["length"]);
                    }
                }

                if (infoDict.ContainsKey("private"))
                {
                    BInt isPrivate = (BInt)infoDict["private"];
                    this.Private = isPrivate != 0;
                }

                if (infoDict.ContainsKey("pieces"))
                {
                    BString pieces = (BString)infoDict["pieces"];
                    for (int x = 0; x < pieces.ByteValue.Length; x += 20)
                    {
                        byte[] hash = pieces.ByteValue.GetBytes(x, 20);
                        this.PieceHashes.Add(hash);
                    }
                }

                if (infoDict.ContainsKey("piece length"))
                {
                    this.PieceSize = (BInt)infoDict["piece length"];
                }
            }

            return true;
        }

        public IReadOnlyCollection<string> GetFiles()
        {
            if (this.files.IsNullOrEmpty())
            {
                return new Collection<string>();
            }

            return this.files?.Select(x => x.Key)?.ToList()?.AsReadOnly();
        }

        #region Static Helpers
        public static IMetadata FromString(string metadata)
        {
            return FromBuffer(Encoding.ASCII.GetBytes(metadata));
        }

        public static IMetadata FromBuffer(byte[] metadata)
        {
            using (MemoryStream ms = new MemoryStream(metadata))
            {
                return new Metadata(ms);
            }
        }

        public static IMetadata FromFile(string filename)
        {
            using (FileStream fs = File.OpenRead(filename))
            {
                return new Metadata(fs);
            }
        }
        #endregion
    }
}
