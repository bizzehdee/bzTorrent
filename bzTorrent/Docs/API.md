# bzTorrent API Reference

This document covers every public type in the `bzTorrent` library, grouped by namespace: what each class/interface/enum is for, and what each public (or subclass-relevant `protected`) constructor, property, method, and event does. It's a reference, not a tutorial — for end-to-end usage examples, see the main [README](../../README.md).

Internal-only types are noted where they show up in a namespace's public surface (e.g. the MSE/PE implementation classes), so the picture is complete, but they aren't usable from outside the `bzTorrent` assembly.

## Table of contents

- [bzTorrent](#bztorrent)
  - [`PeerWireClient`](#bztorrentpeerwireclient), [`PeerWireListener<T>`](#bztorrentpeerwirelistenert), [`PeerMessageBuilder`](#bztorrentpeermessagebuilder), [`IPeerWireClient`](#bztorrentipeerwireclient), [`IPeerCommand`](#bztorrentipeercommand)
  - [`HTTPTrackerClient`](#bztorrenthttptrackerclient), [`UDPTrackerClient`](#bztorrentudptrackerclient), [`BaseScraper`](#bztorrentbasescraper), [`ITrackerClient`](#bztorrentitrackerclient)
  - [`LocalPeerDiscovery<T>`](#bztorrentlocalpeerdiscoveryt), [`ILocalPeerDiscovery<T>`](#bztorrentilocalpeerdiscoveryt)
  - [`IProtocolExtension`](#bztorrentiprotocolextension)
- [bzTorrent.Data](#bztorrentdata)
  - [`IHashProvider`](#bztorrentdatahashprovider), [`IMagnetLink`](#bztorrentdataimagnetlink), [`IMetadata`](#bztorrentdataimetadata), [`MagnetLink`](#bztorrentdatamagnetlink), [`Metadata`](#bztorrentdatametadata), [`MetadataFileInfo`](#bztorrentdatametadatafileinfo), [`MetadataPieceInfo`](#bztorrentdatametadatapieceinfo), [`PeerClientCommands`](#bztorrentdatapeerclientcommands), [`PeerClientHandshake`](#bztorrentdatapeerclienthandshake), [`PeerWirePacket`](#bztorrentdatapeerwirepacket)
- [bzTorrent.DHT](#bztorrentdht)
  - [`DHTClient`](#bztorrentdhtdhtclient), [`DHTNode`](#bztorrentdhtdhtnode), [`DHTRoutingTable`](#bztorrentdhtdhtroutingtable)
- [bzTorrent.IO](#bztorrentio)
  - [`ISocket`](#bztorrentioisocket), [`IPeerConnection`](#bztorrentioipeerconnection), [`BaseSocket`](#bztorrentiobasesocket), [`TCPSocket`](#bztorrentiotcpsocket), [`UDPSocket`](#bztorrentioudpsocket), [`UTPCongestionControl`](#bztorrentioutpcongestioncontrol), [`UTPSocket`](#bztorrentioutpsocket)
  - [`PeerWireConnection<T>`](#bztorrentiopeerwireconnectiont), [`PeerEncryptionMode`](#bztorrentiopeerencryptionmode), [`PeerEncryptionOptions`](#bztorrentiopeerencryptionoptions), [`PeerEncryptionType`](#bztorrentiopeerencryptiontype)
  - [Internal implementation types](#internal-implementation-types-not-part-of-the-public-api) — `MessageStreamEncryption`, `RC4Cipher`
- [bzTorrent.ProtocolExtensions](#bztorrentprotocolextensions)
  - [`IBTExtension`](#bztorrentprotocolextensionsibtextension), [`DHTPortExtension`](#bztorrentprotocolextensionsdhtportextension), [`LTTrackerExchange`](#bztorrentprotocolextensionslttrackerexchange), [`UTPeerExchange`](#bztorrentprotocolextensionsutpeerexchange), [`UTMetadata`](#bztorrentprotocolextensionsutmetadata), [`FastExtensions`](#bztorrentprotocolextensionsfastextensions), [`ExtendedProtocolExtensions`](#bztorrentprotocolextensionsextendedprotocolextensions)
- [bzTorrent.Protocol.Handlers](#bztorrentprotocolhandlers)
  - [`HandlerResult`](#bztorrentprotocolhandlershandlerresult), [`IMessageHandler`](#bztorrentprotocolhandlersimessagehandler), [`HaveHandler`](#bztorrentprotocolhandlershavehandler), [`PieceHandler`](#bztorrentprotocolhandlerspiecehandler), [`BitfieldHandler`](#bztorrentprotocolhandlersbitfieldhandler), [`RequestHandler`](#bztorrentprotocolhandlersrequesthandler), [`MessageDispatcher`](#bztorrentprotocolhandlersmessagedispatcher)
- [bzTorrent.Helpers](#bztorrenthelpers)
  - [`BitfieldParser`](#bztorrenthelpersbitfieldparser), [`PackHelper`](#bztorrenthelperspackhelper), [`UnpackHelper`](#bztorrenthelpersunpackhelper), [`Utils`](#bztorrenthelpersutils)
- [bzTorrent.Extensions](#bztorrentextensions)
  - [`ArgumentExtensions`](#bztorrentextensionsargumentextensions), [`EnumerableExtensions`](#bztorrentextensionsenumerableextensions)

---

## bzTorrent

### `bzTorrent.PeerWireClient`

Implements `IPeerWireClient`. Drives a single peer connection: sends handshake and protocol messages, and dispatches incoming BitTorrent peer-wire commands to events. Wrap an `IPeerConnection` (typically `PeerWireConnection<T>`) in this class, wire up the events you care about, then call `Connect`/`Handshake` followed by repeated `Process()` calls in your own loop.

**Constructors**
- `PeerWireClient(IPeerConnection io)` — wraps the given connection. Does not connect; call `Connect` afterward.

**Properties**
- `bool ReceivedHandshake { get; private set; }` — `true` once the remote peer's handshake has been received and parsed.
- `int Timeout { get; }` — read-only pass-through to the underlying connection's timeout (seconds).
- `bool[] PeerBitField { get; set; }` — local storage for the peer's known-pieces bitfield; not populated automatically — set it yourself (e.g. from the `BitField`/`Have` events) if you want to track it here.
- `bool KeepConnectionAlive { get; set; }` — when `true`, `Process()` sends a keep-alive packet if none has been sent in the last minute and the handshake has completed.
- `string LocalPeerID { get; set; }` — peer ID used as the default for the no-argument `Handshake()` overload.
- `string RemotePeerID { get; private set; }` — populated from the remote peer's handshake once received.
- `string Hash { get; set; }` — info hash used as the default for the no-argument `Handshake()` overload.
- `PeerEncryptionMode EncryptionMode { get; }` — read-only pass-through to the underlying connection's negotiated/configured MSE mode.
- `bool IsEncrypted { get; }` — whether the connection ended up encrypted after handshake negotiation.

**Methods**
- `void RaiseHave(int pieceIndex)` — invokes the `Have` event. Called by `HaveHandler`; not intended for direct use by consumers.
- `void RaiseBitField(int size, bool[] bitField)` — invokes the `BitField` event. Called by `BitfieldHandler`; not intended for direct use.
- `void RaiseRequest(int index, int begin, int length)` — invokes the `Request` event. Called by `RequestHandler`; not intended for direct use.
- `void RaisePiece(int index, int begin, byte[] bytes)` — invokes the `Piece` event. Called by `PieceHandler`; not intended for direct use.
- `void RaiseCancel(int index, int begin, int length)` — invokes the `Cancel` event. Called by `RequestHandler`; not intended for direct use.
- `void Connect(IPEndPoint endPoint)` — resets `ReceivedHandshake` and opens the underlying connection to the given endpoint.
- `void Connect(string ipHost, int port)` — same as above, parsing `ipHost` as an IP address (not a DNS name).
- `void Disconnect()` — closes the underlying connection.
- `bool Handshake()` — sends the handshake using the current `Hash`/`LocalPeerID` property values. Always returns `true`.
- `bool Handshake(string hash, string peerId)` — builds and sends a `PeerClientHandshake` with reserved bytes computed from all registered extensions' `ByteMask`, then calls each extension's `OnHandshake`. Throws `ArgumentNullException` if `hash` or `peerId` is null, and `ArgumentOutOfRangeException` if `hash` is not 40 chars or `peerId` is not 20 chars. Always returns `true`.
- `bool Process()` — call repeatedly (e.g. in a loop) to drive the connection: sends a keep-alive if due, pumps the underlying connection's I/O, raises `HandshakeComplete` on first handshake receipt, raises `NoData` or dispatches all pending received packets to the relevant events/handlers. Returns `false` (and raises `DroppedConnection`) once the underlying connection reports it is no longer connected, or if `IPeerConnection.Process()` throws (in which case it also disconnects).
- `bool SendKeepAlive()` — sends a zero-length keep-alive message. Always returns `true`.
- `bool SendChoke()` / `bool SendUnChoke()` / `bool SendInterested()` / `bool SendNotInterested()` — send the corresponding zero-payload peer-wire message. Always return `true`.
- `bool SendHave(uint index)` — sends a `have` message for the given piece index. Always returns `true`.
- `void SendBitField(bool[] bitField)` — equivalent to `SendBitField(bitField, false)`.
- `bool SendBitField(bool[] bitField, bool obsf)` — packs `bitField` into bytes and sends a `bitfield` message. When `obsf` is `true` and the field is larger than 32 bits, randomly omits up to 16 pieces from the initial bitfield and instead announces them individually via follow-up `have` messages, to obscure the true initial bitfield from passive observers. Always returns `true`.
- `bool SendRequest(uint index, uint start, uint length)` — sends a `request` message for a block of a piece. Always returns `true`.
- `bool SendPiece(uint index, uint start, byte[] data)` — sends a `piece` message containing block data. Always returns `true`.
- `bool SendCancel(uint index, uint start, uint length)` — sends a `cancel` message for a previously requested block. Always returns `true`.
- `void RegisterBTExtension(IProtocolExtension extension)` — adds an extension; its reserved-byte mask is included in future handshakes and its `OnCommand` is consulted for unrecognized command IDs.
- `void UnregisterBTExtension(IProtocolExtension extension)` — removes a previously registered extension.
- `bool SendPacket(PeerWirePacket packet)` — sends an arbitrary pre-built packet directly. Always returns `true`.

**Events**
- `event DroppedConnectionDelegate DroppedConnection` — fires when `Process()` detects the connection has closed (`client`).
- `event NoDataDelegate NoData` — fires from `Process()` when there were no packets to receive on this call (`client`).
- `event HandshakeCompleteDelegate HandshakeComplete` — fires once, the first time the remote peer's handshake is received (`client`).
- `event KeepAliveDelegate KeepAlive` — fires when a keep-alive command is received (`client`).
- `event ChokeDelegate Choke` / `event UnChokeDelegate UnChoke` — fire when the peer chokes/unchokes us (`client`).
- `event InterestedDelegate Interested` / `event NotInterestedDelegate NotInterested` — fire when the peer signals interest state (`client`).
- `event HaveDelegate Have` — fires when the peer announces it has a piece (`client, pieceIdx`).
- `event BitFieldDelegate BitField` — fires when the peer sends its bitfield (`client, size, bitfield`).
- `event RequestDelegate Request` — fires when the peer requests a block (`client, pieceIdx, start, length`).
- `event PieceDelegate Piece` — fires when a block of piece data is received (`client, pieceIdx, start, buffer`).
- `event CancelDelegate Cancel` — fires when the peer cancels a previously sent request (`client, pieceIdx, start, length`).
- `event CommandDelegate Command` — fires for every incoming command before any built-in or extension handling. Return `true` from your handler to suppress the library's own processing of that command (`client, commandLength, commandId, payload` → `bool`).

### `bzTorrent.PeerWireListener<T>`

Listens for inbound peer-wire connections and hands each one to you as a ready-to-use `PeerWireClient`. `T` is the `IPeerConnection` implementation to accept with (e.g. `PeerWireConnection<TCPSocket>`); it must have a public parameterless constructor and (for accepted connections) a constructor taking an `ISocket`.

**Constructors**
- `PeerWireListener()` — listens on `Port` 0 (the OS assigns a port) unless set via `init`.
- `PeerWireListener(int port)` — listens on the given port.

**Properties**
- `int Port { get; init; }` — the port to listen on; can only be set at construction/object-initializer time.
- `PeerEncryptionMode EncryptionMode { get; set; }` — MSE mode applied to the listening connection and propagated to every accepted connection.
- `PeerEncryptionOptions EncryptionOptions { get; }` — encryption options (known info hashes, supported types, padding) propagated to every accepted connection.

**Methods**
- `void StartListening()` — binds to `IPAddress.Any:Port` and begins accepting connections asynchronously.
- `void StopListening()` — ends the outstanding accept operation, if any. Note: does not stop new accepts from being re-issued by an in-flight callback.

**Events**
- `event NewPeerDelegate NewPeer` — fires with a fully-constructed `PeerWireClient` (delegate signature: `void NewPeerDelegate(IPeerWireClient peerWireClient)`) each time a connection is accepted. `EncryptionMode` and `EncryptionOptions` (known info hashes, supported types, max padding) are copied onto the accepted connection before this fires.

### `bzTorrent.PeerMessageBuilder`

Implements `IDisposable`. Fluent builder for constructing a `PeerWirePacket` payload byte-by-byte or field-by-field before sending it via `IPeerWireClient.SendPacket` (this is what `PeerWireClient`'s `Send*` methods use internally).

**Constructors**
- `PeerMessageBuilder(byte msgId)` — starts a new message with the given peer-wire command/message ID and an empty payload.

**Properties**
- `uint PacketLength { get; }` — computed as `5 + payload length` (4-byte length prefix + 1-byte message ID + payload), matching the on-wire packet length.
- `uint MessageLength { get; }` — computed as `1 + payload length` (message ID + payload), the value that goes in the peer-wire length prefix.
- `byte MessageID { get; private set; }` — the message ID passed to the constructor.
- `List<byte> MessagePayload { get; private set; }` — the payload bytes accumulated so far.

**Methods**
- `PeerMessageBuilder Add(byte b)` — appends a single byte. Returns `this` for chaining.
- `PeerMessageBuilder Add(byte[] bytes)` — appends raw bytes. Returns `this` for chaining.
- `PeerMessageBuilder Add(uint n, PackHelper.Endianness endianness = PackHelper.Endianness.Big)` — appends a 4-byte big-endian encoding of `n`. Note: the `endianness` parameter is accepted but not actually used (always packs big-endian via `PackHelper.UInt32`). Returns `this` for chaining.
- `PeerMessageBuilder Add(string str)` — appends the bytes of `str` interpreted as hex. Returns `this` for chaining.
- `PeerWirePacket Message()` — builds and returns a `PeerWirePacket` from the accumulated message ID and payload.
- `void Dispose()` — clears the accumulated payload.

### `bzTorrent.IPeerWireClient`

The public contract implemented by `PeerWireClient`. Also declares the delegate types used for every event (each delegate's first parameter is always the `IPeerWireClient` that raised it). Depend on this interface rather than the concrete class if you want to mock peer connections in tests or swap implementations.

**Properties**
- `int Timeout { get; }` — connection timeout in seconds.
- `bool[] PeerBitField { get; set; }` — caller-managed storage for the peer's known pieces.
- `bool KeepConnectionAlive { get; set; }` — enables automatic keep-alive sending from `Process()`.
- `string LocalPeerID { get; set; }` — default peer ID for the no-argument `Handshake()`.
- `string RemotePeerID { get; }` — the peer ID received from the remote peer's handshake.
- `string Hash { get; set; }` — default info hash for the no-argument `Handshake()`.
- `PeerEncryptionMode EncryptionMode { get; }` — the connection's MSE mode.
- `bool IsEncrypted { get; }` — whether the connection ended up encrypted.

**Methods**
- `void Connect(IPEndPoint endPoint)` / `void Connect(string ipHost, int port)` — open the connection to a peer.
- `void Disconnect()` — close the connection.
- `bool Handshake()` — send the handshake using `Hash`/`LocalPeerID`.
- `bool Handshake(string hash, string peerId)` — send the handshake with explicit values.
- `bool Process()` — pump the connection once; call in a loop. Returns `false` when the connection has dropped.
- `bool SendKeepAlive()` / `bool SendChoke()` / `bool SendUnChoke()` / `bool SendInterested()` / `bool SendNotInterested()` — send the corresponding no-payload message.
- `bool SendHave(uint index)` — announce possession of a piece.
- `void SendBitField(bool[] bitField)` — send the full bitfield.
- `bool SendBitField(bool[] bitField, bool obsf)` — send the bitfield, optionally obfuscating it via follow-up `have` messages.
- `bool SendRequest(uint index, uint start, uint length)` — request a block.
- `bool SendPiece(uint index, uint start, byte[] data)` — send a block of piece data.
- `bool SendCancel(uint index, uint start, uint length)` — cancel a previously sent request.
- `bool SendPacket(PeerWirePacket packet)` — send an arbitrary pre-built packet.
- `void RegisterBTExtension(IProtocolExtension extension)` / `void UnregisterBTExtension(IProtocolExtension extension)` — add/remove a protocol extension.

**Events**
- `event DroppedConnectionDelegate DroppedConnection` — `(client)`, connection closed.
- `event NoDataDelegate NoData` — `(client)`, no packets on this `Process()` call.
- `event HandshakeCompleteDelegate HandshakeComplete` — `(client)`, remote handshake received.
- `event KeepAliveDelegate KeepAlive` — `(client)`, keep-alive received.
- `event ChokeDelegate Choke` / `event UnChokeDelegate UnChoke` — `(client)`, choke state changed.
- `event InterestedDelegate Interested` / `event NotInterestedDelegate NotInterested` — `(client)`, interest state changed.
- `event HaveDelegate Have` — `(client, pieceIdx)`.
- `event BitFieldDelegate BitField` — `(client, size, bitfield)`.
- `event RequestDelegate Request` — `(client, pieceIdx, start, length)`.
- `event PieceDelegate Piece` — `(client, pieceIdx, start, buffer)`.
- `event CancelDelegate Cancel` — `(client, pieceIdx, start, length)`.
- `event CommandDelegate Command` — `(client, commandLength, commandId, payload) → bool`; fires for every incoming command before built-in/extension handling, return `true` to suppress further processing of that command.

### `bzTorrent.IPeerCommand`

Minimal contract for a decoded peer-wire command: an ID, its declared length, and its payload bytes. Implemented by `PeerWirePacket` (see `bzTorrent.Data`).

**Properties**
- `int Length { get; set; }` — declared length of the command.
- `byte CommandID { get; set; }` — the peer-wire message/command ID.
- `byte[] Payload { get; set; }` — the command's payload bytes.

### `bzTorrent.HTTPTrackerClient`

HTTP(S) BitTorrent tracker client (BEP-3 `announce`/`scrape` over HTTP, bencoded responses, compact peer lists). Inherits `Tracker`, `Port`, `Timeout` from `BaseScraper` (see below) and implements `ITrackerClient`. Use this when a torrent's announce URL starts with `http://` or `https://`.

**Constructors**
- `HTTPTrackerClient()` — no request timeout is set (relies on the underlying `HttpWebRequest` default).
- `HTTPTrackerClient(int timeout)` — sets `Timeout` (seconds) used for the scrape request's `HttpWebRequest.Timeout`.

**Methods**
- `AnnounceInfo Announce(string url, string hash, string peerId)` — convenience overload; calls the full overload with `bytesDownloaded=0`, `bytesLeft=0`, `bytesUploaded=0`, `eventTypeFilter=2` (started), `ipAddress=0`, `numWant=-1`, `listenPort=12345`, `extensions=0`.
- `AnnounceInfo Announce(string url, string hash, string peerId, long bytesDownloaded, long bytesLeft, long bytesUploaded, int eventTypeFilter, int ipAddress, int numWant, int listenPort, int extensions)` — issues an HTTP GET announce request. `url` is the tracker's announce URL (a literal `announce`/`scrape` substring is swapped as needed); `hash` is the 40-char hex info hash; `peerId` is sent as raw ASCII bytes, percent-encoded. `ipAddress`, `eventTypeFilter`, and `extensions` are accepted for interface parity with `UDPTrackerClient` but are **not** included in the query string — only `info_hash`, `peer_id`, `port`, `uploaded`, `downloaded`, `left`, `numWant`, `event=started`, and `compact=1` are sent. Returns `null` if the request fails, the response is empty, or the response has no `peers` key. Throws `NotSupportedException` if the tracker returns a dictionary-style (non-compact) peer list.
- `IDictionary<string, AnnounceInfo> Announce(string url, string[] hashes, string peerId)` — calls the 3-arg `Announce` once per hash (one HTTP request per hash) and returns a hash→result map.
- `IDictionary<string, ScrapeInfo> Scrape(string url, string[] hashes)` — issues an HTTP GET scrape request for one or more info hashes in a single request. Returns an empty dictionary (never `null`) if the request fails or the response has no `files` key.

### `bzTorrent.UDPTrackerClient`

UDP BitTorrent tracker client (BEP-15). Inherits `Tracker`, `Port`, `Timeout` from `BaseScraper` and implements `ITrackerClient`. Use this when a torrent's announce URL starts with `udp://`. Every call performs the UDP "connect" handshake (obtaining/refreshing a connection ID) before the actual announce/scrape request.

**Constructors**
- `UDPTrackerClient()` — no send/receive timeout is set.
- `UDPTrackerClient(int timeout)` — sets `Timeout` (seconds), used as both `UdpClient.Client.SendTimeout` and `ReceiveTimeout` (in milliseconds internally).

**Methods**
- `IDictionary<string, ScrapeInfo> Scrape(string url, string[] hashes)` — sends a UDP scrape request for 1–74 hashes in one packet. `url` must match `udp://host[:port]`. Throws `ArgumentOutOfRangeException` (via internal validation) if `hashes` is empty, has more than 74 entries, or contains a hash that isn't 40 hex characters; throws `ArgumentOutOfRangeException` if `url` isn't a valid `udp://` address. Returns an empty dictionary if the network request fails or times out (validation exceptions still propagate).
- `AnnounceInfo Announce(string url, string hash, string peerId)` — convenience overload equivalent to `Announce(url, hash, peerId, 0, 0, 0, 2, 0, -1, 12345, 0)`.
- `AnnounceInfo Announce(string url, string hash, string peerId, long bytesDownloaded, long bytesLeft, long bytesUploaded, int eventTypeFilter, int ipAddress, int numWant, int listenPort, int extensions)` — sends a UDP announce request per the BEP-15 binary packet format (connection id, action, transaction id, info hash, peer id, downloaded/left/uploaded, event, ip, a randomly generated key, num_want, port, extensions). `peerId` is sent as raw ASCII bytes and must be exactly 20 bytes for a compliant tracker. Same input-validation exceptions as `Scrape`. Returns `null` on network failure/timeout.
- `IDictionary<string, AnnounceInfo> Announce(string url, string[] hashes, string peerId)` — validates `url`/`hashes` once, then calls the 3-arg `Announce` per hash (one UDP round trip per hash) and returns a hash→result map.

### `bzTorrent.BaseScraper`

Abstract base class shared by `HTTPTrackerClient` and `UDPTrackerClient`. Holds the common `Timeout`/`Tracker`/`Port` state, tracker-URL/hash validation, and small encoding helpers used by both concrete scrapers. Also declares the `AnnounceInfo`/`ScrapeInfo` result types and the `ScraperType` enum referenced by `HTTPTrackerClient`/`UDPTrackerClient` above. Not intended to be used directly — construct `HTTPTrackerClient` or `UDPTrackerClient` instead.

**Constructors**
- `protected BaseScraper()` — leaves `Timeout` at its default (`0`).
- `protected BaseScraper(int timeout)` — sets `Timeout`.

**Properties**
- `int Timeout { get; init; }` — request timeout in seconds, consumed by the derived classes' socket/HTTP calls.
- `string Tracker { get; private set; }` — the tracker host (UDP) or matched announce URL (HTTP); populated by `ValidateInput`.
- `int Port { get; private set; }` — the tracker port; populated by `ValidateInput` (UDP only — defaults to `80` if no port is in the URL; unset for HTTP).
- `protected readonly byte[] BaseCurrentConnectionId` — the BEP-15 "initial" UDP connection ID constant (`0x41727101980`) that `UDPTrackerClient` seeds its connection-id state with before the first handshake.
- `protected readonly Random Random` — shared RNG (seeded from the current second) used by subclasses to generate UDP transaction IDs/keys.

**Methods**
- `protected void ValidateInput(string url, string[] hashes, ScraperType type)` — validates hash format (40 hex chars) and count (1–74), and validates/parses `url` against a UDP or HTTP tracker regex depending on `type`, populating `Tracker`/`Port` as a side effect. Throws `ArgumentOutOfRangeException` for any violation.
- `protected static string UrlEncodeBytes(byte[] bytes)` — percent-encodes every byte as `%XX` (used for the `peer_id` query parameter).
- `protected static string UrlEncodeHexString(string hexString)` — decodes a hex string to bytes then percent-encodes it (used for the `info_hash` query parameter).
- `protected static byte[] CopyBytes(byte[] sourceArray, int startIndex, int length)` — extracts a sub-array; used when pulling the connection ID out of a UDP response.

**Nested types**
- `public enum ScraperType { UDP, HTTP }` — selects which URL/response format `ValidateInput` and `ScrapeInfo` use.
- `public class AnnounceInfo` — result of an announce call.
  - Constructor: `AnnounceInfo(IEnumerable<IPEndPoint> peers, int waitTime, int seeders, int leechers)`.
  - Properties: `IEnumerable<IPEndPoint> Peers { get; set; }` (the peer swarm returned by the tracker), `int WaitTime { get; set; }` (seconds to wait before the next announce), `int Seeders { get; set; }`, `int Leechers { get; set; }`.
- `public class ScrapeInfo` — result of a scrape call for a single info hash.
  - Constructor: `ScrapeInfo(uint a, uint b, uint c, ScraperType type)` — argument meaning depends on `type`: for `HTTP`, `(a, b, c)` = `(Complete, Downloaded, Incomplete)`; for `UDP`, `(a, b, c)` = `(Seeders, Complete, Leechers)`.
  - Properties: `uint Seeders { get; set; }` (UDP only), `uint Complete { get; set; }`, `uint Leechers { get; set; }` (UDP only), `uint Downloaded { get; set; }` (HTTP only), `uint Incomplete { get; set; }` (HTTP only). Note the HTTP and UDP scrape wire formats use different field semantics, so depending on which client produced a `ScrapeInfo`, only a subset of these properties will be populated (the rest stay `0`).

### `bzTorrent.ITrackerClient`

Common interface implemented by both `HTTPTrackerClient` and `UDPTrackerClient`, so calling code can announce/scrape without knowing which transport a torrent's tracker uses.

**Properties**
- `string Tracker { get; }` — the tracker host/URL, populated after the first successful call.
- `int Port { get; }` — the tracker port (UDP trackers only).

**Methods**
- `BaseScraper.AnnounceInfo Announce(string url, string hash, string peerId)` — announce with default parameters (see the concrete implementations above for the exact defaults).
- `BaseScraper.AnnounceInfo Announce(string url, string hash, string peerId, long bytesDownloaded, long bytesLeft, long bytesUploaded, int eventTypeFilter, int ipAddress, int numWant, int listenPort, int extensions)` — full-control announce.
- `IDictionary<string, BaseScraper.AnnounceInfo> Announce(string url, string[] hashes, string peerId)` — announce for multiple info hashes against the same tracker.
- `IDictionary<string, BaseScraper.ScrapeInfo> Scrape(string url, string[] hashes)` — scrape stats for one or more info hashes.

### `bzTorrent.LocalPeerDiscovery<T>`

Local Peer Discovery (BEP-14): announces and listens for BitTorrent peers on the local network via SSDP-style multicast (`239.192.152.143:6771`), avoiding the need for a tracker/DHT to find peers on the same LAN. `T` is the socket implementation to use for both the multicast reader and sender and must be an `ISocket` with a public parameterless constructor (typically `UDPSocket`). Implements `IDisposable` and `ILocalPeerDiscovery<T>`.

**Constructors**
- `LocalPeerDiscovery()` — creates two `new T()` instances internally for the reader/sender sockets.
- `LocalPeerDiscovery(ISocket receive, ISocket send)` — injects existing socket instances instead (useful for testing with a fake `ISocket`).

**Properties**
- `int TTL { get; set; }` — multicast time-to-live for outgoing announce packets; defaults to `8`. Setting it immediately applies `SocketOptionName.MulticastTimeToLive` to the sender socket.

**Methods**
- `void Open()` — binds the reader socket to the multicast group/port, joins the multicast group on both sockets, connects the sender socket, and starts a background thread that continuously listens for `BT-SEARCH` announce packets from other local peers.
- `void Close()` — stops the background thread (via `Thread.Abort()`) and closes both sockets.
- `void Announce(int listeningPort, string infoHash)` — broadcasts a `BT-SEARCH` HTTP-style multicast datagram advertising `listeningPort` and `infoHash` to other local peers.
- `void Dispose()` — idempotent; calls `Close()` and disposes both underlying sockets, swallowing any exception during cleanup.

**Events**
- `event LocalPeerDiscovery<T>.NewPeerCB NewPeer` — raised on the background listener thread when a valid `BT-SEARCH` packet (containing both a `Port:` and `Infohash:` line) is received from another peer. Handler receives `(IPAddress address, int port, string infoHash)`.

Also declares the delegate `public delegate void NewPeerCB(IPAddress address, int port, string infoHash)` used by the `NewPeer` event.

### `bzTorrent.ILocalPeerDiscovery<T>`

Interface implemented by `LocalPeerDiscovery<T>`, with the same `T : ISocket, new()` constraint. Lets callers depend on the LPD contract without binding to the concrete implementation.

**Properties**
- `int TTL { get; set; }` — see `LocalPeerDiscovery<T>.TTL`.

**Methods**
- `void Open()` — see `LocalPeerDiscovery<T>.Open()`.
- `void Close()` — see `LocalPeerDiscovery<T>.Close()`.
- `void Announce(int listeningPort, string infoHash)` — see `LocalPeerDiscovery<T>.Announce`.

**Events**
- `event LocalPeerDiscovery<T>.NewPeerCB NewPeer` — see `LocalPeerDiscovery<T>.NewPeer`.

### `bzTorrent.IProtocolExtension`

Contract for extensions that register directly on a `PeerWireClient` via `RegisterBTExtension` (as opposed to `IBTExtension`, which plugs into the BEP-10 extended protocol via `ExtendedProtocolExtensions`). `PeerWireClient` uses `ByteMask` to advertise the extension's reserved-byte bit during the handshake and `CommandIDs` to route incoming peer-wire command bytes to the extension.

**Properties**
- `byte[] ByteMask { get; }` — an 8-byte mask OR'd into the handshake's reserved bytes to advertise support for this extension to the remote peer.
- `byte[] CommandIDs { get; }` — the peer-wire message IDs this extension handles; `PeerWireClient` dispatches matching incoming commands to `OnCommand`.

**Methods**
- `bool OnHandshake(IPeerWireClient client)` — called after the peer-wire handshake completes; return `true` if the extension sent data as part of handling the handshake (used by `ExtendedProtocolExtensions` to send its own extended handshake).
- `bool OnCommand(IPeerWireClient client, int commandLength, byte commandId, byte[] payload)` — called when an incoming command's ID matches one of `CommandIDs`; return `true` if the command was handled (stops further dispatch).

---

## bzTorrent.Data

### `bzTorrent.Data.IHashProvider`

Base contract for anything identified by a BitTorrent info-hash (implemented by `IMetadata` and `IMagnetLink`). Depend on this instead of a concrete type when all you need is the hash/name identity of a torrent.

**Properties**
- `byte[] Hash { get; }` — the raw 20-byte SHA-1 info-hash.
- `string HashString { get; }` — the info-hash as a lowercase hex string.
- `string Name { get; }` — the torrent's display name.

### `bzTorrent.Data.IMagnetLink`

Contract for a parsed magnet URI. Extends `IHashProvider` with the tracker URLs embedded in the link. `MagnetLink` is the sole implementation.

**Properties**
- `ICollection<string> Trackers { get; }` — tracker URLs extracted from `tr=` parameters in the magnet URI.

### `bzTorrent.Data.IMetadata`

Contract for a fully- or partially-populated torrent metadata object (i.e. the parsed contents of a `.torrent` file, or a stub built from a magnet link before the info dictionary has been fetched). `Metadata` is the sole implementation; code that just needs to read torrent info should depend on this interface.

**Properties**
- `string Announce { get; }` — the primary tracker URL (`announce` key).
- `ICollection<string> AnnounceList { get; }` — all tracker URLs, including the primary one and any from `announce-list` / a magnet link's `tr=` params.
- `string Comment { get; }` — free-text comment from the torrent file, if present.
- `string CreatedBy { get; }` — the tool/client that created the torrent, if present.
- `DateTime CreationDate { get; }` — creation timestamp, decoded from the Unix `creation date` field.
- `ICollection<byte[]> PieceHashes { get; }` — the SHA-1 hash of each piece, in piece order.
- `long PieceSize { get; }` — size in bytes of each piece (the last piece may be shorter).
- `bool Private { get; }` — true if the torrent's `private` flag is set (peers should only be found via the tracker, not DHT/PEX/LPD).

**Methods**
- `IReadOnlyCollection<string> GetFiles()` — returns the file names in the torrent, ordered by their position in the data stream.
- `IReadOnlyCollection<MetadataFileInfo> GetFileInfos()` — returns full per-file info (name, size, and byte offset), ordered by position.
- `bool Load(MagnetLink magnetLink)` — populates `Hash`/`Name`/`AnnounceList` from a resolved magnet link. Returns `false` if `magnetLink` or its hash is `null`. Does not populate piece/file data — that requires fetching the info dictionary separately (e.g. via the `UTMetadata` protocol extension) and loading it.
- `bool Load(Stream stream)` — parses a bencoded `.torrent` file from `stream`, populating all metadata fields including pieces and files. Returns `false` if the stream doesn't decode to a bencoded dictionary.

### `bzTorrent.Data.MagnetLink`

Concrete `IMagnetLink` implementation and the entry point for turning a `magnet:` URI into either a `MagnetLink` or a stub `IMetadata`. Construct via `Resolve`/`ResolveToMetadata` rather than parsing the URI yourself.

**Constructors**
- `MagnetLink()` — creates an empty magnet link with an initialized (empty) `Trackers` collection. Prefer `Resolve` for parsing an actual URI.

**Properties**
- `string Name { get; private set; }` — display name from the magnet link's `dn=` parameter.
- `byte[] Hash { get; private set; }` — info-hash decoded from the `xt=urn:btih:...` parameter.
- `string HashString { get; private set; }` — hex form of `Hash`; setting it (internally, during `Resolve`) updates `Hash`.
- `ICollection<string> Trackers { get; set; }` — tracker URLs from `tr=` parameters.

**Methods**
- `static MagnetLink Resolve(string magnetLink)` — parses a `magnet:` URI string into a `MagnetLink`, extracting the info-hash (`xt`), display name (`dn`), and tracker list (`tr`). Non-`btih` or malformed `xt` values are ignored; only well-formed 40-hex-char BTIH hashes are accepted.
- `static IMetadata ResolveToMetadata(string magnetLink)` — convenience wrapper that resolves the URI and wraps it in a `Metadata` object (via `Metadata(MagnetLink)`) so it can be used anywhere an `IMetadata` is expected. The result has hash/name/trackers populated but no piece/file data yet.
- `static Task<IMetadata> ResolveToMetadataAsync(string magnetLink)` — asynchronous version of `ResolveToMetadata`, running the (synchronous, CPU-only) resolution on a thread-pool thread via `Task.Run`.
- `static bool IsMagnetLink(string magnetLink)` — returns true if the string starts with `"magnet:"`.

### `bzTorrent.Data.Metadata`

Concrete `IMetadata` implementation and the central class for working with `.torrent` data — parsing a `.torrent` file, resolving a magnet link, or receiving an info dictionary fetched from a peer (BEP-9). Use the static `From*` helpers for one-shot loading from a file/buffer/string, or the constructors/`Load` overloads for more control.

**Constructors**
- `Metadata()` — creates an empty, unpopulated instance; call one of the `Load` overloads to populate it.
- `Metadata(Stream stream)` — creates an instance and immediately parses a bencoded `.torrent` file from `stream` (equivalent to `new Metadata(); Load(stream);`).
- `Metadata(MagnetLink magnetLink)` — creates an instance and populates it from a resolved magnet link (hash/name/trackers only — no piece/file data until the info dictionary is loaded separately).

**Properties**
- `byte[] Hash { get; private set; }` — SHA-1 hash of the bencoded `info` dictionary.
- `string HashString { get; private set; }` — hex form of `Hash`.
- `string Comment { get; private set; }`, `string Announce { get; private set; }`, `ICollection<string> AnnounceList { get; private set; }`, `string CreatedBy { get; private set; }`, `DateTime CreationDate { get; private set; }`, `string Name { get; private set; }`, `long PieceSize { get; private set; }`, `bool Private { get; private set; }` — see `IMetadata` for meaning; all populated by `Load`.
- `ICollection<byte[]> PieceHashes { get; private set; }` — raw 20-byte piece hashes, in order.
- `ICollection<MetadataPieceInfo> Pieces { get; private set; }` — the same piece hashes wrapped with their index, as `MetadataPieceInfo`.

**Methods**
- `bool Load(MagnetLink magnetLink)` — see `IMetadata.Load(MagnetLink)`. Populates `HashString`/`Name`/`AnnounceList` only.
- `bool Load(Stream stream)` — parses a bencoded `.torrent` file, filling in `Announce`, `AnnounceList`, `Comment`, `CreatedBy`, `CreationDate`, and (via the nested `info` dict) piece/file data. Returns `false` if the stream isn't a valid bencoded dictionary.
- `bool Load(BDict infoDict)` — parses an already-decoded bencoded `info` dictionary directly (this is what a `.torrent` file's `Load(Stream)` delegates to internally, and what you'd call after fetching an info dictionary via BEP-9 metadata exchange). Computes `Hash` as the SHA-1 of the dictionary's canonical bencoding, and populates `Name`, `PieceSize`, `Private`, `PieceHashes`, `Pieces`, and the file list (single-file or multi-file layout, both supported). Returns `false` if `infoDict` is `null`.
- `IReadOnlyCollection<string> GetFiles()` — file names ordered by their id/position; empty collection if no files have been loaded.
- `IReadOnlyCollection<MetadataFileInfo> GetFileInfos()` — full per-file records ordered by position; empty collection if no files have been loaded.
- `static IMetadata FromString(string metadata)` — parses a bencoded `.torrent` document from an ASCII string.
- `static IMetadata FromBuffer(byte[] metadata)` — parses a bencoded `.torrent` document from an in-memory byte buffer.
- `static IMetadata FromFile(string filename)` — opens and parses a `.torrent` file from disk.

### `bzTorrent.Data.MetadataFileInfo`

Plain data record describing one file within a torrent's (possibly multi-file) payload, as returned by `IMetadata.GetFileInfos()`.

**Properties**
- `long Id { get; set; }` — the file's position/index within the torrent's file list.
- `string Filename { get; set; }` — relative path/filename of the file.
- `long FileStartByte { get; set; }` — byte offset of this file's start within the torrent's overall concatenated data stream (useful for mapping piece data to file offsets).
- `long FileSize { get; set; }` — size of the file in bytes.

**Methods**
- `string ToString()` — returns `Filename`.

### `bzTorrent.Data.MetadataPieceInfo`

Plain data record pairing a piece index with its expected SHA-1 hash, as found in `Metadata.Pieces`.

**Properties**
- `long Id { get; set; }` — the piece's index within the torrent.
- `byte[] PieceHash { get; set; }` — the expected 20-byte SHA-1 hash of the piece's data.

**Methods**
- `string ToString()` — returns `PieceHash` as a lowercase hex string.

### `bzTorrent.Data.PeerClientCommands`

`byte`-backed enum of peer wire protocol message type IDs (base protocol per BEP-3, plus IDs used by Fast Extensions/BEP-6 and the DHT port extension/BEP-5). Used as `PeerWirePacket.Command`.

**Values**
- `Choke = 0`, `Unchoke = 1`, `Interested = 2`, `NotInterested = 3`, `Have = 4`, `Bitfield = 5`, `Request = 6`, `Piece = 7`, `Cancel = 8` — base BitTorrent protocol messages.
- `DHTPort = 9` — BEP-5 DHT port announcement.
- `SuggestPiece = 13`, `HaveAll = 14`, `HaveNone = 15`, `Reject = 16`, `AllowedFast = 17` — BEP-6 Fast Extensions messages.
- `ExtendedProtocol = 20` — BEP-10 extended protocol container message.
- `KeepAlive = 128` — not a real wire value (the actual keep-alive message is a zero-length packet with no command byte); used internally by this library to represent a parsed keep-alive.

### `bzTorrent.Data.PeerClientHandshake`

Plain data record for the fixed-format BitTorrent handshake message exchanged at the start of every peer wire connection.

**Properties**
- `string ProtocolHeader { get; set; }` — protocol identifier string; defaults to `"BitTorrent protocol"`.
- `string PeerId { get; set; }` — the local or remote peer's 20-byte peer ID.
- `string InfoHash { get; set; }` — the torrent info-hash being requested/offered.
- `byte[] ReservedBytes { get; set; }` — the 8 reserved bytes used to signal supported extensions (e.g. BEP-10 extended protocol, DHT).

### `bzTorrent.Data.PeerWirePacket`

Represents one length-prefixed peer wire protocol message — parses raw bytes off the wire into a command + payload, and serializes a command + payload back into wire format.

**Properties**
- `uint PacketByteLength { get; }` — total wire size of the packet: 4 (length prefix) + `CommandLength`.
- `uint CommandLength { get; set; }` — value of the message's 4-byte length prefix (1 + payload length, or 0 for keep-alive).
- `PeerClientCommands Command { get; set; }` — the message type.
- `byte[] Payload { get; set; }` — the message body following the command byte, if any.

**Methods**
- `bool Parse(byte[] currentPacketBuffer)` — attempts to parse a packet from the start of `currentPacketBuffer`. Returns `false` if fewer than 4 bytes are available, or if the declared `CommandLength` exceeds the bytes available (caller should buffer more data and retry). Sets `Command = PeerClientCommands.KeepAlive` for a zero-length message. On success, populates `Command` and `Payload`.
- `string ToString()` — returns `Command.ToString()`.
- `byte[] GetBytes()` — serializes this packet to its wire format (4-byte big-endian length prefix + command byte + payload), or the 4-byte zero-length keep-alive message if `Command` is `KeepAlive`.

---

## bzTorrent.DHT

### `bzTorrent.DHT.DHTClient`

Implements the BitTorrent Mainline DHT (BEP-5) over UDP: a self-contained node that can bootstrap into the global DHT, maintain a routing table, and iteratively search for peers announcing a given info hash. It also answers incoming DHT queries (`ping`, `find_node`, `get_peers`, `announce_peer`) from other nodes so it participates as a full node rather than a passive client. Implements `IDisposable`.

Typical usage: construct, subscribe to `PeerFound`, call `Start()`, `await BootstrapAsync(...)` with well-known bootstrap nodes, then call `StartSearch(infoHash)` for each torrent you want peers for. `Dispose()` when finished.

**Constructors**
- `DHTClient(int port = 0)` — generates a random 20-byte node ID and binds a `UdpClient` to `port` (`0` picks any free local port). Does not start listening yet — call `Start()` for that.

**Properties**
- `int NodeCount { get; }` — number of nodes currently held in the internal routing table.
- `int LocalPort { get; }` — the local UDP port the client is bound to (useful for announcing yourself or in tests).

**Methods**
- `void Start()` — begins the background receive loop (`ReceiveLoopAsync`) that listens for and dispatches incoming DHT packets. Must be called before `BootstrapAsync`/`StartSearch` will do anything useful, since query responses arrive via this loop.
- `void Stop()` — signals cancellation to the internal `CancellationTokenSource`, which stops the receive loop and any in-progress bootstrap/search loops. Does not dispose the client (see `Dispose()`).
- `async Task BootstrapAsync(IEnumerable<IPEndPoint> nodes)` — seeds the routing table by querying the given bootstrap nodes with `find_node`. Because UDP is lossy, it retries up to 8 times (with short delays) until at least one node responds and the routing table is non-empty, then does one more round of `find_node` against the newly-learned nodes to widen the table. Await this before calling `StartSearch` so searches have nodes to query; if `nodes` is null/empty it returns immediately with an empty table. Individual node failures are swallowed internally and never fault the returned task.
- `void StartSearch(byte[] infoHash)` — fire-and-forget: starts a background loop that performs an iterative `get_peers` search for `infoHash` (waiting for the routing table to be non-empty first), re-running the search every 5 minutes until `Stop()`/`Dispose()` is called. Peers discovered during the search are reported via the `PeerFound` event, not a return value — this method returns immediately.
- `void Dispose()` — marks the client disposed, cancels the internal token source, and disposes the underlying `UdpClient`. Safe to call even if `Start()` was never called.

**Events**
- `event Action<IPEndPoint> PeerFound` — raised (from a background task, not necessarily the calling thread) each time a `get_peers` response from a queried node includes a peer for the info hash currently being searched via `StartSearch`. The handler receives the peer's `IPEndPoint`.

### `bzTorrent.DHT.DHTNode`

A single entry in the DHT routing table: a remote node's 20-byte ID, its network endpoint, and when it was last seen/refreshed. Used by `DHTRoutingTable` and returned from `DHTClient`'s internal node-parsing logic; consumers mostly encounter it indirectly.

**Constructors**
- `DHTNode(byte[] id, IPEndPoint endPoint)` — creates a node record with `LastSeen` initialized to `DateTime.UtcNow`.

**Properties**
- `byte[] Id { get; }` — the node's 20-byte DHT node ID.
- `IPEndPoint EndPoint { get; }` — the node's address and port.
- `DateTime LastSeen { get; set; }` — timestamp of the last time this node was seen/refreshed; used by `DHTRoutingTable` to evict the least-recently-seen node when the table is full.

**Methods**
- `static int CompareDistance(byte[] a, byte[] b, byte[] target)` — compares two node/info-hash IDs by their XOR (Kademlia) distance to `target`. Returns a negative value if `a` is closer than `b`, positive if farther, `0` if equidistant. Used to order/select the closest known nodes to a target ID.

### `bzTorrent.DHT.DHTRoutingTable`

A thread-safe (internally locked) bounded collection of known `DHTNode`s, capped at 1600 entries, used by `DHTClient` to track the nodes it has learned about and to find the nodes closest to a given target ID (Kademlia-style routing).

**Properties**
- `int Count { get; }` — number of nodes currently in the table.

**Methods**
- `void AddOrUpdate(DHTNode node)` — inserts a new node, or, if a node with the same `Id` already exists, refreshes its `LastSeen` timestamp in place (the new `DHTNode` instance passed in is otherwise discarded). If the table is already at its 1600-node cap, the least-recently-seen node is evicted first.
- `List<DHTNode> GetClosest(byte[] targetId, int count)` — returns up to `count` nodes from the table ordered by XOR distance to `targetId`, closest first. Used both to pick nodes to query during a search and to build `find_node`/`get_peers` responses.
- `List<DHTNode> GetAll()` — returns a snapshot copy of every node currently in the table.

---

## bzTorrent.IO

Low-level transport abstractions. `ISocket` is the socket-level contract (`BaseSocket`, `TCPSocket`, `UDPSocket`, `UTPSocket`); `IPeerConnection` is the higher-level, BitTorrent-aware connection contract implemented by `PeerWireConnection<T>`.

### `bzTorrent.IO.ISocket`

Transport-agnostic socket contract used throughout the library so `PeerWireConnection<T>` and friends can be generic over TCP/UDP/uTP. Extends `IDisposable`. Implemented by `BaseSocket` and its derivatives.

**Properties**
- `bool Connected { get; }` — whether the socket currently considers itself connected.
- `int ReceiveTimeout { get; set; }` — milliseconds before a blocking receive gives up.
- `int SendTimeout { get; set; }` — milliseconds before a blocking send (and, in `BaseSocket`, `Connect`) gives up.
- `bool NoDelay { get; set; }` — disables Nagle's algorithm when true (TCP-specific; meaningless for UDP/uTP but still present on the contract).
- `bool ExclusiveAddressUse { get; set; }` — maps to the underlying socket option of the same name.

**Methods**
- `void Connect(EndPoint remoteEP)` — connects to a remote endpoint.
- `void Bind(EndPoint localEP)` — binds the socket to a local endpoint.
- `void Listen(int backlog)` — puts the socket into a listening state.
- `void Disconnect(bool reuseSocket)` — disconnects; `reuseSocket` allows the underlying socket to be reused for a subsequent `Connect`.
- `ISocket Accept()` — blocking accept of an incoming connection; returns a new `ISocket` of the same concrete type.
- `IAsyncResult BeginAccept(AsyncCallback callback, object state)` / `ISocket EndAccept(IAsyncResult ar)` — APM-style async accept pair.
- `IAsyncResult BeginReceive(byte[] buffer, int offset, int size, SocketFlags socketFlags, AsyncCallback callback, object state)` / `int EndReceive(IAsyncResult asyncResult)` — APM-style async receive pair.
- `IAsyncResult BeginReceiveFrom(byte[] buffer, int offset, int size, SocketFlags socketFlags, ref EndPoint remoteEP, AsyncCallback callback, object state)` / `int EndReceiveFrom(IAsyncResult asyncResult, ref EndPoint endPoint)` — APM-style async receive-from pair (connectionless).
- `int Send(byte[] buffer)` — sends data on the connected socket.
- `int SendTo(byte[] buffer, EndPoint remoteEP)` — sends data to an explicit endpoint (connectionless).
- `int Receive(byte[] buffer, int offset, int size)` — synchronous, blocking receive. Used specifically for the MSE/PE handshake, which is negotiated inline before the async receive loop takes over; not all implementations support this (see `UTPSocket`, which throws).
- `void SetSocketOption(...)` (three overloads: `bool`, `int`, `object` values) — sets a socket option at a given `SocketOptionLevel`/`SocketOptionName`.
- `void Close()` — closes the socket.

### `bzTorrent.IO.IPeerConnection`

Connection-level contract for a single peer session: wraps an `ISocket`, the MSE/PE handshake, and BitTorrent packet framing. Implemented by `PeerWireConnection<T>`.

**Properties**
- `PeerEncryptionMode EncryptionMode { get; set; }` — whether/how MSE/PE should be attempted for this connection.
- `PeerEncryptionOptions EncryptionOptions { get; }` — negotiation options (padding limits, known infohashes) for MSE/PE.
- `bool IsEncrypted { get; }` — whether the negotiated session ended up encrypted.
- `bool Connected { get; }` — connection state.
- `int Timeout { get; set; }` — connect/send/receive timeout in seconds.
- `PeerClientHandshake RemoteHandshake { get; }` — the handshake received from the remote peer.

**Methods**
- `void Connect(IPEndPoint endPoint)` — opens the underlying transport connection.
- `void Disconnect()` — closes the connection.
- `void Listen(EndPoint ep)` — puts the connection into a listening state for incoming peers.
- `IPeerConnection Accept()` — blocking accept of an incoming peer connection.
- `IAsyncResult BeginAccept(AsyncCallback callback)` / `ISocket EndAccept(IAsyncResult ar)` — APM-style async accept pair.
- `bool Process()` — drives the connection's internal state machine; call in a loop.
- `void Handshake(PeerClientHandshake handshake)` — sends the local handshake (and, if `EncryptionMode` allows it, negotiates MSE/PE first).
- `bool HasPackets()` — whether at least one fully-framed `PeerWirePacket` is ready to be read.
- `PeerWirePacket Receive()` — pops the next framed packet.
- `void Send(PeerWirePacket packet)` — frames and sends a packet.

### `bzTorrent.IO.BaseSocket`

Abstract `ISocket` base class that wraps a `System.Net.Sockets.Socket` and forwards most members to it directly. `TCPSocket`, `UDPSocket`, and `UTPSocket` all derive from this; they mainly override `Accept`/`EndAccept` (to return the correctly-typed wrapper) and, for the connectionless transports, `Connect`/`Disconnect`/`Connected`/`NoDelay`.

**Constructors**
- `BaseSocket(Socket socket)` — wraps an existing `Socket`. Called via `base(...)` from derived-class constructors; the class itself is abstract.

**Properties** (all `virtual`, delegate to the wrapped `Socket` unless overridden)
- `bool Connected { get; }` — `_socket.Connected`.
- `int ReceiveTimeout { get; set; }` — `_socket.ReceiveTimeout`.
- `int SendTimeout { get; set; }` — `_socket.SendTimeout`.
- `bool NoDelay { get; set; }` — `_socket.NoDelay`.
- `bool ExclusiveAddressUse { get; set; }` — `_socket.ExclusiveAddressUse`.

**Methods**
- `ISocket Accept()` — `abstract`; derived classes must implement so the returned instance is the correct concrete type.
- `void Bind(EndPoint localEP)` — `virtual`, forwards to `Socket.Bind`.
- `void Connect(EndPoint remoteEP)` — `virtual`. If `SendTimeout` is unset (`<= 0`), does a plain blocking `Socket.Connect`. Otherwise uses `BeginConnect`/`EndConnect` with a wait on the timeout (chosen over `Poll` because it correctly updates `Socket.Connected`); on timeout it closes the socket and throws `SocketException(SocketError.TimedOut)`.
- `void Disconnect(bool reuseSocket)` — `virtual`, forwards to `Socket.Disconnect`.
- `void Dispose()` — `virtual`, disposes the wrapped `Socket`.
- `void Listen(int backlog)` — `virtual`, forwards to `Socket.Listen`.
- `int Send(byte[] buffer)` — `virtual`, forwards to `Socket.Send`.
- `int Receive(byte[] buffer, int offset, int size)` — `virtual`, forwards to `Socket.Receive` with `SocketFlags.None`.
- `void SetSocketOption(...)` (three overloads) — `virtual`, forward to the matching `Socket.SetSocketOption` overload.
- `IAsyncResult BeginAccept(AsyncCallback callback, object state)` — forwards to `Socket.BeginAccept` (not overridable — no `virtual`).
- `ISocket EndAccept(IAsyncResult ar)` — `abstract`; derived classes wrap the accepted `Socket` in their own type.
- `IAsyncResult BeginReceive(...)` / `int EndReceive(IAsyncResult asyncResult)` — `virtual`, forward to `Socket.BeginReceive`/`EndReceive`.
- `IAsyncResult BeginReceiveFrom(...)` / `int EndReceiveFrom(...)` — `virtual`, forward to `Socket.BeginReceiveFrom`/`EndReceiveFrom`.
- `int SendTo(byte[] buffer, EndPoint remoteEP)` — `virtual`, forwards to `Socket.SendTo`.
- `void Close()` — forwards to `Socket.Close()` (not overridable).

### `bzTorrent.IO.TCPSocket`

Concrete `ISocket` over TCP (`SocketType.Stream` / `ProtocolType.Tcp`). This is the socket type used for a standard, non-uTP BitTorrent peer connection.

**Constructors**
- `TCPSocket(Socket socket)` — wraps an existing TCP `Socket`.
- `TCPSocket()` — creates a new TCP/IPv4 `Socket`.

**Methods**
- `ISocket Accept()` — overrides `BaseSocket.Accept()`; blocking-accepts and wraps the result as a new `TCPSocket`, returning `null` if the accept throws.
- `ISocket EndAccept(IAsyncResult ar)` — overrides `BaseSocket.EndAccept()`; same wrap-or-`null` behavior for the async accept.

### `bzTorrent.IO.UDPSocket`

Concrete `ISocket` over UDP (`SocketType.Dgram` / `ProtocolType.Udp`). Since UDP has no real connection, this class fakes "connected" semantics by just recording the remote endpoint and a flag; used both directly (e.g. `LocalPeerDiscovery<UDPSocket>`) and as the base transport `UTPSocket` builds on.

**Constructors**
- `UDPSocket(Socket socket)` — wraps an existing UDP `Socket`.
- `UDPSocket()` — creates a new UDP/IPv4 `Socket`.

**Properties**
- `bool Connected { get; }` — overrides `BaseSocket.Connected`; returns the internal `isConnected` flag rather than querying the OS socket (which is never truly "connected" for UDP).
- `bool NoDelay { get; set; }` — overrides `BaseSocket.NoDelay` as a plain auto-property (no-op storage; UDP has no Nagle's algorithm to disable).

**Methods**
- `ISocket Accept()` — overrides `BaseSocket.Accept()`; wraps the result as a new `UDPSocket`, `null` on failure.
- `ISocket EndAccept(IAsyncResult ar)` — overrides `BaseSocket.EndAccept()`; same pattern for async accept.
- `void Connect(EndPoint remoteEP)` — overrides `BaseSocket.Connect()`; does not touch the OS socket at all — just stores `remoteEP` and sets `Connected` to true.
- `void Disconnect(bool reuseSocket)` — overrides `BaseSocket.Disconnect()`; just clears the `Connected` flag.

### `bzTorrent.IO.UTPCongestionControl`

Pure, timestamp-driven implementation of the LEDBAT-style congestion-control algorithm documented in [`bzTorrent/Docs/utp-protocol.md`](utp-protocol.md) (BEP-29 / libutp). Has no socket or timer dependencies — the caller feeds it delay samples and ack events with explicit microsecond timestamps — so it can be (and is) driven deterministically in unit tests. `UTPSocket` owns one instance per connection.

**Constants**
- `const uint CControlTarget = 100_000` — target queuing delay in microseconds (100ms), the LEDBAT `CCONTROL_TARGET`.
- `const uint MaxCwndIncreaseBytesPerRtt = 3000` — maximum window growth per RTT when delay is at/below target.
- `const uint MinWindowSize = 10` — absolute floor for `MaxWindow`, in bytes.
- `const uint MaxWindowDecay = 100_000` — minimum microseconds between automatic window-halving decays.

**Constructors**
- `UTPCongestionControl(uint socketSendBufferSize)` — `socketSendBufferSize` is both the initial value and the hard ceiling for `MaxWindow`.

**Properties**
- `uint MaxWindow { get; }` — the current congestion window (max bytes allowed in flight), clamped to `[MinWindowSize, socketSendBufferSize]`.

**Methods**
- `void OnDelaySample(uint delayMicros)` — records a one-way delay sample (the `timestamp_difference_microseconds` field from a received uTP packet, i.e. the delay the remote peer measured in *our* send direction). Kept in a 13-sample rolling history; the minimum of that history approximates the uncongested base delay and is what `OnAck` uses as "our_delay".
- `void OnAck(uint bytesAcked, uint currentMicros)` — applies a cumulative ack. If `bytesAcked > 0`, recomputes `MaxWindow` via the LEDBAT `off_target`/`window_factor`/`delay_factor` formula (grows the window when measured delay is below `CControlTarget`, shrinks it when above, floored at `MinWindowSize`). Always also checks the decay timer, so this should be called on *every* received packet — including pure acks with `bytesAcked == 0` — to keep the decay clock advancing. On the very first call, it only primes the decay timer and does not decay.

### `bzTorrent.IO.UTPSocket`

Concrete `ISocket` implementing the BEP-29 Micro Transport Protocol (uTP) over a UDP socket, including LEDBAT congestion control via `UTPCongestionControl`.

> **Experimental / incomplete.** The type's own doc comment warns: *"This is an incomplete implementation, and is quite buggy, and needs a lot of work to fix the issues. use the TCPSocket for a standard BT connection for now."* It has no packet retransmission and no reordering/reassembly — it tracks in-flight bytes and processes cumulative acks well enough to drive congestion control, but a lost packet is simply never recovered. `Receive(byte[], int, int)` is explicitly unsupported (see below) because MSE/PE cannot run over uTP's packetized, stateful receive path.

**Nested types**
- `enum UTPSocket.PacketType : byte { STData = 0, STFin = 1, STState = 2, STReset = 3, STSyn = 4 }` — the four-bit uTP packet type field.
- `class UTPPacketHeader` (top-level in the same namespace/file) — parses a raw 20-byte uTP header.
  - Properties (all `{ get; private set; }`): `byte Version`, `UTPSocket.PacketType PacketType`, `ushort ConnectionIdRecvd`, `uint TimestampRecvd`, `uint TimestampDiffRecvd`, `uint WndSizeRecvd`, `ushort SeqNumberRecvd`, `ushort AckNumberRecvd`.
  - Method: `void Parse(byte[] buffer)` — populates the properties above from the first 20 bytes of `buffer` (big-endian).

**Constructors**
- `UTPSocket(Socket socket)` — wraps an existing `Socket`.
- `UTPSocket()` — creates a new UDP/IPv4 `Socket` (uTP always rides over UDP datagrams).

**Properties**
- `bool Connected { get; }` — overrides `BaseSocket.Connected`; true once the local `Connect`/handshake state considers the session open.
- `bool NoDelay { get; set; }` — overrides `BaseSocket.NoDelay` as a plain auto-property (unused by uTP itself).

**Methods**
- `void Connect(EndPoint remoteEP)` — overrides `BaseSocket.Connect()`. Generates a random local connection ID and sends an `ST_SYN` packet to begin the handshake; does not block for the peer's response.
- `void Disconnect(bool reuseSocket)` — overrides `BaseSocket.Disconnect()`. Sends an `ST_FIN` packet and marks the socket disconnected.
- `void Listen(int backlog)` — overrides `BaseSocket.Listen()`. Sets `SocketOptionName.ReuseAddress` and binds the underlying UDP socket (note: `backlog` is unused — UDP has no listen backlog).
- `int Send(byte[] buffer)` — overrides `BaseSocket.Send()`. Waits (up to ~2 seconds, polling every 5ms) for the current LEDBAT window (`UTPCongestionControl.MaxWindow`) to have room for `buffer.Length` bytes in flight, then sends it as an `ST_DATA` packet. This is a best-effort throttle, not a queue — after the wait budget it sends anyway rather than blocking indefinitely.
- `ISocket Accept()` — overrides `BaseSocket.Accept()`; wraps the result as a new `UTPSocket`, `null` on failure. (Uses the OS-level `Socket.Accept`, which is not meaningful for a UDP-backed transport — inherited from the same pattern as `TCPSocket`/`UDPSocket` rather than a real uTP-level accept.)
- `ISocket EndAccept(IAsyncResult ar)` — overrides `BaseSocket.EndAccept()`; same wrap-or-`null` pattern.
- `IAsyncResult BeginReceive(byte[] buffer, int offset, int size, SocketFlags socketFlags, AsyncCallback callback, object state)` — overrides `BaseSocket.BeginReceive()`. This is where the uTP state machine actually lives: it issues an async `BeginReceiveFrom`, and on completion parses the uTP header, feeds the delay sample and any newly-acked bytes into `UTPCongestionControl`, advances handshake/connection state (`ST_SYN`→`ST_STATE` completes the connection; `ST_FIN` closes it), sends an `ST_STATE` ack for any non-state packet received, strips the 20-byte uTP header from `buffer` before invoking `callback`, and updates `EndReceive`'s effective length accordingly.
- `int EndReceive(IAsyncResult asyncResult)` — overrides `BaseSocket.EndReceive()`; returns the received byte count minus the 20-byte uTP header.
- `int Receive(byte[] buffer, int offset, int size)` — overrides `BaseSocket.Receive()`; **always throws `NotSupportedException`**. A synchronous byte-stream receive is meaningless for uTP (it would return raw UDP datagrams, header included, and bypass the uTP state machine), so MSE/PE — which needs this synchronous receive during its handshake — is not supported over uTP. Use `TCPSocket` for encrypted peers.

### `bzTorrent.IO.PeerWireConnection<T>`

The main connection type: binds a socket implementation (`T`) to the BitTorrent peer-wire protocol and, optionally, MSE/PE encryption negotiation. `T` is constrained to `ISocket, new()` — the same `PeerWireConnection<T>` type is reused for plaintext TCP (`PeerWireConnection<TCPSocket>`), UDP-based transports (`PeerWireConnection<UDPSocket>`), and uTP (`PeerWireConnection<UTPSocket>`) by swapping the type parameter; `T`'s parameterless constructor is used whenever a fresh socket is needed (`Connect`, `Listen`). Implements `IPeerConnection`.

**Constructors**
- `PeerWireConnection()` — no socket is created yet; call `Connect` (outgoing) or `Listen`/`Accept` (incoming) before using the connection.
- `PeerWireConnection(ISocket _socket)` — wraps an already-connected socket (sets `NoDelay` and applies `Timeout`). Used internally by `Accept()` to wrap an accepted socket; can also be used directly to adapt a pre-existing socket.

**Properties**
- `PeerEncryptionMode EncryptionMode { get; set; }` — controls MSE/PE negotiation on `Handshake`/incoming connections. Default `PeerEncryptionMode.PlainText` (no MSE attempted). See `PeerEncryptionMode` below.
- `bool IsEncrypted { get; }` — `true` once the MSE/PE handshake has completed *and* an RC4 payload cipher was negotiated (not just that MSE ran — a negotiated plaintext payload leaves this `false`).
- `PeerEncryptionOptions EncryptionOptions { get; }` — per-connection MSE/PE settings (supported crypto types, max padding, known infohashes for accepting incoming encrypted connections). Mutate this before connecting/accepting.
- `int Timeout { get; set; }` — socket read/write timeout in **seconds**. Setting it also updates the underlying socket's `ReceiveTimeout`/`SendTimeout` (in milliseconds) if a socket already exists, so it can be applied after construction (e.g. via an object initializer on an already-wrapped/accepted socket).
- `bool Connected { get; }` — `true` iff the underlying socket exists and reports connected. Reflects socket liveness only, **not** whether the BitTorrent handshake has been received — check `RemoteHandshake` or subscribe to the client's handshake-complete event for that.
- `PeerClientHandshake RemoteHandshake { get; }` — the parsed incoming BitTorrent handshake (protocol header, reserved bytes, infohash, peer ID), or `null` until one has been fully received.

**Methods**
- `void Connect(IPEndPoint endPoint)` — creates a new `T` socket, resets any encryption/handshake state left over from a previous connection, and connects to `endPoint`.
- `void Disconnect()` — disconnects and clears the socket and all encryption/handshake state. The connection cannot be reused afterwards (call `Connect`/`Listen` again on a new instance, or reconnect via `Connect`).
- `void Listen(EndPoint ep)` — lazily creates the socket if needed, binds to `ep`, and starts listening with a backlog of 10.
- `IPeerConnection Accept()` — synchronously accepts a pending incoming connection on a listening socket and returns a new `PeerWireConnection<T>` wrapping it. Propagates `Timeout`, `EncryptionMode`, all known infohashes, `SupportedTypes`, and `MaxPaddingBytes` from this connection's `EncryptionOptions` to the accepted connection, so encrypted incoming connections are configured automatically **when this method is used**. (`PeerWireListener<T>` does not call this internally — see its docs for the resulting gap.)
- `IAsyncResult BeginAccept(AsyncCallback callback)` — begins an asynchronous accept on the listening socket.
- `ISocket EndAccept(IAsyncResult ar)` — completes an asynchronous accept and returns the raw `ISocket`; unlike `Accept()`, the caller is responsible for wrapping it in a `PeerWireConnection<T>` (and propagating encryption settings) themselves.
- `bool Process()` — drives the connection: if not already receiving, starts an async receive; flushes any packets queued via `Send` (encrypting them first if a send cipher was negotiated); returns `Connected`. Intended to be called repeatedly in a loop (`while (connection.Process()) { ... }`) by the owning client.
- `void Send(PeerWirePacket packet)` — enqueues a packet to be written to the socket on the next `Process()` call.
- `PeerWirePacket Receive()` — dequeues and returns the next fully-parsed incoming packet, or `null` if none is available yet.
- `void Handshake(PeerClientHandshake handshake)` — sends the initial BitTorrent handshake (protocol header, reserved bytes, infohash, peer ID). If `EncryptionMode` is not `PlainText` and MSE hasn't completed yet, first runs the outgoing MSE key exchange for `handshake.InfoHash`. In `PreferEncryption` mode, if the MSE exchange throws, the connection is silently reconnected and the handshake is resent in plaintext instead of propagating the failure.
- `bool HasPackets()` — `true` if `Receive()` would currently return a packet.

### `bzTorrent.IO.PeerEncryptionMode`

Enum controlling how a `PeerWireConnection<T>` negotiates MSE/PE encryption for outgoing connections (and, symmetrically, what it accepts for incoming ones).

**Values**
- `PlainText` — no MSE; the BitTorrent handshake is sent/received in the clear. Default.
- `PreferEncryption` — attempt MSE, but fall back to plaintext if the peer will not encrypt.
- `RequireEncryption` — require MSE; refuse any connection that is not encrypted.

### `bzTorrent.IO.PeerEncryptionOptions`

Per-connection MSE/PE configuration, exposed via `PeerWireConnection<T>.EncryptionOptions`. Thread-safe for the known-infohash list (internally locked).

**Properties**
- `PeerEncryptionType SupportedTypes { get; set; }` — the crypto methods this side supports/advertises during negotiation. Default `PeerEncryptionType.RC4`. RC4 support is required for MSE to work at all (see `PeerWireConnection<T>.Handshake`, which throws if `RC4` isn't in this set).
- `int MaxPaddingBytes { get; set; }` — upper bound on the random padding generated for/tolerated during MSE negotiation. Default `512` (the spec default).

**Methods**
- `void AddKnownInfoHash(string infoHash)` — registers an infohash this side is willing to accept encrypted **incoming** connections for. Required before accepting encrypted connections: an incoming MSE handshake hides the infohash until it's matched against this known set, so with no entries an incoming MSE handshake cannot succeed. Throws `ArgumentNullException` if `infoHash` is `null`.
- `IReadOnlyList<string> GetKnownInfoHashes()` — returns a snapshot copy of the registered infohashes.

### `bzTorrent.IO.PeerEncryptionType`

`[Flags]` enum of the MSE/PE `crypto_provide`/`crypto_select` bitfield values exactly as sent on the wire (`uint`-backed).

**Values**
- `PlainText = 0x01` — payload sent unencrypted after the MSE handshake completes.
- `RC4 = 0x02` — payload encrypted with RC4 (the commonly supported/default method).

### Internal implementation types (not part of the public API)

Two more types live in this area of `bzTorrent.IO` but are declared `internal`, so they are **not** accessible from outside the `bzTorrent` assembly — they're implementation details of MSE/PE, included here only for completeness:

- **`MessageStreamEncryption`** (`internal static class`) — implements the MSE/PE Diffie-Hellman handshake and RC4 stream setup (steps described in the file's header comment: public-key exchange, `HASH`-based verification/infohash matching, `VC` synchronization, `crypto_provide`/`crypto_select` negotiation). Exposes `CreateLocalPublicKey`, `CreatePadding`, `CompleteOutgoing` (initiator side), and `CompleteIncoming` (receiver side, matches the hidden infohash against `PeerEncryptionOptions.GetKnownInfoHashes()`) — all consumed internally by `PeerWireConnection<T>`.
- **`RC4Cipher`** (`internal sealed class`) — a standard ARC4/RC4 stream cipher (KSA + PRGA) used for MSE's obfuscation/payload stream. One instance encrypts a single direction only and is not thread-safe. Exposes `Process(byte[] data)` (XOR with the next keystream bytes) and `Skip(int count)` (advance the keystream, e.g. to discard the first 1024 bytes per the MSE spec).

---

## bzTorrent.ProtocolExtensions

BEP extension modules that plug into `PeerWireClient` (via `RegisterBTExtension`, implementing `IProtocolExtension`) or `ExtendedProtocolExtensions` (via `RegisterProtocolExtension`, implementing `IBTExtension`, for BEP-10 sub-extensions).

### `bzTorrent.ProtocolExtensions.IBTExtension`

Contract for BEP-10 sub-extensions (PEX, metadata exchange, tracker exchange, etc.) that plug into an `ExtendedProtocolExtensions` instance via `RegisterProtocolExtension`, rather than registering directly on `PeerWireClient`.

**Properties**
- `string Protocol { get; }` — the extension's protocol name as sent in the BEP-10 `m` dictionary (e.g. `"ut_pex"`, `"ut_metadata"`, `"lt_tex"`); used by the parent `ExtendedProtocolExtensions` to route messages.

**Methods**
- `void Init(ExtendedProtocolExtensions parent)` — called once when the extension is registered; use this to store the parent for later calls to `SendExtended`/`GetIncomingMessageID`.
- `void Deinit()` — called when the extension is unregistered via `UnregisterProtocolExtension`.
- `void OnHandshake(IPeerWireClient peerWireClient, byte[] handshake)` — called with the raw bencoded BEP-10 extended-handshake payload when the remote peer advertises support for this protocol.
- `void OnExtendedMessage(IPeerWireClient peerWireClient, byte[] bytes)` — called with the raw bencoded payload of an incoming extended message addressed to this extension.

### `bzTorrent.ProtocolExtensions.DHTPortExtension`

Implements the BEP-5 `port` message, which peers use to advertise the UDP port their DHT node listens on during the handshake. Registers directly on `PeerWireClient` via `RegisterBTExtension` (implements `IProtocolExtension`, not `IBTExtension` — no `ExtendedProtocolExtensions` needed).

**Properties**
- `bool RemoteUsesDHT { get; private set; }` — not set by this class (always `false`); appears to be a placeholder, since the constructor never flips it in response to a received port.
- `byte[] ByteMask { get; }` — `{0,0,0,0,0,0,0,0x1}`, the BEP-5 DHT reserved-byte bit.
- `byte[] CommandIDs { get; }` — `{9}`, the peer-wire `port` message ID.

**Methods**
- `bool OnHandshake(IPeerWireClient client)` — always returns `false` (no data sent on handshake).
- `bool OnCommand(IPeerWireClient client, int commandLength, byte commandId, byte[] payload)` — decodes the peer's DHT port from `payload` and raises `Port`; returns `true` if `commandId == 9`, otherwise `false`.

**Events**
- `event PortDelegate Port` — `void PortDelegate(IPeerWireClient peerWireClient, ushort port)`; fires when the peer advertises its DHT node's listening port.

### `bzTorrent.ProtocolExtensions.LTTrackerExchange`

Implements BEP-28 tracker exchange (`lt_tex`), letting peers share tracker URLs they know about for the current torrent. Register with `ExtendedProtocolExtensions.RegisterProtocolExtension` (requires BEP-10).

**Properties**
- `string Protocol { get; }` — `"lt_tex"`.

**Methods**
- `void Init(ExtendedProtocolExtensions parent)` — no-op (parent is not retained; this extension only receives, it never sends via `SendExtended`).
- `void Deinit()` — no-op.
- `void OnHandshake(IPeerWireClient peerWireClient, byte[] handshake)` — decodes the handshake payload but does not currently act on it.
- `void OnExtendedMessage(IPeerWireClient peerWireClient, byte[] bytes)` — decodes an `added` list of tracker URL strings from the message and raises `TrackerAdded` once per URL.
- `IDictionary<string, IBencodingType> GetAdditionalHandshake(IPeerWireClient peerWireClient)` — returns a `{"tr": <info hash>}` entry to merge into the outgoing BEP-10 handshake; not invoked automatically by `ExtendedProtocolExtensions.OnHandshake`, so a caller wanting this sent must currently wire it in themselves.

**Events**
- `event TrackerAddedDelegate TrackerAdded` — `void TrackerAddedDelegate(IPeerWireClient client, IBTExtension extension, string newTracker)`; fires once per tracker URL the peer reports.

### `bzTorrent.ProtocolExtensions.UTPeerExchange`

Implements BEP-11 peer exchange (`ut_pex`), letting peers share lists of other peers in the swarm. Register with `ExtendedProtocolExtensions.RegisterProtocolExtension` (requires BEP-10).

**Properties**
- `string Protocol { get; }` — `"ut_pex"`.

**Methods**
- `void Init(ExtendedProtocolExtensions parent)` — stores `parent`, used later by `SendMessage`.
- `void Deinit()` — no-op.
- `void OnHandshake(IPeerWireClient peerWireClient, byte[] handshake)` — decodes the handshake payload but does not currently act on it.
- `void OnExtendedMessage(IPeerWireClient peerWireClient, byte[] bytes)` — decodes `added`/`added.f` (6-byte IP:port + flag entries) and `dropped` (6-byte IP:port entries) lists, raising `Added` once per added peer and `Dropped` once per dropped peer.
- `void SendMessage(IPeerWireClient peerWireClient, IPEndPoint[] addedEndPoints, byte[] flags, IPEndPoint[] droppedEndPoints)` — encodes and sends a PEX message advertising newly-known (`addedEndPoints`) and no-longer-available (`droppedEndPoints`) peers to `peerWireClient`. No-ops if both arrays are `null`. Note: the `flags` parameter is accepted but not currently encoded into the `added.f` field.

**Events**
- `event AddedDelegate Added` — `void AddedDelegate(IPeerWireClient client, IBTExtension extension, IPEndPoint endpoint, byte flags)`; fires once per peer the remote reports as newly available. Use this to grow your peer pool.
- `event DroppedDelegate Dropped` — `void DroppedDelegate(IPeerWireClient client, IBTExtension extension, IPEndPoint endpoint)`; fires once per peer the remote reports as no longer available.

### `bzTorrent.ProtocolExtensions.UTMetadata`

Implements BEP-9 metadata exchange (`ut_metadata`), letting a client fetch the full `.torrent` info dictionary from a peer when it only has a magnet link. Register with `ExtendedProtocolExtensions.RegisterProtocolExtension` (requires BEP-10).

**Properties**
- `string Protocol { get; }` — `"ut_metadata"`.

**Methods**
- `void Init(ExtendedProtocolExtensions parent)` — stores `parent` and resets internal receive buffers.
- `void Deinit()` — no-op.
- `void OnHandshake(IPeerWireClient peerWireClient, byte[] handshake)` — reads `metadata_size` from the peer's extended handshake (if present), sizes internal buffers accordingly, and immediately calls `RequestMetaData` to start fetching all pieces.
- `void OnExtendedMessage(IPeerWireClient peerWireClient, byte[] bytes)` — writes an incoming 16 KiB metadata piece into the internal buffer; once every piece has been received, decodes the full buffer and raises `MetaDataReceived`.
- `void RequestMetaData(IPeerWireClient peerWireClient)` — sends a `msg_type: 0` (request) message for every metadata piece to `peerWireClient`. Called automatically from `OnHandshake`, but can be called again manually (e.g. to retry).

**Events**
- `event MetaDataReceivedDelegate MetaDataReceived` — `void MetaDataReceivedDelegate(IPeerWireClient peerWireClient, IBTExtension extension, BDict metadata)`; fires once the full info dictionary has been reassembled from all pieces. `metadata` is the raw bencoded (bzBencode) info dictionary — load it into a `Metadata` object via `Metadata.Load(BDict)`.

### `bzTorrent.ProtocolExtensions.FastExtensions`

Implements BEP-6 fast extensions (`HaveAll`/`HaveNone`/`AllowedFast`/`SuggestPiece`/`RejectRequest`). Registers directly on `PeerWireClient` via `RegisterBTExtension` (implements `IProtocolExtension`, no `ExtendedProtocolExtensions` needed).

**Properties**
- `byte[] ByteMask { get; }` — `{0,0,0,0,0,0,0,0x04}`, the BEP-6 reserved-byte bit.
- `byte[] CommandIDs { get; }` — `{13,14,15,16,17}` (Suggest Piece, Have All, Have None, Reject Request, Allowed Fast).

**Methods**
- `bool OnHandshake(IPeerWireClient client)` — always returns `false`.
- `bool OnCommand(IPeerWireClient client, int commandLength, byte commandId, byte[] payload)` — decodes the payload for whichever of the five fast-extension commands matches `commandId` and raises the corresponding event. Always returns `false` (does not stop further dispatch).

**Events**
- `event SuggestPeiceDelegate SuggestPiece` — `void SuggestPeiceDelegate(IPeerWireClient client, int pieceIdx)`; peer recommends downloading this piece.
- `event RejectDelegate Reject` — `void RejectDelegate(IPeerWireClient client, int pieceIdx, int start, int length)`; peer rejects a previously-sent block request.
- `event HaveAllDelegate HaveAll` — `void HaveAllDelegate(IPeerWireClient client)`; peer has every piece (seeder shortcut for a full bitfield).
- `event HaveNoneDelegate HaveNone` — `void HaveNoneDelegate(IPeerWireClient client)`; peer has no pieces yet.
- `event AllowedFastDelegate AllowedFast` — `void AllowedFastDelegate(IPeerWireClient client, int pieceIdx)`; peer allows requesting this piece even while choked.

### `bzTorrent.ProtocolExtensions.ExtendedProtocolExtensions`

Implements BEP-10, the extended-messaging container protocol required by PEX, metadata exchange, and tracker exchange. Registers directly on `PeerWireClient` via `RegisterBTExtension` (implements `IProtocolExtension`); BEP-10 sub-extensions (`IBTExtension` implementations) are then registered on *this* instance via `RegisterProtocolExtension`, and it routes handshake/message data to each by protocol name.

**Constructors**
- `ExtendedProtocolExtensions()` — creates empty internal extension/ID-mapping lists.

**Properties**
- `byte[] ByteMask { get; }` — `{0,0,0,0,0,0x10,0,0}`, the BEP-10 reserved-byte bit.
- `byte[] CommandIDs { get; }` — `{20}`, the peer-wire extended-message command ID.

**Methods**
- `bool OnHandshake(IPeerWireClient client)` — builds and sends the BEP-10 extended handshake (an `m` dictionary mapping each registered sub-extension's protocol name to a locally-assigned message ID) to `client`; always returns `true`.
- `bool OnCommand(IPeerWireClient client, int commandLength, byte commandId, byte[] payload)` — routes incoming extended messages (`commandId == 20`) to `ProcessExtended`; returns `true` if handled, `false` if `commandId` isn't 20.
- `void RegisterProtocolExtension(IPeerWireClient client, IBTExtension extension)` — adds `extension` to the set advertised in future handshakes and calls its `Init(this)`.
- `void UnregisterProtocolExtension(IPeerWireClient client, IBTExtension extension)` — removes `extension` and calls its `Deinit()`.
- `byte GetOutgoingMessageID(IPeerWireClient client, IBTExtension extension)` — returns the message ID *we* assigned to `extension` for `client` (as sent in our handshake's `m` dict), or `0` if not found.
- `byte GetIncomingMessageID(IPeerWireClient client, IBTExtension extension)` — returns the message ID the *remote peer* assigned to `extension` (learned from their handshake), used when sending extended messages to them via `SendExtended`. Returns `0` if not found.
- `bool SendExtended(IPeerWireClient client, byte extMsgId, byte[] bytes)` — wraps `bytes` in an extended-message packet addressed to `extMsgId` and sends it via `client.SendPacket`; always returns `true`.

**Nested type: `ExtendedProtocolExtensions.ClientProtocolIDMap`**

Internal bookkeeping record pairing a client + protocol name with an assigned message ID; public because it's used as a public constructor/properties, but not something consumers typically construct directly.

- `ClientProtocolIDMap(IPeerWireClient client, string protocol, byte commandId)` — constructor.
- `IPeerWireClient Client { get; set; }`
- `string Protocol { get; set; }`
- `byte CommandID { get; set; }`

---

## bzTorrent.Protocol.Handlers

This namespace implements per-message-type handling for incoming peer wire protocol messages; `MessageDispatcher` routes each incoming `PeerWirePacket` to the `IMessageHandler` registered for its command byte.

### `bzTorrent.Protocol.Handlers.HandlerResult`

Enum returned by an `IMessageHandler` to tell the dispatcher how a message was processed.

**Members**
- `Handled` — handler consumed the message; do not process further.
- `NotHandled` — handler did not process this message; try next handler.
- `CloseConnection` — handler encountered an error; close the connection.

### `bzTorrent.Protocol.Handlers.IMessageHandler`

Interface for handling a single peer wire protocol message type. Implementations are registered against a command byte in `MessageDispatcher`.

**Methods**
- `HandlerResult Handle(PeerWireClient client, PeerWirePacket packet)` — processes an incoming message for the given client context and returns a `HandlerResult` indicating how the dispatcher should proceed.

### `bzTorrent.Protocol.Handlers.HaveHandler`

Handles `Have` messages (peer announces it now has a given piece). Implements `IMessageHandler`.

**Methods**
- `HandlerResult Handle(PeerWireClient client, PeerWirePacket packet)` — reads the 4-byte big-endian piece index from the payload and raises `client.RaiseHave(pieceIndex)`. Returns `CloseConnection` if the payload is shorter than 4 bytes (malformed message), otherwise `Handled`.

### `bzTorrent.Protocol.Handlers.PieceHandler`

Handles `Piece` messages (peer sends a block of piece data). Implements `IMessageHandler`.

**Methods**
- `HandlerResult Handle(PeerWireClient client, PeerWirePacket packet)` — reads the 4-byte big-endian piece index and 4-byte big-endian begin offset from the payload, extracts the remaining bytes as the data block, and raises `client.RaisePiece(index, begin, buffer)`. Returns `CloseConnection` if the payload is shorter than 8 bytes, otherwise `Handled`.

### `bzTorrent.Protocol.Handlers.BitfieldHandler`

Handles `Bitfield` messages (peer announces which pieces it has). Implements `IMessageHandler`.

**Methods**
- `HandlerResult Handle(PeerWireClient client, PeerWirePacket packet)` — parses the payload into a `bool[]` via `BitfieldParser.Parse`, sets `client.PeerBitField`, and raises `client.RaiseBitField(bitfieldLength * 8, bitfield)`. Returns `CloseConnection` if the payload is shorter than `packet.CommandLength` or parsing throws, otherwise `Handled`.

### `bzTorrent.Protocol.Handlers.RequestHandler`

Handles both `Request` (peer wants a block) and `Cancel` (peer no longer wants a previously requested block) messages; which one is determined by a constructor flag. Implements `IMessageHandler`.

**Constructors**
- `RequestHandler(bool isCancel = false)` — creates a handler for `Request` messages by default, or `Cancel` messages if `isCancel` is `true`. `MessageDispatcher`'s default setup registers one instance of each.

**Methods**
- `HandlerResult Handle(PeerWireClient client, PeerWirePacket packet)` — reads 4-byte big-endian `index`, `begin`, and `length` fields from the payload and raises either `client.RaiseCancel(index, begin, length)` or `client.RaiseRequest(index, begin, length)` depending on how the handler was constructed. Returns `CloseConnection` if the payload is shorter than 12 bytes, otherwise `Handled`.

### `bzTorrent.Protocol.Handlers.MessageDispatcher`

Routes incoming peer wire messages to the `IMessageHandler` registered for their command byte. On construction it pre-registers default handlers for `Have`, `Bitfield`, `Request`, `Cancel`, and `Piece` (from `PeerClientCommands`).

**Constructors**
- `MessageDispatcher()` — creates a dispatcher and registers the default set of handlers (`HaveHandler`, `BitfieldHandler`, `RequestHandler` for both request and cancel, `PieceHandler`).

**Methods**
- `void RegisterHandler(byte commandId, IMessageHandler handler)` — registers (or replaces) the handler used for a given command byte. Throws `ArgumentNullException` if `handler` is `null`.
- `void UnregisterHandler(byte commandId)` — removes the handler registered for a given command byte, if any.
- `bool Dispatch(PeerWireClient client, PeerWirePacket packet)` — looks up the handler for `packet.Command`, invokes it, and returns `true` if handled. If no handler is registered, returns `false`. If the handler returns `HandlerResult.CloseConnection`, calls `client.Disconnect()` and returns `true`.

---

## bzTorrent.Helpers

### `bzTorrent.Helpers.BitfieldParser`

Static helper that decodes a raw BitTorrent bitfield payload (one bit per piece, MSB-first within each byte) into a `bool[]`.

**Methods**
- `bool[] Parse(byte[] payload, int expectedBits = -1)` — expands `payload` into a boolean array of length `payload.Length * 8` (bit 0 of each byte is the highest-order bit, i.e. MSB-first). If `expectedBits` is given and smaller than the decoded length, the result is trimmed to that length. Throws `ArgumentNullException` if `payload` is null.

### `bzTorrent.Helpers.PackHelper`

Static helper for encoding primitive values and hex strings into byte arrays, primarily for building BitTorrent wire-protocol messages. The integer pack methods (`Int16`/`Int32`/`Int64`/`UInt16`/`UInt32`/`UInt64`) always emit **network byte order (big-endian)**, regardless of host machine endianness or any parameter — there is no endianness override for them.

**Nested types**
- `enum Endianness { Machine, Big, Little }` — used by `Float`, `Double`, and `Hex` to select byte ordering.

**Methods**
- `byte[] Int16(short i)`, `byte[] Int32(int i)`, `byte[] Int64(long i)` — encode a signed integer as big-endian bytes (via `IPAddress.HostToNetworkOrder`).
- `byte[] UInt16(ushort value)`, `byte[] UInt32(uint value)`, `byte[] UInt64(ulong value)` — encode an unsigned integer as big-endian bytes.
- `byte[] Float(float f, Endianness e = Endianness.Machine)`, `byte[] Double(double f, Endianness e = Endianness.Machine)` — encode a float/double via `BitConverter.GetBytes`. The `e` parameter is accepted but not currently applied (byte order always follows the host machine).
- `byte[] Hex(string str, Endianness e = Endianness.Machine)` — converts a hex-digit string into raw bytes (pads an odd-length string with a trailing `'0'`). `e` controls whether byte pairs are read from the string in forward or reversed order; all current call sites use the default `Machine`.

### `bzTorrent.Helpers.UnpackHelper`

Static helper for decoding primitive values back out of byte arrays — the read-side counterpart to `PackHelper`. Unlike `PackHelper`'s integer methods, these accept an explicit `Endianness` and default to `Machine` (host-native) ordering, so callers decoding big-endian wire data must pass `Endianness.Big` explicitly.

**Nested types**
- `enum Endianness { Machine, Big, Little }`

**Methods**
- `short Int16(byte[] bytes, int start, Endianness e = Endianness.Machine)`, `int Int32(byte[] bytes, int start, Endianness e = Endianness.Machine)`, `long Int64(byte[] bytes, int start, Endianness e = Endianness.Machine)` — read a signed integer of the corresponding width starting at `start`, byte-swapping first if `e` doesn't match the host's native endianness.
- `ushort UInt16(byte[] bytes, int start, Endianness e = Endianness.Machine)`, `uint UInt32(byte[] bytes, int start, Endianness e = Endianness.Machine)`, `ulong UInt64(byte[] bytes, int start, Endianness e = Endianness.Machine)` — unsigned counterparts of the above.
- `string Hex(byte[] bytes, Endianness e = Endianness.Machine)` — renders `bytes` as an uppercase hex string (two characters per byte, no separators). Note: `e` is accepted but not used by the implementation.

### `bzTorrent.Helpers.Utils`

Grab-bag of extension methods used throughout the library for bit-level and byte-array manipulation.

**Methods**
- `bool GetBit(this byte t, ushort n)` — returns whether bit `n` (0 = least significant) is set in `t`.
- `byte SetBit(this byte t, ushort n)` — returns a copy of `t` with bit `n` set.
- `byte[] GetBytes(this byte[] bytes, int start, int length = -1)` — returns a sub-array of `length` bytes starting at `start`; if `length` is omitted (`-1`), copies through the end of the array. Returns an empty array if `length == 0`.
- `byte[] Cat(this byte[] first, byte[] second)` — concatenates two byte arrays into a new array.
- `bool Contains<T>(this T[] ar, T o)` — linear search for `o` in `ar` using `Equals`.
- `bool Contains<T>(this T[] ar, Func<T, bool> expr)` — linear search for the first element matching predicate `expr`.

---

## bzTorrent.Extensions

### `bzTorrent.Extensions.ArgumentExtensions`

Small fluent guard-clause helpers for argument validation.

**Methods**
- `void ThrowIfNull<T>(this T source, string paramName)` — throws `ArgumentNullException(paramName)` if `source` is null.
- `void ThrowIfNullOrEmpty<T>(this IEnumerable<T> source, string paramName)` — throws `ArgumentNullException(paramName, "Cannot be null or empty")` if `source` is null or has no elements (uses `EnumerableExtensions.IsNullOrEmpty`).

### `bzTorrent.Extensions.EnumerableExtensions`

**Methods**
- `bool IsNullOrEmpty<T>(this IEnumerable<T> source)` — returns true if `source` is null or contains no elements.
