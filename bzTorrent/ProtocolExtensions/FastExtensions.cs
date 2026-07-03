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
using bzTorrent.Helpers;

namespace bzTorrent.ProtocolExtensions
{
	public class FastExtensions : IProtocolExtension
	{
		public delegate void SuggestPeiceDelegate(IPeerWireClient client, int pieceIdx);
		public delegate void RejectDelegate(IPeerWireClient client, int pieceIdx, int start, int length);
		public delegate void HaveAllDelegate(IPeerWireClient client);
		public delegate void HaveNoneDelegate(IPeerWireClient client);
		public delegate void AllowedFastDelegate(IPeerWireClient client, int pieceIdx);

		public event SuggestPeiceDelegate SuggestPiece;
		public event RejectDelegate Reject;
		public event HaveAllDelegate HaveAll;
		public event HaveNoneDelegate HaveNone;
		public event AllowedFastDelegate AllowedFast;

		public byte[] ByteMask
		{
			get => new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04 };
		}

		public byte[] CommandIDs
		{
			get => new byte[]
				{
					13,//Suggest Piece
                    14,//Have all
                    15,//Have none
                    16,//Reject Request
                    17 //Allowed Fast
                };
		}

		public bool OnHandshake(IPeerWireClient client)
		{
			return false;
		}

		public bool OnCommand(IPeerWireClient client, int commandLength, byte commandId, byte[] payload)
		{
			switch (commandId)
			{
				case 13:
					ProcessSingleIndex(payload, index => SuggestPiece?.Invoke(client, index));
					return true;
				case 14:
					OnHaveAll(client);
					return true;
				case 15:
					OnHaveNone(client);
					return true;
				case 16:
					ProcessReject(client, payload);
					return true;
				case 17:
					ProcessSingleIndex(payload, index => AllowedFast?.Invoke(client, index));
					return true;
				default:
					return false;
			}
		}

		// Suggest Piece (13) and Allowed Fast (17) carry the same single 4-byte piece index and
		// differ only in which event fires; one parser, caller supplies the event.
		private static void ProcessSingleIndex(byte[] payload, Action<int> raise)
		{
			var index = UnpackHelper.Int32(payload, 0, Endianness.Big);

			raise(index);
		}

		private void ProcessReject(IPeerWireClient client, byte[] payload)
		{
			var index = UnpackHelper.Int32(payload, 0, Endianness.Big);
			var begin = UnpackHelper.Int32(payload, 4, Endianness.Big);
			var length = UnpackHelper.Int32(payload, 8, Endianness.Big);

			OnReject(client, index, begin, length);
		}

		private void OnHaveAll(IPeerWireClient client)
		{
			HaveAll?.Invoke(client);
		}

		private void OnHaveNone(IPeerWireClient client)
		{
			HaveNone?.Invoke(client);
		}

		private void OnReject(IPeerWireClient client, int index, int begin, int length)
		{
			Reject?.Invoke(client, index, begin, length);
		}
	}
}
