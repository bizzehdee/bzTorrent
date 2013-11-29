using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Torrent.bencode;
using System.Text;
using System.Text.RegularExpressions;

namespace System.Net.Torrent
{
    public class HTTPScraper : BaseScraper, IScraper
    {
        public HTTPScraper(int timeout) 
            : base(timeout)
        {

        }

        public IEnumerable<IPEndPoint> Announce(string url, string hash)
        {
            byte[] hashBytes = Pack.Hex(hash);

            String realUrl = url.Replace("scrape", "announce") + "?";

            String hashEncoded = "";
            foreach (byte b in hashBytes)
            {
                hashEncoded += String.Format("%{0:X2}", b);
            }
            realUrl += "info_hash=" + hashEncoded;
            realUrl += "&peer_id=" + hashEncoded;
            realUrl += "&port=12345";
            realUrl += "&uploaded=0";
            realUrl += "&downloaded=0";
            realUrl += "&left=0";
            realUrl += "&event=started";
            realUrl += "&compact=1";

            HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(realUrl);
            webRequest.Accept = "*/*";
            webRequest.UserAgent = "System.Net.Torrent";
            HttpWebResponse webResponse = (HttpWebResponse)webRequest.GetResponse();

            Stream stream = webResponse.GetResponseStream();
            BinaryReader binaryReader = new BinaryReader(stream);

            byte[] bytes = new byte[0];

            while (true)
            {
                try
                {
                    byte[] b = new byte[1];
                    b[0] = (byte)binaryReader.ReadByte();
                    bytes = bytes.Concat(b).ToArray();
                }
                catch (Exception)
                {
                    break;
                }
            }

            BDict decoded = (BDict)BencodingUtils.Decode(bytes);
            if (decoded.Count == 0) return null;

            if (!decoded.ContainsKey("peers")) return null;

            BString peerBinary = (BString)decoded["peers"];

            return GetPeers(peerBinary.ByteValue);
        }

        private IEnumerable<IPEndPoint> GetPeers(byte[] peerData)
        {
            for (int i = 0; i < peerData.Length; i += 6)
            {
                long addr = Unpack.UInt32(peerData, i, Unpack.Endianness.Big);
                ushort port = Unpack.UInt16(peerData, i + 4, Unpack.Endianness.Big);

                yield return new IPEndPoint(addr, port);
            }
        }

        public Dictionary<string, IEnumerable<IPEndPoint>> Announce(string url, string[] hashes)
        {
            Dictionary<string, IEnumerable<IPEndPoint>> returnValue = new Dictionary<string, IEnumerable<IPEndPoint>>();

            foreach (String hash in hashes)
            {
                returnValue.Add(hash, Announce(url, hash));
            }

            return returnValue;
        }

        public Dictionary<string, Tuple<uint, uint, uint>> Scrape(string url, string[] hashes)
        {
            Dictionary<String, Tuple<UInt32, UInt32, UInt32>> returnVal = new Dictionary<string, Tuple<UInt32, UInt32, UInt32>>();

            String realUrl = url.Replace("announce", "scrape") + "?";

            String hashEncoded = "";
            foreach (String hash in hashes)
            {
                byte[] hashBytes = Pack.Hex(hash);

                foreach (byte b in hashBytes)
                {
                    hashEncoded += String.Format("%{0:X2}", b);
                }

                realUrl += "info_hash=" + hashEncoded + "&";
            }

            HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(realUrl);
            webRequest.Accept = "*/*";
            webRequest.UserAgent = "System.Net.Torrent";
            HttpWebResponse webResponse = (HttpWebResponse)webRequest.GetResponse();

            Stream stream = webResponse.GetResponseStream();
            BinaryReader binaryReader = new BinaryReader(stream);

            byte[] bytes = new byte[0];
            
            while (true)
            {
                try
                {
                    byte[] b = new byte[1];
                    b[0] = (byte)binaryReader.ReadByte();
                    bytes = bytes.Concat(b).ToArray();
                }
                catch (Exception)
                {
                    break;
                }
            }

            BDict decoded = (BDict)BencodingUtils.Decode(bytes);
            if (decoded.Count == 0) return null;

            if (!decoded.ContainsKey("files")) return null;

            BDict bDecoded = (BDict)decoded["files"];

            foreach (String k in bDecoded.Keys)
            {
                BDict d = (BDict)bDecoded[k];

                if (d.ContainsKey("complete") && d.ContainsKey("downloaded") && d.ContainsKey("incomplete"))
                {
                    String rk = Unpack.Hex(BencodingUtils.ExtendedASCIIEncoding.GetBytes(k));
                    returnVal.Add(rk, new Tuple<uint, uint, uint>((uint)((BInt)d["complete"]).Value, (uint)((BInt)d["downloaded"]).Value, (uint)((BInt)d["incomplete"]).Value));
                }
            }

            return returnVal;
        }

    }
}
