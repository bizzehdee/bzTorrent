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

using System;
using System.Collections.Generic;
using System.Linq;

namespace bzTorrent.IO
{
	/// <summary>
	/// Tracks in-flight (unacked) uTP packets and drives RTO-based retransmission, per BEP 29 /
	/// libutp. Pure algorithm with no socket/timer dependencies — the caller supplies all
	/// timestamps and is responsible for actually resending due packets — so it can be driven
	/// deterministically in tests, mirroring <see cref="UTPCongestionControl"/>.
	/// </summary>
	public class UTPRetransmissionTracker
	{
		public class TrackedPacket
		{
			public ushort Seq { get; internal set; }
			public UTPSocket.PacketType Type { get; internal set; }
			public byte[] Payload { get; internal set; }
			public DateTime LastSentAt { get; internal set; }
			public int TransmitCount { get; internal set; }
		}

		public const double MinRtoMs = 500;
		public const double MaxRtoMs = 60000;
		public const int MaxRetransmits = 8;

		private readonly Dictionary<ushort, TrackedPacket> sentPackets = new();

		public double SrttMs { get; private set; } = -1;
		public double RttVarMs { get; private set; }
		public double RtoMs { get; private set; } = 1000;

		public uint BytesInFlight => (uint)sentPackets.Values.Sum(p => p.Payload.Length);

		public int InFlightCount => sentPackets.Count;

		// Serial number comparison (RFC 1982) so ack clearing keeps working across seq_nr wraparound.
		private static bool SequenceLessThanOrEqual(ushort seq, ushort reference)
		{
			return (ushort)(reference - seq) < 0x8000;
		}

		public void OnSent(ushort seq, UTPSocket.PacketType type, byte[] payload, DateTime now)
		{
			sentPackets[seq] = new TrackedPacket { Seq = seq, Type = type, Payload = payload ?? Array.Empty<byte>(), LastSentAt = now, TransmitCount = 1 };
		}

		/// <summary>
		/// Clears every packet acked by a cumulative ack number, sampling RTT from the ones that
		/// were never retransmitted (Karn's algorithm — a retransmitted packet's ack is ambiguous
		/// about which transmission it actually acks).
		/// </summary>
		/// <returns>Total bytes acked, for congestion-control window accounting.</returns>
		public uint OnAck(ushort ackNumber, DateTime now)
		{
			uint bytesAcked = 0;

			var ackedSeqNumbers = sentPackets.Keys
				.Where(seq => SequenceLessThanOrEqual(seq, ackNumber))
				.ToList();

			foreach (var seq in ackedSeqNumbers)
			{
				var packet = sentPackets[seq];
				bytesAcked += (uint)packet.Payload.Length;

				if (packet.TransmitCount == 1)
				{
					SampleRtt((now - packet.LastSentAt).TotalMilliseconds);
				}

				sentPackets.Remove(seq);
			}

			return bytesAcked;
		}

		/// <summary>
		/// Packets whose RTO has elapsed and should be resent now. Does not mutate state — call
		/// <see cref="MarkRetransmitted"/> for each one actually resent.
		/// </summary>
		public IReadOnlyList<TrackedPacket> GetDueForRetransmit(DateTime now)
		{
			return sentPackets.Values
				.Where(p => p.TransmitCount <= MaxRetransmits && (now - p.LastSentAt).TotalMilliseconds >= CurrentRto(p))
				.ToList();
		}

		/// <summary>
		/// True if any in-flight packet has exceeded MaxRetransmits without being acked — the
		/// caller should treat the connection as dead.
		/// </summary>
		public bool HasExceededMaxRetransmits(DateTime now)
		{
			return sentPackets.Values.Any(p => p.TransmitCount > MaxRetransmits && (now - p.LastSentAt).TotalMilliseconds >= CurrentRto(p));
		}

		public void MarkRetransmitted(TrackedPacket packet, DateTime now)
		{
			packet.TransmitCount++;
			packet.LastSentAt = now;
		}

		private double CurrentRto(TrackedPacket packet)
		{
			var backoff = Math.Pow(2, Math.Min(packet.TransmitCount - 1, 6));
			return Math.Min(RtoMs * backoff, MaxRtoMs);
		}

		// Jacobson/Karels RTT estimation — the same approach TCP uses to derive its RTO.
		private void SampleRtt(double sampleMs)
		{
			if (SrttMs < 0)
			{
				SrttMs = sampleMs;
				RttVarMs = sampleMs / 2;
			}
			else
			{
				RttVarMs = (0.75 * RttVarMs) + (0.25 * Math.Abs(SrttMs - sampleMs));
				SrttMs = (0.875 * SrttMs) + (0.125 * sampleMs);
			}

			RtoMs = Math.Max(MinRtoMs, Math.Min(MaxRtoMs, SrttMs + (4 * RttVarMs)));
		}
	}
}
