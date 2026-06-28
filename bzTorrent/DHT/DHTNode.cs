using System;
using System.Net;

namespace bzTorrent.DHT
{
    public class DHTNode
    {
        public byte[] Id { get; }
        public IPEndPoint EndPoint { get; }
        public DateTime LastSeen { get; set; }

        public DHTNode(byte[] id, IPEndPoint endPoint)
        {
            Id = id;
            EndPoint = endPoint;
            LastSeen = DateTime.UtcNow;
        }

        // Returns negative if a is closer to target than b
        public static int CompareDistance(byte[] a, byte[] b, byte[] target)
        {
            for (int i = 0; i < 20; i++)
            {
                int da = (a[i] ^ target[i]) & 0xFF;
                int db = (b[i] ^ target[i]) & 0xFF;
                if (da != db) return da.CompareTo(db);
            }
            return 0;
        }
    }
}
