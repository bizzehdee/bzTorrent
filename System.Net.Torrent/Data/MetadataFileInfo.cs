using System;
using System.Collections.Generic;
using System.Text;

namespace System.Net.Torrent.Data
{
    public class MetadataFileInfo
    {
        public long Id { get; set; }
        public string Filename { get; set; }
        public long FileStartByte { get; set; }
        public long FileSize { get; set; }

        public override string ToString()
        {
            return Filename;
        }
    }
}
