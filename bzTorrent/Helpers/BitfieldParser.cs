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
                    bits[(i * 8) + bit] = (b & (1 << (7 - bit))) != 0;
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
