/*
Copyright (c) 2021, Darren Horrocks
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

using bzTorrent.Helpers;

namespace bzTorrent.Data
{
	public class PeerWirePacket
	{
		public uint PacketByteLength => 4 + CommandLength;

		public uint CommandLength { get; set; }
		public PeerClientCommands Command { get; set; }
		public byte[] Payload { get; set; }

		public bool Parse(byte[] currentPacketBuffer)
		{
			if(currentPacketBuffer.Length < 4)
			{
				return false;
			}

			var commandLength = UnpackHelper.UInt32(currentPacketBuffer, 0, UnpackHelper.Endianness.Big);
			CommandLength = commandLength;

			if (CommandLength == 0)
			{
				//keep-alive message
				Command = PeerClientCommands.KeepAlive;
				return true;
			}

			if (CommandLength > (currentPacketBuffer.Length - 4))
			{
				//need more data first
				return false;
			}

			if (CommandLength > 0)
			{
				Command = (PeerClientCommands)currentPacketBuffer[4];
			}

			if(CommandLength > 1)
			{
				Payload = currentPacketBuffer.GetBytes(5, (int)CommandLength - 1);
			}		

			return true;
		}

		public override string ToString() { return Command.ToString(); }

		public byte[] GetBytes()
		{
			if (Command == PeerClientCommands.KeepAlive)
			{
				return new byte[4] { 0, 0, 0, 0 };
			}

			var messageBytes = new byte[PacketByteLength];
			var lengthBytes = PackHelper.UInt32((uint)(1 + Payload.Length));

			lengthBytes.CopyTo(messageBytes, 0);

			messageBytes[4] = (byte)Command;

			Payload.CopyTo(messageBytes, 5);

			return messageBytes;
		}
	}
}
