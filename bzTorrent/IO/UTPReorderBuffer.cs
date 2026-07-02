/*
Copyright (c) 2026, Darren Horrocks
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

namespace bzTorrent.IO
{
	/// <summary>
	/// Reassembles uTP ST_DATA packets into an in-order byte stream, buffering packets that
	/// arrive ahead of a still-outstanding gap and releasing them once the gap fills in. Pure
	/// algorithm with no socket dependencies, mirroring <see cref="UTPCongestionControl"/> and
	/// <see cref="UTPRetransmissionTracker"/> so it can be driven deterministically in tests.
	/// </summary>
	public class UTPReorderBuffer
	{
		public readonly struct Result
		{
			public Result(ushort ackNumber, IReadOnlyList<byte[]> deliverablePayloads)
			{
				AckNumber = ackNumber;
				DeliverablePayloads = deliverablePayloads;
			}

			public ushort AckNumber { get; }

			/// <summary>Zero or more payloads now ready for delivery, in order.</summary>
			public IReadOnlyList<byte[]> DeliverablePayloads { get; }
		}

		private readonly Dictionary<ushort, byte[]> buffered = new();

		// Serial number comparison (RFC 1982) so this keeps working across seq_nr wraparound.
		private static bool SequenceLessThanOrEqual(ushort seq, ushort reference)
		{
			return (ushort)(reference - seq) < 0x8000;
		}

		/// <summary>
		/// Accepts a received ST_DATA packet. Returns the (possibly unchanged) ack number and any
		/// payloads that are now deliverable in order — the packet just received, plus any
		/// previously-buffered packets that a gap it filled makes deliverable too.
		/// </summary>
		public Result Accept(ushort seq, ushort ackNumber, byte[] payload)
		{
			var expectedNext = (ushort)(ackNumber + 1);

			if (seq == expectedNext)
			{
				var delivered = new List<byte[]> { payload };
				ackNumber = seq;

				while (buffered.TryGetValue((ushort)(ackNumber + 1), out var next))
				{
					buffered.Remove((ushort)(ackNumber + 1));
					ackNumber++;
					delivered.Add(next);
				}

				return new Result(ackNumber, delivered);
			}

			if (!SequenceLessThanOrEqual(seq, ackNumber))
			{
				// Ahead of what we can deliver yet — a gap is still outstanding. Buffer it until
				// the missing packet(s) arrive.
				buffered[seq] = payload;
			}

			// else: duplicate/old packet already delivered — drop it.
			return new Result(ackNumber, System.Array.Empty<byte[]>());
		}

		public int BufferedCount => buffered.Count;
	}
}
