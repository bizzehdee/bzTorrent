using System;
using System.Collections.Generic;

namespace bzTorrent.IO
{
	/// <summary>
	/// Socket read-framing helpers for the MSE/PE handshake: blocking exact reads, the
	/// sliding-window needle scanner used to skip variable-length padding, and a small FIFO
	/// buffer that lets already-received bytes be replayed ahead of the socket. Split out of
	/// the former MessageStreamEncryption god class.
	/// </summary>
	internal static class MseFraming
	{
		/// <summary>Blocking read of exactly <paramref name="length"/> bytes.</summary>
		public static byte[] ReadExact(IConnectableSocket socket, int length)
		{
			var buffer = new byte[length];
			var offset = 0;
			while (offset < length)
			{
				var read = socket.Receive(buffer, offset, length - offset);
				if (read <= 0)
				{
					throw new InvalidOperationException("Socket closed during MSE handshake");
				}

				offset += read;
			}

			return buffer;
		}

		/// <summary>
		/// Reads bytes one at a time from <paramref name="nextByte"/> until the last
		/// needle.Length bytes equal the needle, or the padding limit is exceeded. Used to skip
		/// PadA/PadB and land on a known marker. Previously this was two near-identical methods
		/// differing only in the byte source.
		/// </summary>
		public static void ScanForNeedle(Func<byte> nextByte, byte[] needle, int maxPadding, string notFoundMessage)
		{
			var window = new byte[needle.Length];
			var filled = 0;
			var scanned = 0;
			var limit = maxPadding + needle.Length;

			while (scanned < limit)
			{
				var b = nextByte();
				scanned++;

				if (filled < window.Length)
				{
					window[filled++] = b;
				}
				else
				{
					Array.Copy(window, 1, window, 0, window.Length - 1);
					window[window.Length - 1] = b;
				}

				if (filled == window.Length && MseCrypto.BytesEqual(window, needle))
				{
					return;
				}
			}

			throw new InvalidOperationException(notFoundMessage);
		}

		/// <summary>A FIFO byte buffer seeded with already-received bytes, topped up from the socket.</summary>
		public sealed class StreamBuffer
		{
			private readonly Queue<byte> buffer = new Queue<byte>();

			public StreamBuffer(byte[] initial)
			{
				foreach (var b in initial)
				{
					buffer.Enqueue(b);
				}
			}

			public byte[] Read(IConnectableSocket socket, int length)
			{
				while (buffer.Count < length)
				{
					foreach (var b in ReadExact(socket, length - buffer.Count))
					{
						buffer.Enqueue(b);
					}
				}

				var result = new byte[length];
				for (var i = 0; i < length; i++)
				{
					result[i] = buffer.Dequeue();
				}

				return result;
			}

			public byte[] DrainBuffered()
			{
				var result = new byte[buffer.Count];
				for (var i = 0; i < result.Length; i++)
				{
					result[i] = buffer.Dequeue();
				}

				return result;
			}
		}
	}
}
