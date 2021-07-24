namespace System.Net.Torrent.Data
{
    public interface IHashProvider
    {
        public byte[] Hash { get; }

        public string HashString { get; }

        public string Name { get; }
    }
}
