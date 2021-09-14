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
using System.Collections.ObjectModel;
using System.Net.Torrent.Helpers;
using System.Threading.Tasks;

namespace System.Net.Torrent.Data
{
    public class MagnetLink : IMagnetLink
    {
        public string Name { get; private set; }

        public byte[] Hash { get; private set; }

        public string HashString
        {
            get => UnpackHelper.Hex(Hash);
            private set => Hash = PackHelper.Hex(value);
        }

        public ICollection<string> Trackers { get; set; }

        public MagnetLink()
        {
            Trackers = new Collection<string>();
        }

        public static MagnetLink Resolve(string magnetLink)
        {
            IEnumerable<KeyValuePair<string, string>> values = null;

            if (IsMagnetLink(magnetLink))
            {
                values = SplitURLIntoParts(magnetLink.Substring(8));
            }

            if (values == null)
            {
                return null;
            }

            var magnet = new MagnetLink();

            foreach (var pair in values)
            {
                if (pair.Key == "xt")
                {
                    if (!IsXTValidHash(pair.Value))
                    {
                        continue;
                    }

                    magnet.HashString = pair.Value.Substring(9);
                }

                if (pair.Key == "dn")
                {
                    magnet.Name = pair.Value;
                }

                if (pair.Key == "tr")
                {
                    magnet.Trackers.Add(pair.Value);
                }
            }

            return magnet;
        }

        public static IMetadata ResolveToMetadata(string magnetLink)
        {
            return new Metadata(Resolve(magnetLink));
        }

        public static async Task<IMetadata> ResolveToMetadataAsync(string magnetLink)
        {
            return await Task.Run(() => new Metadata(Resolve(magnetLink)));
        }

        public static bool IsMagnetLink(string magnetLink)
        {
            return magnetLink.StartsWith("magnet:");
        }

        private static bool IsXTValidHash(string xt)
        {
            return xt.Length == 49 && xt.StartsWith("urn:btih:");
        }

        private static IEnumerable<KeyValuePair<string, string>> SplitURLIntoParts(string magnetLink)
        {
            var parts = magnetLink.Split('&');
            var values = new Collection<KeyValuePair<string, string>>();

            foreach (var str in parts)
            {
                var kv = str.Split('=');
                values.Add(new KeyValuePair<string, string>(kv[0], Uri.UnescapeDataString(kv[1])));
            }

            return values;
        }
    }
}
