using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using bzBencode;

namespace bzTorrent.DHT
{
	/// <summary>
	/// Owns DHT transaction-id allocation and the correlation of query responses back to the
	/// awaiting caller. Extracted from DHTClient (SRP): the send/receive plumbing no longer
	/// also manages the pending-request table.
	/// </summary>
	internal sealed class DHTTransactionRegistry
	{
		private readonly ConcurrentDictionary<string, TaskCompletionSource<BDict>> _pending = new();
		private int _counter;

		/// <summary>
		/// Allocates the next 2-byte transaction id and its dictionary key. A pending completion
		/// is not registered yet — the query is built with this id first, then sent (and
		/// registered) via <see cref="Register"/>.
		/// </summary>
		public (byte[] TxId, string TxKey) NextTxId()
		{
			var id = Interlocked.Increment(ref _counter);
			var bytes = new byte[] { (byte)((id >> 8) & 0xFF), (byte)(id & 0xFF) };
			return (bytes, Convert.ToBase64String(bytes));
		}

		/// <summary>Registers a pending completion for <paramref name="txKey"/> and returns the task to await.</summary>
		public TaskCompletionSource<BDict> Register(string txKey)
		{
			var tcs = new TaskCompletionSource<BDict>();
			_pending[txKey] = tcs;
			return tcs;
		}

		/// <summary>Drops a pending completion without resolving it (e.g. the send failed).</summary>
		public void Abandon(string txKey)
		{
			_pending.TryRemove(txKey, out _);
		}

		/// <summary>Drops and cancels a pending completion (e.g. it timed out).</summary>
		public void Cancel(string txKey)
		{
			if (_pending.TryRemove(txKey, out var tcs))
			{
				tcs.TrySetCanceled();
			}
		}

		/// <summary>Completes the pending request matching the response's transaction id, if any.</summary>
		public void Complete(byte[] responseTxId, BDict message)
		{
			var key = Convert.ToBase64String(responseTxId);
			if (_pending.TryRemove(key, out var tcs))
			{
				tcs.TrySetResult(message);
			}
		}
	}
}
