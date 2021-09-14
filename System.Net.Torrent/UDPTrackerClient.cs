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
using System.Data;
using System.Linq;
using System.Net.Sockets;
using System.Net.Torrent.Helpers;
using System.Text;

namespace System.Net.Torrent
{
    public class UDPTrackerClient : BaseScraper, ITrackerClient
    {
        private byte[] _currentConnectionId;

        public UDPTrackerClient(int timeout)
            : base(timeout)
        {
            _currentConnectionId = BaseCurrentConnectionId;
        }

        public IDictionary<string, ScrapeInfo> Scrape(string url, string[] hashes)
        {
            var returnVal = new Dictionary<string, ScrapeInfo>();

            ValidateInput(url, hashes, ScraperType.UDP);

            var transactionId = Random.Next(0, 65535);

            var udpClient = new UdpClient(Tracker, Port)
                {
                    Client =
                        {
                            SendTimeout = Timeout*1000,
                            ReceiveTimeout = Timeout*1000
                        }
                };

            var sendBuf = _currentConnectionId.Concat(PackHelper.Int32(0)).Concat(PackHelper.Int32(transactionId)).ToArray();
            udpClient.Send(sendBuf, sendBuf.Length);

            IPEndPoint endPoint = null;
            var recBuf = udpClient.Receive(ref endPoint);

            if (recBuf == null)
            {
                throw new NoNullAllowedException("udpClient failed to receive");
            }

            if (recBuf.Length < 0)
            {
                throw new InvalidOperationException("udpClient received no response");
            }

            if (recBuf.Length < 16)
            {
                throw new InvalidOperationException("udpClient did not receive entire response");
            }

            var recAction = UnpackHelper.UInt32(recBuf, 0, UnpackHelper.Endianness.Big);
            var recTrasactionId = UnpackHelper.UInt32(recBuf, 4, UnpackHelper.Endianness.Big);

            if (recAction != 0 || recTrasactionId != transactionId)
            {
                throw new Exception("Invalid response from tracker");
            }

            _currentConnectionId = CopyBytes(recBuf, 8, 8);

            var hashBytes = new byte[0];
            hashBytes = hashes.Aggregate(hashBytes, (current, hash) => current.Concat(PackHelper.Hex(hash)).ToArray());

            var expectedLength = 8 + (12 * hashes.Length);

            sendBuf = _currentConnectionId.Concat(PackHelper.Int32(2)).Concat(PackHelper.Int32(transactionId)).Concat(hashBytes).ToArray();
            udpClient.Send(sendBuf, sendBuf.Length);

            recBuf = udpClient.Receive(ref endPoint);

            if (recBuf == null)
            {
                throw new NoNullAllowedException("udpClient failed to receive");
            }

            if (recBuf.Length < 0)
            {
                throw new InvalidOperationException("udpClient received no response");
            }

            if (recBuf.Length < expectedLength)
            {
                throw new InvalidOperationException("udpClient did not receive entire response");
            }

            recAction = UnpackHelper.UInt32(recBuf, 0, UnpackHelper.Endianness.Big);
            recTrasactionId = UnpackHelper.UInt32(recBuf, 4, UnpackHelper.Endianness.Big);

            _currentConnectionId = CopyBytes(recBuf, 8, 8);

            if (recAction != 2 || recTrasactionId != transactionId)
            {
                throw new Exception("Invalid response from tracker");
            }

            var startIndex = 8;
            foreach (var hash in hashes)
            {
                var seeders = UnpackHelper.UInt32(recBuf, startIndex, UnpackHelper.Endianness.Big);
                var completed = UnpackHelper.UInt32(recBuf, startIndex + 4, UnpackHelper.Endianness.Big);
                var Leechers = UnpackHelper.UInt32(recBuf, startIndex + 8, UnpackHelper.Endianness.Big);

                returnVal.Add(hash, new ScrapeInfo(seeders, completed, Leechers, ScraperType.UDP));

                startIndex += 12;
            }

            udpClient.Close();

            return returnVal;
        }

        public AnnounceInfo Announce(string url, string hash, string peerId)
        {
            return Announce(url, hash, peerId, 0, 0, 0, 2, 0, -1, 12345, 0);
        }

        public AnnounceInfo Announce(string url, string hash, string peerId, long bytesDownloaded, long bytesLeft, long bytesUploaded, 
            int eventTypeFilter, int ipAddress, int numWant, int listenPort, int extensions)
        {
            var returnValue = new List<IPEndPoint>();

            ValidateInput(url, new[] { hash }, ScraperType.UDP);

            _currentConnectionId = BaseCurrentConnectionId;
            var trasactionId = Random.Next(0, 65535);

            var udpClient = new UdpClient(Tracker, Port)
                {
                    DontFragment = true,
                    Client =
                        {
                            SendTimeout = Timeout*1000,
                            ReceiveTimeout = Timeout*1000
                        }
                };

            var sendBuf = _currentConnectionId.Concat(PackHelper.Int32(0)).Concat(PackHelper.Int32(trasactionId)).ToArray();
            udpClient.Send(sendBuf, sendBuf.Length);

            IPEndPoint endPoint = null;
            byte[] recBuf;

            try
            {
                recBuf = udpClient.Receive(ref endPoint);
            }
            catch (Exception)
            {
                return null;
            }

            if (recBuf == null)
            {
                throw new NoNullAllowedException("udpClient failed to receive");
            }

            if (recBuf.Length < 0)
            {
                throw new InvalidOperationException("udpClient received no response");
            }

            if (recBuf.Length < 16)
            {
                throw new InvalidOperationException("udpClient did not receive entire response");
            }

            var recAction = UnpackHelper.UInt32(recBuf, 0, UnpackHelper.Endianness.Big);
            var recTrasactionId = UnpackHelper.UInt32(recBuf, 4, UnpackHelper.Endianness.Big);

            if (recAction != 0 || recTrasactionId != trasactionId)
            {
                throw new Exception("Invalid response from tracker");
            }

            _currentConnectionId = CopyBytes(recBuf, 8, 8);

            var hashBytes = PackHelper.Hex(hash).ToArray();

            var key = Random.Next(0, 65535);

            sendBuf = _currentConnectionId. /*connection id*/
                Concat(PackHelper.Int32(1)). /*action*/
                Concat(PackHelper.Int32(trasactionId)). /*trasaction Id*/
                Concat(hashBytes). /*hash*/
                Concat(Encoding.ASCII.GetBytes(peerId)). /*my peer id*/
                Concat(PackHelper.Int64(bytesDownloaded)). /*bytes downloaded*/
                Concat(PackHelper.Int64(bytesLeft)). /*bytes left*/
                Concat(PackHelper.Int64(bytesUploaded)). /*bytes uploaded*/
                Concat(PackHelper.Int32(eventTypeFilter)). /*event, 0 for none, 2 for just started*/
                Concat(PackHelper.Int32(ipAddress)). /*ip, 0 for this one*/
                Concat(PackHelper.Int32(key)). /*unique key*/
                Concat(PackHelper.Int32(numWant)). /*num want, -1 for as many as pos*/
                Concat(PackHelper.Int32(listenPort)). /*listen port*/
                Concat(PackHelper.Int32(extensions)).ToArray(); /*extensions*/
            udpClient.Send(sendBuf, sendBuf.Length);

            try
            {
                recBuf = udpClient.Receive(ref endPoint);
            }
            catch (Exception)
            {
                return null;
            }

            recAction = UnpackHelper.UInt32(recBuf, 0, UnpackHelper.Endianness.Big);
            recTrasactionId = UnpackHelper.UInt32(recBuf, 4, UnpackHelper.Endianness.Big);

            var waitTime = (int)UnpackHelper.UInt32(recBuf, 8, UnpackHelper.Endianness.Big);
            var Leechers = (int)UnpackHelper.UInt32(recBuf, 12, UnpackHelper.Endianness.Big);
            var seeders = (int)UnpackHelper.UInt32(recBuf, 16, UnpackHelper.Endianness.Big);

            if (recAction != 1 || recTrasactionId != trasactionId)
            {
                throw new Exception("Invalid response from tracker");
            }

            for (var i = 20; i < recBuf.Length; i += 6)
            {
                var ip = UnpackHelper.UInt32(recBuf, i, UnpackHelper.Endianness.Big);
                var port = UnpackHelper.UInt16(recBuf, i + 4, UnpackHelper.Endianness.Big);

                returnValue.Add(new IPEndPoint(ip, port));
            }

            udpClient.Close();

            return new AnnounceInfo(returnValue, waitTime, seeders, Leechers);
        }

        public IDictionary<string, AnnounceInfo> Announce(string url, string[] hashes, string peerId)
        {
            ValidateInput(url, hashes, ScraperType.UDP);

            var returnVal = hashes.ToDictionary(hash => hash, hash => Announce(url, hash, peerId));

            return returnVal;
        }

        private static byte[] CopyBytes(byte[] bytes, int start, int length)
        {
            var intBytes = new byte[length];
            for (var i = 0; i < length; i++)
            {
                intBytes[i] = bytes[start + i];
            }

            return intBytes;
        }
    }
}
