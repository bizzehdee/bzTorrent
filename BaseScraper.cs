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
using System.Text.RegularExpressions;

namespace System.Net.Torrent
{
    public abstract class BaseScraper
    {
        protected readonly Regex HashRegex = new Regex("^[a-f0-9]{40}$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase);
        protected readonly Regex UDPRegex = new Regex("udp://([^:/]*)(?::([0-9]*))?(?:/)?", RegexOptions.ECMAScript | RegexOptions.IgnoreCase);
        protected readonly Regex HTTPRegex = new Regex("(http://.*?/)announce?|scrape?([^/]*)$", RegexOptions.ECMAScript | RegexOptions.IgnoreCase);
        protected readonly byte[] BaseCurrentConnectionId = { 0x00, 0x00, 0x04, 0x17, 0x27, 0x10, 0x19, 0x80 };
        protected readonly Random Random = new Random(DateTime.Now.Second);

        public Int32 Timeout { get; private set; }
        public String Tracker { get; private set; }
        public Int32 Port { get; private set; }

        protected BaseScraper(Int32 timeout)
        {
            Timeout = timeout;
        }

        public enum ScraperType
        {
            UDP,
            HTTP
        }

        protected void ValidateInput(String url, String[] hashes, ScraperType type)
        {
            if (hashes.Length < 1)
            {
                throw new ArgumentOutOfRangeException("hashes", hashes, "Must have at least one hash when calling scrape");
            }

            if (hashes.Length > 74)
            {
                throw new ArgumentOutOfRangeException("hashes", hashes, "Must have a maximum of 74 hashes when calling scrape");
            }

            foreach (String hash in hashes)
            {
                if (!HashRegex.IsMatch(hash))
                {
                    throw new ArgumentOutOfRangeException("hashes", hash, "Hash is not valid");
                }
            }

            if (type == ScraperType.UDP)
            {
                Match match = UDPRegex.Match(url);

                if (!match.Success)
                {
                    throw new ArgumentOutOfRangeException("url", url, "URL is not a valid UDP tracker address");
                }

                Tracker = match.Groups[1].Value;
                Port = match.Groups.Count == 3 ? Convert.ToInt32(match.Groups[2].Value) : 80;
            }
            else if (type == ScraperType.HTTP)
            {
                Match match = HTTPRegex.Match(url);

                if (!match.Success)
                {
                    throw new ArgumentOutOfRangeException("url", url, "URL is not a valid HTTP tracker address");
                }

                Tracker = match.Groups[0].Value;
            }
        }

        public class AnnounceInfo
        {
            public IEnumerable<EndPoint> Peers { get; set; }
            public Int32 WaitTime { get; set; }
            public Int32 Seeders { get; set; }
            public Int32 Leachers { get; set; }

            public AnnounceInfo(IEnumerable<EndPoint> peers, Int32 a, Int32 b, Int32 c)
            {
                Peers = peers;

                WaitTime = a;
                Seeders = b;
                Leachers = c;
            }
        }

        public class ScrapeInfo
        {
            public UInt32 Seeders { get; set; }
            public UInt32 Complete { get; set; }
            public UInt32 Leachers { get; set; }
            public UInt32 Downloaded { get; set; }
            public UInt32 Incomplete { get; set; }

            public ScrapeInfo(UInt32 a, UInt32 b, UInt32 c, ScraperType type)
            {
                if (type == ScraperType.HTTP)
                {
                    Complete = a;
                    Downloaded = b;
                    Incomplete = c;
                }
                else if (type == ScraperType.UDP)
                {
                    Seeders = a;
                    Complete = b;
                    Leachers = c;
                }
            }
        }
    }
}
