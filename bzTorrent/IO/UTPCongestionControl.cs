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

namespace bzTorrent.IO
{
	/// <summary>
	/// LEDBAT-based congestion control for uTP, as described in bzTorrent/Docs/utp-protocol.md
	/// (derived from BEP 29 / libutp). Pure algorithm with no socket/timer dependencies so it can
	/// be driven deterministically in tests; the caller supplies all timestamps.
	/// </summary>
	public class UTPCongestionControl
	{
		public const uint CControlTarget = 100 * 1000; // 100ms in microseconds
		public const uint MaxCwndIncreaseBytesPerRtt = 3000;
		public const uint MinWindowSize = 10;
		public const uint MaxWindowDecay = 100 * 1000; // microseconds between window decays

		// Small rolling window of one-way delay samples; the minimum of these approximates the
		// base (uncongested) delay, per LEDBAT's "our_delay" measurement.
		private const int DelayHistorySize = 13;

		private readonly uint socketSendBufferSize;
		private readonly uint[] delayHistory = new uint[DelayHistorySize];
		private int delayHistoryCount;
		private int delayHistoryIndex;
		private bool decayInitialised;
		private uint lastDecayMicros;

		public uint MaxWindow { get; private set; }

		public UTPCongestionControl(uint socketSendBufferSize)
		{
			this.socketSendBufferSize = socketSendBufferSize;
			MaxWindow = socketSendBufferSize;
		}

		/// <summary>
		/// Feeds a one-way delay sample (the timestamp_difference_microseconds field from a
		/// received packet, which reflects the delay the remote peer observed in our send
		/// direction).
		/// </summary>
		public void OnDelaySample(uint delayMicros)
		{
			delayHistory[delayHistoryIndex] = delayMicros;
			delayHistoryIndex = (delayHistoryIndex + 1) % DelayHistorySize;
			if (delayHistoryCount < DelayHistorySize)
			{
				delayHistoryCount++;
			}
		}

		/// <summary>
		/// Applies a cumulative ack, updating max_window per the LEDBAT formula, then checks
		/// whether the periodic window decay should fire. Call this on every received packet
		/// (even with bytesAcked = 0) so the decay timer keeps advancing.
		/// </summary>
		public void OnAck(uint bytesAcked, uint currentMicros)
		{
			if (bytesAcked > 0)
			{
				var ourDelay = (double)MinDelay();
				var offTarget = CControlTarget - ourDelay;
				var windowFactor = Math.Min(bytesAcked, MaxWindow) / (double)Math.Max(MaxWindow, bytesAcked);
				var delayFactor = offTarget / CControlTarget;
				var scaledGain = MaxCwndIncreaseBytesPerRtt * windowFactor * delayFactor;

				MaxWindow = scaledGain + MaxWindow < MinWindowSize
					? MinWindowSize
					: (uint)(MaxWindow + scaledGain);

				MaxWindow = Math.Min(Math.Max(MaxWindow, MinWindowSize), socketSendBufferSize);
			}

			MaybeDecay(currentMicros);
		}

		private uint MinDelay()
		{
			var min = uint.MaxValue;
			for (var i = 0; i < delayHistoryCount; i++)
			{
				if (delayHistory[i] < min)
				{
					min = delayHistory[i];
				}
			}

			return min == uint.MaxValue ? 0 : min;
		}

		private void MaybeDecay(uint currentMicros)
		{
			if (!decayInitialised)
			{
				lastDecayMicros = currentMicros;
				decayInitialised = true;
				return;
			}

			if (unchecked(currentMicros - lastDecayMicros) >= MaxWindowDecay)
			{
				MaxWindow = Math.Max(MinWindowSize, MaxWindow / 2);
				lastDecayMicros = currentMicros;
			}
		}
	}
}
