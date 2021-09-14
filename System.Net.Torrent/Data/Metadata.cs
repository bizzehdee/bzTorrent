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

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using bzBencode;
using System.Net.Torrent.Extensions;
using System.Net.Torrent.Helpers;
using System.Security.Cryptography;
using System.Text;

namespace System.Net.Torrent.Data
{
    public class Metadata : IMetadata
    {
        private IBencodingType _root;
        private ICollection<MetadataFileInfo> files;

        public byte[] Hash { get; private set; }

        public string HashString
        {
            get => UnpackHelper.Hex(Hash);
            private set => Hash = PackHelper.Hex(value);
        }

        public string Comment { get; private set; }

        public string Announce { get; private set; }

        public ICollection<string> AnnounceList { get; private set; }

        public string CreatedBy { get; private set; }

        public DateTime CreationDate { get; private set; }

        public string Name { get; private set; }

        public long PieceSize { get; private set; }

        public ICollection<byte[]> PieceHashes { get; private set; }
        public ICollection<MetadataPieceInfo> Pieces { get; private set; }

        public bool Private { get; private set; }

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
            Pieces = new Collection<MetadataPieceInfo>();
            files = new List<MetadataFileInfo>();
        }

        public bool Load(MagnetLink magnetLink)
        {
            if (magnetLink?.Hash == null)
            {
                return false;
            }

            HashString = magnetLink.HashString;

            if (magnetLink.Trackers != null)
            {
                foreach (var tracker in magnetLink.Trackers)
                {
                    AnnounceList.Add(tracker);
                }
            }

            return true;
        }

        public bool Load(Stream stream)
        {
            _root = BencodingUtils.Decode(stream);
            if (_root == null)
            {
                return false;
            }

            var dictRoot = (_root as BDict);
            if (dictRoot == null)
            {
                return false;
            }

            if (dictRoot.ContainsKey("announce"))
            {
                Announce = (BString)dictRoot["announce"];
            }

            if (dictRoot.ContainsKey("announce-list"))
            {
                var announceList = (BList)dictRoot["announce-list"];
                foreach (var type in announceList)
                {
                    if (type is BString)
                    {
                        AnnounceList.Add(type as BString);
                    }
                    else
                    {
                        var list = type as BList;
                        if (list == null)
                        {
                            continue;
                        }

                        var listType = list;
                        foreach (var bencodingType in listType)
                        {
                            var s = (BString)bencodingType;
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
                var infoDict = (BDict)dictRoot["info"];

                using (var sha1 = new SHA1Managed())
                {
                    var str = BencodingUtils.EncodeBytes(infoDict);
                    Hash = sha1.ComputeHash(str);
                }

                if (infoDict.ContainsKey("files"))
                {
                    //multi file mode
                    var fileList = (BList)infoDict["files"];
                    var id = 0L;
                    var startByte = 0L;
                    foreach (var bencodingType in fileList)
                    {
                        var fileDict = (BDict)bencodingType;

                        var filename = string.Empty;
                        long filesize = default;

                        if (fileDict.ContainsKey("path"))
                        {
                            var filenameList = (BList)fileDict["path"];
                            foreach (var type in filenameList)
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

                        var fileInfo = new MetadataFileInfo
                        {
                            Id = id,
                            FileSize = filesize,
                            Filename = filename,
                            FileStartByte = startByte
                        };

                        id++;
                        startByte += filesize;

                        files.Add(fileInfo);
                    }
                }

                if (infoDict.ContainsKey("name"))
                {
                    Name = (BString)infoDict["name"];
                    if (files.Count == 0 && infoDict.ContainsKey("length"))
                    {
                        files.Add(new MetadataFileInfo
                        {
                            Id = 0,
                            FileSize = (BInt)infoDict["length"],
                            Filename = Name,
                            FileStartByte = 0
                        });
                    }
                }

                if (infoDict.ContainsKey("private"))
                {
                    var isPrivate = (BInt)infoDict["private"];
                    Private = isPrivate != 0;
                }

                if (infoDict.ContainsKey("pieces"))
                {
                    var pieces = (BString)infoDict["pieces"];
                    for (var x = 0; x < pieces.ByteValue.Length; x += 20)
                    {
                        var hash = pieces.ByteValue.GetBytes(x, 20);
                        var pieceInfo = new MetadataPieceInfo
                        {
                            Id = x / 20,
                            PieceHash = hash
                        };

                        PieceHashes.Add(hash);
                        Pieces.Add(pieceInfo);
                    }
                }

                if (infoDict.ContainsKey("piece length"))
                {
                    PieceSize = (BInt)infoDict["piece length"];
                }
            }

            return true;
        }

        public IReadOnlyCollection<string> GetFiles()
        {
            if (files.IsNullOrEmpty())
            {
                return new Collection<string>();
            }

            return files?.OrderBy(f => f.Id).Select(x => x.Filename)?.ToList()?.AsReadOnly();
        }

        public IReadOnlyCollection<MetadataFileInfo> GetFileInfos()
        {
            if (files.IsNullOrEmpty())
            {
                return new Collection<MetadataFileInfo>();
            }

            return files?.OrderBy(f => f.Id).ToList()?.AsReadOnly();
        }

        #region Static Helpers
        public static IMetadata FromString(string metadata)
        {
            return FromBuffer(Encoding.ASCII.GetBytes(metadata));
        }

        public static IMetadata FromBuffer(byte[] metadata)
        {
            using (var ms = new MemoryStream(metadata))
            {
                return new Metadata(ms);
            }
        }

        public static IMetadata FromFile(string filename)
        {
            using (var fs = File.OpenRead(filename))
            {
                return new Metadata(fs);
            }
        }
        #endregion
    }
}
