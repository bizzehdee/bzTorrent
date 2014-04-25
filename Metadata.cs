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
		private IDictionary<String, Int64> Files { get; set; }

        public Metadata()
        {
            AnnounceList = new Collection<string>();
            PieceHashes = new Collection<byte[]>();
			Files = new Dictionary<string, long>();
        }

        public Metadata(Stream stream)
        {
            AnnounceList = new Collection<string>();
            PieceHashes = new Collection<byte[]>();
			Files = new Dictionary<string, long>();

            Load(stream);
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
                        byte[] hash = Utils.CopyBytes(pieces.ByteValue, x, 20);
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
