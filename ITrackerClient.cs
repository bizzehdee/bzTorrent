using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace System.Net.Torrent
{
    public interface ITrackerClient
    {
        String Tracker { get; }
        Int32 Port { get; }

        IEnumerable<IPEndPoint> Announce(String url, String hash);
        Dictionary<String, IEnumerable<IPEndPoint>> Announce(String url, String[] hashes);
        Dictionary<String, Tuple<UInt32, UInt32, UInt32>> Scrape(String url, String[] hashes);
    }
}
