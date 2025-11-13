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

using bzTorrent.Data;
using bzTorrent.Helpers;

namespace bzTorrent.Protocol.Handlers
{
    /// <summary>
    /// Handles Piece messages (peer sends a block of data).
    /// </summary>
    public class PieceHandler : IMessageHandler
    {
        public HandlerResult Handle(PeerWireClient client, PeerWirePacket packet)
        {
            if (packet.Payload.Length < 8)
            {
                // Malformed: need at least 8 bytes (index 4 bytes, begin 4 bytes, data follows)
                return HandlerResult.CloseConnection;
            }

            var index = UnpackHelper.Int32(packet.Payload, 0, UnpackHelper.Endianness.Big);
            var begin = UnpackHelper.Int32(packet.Payload, 4, UnpackHelper.Endianness.Big);

            // Extract the remaining data (from byte 8 onwards)
            var buffer = packet.Payload.GetBytes(8);

            client.RaisePiece(index, begin, buffer);

            return HandlerResult.Handled;
        }
    }
}
