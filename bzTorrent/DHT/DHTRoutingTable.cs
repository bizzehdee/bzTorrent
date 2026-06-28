using System;
using System.Collections.Generic;
using System.Linq;

namespace bzTorrent.DHT
{
    public class DHTRoutingTable
    {
        private const int MaxNodes = 1600;
        private readonly List<DHTNode> _nodes = new List<DHTNode>();
        private readonly object _lock = new object();

        public int Count
        {
            get { lock (_lock) return _nodes.Count; }
        }

        public void AddOrUpdate(DHTNode node)
        {
            lock (_lock)
            {
                for (int i = 0; i < _nodes.Count; i++)
                {
                    if (IdsEqual(_nodes[i].Id, node.Id))
                    {
                        _nodes[i].LastSeen = DateTime.UtcNow;
                        return;
                    }
                }

                if (_nodes.Count >= MaxNodes)
                    _nodes.Remove(_nodes.OrderBy(n => n.LastSeen).First());

                _nodes.Add(node);
            }
        }

        public List<DHTNode> GetClosest(byte[] targetId, int count)
        {
            lock (_lock)
            {
                return _nodes
                    .OrderBy(n => n.Id, new XorDistanceComparer(targetId))
                    .Take(count)
                    .ToList();
            }
        }

        public List<DHTNode> GetAll()
        {
            lock (_lock) return new List<DHTNode>(_nodes);
        }

        private static bool IdsEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private class XorDistanceComparer : IComparer<byte[]>
        {
            private readonly byte[] _target;
            public XorDistanceComparer(byte[] target) => _target = target;
            public int Compare(byte[] a, byte[] b) => DHTNode.CompareDistance(a, b, _target);
        }
    }
}
