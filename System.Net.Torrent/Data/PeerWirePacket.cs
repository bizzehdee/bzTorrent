using System;
using System.Collections.Generic;
using System.Net.Torrent.Helpers;
using System.Text;

namespace System.Net.Torrent.Data
{
	public class PeerWirePacket
	{
		public int PacketByteLength => 4 + CommandLength;

		public int CommandLength { get; set; }
		public PeerClientCommands Command { get; set; }
		public byte[] Payload { get; set; }

		public bool Parse(byte[] currentPacketBuffer)
		{
			var commandLength = UnpackHelper.Int32(currentPacketBuffer, 0, UnpackHelper.Endianness.Big);

			if (commandLength > (currentPacketBuffer.Length - 4))
			{
				//need more data first
				return false;
			}

			CommandLength = commandLength;

			if(CommandLength == 0)
			{
				//keep-alive message
				Command = PeerClientCommands.KeepAlive;
				return true;
			}

			Command = (PeerClientCommands)currentPacketBuffer[4];

			Payload = currentPacketBuffer.GetBytes(5, CommandLength);

			return true;
		}
	}
}
