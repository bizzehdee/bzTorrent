using System;
using System.Collections.Generic;

namespace bzTorrent.IO
{
	/// <summary>
	/// Per-connection MSE/PE settings.
	/// </summary>
	public class PeerEncryptionOptions
	{
		private readonly object gate = new object();
		private readonly List<string> knownInfoHashes = new List<string>();

		/// <summary>The crypto methods this side supports/advertises. RC4 is required for MSE.</summary>
		public PeerEncryptionType SupportedTypes { get; set; } = PeerEncryptionType.RC4;

		/// <summary>Upper bound on the random padding sent/tolerated during negotiation (spec max 512).</summary>
		public int MaxPaddingBytes { get; set; } = 512;

		/// <summary>
		/// Infohashes a listener is willing to accept encrypted connections for. An incoming
		/// MSE handshake hides the infohash, so it can only be matched against a known set.
		/// </summary>
		public void AddKnownInfoHash(string infoHash)
		{
			if (infoHash == null)
			{
				throw new ArgumentNullException(nameof(infoHash));
			}

			lock (gate)
			{
				knownInfoHashes.Add(infoHash);
			}
		}

		public IReadOnlyList<string> GetKnownInfoHashes()
		{
			lock (gate)
			{
				return knownInfoHashes.ToArray();
			}
		}
	}
}
