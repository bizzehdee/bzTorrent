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
using bzTorrent.Extensions;
using bzTorrent.Helpers;
using System.Security.Cryptography;
using System.Text;
using System;

namespace bzTorrent.Data
{
	public class Metadata : IMetadata
	{
		private IBencodingType _root;
		private readonly ICollection<MetadataFileInfo> files = new List<MetadataFileInfo>();

		public InfoHash Hash { get; private set; }

		public string HashString => Hash?.Hex;

		public string Comment { get; private set; }

		public string Announce { get; private set; }

		public ICollection<string> AnnounceList { get; private set; } = new Collection<string>();

		public string CreatedBy { get; private set; }

		public DateTime CreationDate { get; private set; }

		public string Name { get; private set; }

		public long PieceSize { get; private set; }

		public ICollection<byte[]> PieceHashes { get; private set; } = new Collection<byte[]>();
		public ICollection<MetadataPieceInfo> Pieces { get; private set; } = new Collection<MetadataPieceInfo>();

		public bool Private { get; private set; }

		public ICollection<string> WebSeeds { get; private set; } = new Collection<string>();

		public bool IsMultiFile { get; private set; }

		public Metadata()
		{

		}

		public Metadata(Stream stream)
		{
			this.Load(stream);
		}

		public Metadata(MagnetLink magnetLink)
		{
			Load(magnetLink);
		}

		public bool Load(MagnetLink magnetLink)
		{
			if (magnetLink?.Hash == null)
			{
				return false;
			}

			Hash = magnetLink.Hash;
			Name = magnetLink.Name;

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

			return LoadRoot(dictRoot);
		}

		private bool LoadRoot(BDict dictRoot)
		{
			_root = dictRoot;

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
				// Unix epoch seconds -> a UTC DateTime (the old new DateTime(1970,1,1) had
				// Kind=Unspecified; the write side already uses Unix time via DateTimeOffset).
				CreationDate = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
			}

			if (dictRoot.ContainsKey("url-list"))
			{
				WebSeeds = new Collection<string>();
				var urlList = dictRoot["url-list"];
				if (urlList is BString single)
				{
					WebSeeds.Add(single);
				}
				else if (urlList is BList list)
				{
					foreach (var entry in list)
					{
						WebSeeds.Add((BString)entry);
					}
				}
			}

			if (dictRoot.ContainsKey("info"))
			{
				LoadInfoDictionary((BDict)dictRoot["info"]);
			}

			return true;
		}

		public bool LoadInfoDictionary(BDict infoDict)
		{
			if (infoDict == null) return false;

			using (var sha1 = new SHA1Managed())
			{
				var str = BencodingUtils.EncodeBytes(infoDict);
				Hash = new InfoHash(sha1.ComputeHash(str));
			}

			files.Clear();
			PieceHashes = new Collection<byte[]>();
			Pieces = new Collection<MetadataPieceInfo>();

			IsMultiFile = infoDict.ContainsKey("files");

			if (infoDict.ContainsKey("files"))
			{
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
							filename += MetadataFileInfo.PathSeparator;
						}
						filename = filename.Trim(MetadataFileInfo.PathSeparator);
					}

					if (fileDict.ContainsKey("length"))
						filesize = (BInt)fileDict["length"];

					files.Add(new MetadataFileInfo { Id = id, FileSize = filesize, Filename = filename, FileStartByte = startByte });
					id++;
					startByte += filesize;
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
				Private = (BInt)infoDict["private"] != 0;

			if (infoDict.ContainsKey("pieces"))
			{
				var pieces = (BString)infoDict["pieces"];
				for (var x = 0; x < pieces.ByteValue.Length; x += 20)
				{
					var hash = pieces.ByteValue.GetBytes(x, 20);
					PieceHashes.Add(hash);
					Pieces.Add(new MetadataPieceInfo { Id = x / 20, PieceHash = hash });
				}
			}

			if (infoDict.ContainsKey("piece length"))
				PieceSize = (BInt)infoDict["piece length"];

			return true;
		}

		public void Save(Stream stream)
		{
			if (_root == null)
			{
				throw new InvalidOperationException("No metadata to save — load or create metadata first.");
			}

			BencodingUtils.Encode(_root, stream);
		}

		public void SaveToFile(string filename)
		{
			using (var fs = File.Open(filename, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				Save(fs);
			}
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
			var fs = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
			var meta = new Metadata();
			meta.Load(fs);
			fs.Close();
			return meta;

		}

		/// <summary>
		/// Builds torrent metadata from a file or directory on disk: hashes the content into
		/// pieces and produces a Metadata instance ready to be written out with Save/SaveToFile.
		/// </summary>
		/// <param name="sourcePath">A single file, or a directory whose files (recursively) become a multi-file torrent.</param>
		/// <param name="announceUrls">Tracker URLs, in priority order. May be null/empty for a trackerless (DHT/PEX-only) torrent.</param>
		/// <param name="pieceSize">Piece size in bytes. Pass 0 to pick a size automatically based on total content size.</param>
		public static IMetadata CreateFromPath(string sourcePath, IEnumerable<string> announceUrls = null, long pieceSize = 0, string comment = null, bool isPrivate = false)
		{
			var rootDict = TorrentBuilder.Build(sourcePath, announceUrls, pieceSize, comment, isPrivate);

			var metadata = new Metadata();
			metadata.LoadRoot(rootDict);
			return metadata;
		}
		#endregion
	}
}
