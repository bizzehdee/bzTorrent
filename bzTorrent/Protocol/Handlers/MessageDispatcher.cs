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
using System.Collections.Generic;
using bzTorrent.Data;

namespace bzTorrent.Protocol.Handlers
{
    /// <summary>
    /// Routes incoming peer wire messages to appropriate handlers.
    /// </summary>
    public class MessageDispatcher
    {
        private readonly Dictionary<byte, IMessageHandler> _handlers = new();

        public MessageDispatcher()
        {
            // Register default handlers for standard protocol messages
            RegisterHandler((byte)PeerClientCommands.Have, new HaveHandler());
            RegisterHandler((byte)PeerClientCommands.Bitfield, new BitfieldHandler());
            RegisterHandler((byte)PeerClientCommands.Request, new RequestHandler(isCancel: false));
            RegisterHandler((byte)PeerClientCommands.Cancel, new RequestHandler(isCancel: true));
            RegisterHandler((byte)PeerClientCommands.Piece, new PieceHandler());
        }

        /// <summary>
        /// Register a handler for a specific command ID.
        /// </summary>
        public void RegisterHandler(byte commandId, IMessageHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            _handlers[commandId] = handler;
        }

        /// <summary>
        /// Unregister a handler for a specific command ID.
        /// </summary>
        public void UnregisterHandler(byte commandId)
        {
            _handlers.Remove(commandId);
        }

        /// <summary>
        /// Dispatch a message to its handler.
        /// Returns true if the message was handled, false otherwise.
        /// </summary>
        public bool Dispatch(PeerWireClient client, PeerWirePacket packet)
        {
            var commandId = (byte)packet.Command;
            if (_handlers.TryGetValue(commandId, out var handler))
            {
                var result = handler.Handle(client, packet);
                if (result == HandlerResult.CloseConnection)
                {
                    client.Disconnect();
                    return true;
                }
                return result == HandlerResult.Handled;
            }

            return false;
        }
    }
}
