namespace System.Net.Torrent
{
    public static class Utils
    {
        public static bool GetBit(this byte t, UInt16 n)
        {
            return (t & (1 << n)) != 0;
        }

        public static byte[] CopyBytes(byte[] bytes, Int32 start, Int32 length)
        {
            byte[] intBytes = new byte[length];
            for (int i = 0; i < length; i++) intBytes[i] = bytes[start + i];
            return intBytes;
        }
    }
}
