using System;

namespace bzTorrent.Helpers
{
    /// <summary>
    /// Helper to parse a bitfield payload into a boolean array.
    /// </summary>
    public static class BitfieldParser
    {
        /// <summary>
        /// Parse a bitfield payload where each bit represents the presence of a piece.
        /// The returned array length will be payload.Length * 8. If expectedBits is supplied
        /// and is less than that, the returned array will be trimmed to expectedBits.
        /// </summary>
        /// <param name="payload">Raw payload bytes from the peer.</param>
        /// <param name="expectedBits">Optional expected number of bits (pieces).</param>
        /// <returns>Boolean array where true indicates the peer has the piece.</returns>
        public static bool[] Parse(byte[] payload, int expectedBits = -1)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            var totalBits = payload.Length * 8;
            var bits = new bool[totalBits];

            for (int i = 0; i < payload.Length; i++)
            {
                var b = payload[i];
                // original code used GetBit(0..7) with bit 0 being LSB; preserve that ordering
                for (int bit = 0; bit < 8; bit++)
                {
                    bits[(i * 8) + bit] = (b & (1 << bit)) != 0;
                }
            }

            if (expectedBits > 0 && expectedBits < bits.Length)
            {
                var trimmed = new bool[expectedBits];
                Array.Copy(bits, 0, trimmed, 0, expectedBits);
                return trimmed;
            }

            return bits;
        }
    }
}
