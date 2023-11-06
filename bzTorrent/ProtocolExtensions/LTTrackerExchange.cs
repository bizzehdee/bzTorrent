﻿/*
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
using bzBencode;

namespace bzTorrent.ProtocolExtensions
{
	public class LTTrackerExchange : IBTExtension
	{
		public delegate void TrackerAddedDelegate(IPeerWireClient client, IBTExtension extension, string newTracker);
		public event TrackerAddedDelegate TrackerAdded;

		public string Protocol
		{
			get => "lt_tex";
		}

		public void Init(ExtendedProtocolExtensions parent)
		{

		}

		public void Deinit()
		{

		}

		public void OnHandshake(IPeerWireClient peerWireClient, byte[] handshake)
		{
			var dict = (BDict)BencodingUtils.Decode(handshake);
		}

		public void OnExtendedMessage(IPeerWireClient peerWireClient, byte[] bytes)
		{
			var dict = (BDict)BencodingUtils.Decode(bytes);
			if (dict.ContainsKey("added"))
			{
				var trackerList = (BList)dict["added"];

				foreach (var tracker in trackerList)
				{
					TrackerAdded?.Invoke(peerWireClient, this, tracker.ToString());
				}
			}
		}

		public IDictionary<string, IBencodingType> GetAdditionalHandshake(IPeerWireClient peerWireClient)
		{
			return new Dictionary<string, IBencodingType> { { "tr", new BString(peerWireClient.Hash) } };
		}
	}
}
