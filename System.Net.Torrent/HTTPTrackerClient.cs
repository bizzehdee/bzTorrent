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
using System.IO;
using System.Linq;
using bzBencode;
using System.Net.Torrent.Helpers;
using System.Text;

namespace System.Net.Torrent
{
    public class HTTPTrackerClient : BaseScraper, ITrackerClient
    {
        public HTTPTrackerClient(int timeout) 
            : base(timeout)
        {

        }

        private static IEnumerable<IPEndPoint> GetPeers(byte[] peerData)
        {
            for (var i = 0; i < peerData.Length; i += 6)
            {
                long addr = UnpackHelper.UInt32(peerData, i, UnpackHelper.Endianness.Big);
                var port = UnpackHelper.UInt16(peerData, i + 4, UnpackHelper.Endianness.Big);

                yield return new IPEndPoint(addr, port);
            }
        }

        public IDictionary<string, AnnounceInfo> Announce(string url, string[] hashes, string peerId)
        {
            return hashes.ToDictionary(hash => hash, hash => Announce(url, hash, peerId));
        }

        public AnnounceInfo Announce(string url, string hash, string peerId)
        {
            return Announce(url, hash, peerId, 0, 0, 0, 2, 0, -1, 12345, 0);
        }

        public AnnounceInfo Announce(string url, string hash, string peerId, long bytesDownloaded, long bytesLeft, long bytesUploaded, 
            int eventTypeFilter, int ipAddress, int numWant, int listenPort, int extensions)
        {
            var hashBytes = PackHelper.Hex(hash);
            var peerIdBytes = Encoding.ASCII.GetBytes(peerId);

            var realUrl = url.Replace("scrape", "announce") + "?";

            var hashEncoded = "";
            foreach (var b in hashBytes)
            {
                hashEncoded += string.Format("%{0:X2}", b);
            }

            var peerIdEncoded = "";
            foreach (var b in peerIdBytes)
            {
                peerIdEncoded += string.Format("%{0:X2}", b);
            }

            realUrl += "info_hash=" + hashEncoded;
            realUrl += "&peer_id=" + peerIdEncoded;
            realUrl += "&port=" + listenPort;
            realUrl += "&uploaded=" + bytesUploaded;
            realUrl += "&downloaded=" + bytesDownloaded;
            realUrl += "&left=" + bytesLeft;
            realUrl += "&event=started";
            realUrl += "&compact=1";

            var webRequest = (HttpWebRequest)WebRequest.Create(realUrl);
            webRequest.Accept = "*/*";
            webRequest.UserAgent = "System.Net.Torrent";
            var webResponse = (HttpWebResponse)webRequest.GetResponse();

            var stream = webResponse.GetResponseStream();

            if (stream == null)
            {
                return null;
            }

            var binaryReader = new BinaryReader(stream);

            var bytes = new byte[0];

            while (true)
            {
                try
                {
                    var b = new byte[1];
                    b[0] = binaryReader.ReadByte();
                    bytes = bytes.Concat(b).ToArray();
                }
                catch (Exception)
                {
                    break;
                }
            }

            var decoded = (BDict)BencodingUtils.Decode(bytes);
            if (decoded.Count == 0)
            {
                return null;
            }

            if (!decoded.ContainsKey("peers"))
            {
                return null;
            }

            if (!(decoded["peers"] is BString))
            {
                throw new NotSupportedException("Dictionary based peers not supported");
            }

            var waitTime = 0;
            var seeders = 0;
            var leechers = 0;

            if (decoded.ContainsKey("interval"))
            {
                waitTime = (BInt)decoded["interval"];
            }

            if (decoded.ContainsKey("complete"))
            {
                seeders = (BInt)decoded["complete"];
            }

            if (decoded.ContainsKey("incomplete"))
            {
                leechers = (BInt)decoded["incomplete"];
            }

            var peerBinary = (BString)decoded["peers"];

            return new AnnounceInfo(GetPeers(peerBinary.ByteValue), waitTime, seeders, leechers);
        }

        public IDictionary<string, ScrapeInfo> Scrape(string url, string[] hashes)
        {
            var returnVal = new Dictionary<string, ScrapeInfo>();

            var realUrl = url.Replace("announce", "scrape") + "?";

            var hashEncoded = "";
            foreach (var hash in hashes)
            {
                var hashBytes = PackHelper.Hex(hash);

                hashEncoded = hashBytes.Aggregate(hashEncoded, (current, b) => current + string.Format("%{0:X2}", b));

                realUrl += "info_hash=" + hashEncoded + "&";
            }

            var webRequest = (HttpWebRequest)WebRequest.Create(realUrl);
            webRequest.Accept = "*/*";
            webRequest.UserAgent = "System.Net.Torrent";
            webRequest.Timeout = Timeout*1000;
            var webResponse = (HttpWebResponse)webRequest.GetResponse();

            var stream = webResponse.GetResponseStream();

            if (stream == null)
            {
                return null;
            }

            var binaryReader = new BinaryReader(stream);

            var bytes = new byte[0];
            
            while (true)
            {
                try
                {
                    var b = new byte[1];
                    b[0] = binaryReader.ReadByte();
                    bytes = bytes.Concat(b).ToArray();
                }
                catch (Exception)
                {
                    break;
                }
            }

            var decoded = (BDict)BencodingUtils.Decode(bytes);
            if (decoded.Count == 0)
            {
                return null;
            }

            if (!decoded.ContainsKey("files"))
            {
                return null;
            }

            var bDecoded = (BDict)decoded["files"];

            foreach (var k in bDecoded.Keys)
            {
                var d = (BDict)bDecoded[k];

                if (d.ContainsKey("complete") && d.ContainsKey("downloaded") && d.ContainsKey("incomplete"))
                {
                    var rk = UnpackHelper.Hex(BencodingUtils.ExtendedASCIIEncoding.GetBytes(k));
                    returnVal.Add(rk, new ScrapeInfo((uint)((BInt)d["complete"]).Value, (uint)((BInt)d["downloaded"]).Value, (uint)((BInt)d["incomplete"]).Value, ScraperType.HTTP));
                }
            }

            return returnVal;
        }
    }
}
