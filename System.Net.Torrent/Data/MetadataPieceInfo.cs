using System;
using System.Collections.Generic;
using System.Net.Torrent.Helpers;
using System.Text;

namespace System.Net.Torrent.Data
{
    public class MetadataPieceInfo
    {
        public long Id { get; set; }
        public byte[] PieceHash { get; set; }

        public override string ToString()
        {
            return UnpackHelper.Hex(PieceHash);
        }
    }
}
