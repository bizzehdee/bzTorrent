/*
Copyright (c) 2021, Darren Horrocks
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using bzBencode;

namespace bzTorrent.Data
{
	/// <summary>
	/// Builds a torrent's bencode root dictionary from files on disk (enumerating files, hashing
	/// content into pieces, assembling the info/announce dictionaries). Split out of Metadata so
	/// the "create a torrent from disk" concern is separate from parsing/holding/serialising an
	/// existing torrent — <see cref="Metadata.CreateFromPath"/> is a thin facade over this.
	/// </summary>
	internal static class TorrentBuilder
	{
		public static BDict Build(string sourcePath, IEnumerable<string> announceUrls, long pieceSize, string comment, bool isPrivate)
		{
			if (string.IsNullOrEmpty(sourcePath))
			{
				throw new ArgumentNullException(nameof(sourcePath));
			}

			var isDirectory = Directory.Exists(sourcePath);
			if (!isDirectory && !File.Exists(sourcePath))
			{
				throw new FileNotFoundException("Source path not found", sourcePath);
			}

			var fileEntries = new List<(string RelativePath, string FullPath, long Size)>();
			string torrentName;

			if (isDirectory)
			{
				torrentName = new DirectoryInfo(sourcePath).Name;
				var basePath = Path.GetFullPath(sourcePath);
				foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
				{
					var relative = Path.GetFullPath(file).Substring(basePath.Length)
						.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
					fileEntries.Add((relative, file, new FileInfo(file).Length));
				}

				if (fileEntries.Count == 0)
				{
					throw new InvalidOperationException("Source directory contains no files");
				}
			}
			else
			{
				torrentName = Path.GetFileName(sourcePath);
				fileEntries.Add((torrentName, sourcePath, new FileInfo(sourcePath).Length));
			}

			var totalSize = fileEntries.Sum(f => f.Size);
			if (pieceSize <= 0)
			{
				pieceSize = RecommendedPieceSize(totalSize);
			}

			var infoDict = new BDict
			{
				["piece length"] = (BInt)pieceSize,
				["pieces"] = new BString { ByteValue = HashPieces(fileEntries, pieceSize) },
				["name"] = (BString)torrentName
			};

			if (isPrivate)
			{
				infoDict["private"] = (BInt)1;
			}

			if (isDirectory)
			{
				var filesList = new BList();
				foreach (var entry in fileEntries)
				{
					var pathParts = entry.RelativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
					var pathList = new BList();
					foreach (var part in pathParts)
					{
						pathList.Add((BString)part);
					}

					filesList.Add(new BDict
					{
						["length"] = (BInt)entry.Size,
						["path"] = pathList
					});
				}
				infoDict["files"] = filesList;
			}
			else
			{
				infoDict["length"] = (BInt)totalSize;
			}

			var rootDict = new BDict { ["info"] = infoDict };

			var trackers = announceUrls?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? new List<string>();
			if (trackers.Count > 0)
			{
				rootDict["announce"] = (BString)trackers[0];

				var announceList = new BList();
				foreach (var tracker in trackers)
				{
					var tier = new BList { (BString)tracker };
					announceList.Add(tier);
				}
				rootDict["announce-list"] = announceList;
			}

			if (!string.IsNullOrEmpty(comment))
			{
				rootDict["comment"] = (BString)comment;
			}

			rootDict["created by"] = (BString)"bzTorrent";
			rootDict["creation date"] = (BInt)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

			return rootDict;
		}

		private static long RecommendedPieceSize(long totalSize)
		{
			// Common client heuristic: bigger torrents get bigger pieces so the piece count
			// (and the "pieces" field/tracker overhead it drives) stays reasonable.
			var pieceSize = 16L * 1024;
			while (totalSize / pieceSize > 1500 && pieceSize < 16L * 1024 * 1024)
			{
				pieceSize *= 2;
			}
			return pieceSize;
		}

		private static byte[] HashPieces(List<(string RelativePath, string FullPath, long Size)> fileEntries, long pieceSize)
		{
			using (var sha1 = new SHA1Managed())
			using (var hashOutput = new MemoryStream())
			{
				var buffer = new byte[pieceSize];
				var bufferFill = 0;

				void FlushPiece()
				{
					if (bufferFill == 0) return;
					var hash = sha1.ComputeHash(buffer, 0, bufferFill);
					hashOutput.Write(hash, 0, hash.Length);
					bufferFill = 0;
				}

				foreach (var entry in fileEntries)
				{
					using (var fileStream = File.Open(entry.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
					{
						int read;
						while ((read = fileStream.Read(buffer, bufferFill, (int)(pieceSize - bufferFill))) > 0)
						{
							bufferFill += read;
							if (bufferFill == pieceSize)
							{
								FlushPiece();
							}
						}
					}
				}
				FlushPiece();

				return hashOutput.ToArray();
			}
		}
	}
}
