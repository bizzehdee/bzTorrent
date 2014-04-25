using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Torrent.bencode;
using System.Text;

namespace System.Net.Torrent
{
    public class Metadata
    {
        private IBencodingType _root;

        public String Comment { get; set; }
        public String Announce { get; set; }
        public ICollection<String> AnnounceList { get; set; }
        public String CreatedBy { get; set; }
        public DateTime CreationDate { get; set; }
        public String Name { get; set; }
        public Int64 PieceSize { get; set; }
        public ICollection<byte[]> PieceHashes { get; set; }
        public bool Private { get; set; }

        public Metadata()
        {
            AnnounceList = new Collection<string>();
            PieceHashes = new Collection<byte[]>();
        }

        public Metadata(Stream stream)
        {
            AnnounceList = new Collection<string>();
            PieceHashes = new Collection<byte[]>();

            Load(stream);
        }

        public bool Load(Stream stream)
        {
            _root = BencodingUtils.Decode(stream);
            if (_root == null) return false;

            BDict _dictRoot = (_root as BDict);
            if (_dictRoot == null) return false;

            if (_dictRoot.ContainsKey("announce"))
            {
                Announce = (BString)_dictRoot["announce"];
            }

            if (_dictRoot.ContainsKey("announce-list"))
            {
                BList announceList = (BList)_dictRoot["announce-list"];
                foreach (IBencodingType type in announceList)
                {
                    if (type is BString)
                    {
                        AnnounceList.Add(type as BString);
                    }
                    else
                    {
                        BList list = type as BList;
                        if (list != null)
                        {
                            BList listType = list;
                            foreach (BString s in listType)
                            {
                                AnnounceList.Add(s);
                            }
                        }
                    }
                }
            }

            if (_dictRoot.ContainsKey("comment"))
            {
                Comment = (BString)_dictRoot["comment"];
            }

            if (_dictRoot.ContainsKey("created by"))
            {
                CreatedBy = (BString)_dictRoot["created by"];
            }

            if (_dictRoot.ContainsKey("creation date"))
            {
                long ts = (BInt)_dictRoot["creation date"];
                CreationDate = new DateTime(1970, 1, 1).AddSeconds(ts);
            }

            if (_dictRoot.ContainsKey("info"))
            {
                BDict _infoDict = (BDict)_dictRoot["info"];

                if (_infoDict.ContainsKey("name"))
                {
                    Name = (BString)_infoDict["name"];
                }

                if (_infoDict.ContainsKey("private"))
                {
                    BInt isPrivate = (BInt)_infoDict["private"];
                    Private = isPrivate != 0;
                }

                if (_infoDict.ContainsKey("pieces"))
                {
                    BString pieces = (BString)_infoDict["pieces"];
                    for (int x = 0; x < pieces.ByteValue.Length; x += 20)
                    {
                        byte[] hash = Utils.CopyBytes(pieces.ByteValue, x, 20);
                        PieceHashes.Add(hash);
                    }
                }

                if (_infoDict.ContainsKey("piece length"))
                {
                    PieceSize = (BInt)_infoDict["piece length"];
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
