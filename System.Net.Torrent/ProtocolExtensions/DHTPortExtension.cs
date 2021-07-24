﻿namespace System.Net.Torrent.ProtocolExtensions
{
    using System.Net.Torrent.Helpers;

    public class DHTPortExtension : IProtocolExtension
    {
        public event Action<IPeerWireClient, UInt16> Port;
        public bool RemoteUsesDHT { get; private set; }

        public byte[] ByteMask
        {
            get { return new byte[] {0, 0, 0, 0, 0, 0, 0, 0x1}; }
        }

        public byte[] CommandIDs
        {
            get { return new byte[] {9}; }
        }

        public bool OnHandshake(IPeerWireClient client)
        {
            return false;
        }

        public bool OnCommand(IPeerWireClient client, int commandLength, byte commandId, byte[] payload)
        {
            if (commandId == 9)
            {
                UInt16 port = UnpackHelper.UInt16(payload, 0, UnpackHelper.Endianness.Big);

                this.OnPort(client, port);
                return true;
            }

            return false;
        }

        private void OnPort(IPeerWireClient client, UInt16 port)
        {
            if (Port != null)
            {
                Port(client, port);
            }
        }
    }
}
