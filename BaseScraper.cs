using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        protected enum ScraperType
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
                    throw new ArgumentOutOfRangeException("url", url, "URL is not a valid UDP tracker address");
                }

                Tracker = match.Groups[0].Value;
            }
        }
    }
}
