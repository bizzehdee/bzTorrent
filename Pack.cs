namespace System.Net.Torrent
{
    public static class Pack
    {
        public enum Endianness
        {
            Machine,
            Big,
            Little
        }

        private static bool NeedsFlipping(Endianness e)
        {
            switch (e)
            {
                case Endianness.Big:
                    return BitConverter.IsLittleEndian;
                case Endianness.Little:
                    return !BitConverter.IsLittleEndian;
            }

            return false;
        }

        public static byte[] Int16(Int16 i, Endianness e = Endianness.Machine)
        {
            byte[] bytes = BitConverter.GetBytes(i);

            if (NeedsFlipping(e)) Array.Reverse(bytes);

            return bytes;
        }

        public static byte[] Int32(Int32 i, Endianness e = Endianness.Machine)
        {
            byte[] bytes = BitConverter.GetBytes(i);

            if (NeedsFlipping(e)) Array.Reverse(bytes);

            return bytes;
        }

        public static byte[] Int64(Int64 i, Endianness e = Endianness.Machine)
        {
            byte[] bytes = BitConverter.GetBytes(i);

            if (NeedsFlipping(e)) Array.Reverse(bytes);

            return bytes;
        }

        public static byte[] UInt16(UInt16 i, Endianness e = Endianness.Machine)
        {
            byte[] bytes = BitConverter.GetBytes(i);

            if (NeedsFlipping(e)) Array.Reverse(bytes);

            return bytes;
        }

        public static byte[] UInt32(UInt32 i, Endianness e = Endianness.Machine)
        {
            byte[] bytes = BitConverter.GetBytes(i);

            if (NeedsFlipping(e)) Array.Reverse(bytes);

            return bytes;
        }

        public static byte[] UInt64(UInt64 i, Endianness e = Endianness.Machine)
        {
            byte[] bytes = BitConverter.GetBytes(i);

            if (NeedsFlipping(e)) Array.Reverse(bytes);

            return bytes;
        }

        public static byte[] Float(float f, Endianness e = Endianness.Machine)
        {
            return BitConverter.GetBytes(f);
        }

        public static byte[] Double(double f, Endianness e = Endianness.Machine)
        {
            return BitConverter.GetBytes(f);
        }

        public static byte[] Hex(String str, Endianness e = Endianness.Machine)
        {
            if ((str.Length % 2) == 1) str += '0';

            byte[] bytes = new byte[str.Length / 2];
            for (int i = 0; i < str.Length; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(str.Substring(NeedsFlipping(e) ? ((str.Length - (i*2)) - 2) : i, 2), 16);
            }

            return bytes;
        }
    }
}
