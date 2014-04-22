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

namespace System.Net.Torrent
{
    public class UDPTrackerClient : BaseScraper, ITrackerClient
    {
        private byte[] _currentConnectionId;

        public UDPTrackerClient(Int32 timeout) 
            : base(timeout)
        {
            _currentConnectionId = BaseCurrentConnectionId;
        }

		public Dictionary<String, ScrapeInfo> Scrape(String url, String[] hashes)
        {
			Dictionary<String, ScrapeInfo> returnVal = new Dictionary<string, ScrapeInfo>();

            ValidateInput(url, hashes, ScraperType.UDP);

            Int32 trasactionId = Random.Next(0, 65535);

            UdpClient udpClient = new UdpClient(Tracker, Port)
                {
                    Client =
                        {
                            SendTimeout = Timeout*1000, 
                            ReceiveTimeout = Timeout*1000
                        }
                };

            byte[] sendBuf = _currentConnectionId.Concat(Pack.Int32(0)).Concat(Pack.Int32(trasactionId)).ToArray();
            udpClient.Send(sendBuf, sendBuf.Length);

            IPEndPoint endPoint = null;
            byte[] recBuf = udpClient.Receive(ref endPoint);

            if(recBuf == null) throw new NoNullAllowedException("udpClient failed to receive");
            if(recBuf.Length < 0) throw new InvalidOperationException("udpClient received no response");
            if(recBuf.Length < 16) throw new InvalidOperationException("udpClient did not receive entire response");

            UInt32 recAction = Unpack.UInt32(recBuf, 0, Unpack.Endianness.Big);
            UInt32 recTrasactionId = Unpack.UInt32(recBuf, 4, Unpack.Endianness.Big);

            if (recAction != 0 || recTrasactionId != trasactionId)
            {
                throw new Exception("Invalid response from tracker");
            }

            _currentConnectionId = CopyBytes(recBuf, 8, 8);

            byte[] hashBytes = new byte[0];
            hashBytes = hashes.Aggregate(hashBytes, (current, hash) => current.Concat(Pack.Hex(hash)).ToArray());

            int expectedLength = 8 + (12 * hashes.Length);

            sendBuf = _currentConnectionId.Concat(Pack.Int32(2)).Concat(Pack.Int32(trasactionId)).Concat(hashBytes).ToArray();
            udpClient.Send(sendBuf, sendBuf.Length);

            recBuf = udpClient.Receive(ref endPoint);

            if (recBuf == null) throw new NoNullAllowedException("udpClient failed to receive");
            if (recBuf.Length < 0) throw new InvalidOperationException("udpClient received no response");
            if (recBuf.Length < expectedLength) throw new InvalidOperationException("udpClient did not receive entire response");

            recAction = Unpack.UInt32(recBuf, 0, Unpack.Endianness.Big);
            recTrasactionId = Unpack.UInt32(recBuf, 4, Unpack.Endianness.Big);

            _currentConnectionId = CopyBytes(recBuf, 8, 8);

            if (recAction != 2 || recTrasactionId != trasactionId)
            {
                throw new Exception("Invalid response from tracker");
            }

            Int32 startIndex = 8;
            foreach (String hash in hashes)
            {
                UInt32 seeders = Unpack.UInt32(recBuf, startIndex, Unpack.Endianness.Big);
                UInt32 completed = Unpack.UInt32(recBuf, startIndex + 4, Unpack.Endianness.Big);
                UInt32 leachers = Unpack.UInt32(recBuf, startIndex + 8, Unpack.Endianness.Big);

				returnVal.Add(hash, new ScrapeInfo(seeders, completed, leachers, ScraperType.UDP));

                startIndex += 12;
            }

            udpClient.Close();

            return returnVal;
        }

        public IEnumerable<IPEndPoint> Announce(String url, String hash, String peerId)
        {
            return Announce(url, hash, peerId, 0, 0, 0, 2, 0, -1, 12345, 0);
        }

        public IEnumerable<IPEndPoint> Announce(String url, String hash, String peerId, Int64 bytesDownloaded, Int64 bytesLeft, Int64 bytesUploaded, 
            Int32 eventTypeFilter, Int32 ipAddress, Int32 numWant, Int32 listenPort, Int32 extensions)
        {
            List<IPEndPoint> returnValue = new List<IPEndPoint>();

            ValidateInput(url, new[] { hash }, ScraperType.UDP);

            _currentConnectionId = BaseCurrentConnectionId;
            Int32 trasactionId = Random.Next(0, 65535);

            UdpClient udpClient = new UdpClient(Tracker, Port)
                {
                    DontFragment = true,
                    Client =
                        {
                            SendTimeout = Timeout*1000,
                            ReceiveTimeout = Timeout*1000
                        }
                };

            byte[] sendBuf = _currentConnectionId.Concat(Pack.Int32(0)).Concat(Pack.Int32(trasactionId)).ToArray();
            udpClient.Send(sendBuf, sendBuf.Length);

            IPEndPoint endPoint = null;
            byte[] recBuf = udpClient.Receive(ref endPoint);

            if (recBuf == null) throw new NoNullAllowedException("udpClient failed to receive");
            if (recBuf.Length < 0) throw new InvalidOperationException("udpClient received no response");
            if (recBuf.Length < 16) throw new InvalidOperationException("udpClient did not receive entire response");

            UInt32 recAction = Unpack.UInt32(recBuf, 0, Unpack.Endianness.Big);
            UInt32 recTrasactionId = Unpack.UInt32(recBuf, 4, Unpack.Endianness.Big);

            if (recAction != 0 || recTrasactionId != trasactionId)
            {
                throw new Exception("Invalid response from tracker");
            }

            _currentConnectionId = CopyBytes(recBuf, 8, 8);

            byte[] hashBytes = Pack.Hex(hash).ToArray();

            Int32 key = Random.Next(0, 65535);

            sendBuf = _currentConnectionId. /*connection id*/
                Concat(Pack.Int32(1)). /*action*/
                Concat(Pack.Int32(trasactionId)). /*trasaction Id*/
                Concat(hashBytes). /*hash*/
                Concat(Pack.Hex(peerId)). /*my peer id*/
                Concat(Pack.Int64(bytesDownloaded)). /*bytes downloaded*/
                Concat(Pack.Int64(bytesLeft)). /*bytes left*/
                Concat(Pack.Int64(bytesUploaded)). /*bytes uploaded*/
                Concat(Pack.Int32(eventTypeFilter)). /*event, 0 for none, 2 for just started*/
                Concat(Pack.Int32(ipAddress)). /*ip, 0 for this one*/
                Concat(Pack.Int32(key)). /*unique key*/
                Concat(Pack.Int32(numWant)). /*num want, -1 for as many as pos*/
                Concat(Pack.Int32(listenPort)). /*listen port*/
                Concat(Pack.Int32(extensions)).ToArray(); /*extensions*/
            udpClient.Send(sendBuf, sendBuf.Length);

            recBuf = udpClient.Receive(ref endPoint);

            recAction = Unpack.UInt32(recBuf, 0, Unpack.Endianness.Big);
            recTrasactionId = Unpack.UInt32(recBuf, 4, Unpack.Endianness.Big);

            int waitTime = (int)Unpack.UInt32(recBuf, 8, Unpack.Endianness.Big);
            int leachers = (int)Unpack.UInt32(recBuf, 12, Unpack.Endianness.Big);
            int seeders = (int)Unpack.UInt32(recBuf, 16, Unpack.Endianness.Big);

            if (recAction != 1 || recTrasactionId != trasactionId)
            {
                throw new Exception("Invalid response from tracker");
            }

            for (Int32 i = 20; i < recBuf.Length; i += 6)
            {
                UInt32 ip = Unpack.UInt32(recBuf, i, Unpack.Endianness.Big);
                UInt16 port = Unpack.UInt16(recBuf, i + 4, Unpack.Endianness.Big);

                returnValue.Add(new IPEndPoint(ip, port));
            }

            udpClient.Close();

            return returnValue;
        }

        public Dictionary<String, IEnumerable<IPEndPoint>> Announce(String url, String[] hashes, String peerId)
        {
            ValidateInput(url, hashes, ScraperType.UDP);

            Dictionary<String, IEnumerable<IPEndPoint>> returnVal = hashes.ToDictionary(hash => hash, hash => Announce(url, hash, peerId));

            return returnVal;
        }

        private static byte[] CopyBytes(byte[] bytes, Int32 start, Int32 length)
        {
            byte[] intBytes = new byte[length];
            for (int i = 0; i < length; i++) intBytes[i] = bytes[start + i];
            return intBytes;
        }
    }
}
