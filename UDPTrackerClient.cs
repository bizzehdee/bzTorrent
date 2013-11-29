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

        public Dictionary<String, Tuple<UInt32, UInt32, UInt32>> Scrape(String url, String[] hashes)
        {
            Dictionary<String, Tuple<UInt32, UInt32, UInt32>> returnVal = new Dictionary<string, Tuple<UInt32, UInt32, UInt32>>();

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

            UInt32 recAction = UnpackN(recBuf, 0);
            UInt32 recTrasactionId = UnpackN(recBuf, 4);

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

            recAction = UnpackN(recBuf, 0);
            recTrasactionId = UnpackN(recBuf, 4);

            _currentConnectionId = CopyBytes(recBuf, 8, 8);

            if (recAction != 2 || recTrasactionId != trasactionId)
            {
                throw new Exception("Invalid response from tracker");
            }

            Int32 startIndex = 8;
            foreach (String hash in hashes)
            {
                UInt32 seeders = UnpackN(recBuf, startIndex);
                UInt32 completed = UnpackN(recBuf, startIndex + 4);
                UInt32 leachers = UnpackN(recBuf, startIndex + 8);

                returnVal.Add(hash, new Tuple<uint, uint, uint>(seeders, completed, leachers));

                startIndex += 12;
            }

            udpClient.Close();

            return returnVal;
        }

        public IEnumerable<IPEndPoint> Announce(String url, String hash)
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

            UInt32 recAction = UnpackN(recBuf, 0);
            UInt32 recTrasactionId = UnpackN(recBuf, 4);

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
                Concat(Pack.Hex("d9d34ac8f0a8edb70741fc7279a0458d1d4bbed1")). /*my peer id*/
                Concat(Pack.Int64(0)). /*bytes downloaded*/
                Concat(Pack.Int64(0)). /*bytes left*/
                Concat(Pack.Int64(0)). /*bytes uploaded*/
                Concat(Pack.Int32(2)). /*event, 0 for none, 2 for just started*/
                Concat(Pack.Int32(0)). /*ip, 0 for this one*/
                Concat(Pack.Int32(key)). /*unique key*/
                Concat(Pack.Int32(-1)). /*num want, -1 for as many as pos*/
                Concat(Pack.Int32(19624)). /*listen port*/
                Concat(Pack.Int32(0)).ToArray(); /*extensions*/
            udpClient.Send(sendBuf, sendBuf.Length);

            recBuf = udpClient.Receive(ref endPoint);

            recAction = UnpackN(recBuf, 0);
            recTrasactionId = UnpackN(recBuf, 4);

            int waitTime = (int) UnpackN(recBuf, 8);
            int leachers = (int)UnpackN(recBuf, 12);
            int seeders = (int)UnpackN(recBuf, 16);

            if (recAction != 1 || recTrasactionId != trasactionId)
            {
                throw new Exception("Invalid response from tracker");
            }

            for (Int32 i = 20; i < recBuf.Length; i += 6)
            {
                UInt32 ip = UnpackN(recBuf, i);
                UInt16 port = UnpackN16(recBuf, i + 4);

                returnValue.Add(new IPEndPoint(ip, port));
            }

            udpClient.Close();

            return returnValue;
        }

        public Dictionary<String, IEnumerable<IPEndPoint>> Announce(String url, String[] hashes)
        {
            ValidateInput(url, hashes, ScraperType.UDP);

            Dictionary<String, IEnumerable<IPEndPoint>> returnVal = hashes.ToDictionary(hash => hash, hash => Announce(url, hash));

            return returnVal;
        }

        private static UInt32 UnpackN(byte[] bytes, Int32 start)
        {
            byte[] intBytes = CopyBytes(bytes, start, 4);

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(intBytes);
            }

            return BitConverter.ToUInt32(intBytes, 0);
        }

        private static UInt16 UnpackN16(byte[] bytes, Int32 start)
        {
            byte[] intBytes = CopyBytes(bytes, start, 2);

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(intBytes);
            }

            return BitConverter.ToUInt16(intBytes, 0);
        }

        private static byte[] CopyBytes(byte[] bytes, Int32 start, Int32 length)
        {
            byte[] intBytes = new byte[length];
            for (int i = 0; i < length; i++) intBytes[i] = bytes[start + i];
            return intBytes;
        }
    }
}
