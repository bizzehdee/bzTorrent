# bzTorrent Maintainability Plan — SOLID & DRY Refactor

> **Status:** Phases 0–11 landed on `master` (behaviour-preserving). The breaking
> changes those phases deferred were then completed on `refactor/solid-dry-4.0`
> (version bumped to 4.0.0) as items **D1–D12** — all committed, build clean, 187
> tests green. Summary of the 4.0 breaking work:
> - D1 removed `PeerMessageBuilder.IDisposable`.
> - D2 `IHashProvider.Hash`: `byte[]` → `InfoHash` value type.
> - D3 promoted `AnnounceInfo`/`ScrapeInfo` to top-level types (`FromHttp`/`FromUdp`).
> - D4 replaced the 11-param `Announce` with an `AnnounceRequest` options object.
> - D5 extracted `TrackerUrlEncoder` from `BaseScraper`.
> - D6 renamed `Metadata.Load(BDict)` → `LoadInfoDictionary`; portable `/` path separator.
> - D7 segregated `IPeerWireClient` → control/sender/events (composite).
> - D8 made `ProcessCommand` fully table-driven.
> - D9 segregated `ISocket` → connectable/datagram/listener (MSE narrowed to `IConnectableSocket`).
> - D10 introduced `IExtensionHost` (DIP) for `IBTExtension.Init`.
> - D11 extracted `DHTQueryResponder` (DHT server role).
> - D12 gave `PeerWireListener` an injectable factory + fixed `StopListening`.
>
> A few sub-parts were deliberately *not* done, with rationale in the commit
> messages: the socket-factory rewrite of `PeerWireConnection<T>` (Phase 3 showed
> it's high-risk for no functional gain), the delegate→`EventArgs` consolidation
> (25+ subscriber rewrites for style), fully merging the two extension-dispatch
> models (they serve different layers), the tracker transport DIP (no clean shared
> abstraction / no fake consumer), a separate Metadata parser type (the disk-builder
> was already extracted), and the DHT transport/PeerSearch extraction (async
> receive-loop restructure, loopback-only coverage).

This document catalogs concrete SOLID and DRY violations in the core `bzTorrent`
library and lays out a prioritized, incremental plan to fix them. Line references
are anchors at time of writing and may drift as work proceeds.

The library already shows the *right* instincts in places — `IPeerConnection`/
`ISocket` are injected into `PeerWireClient`, and the newer
`Protocol/Handlers/MessageDispatcher` is a clean, table-driven design. The goal of
this plan is to bring the rest of the codebase up to that same standard and to
eliminate the large amount of copy-pasted parsing/serialization logic.

## Guiding principles

1. **One codec, many callers.** Big-endian packing, compact peer/node parsing, hex
   conversion, and bencode-dictionary decoding are each currently implemented 2–6
   times. Each should exist exactly once.
2. **Inject collaborators, don't `new` them.** Sockets, clocks, dispatchers,
   routing tables, and encryption handshakes should be passed in behind
   interfaces so the units are testable without real networking.
3. **Split god classes by responsibility** (parse / serialize / transport / state).
4. **Prefer table-driven dispatch over `switch`/`if` chains** (extend the existing
   `MessageDispatcher` model instead of the parallel `IProtocolExtension` model).
5. **No behavior change without tests.** Every extraction is backed by
   characterization tests first (the repo already has a solid test project).
6. **Stop swallowing exceptions.** Blanket `catch { }` / `catch (Exception) { return null; }`
   appears 15+ times and hides real failures; replace with typed handling.

---

## Phase 0 — Foundational shared codecs (do first; unblocks everything else) — ✅ DONE

These are pure, side-effect-free helpers with the highest duplication and the
lowest risk to extract. They are prerequisites for later phases.

**Status:** Implemented. New shared types: `Helpers/Endianness.cs`,
`Helpers/ByteOrder.cs`, `Helpers/HexCodec.cs`, `Helpers/CompactPeer.cs`.
Net −290 lines across the library; 12 new `CompactPeerTests` added; full suite
green (156 tests). Two intentional wire-format corrections fell out of routing
ut_pex through the shared codec (see 0.3). The standalone `InfoHash` value type
(part of 0.4 below) was deferred to Phase 10 — see the note there.

### 0.1 Unify big-endian pack/unpack
- **Problem (DRY):** `PackHelper` and `UnpackHelper` each define their own
  identical `Endianness` enum and `NeedsFlipping` method
  (`PackHelper.cs:38-59`, `UnpackHelper.cs:37-42,116-129`); they have already
  drifted. `UnpackHelper.Int16/Int32/Int64/UInt16/UInt32/UInt64`
  (`UnpackHelper.cs:44-114`) are six copies of one method. Inside `PackHelper`,
  `UInt16/UInt32/UInt64` hand-roll bit-shifting and **ignore** the `Endianness`
  parameter, while `Int16/32/64` delegate to `IPAddress.HostToNetworkOrder`;
  `Float`/`Double` accept an endianness arg they never use (`PackHelper.cs:116-125`).
- **Action:** Introduce a single `bzTorrent.Helpers.Endianness` enum and one
  `ByteOrder` helper (or size-parameterized generic) used by both `PackHelper`
  and `UnpackHelper`. Collapse the six unpack methods and the three pack
  shift-blocks. Make every method honor (or explicitly not expose) the endianness
  parameter consistently.
- **Also fixes:** the second, parallel big-endian implementation in
  `MessageStreamEncryption` (`UInt16BE`/`UInt32BE`/`ReadUInt16BE`/`ReadUInt32BE`,
  `MessageStreamEncryption.cs:403-422`) should be deleted and routed through the
  shared helper.

### 0.2 One hex codec
- **Problem (DRY):** hex string building/parsing exists in `UnpackHelper.Hex`
  (`:130-140`, `str += ...` in a loop), `PackHelper.Hex` (`:128-142`, suspicious
  index math and end-padding for odd lengths), `BaseScraper.UrlEncodeBytes`
  (`:117-120`), and again in `MessageStreamEncryption.HexToBytes` (`:424`).
- **Action:** One `Hex.ToBytes` / `Hex.ToString` pair. Verify odd-length and
  offset handling with tests (current `PackHelper.Hex` padding is asymmetric with
  `UnpackHelper.Hex`).

### 0.3 One compact peer/node codec
- **Problem (DRY):** the compact-peer 6/18-byte stride parse is implemented in
  `HTTPTrackerClient.GetPeers`/`GetPeers6` (`:112-140`),
  `UDPTrackerClient.ParseAnnouncePeers` (`:145-177`, carries the same copy-pasted
  comment), `UTPeerExchange.OnExtendedMessage` (twice, `:76-85` and `:92-100`),
  and `DHTClient.ParseCompactPeers`/`ParseCompactPeers6` (`:558-584`). Compact
  *node* parsing is duplicated in `DHTClient.CompactNodes`/`CompactNodes6`
  (`:500-528`) and `ParseCompactNodes`/`ParseCompactNodes6` (`:530-556`) — six
  methods that collapse to two parameterized by address size (4 vs 16).
  Serialization has already **drifted and diverged** in
  `UTPeerExchange.SendMessage`: the `added` loop writes the port at offset `x*6+4`
  via `BitConverter` (`:119`) while `dropped` writes at `x*6+2` via `SetValue`
  (`:132`) — a latent wire bug.
- **Action:** Create `bzTorrent.Data.CompactAddress` (or `PeerListCodec` /
  `NodeListCodec`) with `ParsePeers(bytes, isV6)` / `WritePeers(...)` and
  `ParseNodes(bytes, isV6)` / `WriteNodes(...)`. Route all six+ call sites through
  it. This is the single highest-leverage DRY fix in the repo.

### 0.4 Shared value types
- **`InfoHash` value type:** the `HashString` get/set property is duplicated
  verbatim between `Metadata.cs:52-56` and `MagnetLink.cs:45-49`. Extract an
  `InfoHash` struct that owns the hex↔bytes conversion.
  **Deferred to Phase 10.** Both `HashString` properties already delegate to the
  now-shared hex codec (via `PackHelper.Hex`/`UnpackHelper.Hex` → `HexCodec`), so
  the conversion logic is no longer duplicated. A full `InfoHash` struct would
  rewire the underlying `string Hash` storage that bencode load/save depends on —
  that belongs with the `Metadata` split in Phase 10, not this low-risk phase.
- **Node-ID length constant:** `20` is hard-coded as the node/info-hash length in
  `DHTNode.CompareDistance:22` and as `new byte[20]` in `DHTClient` (`:44,534,547`).
  Introduce a shared constant.
- **Subarray copy:** `Utils.GetBytes` (`Utils.cs:47-65`, manual loop) and
  `BaseScraper.CopyBytes` (`:134-139`, `Array.Copy`) do the same job — keep one
  (the `Array.Copy`/`Buffer.BlockCopy` version).
- **Reinvented BCL:** `Utils.Contains<T>` (`:77-101`) and `Utils.Cat` (`:67-75`)
  duplicate `Enumerable.Contains`/`Any`/`Concat`; evaluate replacing with LINQ or
  document why the custom versions exist (allocation avoidance) and consolidate.

---

## Phase 1 — Kill the parallel-implementation bugs (correctness-adjacent) — ✅ DONE

These are DRY violations that have already produced (or are one edit away from)
real defects. Fix alongside Phase 0 codecs.

**Status:** Implemented. `FastExtensions.OnCommand` now returns `true` on handled
commands; `PeerMessageBuilder.Add(uint, Endianness)` honors its argument (via a
new `PackHelper.UInt32(uint, Endianness)` overload); `PeerWirePacket.GetBytes`
derives the length prefix and buffer size from one payload value and is null-safe;
`MagnetLink.Resolve` returns a blank link for non-magnet input instead of NREing;
`LocalPeerDiscovery.Close` uses the cooperative `_killSwitch` + bounded `Join`
instead of `Thread.Abort()`; dead discarded-decode/`sendBuffer` code removed. The
ut_pex port-offset item was already fixed in Phase 0 (0.3). 12 regression tests
added; full suite green (168). LPD async-callback-on-closed-socket hardening is
left for Phase 11 as noted there.

- **`FastExtensions.OnCommand` always returns `false`** (`FastExtensions.cs:93`)
  even on handled commands 13–17 — an LSP violation of the `OnCommand`
  "handled?" contract that will misbehave in any dispatcher relying on the return
  value.
- **`UTPeerExchange.SendMessage` divergent port offsets** (`:119` vs `:132`) — fixed
  by the compact-peer codec in 0.3.
- **`PeerMessageBuilder.Add(uint, Endianness)` ignores its `endianness` arg**
  (`PeerMessageBuilder.cs:66-71`) — always big-endian; callers requesting
  little-endian are silently wrong.
- **`PeerWirePacket` derives length two ways** (`PacketByteLength = 4 + CommandLength`
  at `:37` vs `GetBytes` using `1 + Payload.Length` at `:89`); with
  `CommandLength == 1`, `Payload` is left null (`:71`) and `GetBytes` NREs. Single
  source of truth for length.
- **`MagnetLink.Resolve` NRE** on non-magnet input (`values` stays null then
  `foreach` at `:69`).
- **`LocalPeerDiscovery.Close` calls `thread.Abort()`** (`:126`) — throws
  `PlatformNotSupportedException` on modern .NET; the `_killSwitch` cooperative
  path already exists and should be the only mechanism.
- **Dead code to remove:** discarded bencode decode results in
  `LTTrackerExchange.OnHandshake:59` and `UTPeerExchange.OnHandshake:65`; unused
  `sendBuffer` in `UTMetadata.RequestMetaData:129`.

---

## Phase 2 — The UTPSocket god class (biggest structural problem) — ◑ PARTIAL

`IO/UTPSocket.cs` (793 lines) fuses at least six responsibilities and switches
between two fundamentally different object roles (listener vs. data socket, and
parent vs. accepted-child) via mutable boolean flags `isServerChild` /
`isListening` (`:155,159`). This is the single worst SRP/LSP/OCP offender.

**Status — done in this pass (safe, contract-preserving decomposition):**
- Extracted `IO/UTPPacketCodec.cs`: the uTP header parse (was `UTPPacketHeader.Parse`)
  and build (was inlined in `UTPSocket.SendPacket`) now live in one codec;
  `UTPPacketHeader` is a plain data holder. Removes the wire-format responsibility
  from the socket and is unit-tested (`UTPPacketCodecTests`, 6 cases).
- Removed the dead `static readonly byte[] socketBuffer` (latent cross-instance
  shared state; it was never read).
- Replaced the `20` header-size literals with `UTPPacketCodec.HeaderSize`.
- Validated end-to-end: the real two-`UTPSocket` loopback tests
  (`UTPSocketPassiveOpenTests`, `UTPSocketReceiveTests`) still pass.

**Status — deferred to Phase 3 (the role-type split is coupled to the ISocket
rework):** The headline split of listener / data-socket / accepted-child into
distinct types cannot be done in isolation. `PeerWireConnection<T> where T :
ISocket, new()` uses a *single* `T` for `Connect` (client), `Listen`/`Accept`
(server), and re-wraps `socket.Accept()` as `new PeerWireConnection<T>(...)`
(`PeerWireConnection.cs:111,140,147`), and the Demo instantiates it as
`PeerWireConnection<UTPSocket>`. So `UTPSocket` must remain one type that serves
all three roles until the `ISocket` fat interface is segregated and
`PeerWireConnection`/`PeerWireListener` take a socket **factory** instead of
`new T()` — exactly the Phase 3 work below. The type split is therefore folded
into Phase 3. DIP-injecting the reliability collaborators
(`UTPCongestionControl`/`UTPRetransmissionTracker`/`UTPReorderBuffer`) was judged
low-value for now: they are concrete classes already covered by their own tests,
so injecting them without extracting interfaces buys little; revisit if/when the
type split lands.

- **Split the roles into distinct types:**
  - `UTPListener` — the passive-open/demux path (`Listen:258`,
    `BeginDemuxReceive:438`, `DispatchIncoming:482`, `RemoveChild:427`,
    `activeChildren`/`pendingAccepts`).
  - `UTPConnection` — the per-connection data socket / state machine
    (`Connect:223`, `SenduTPData:335`, `ProcessIncomingPacket:639`, handshake at
    `:665-678`).
  - `UTPPacketCodec` — header parse/build (`UTPPacketHeader.Parse:52`,
    `SendPacket` header building `:307-323`).
- **Inject collaborators (DIP):** stop `new`-ing `UTPCongestionControl` (`:125`),
  `UTPRetransmissionTracker` (`:135`), `UTPReorderBuffer` (`:136`), `Timer`
  (`:220,232`), `Random` (`:112`). Pass them in (also enables unit testing the
  congestion/reliability logic in isolation — the supporting classes already have
  their own tests).
- **Remove `static readonly byte[] socketBuffer`** (`:114`) — shared mutable state
  across all instances; a latent data race. Use per-instance/per-call buffers
  consistently (the demux path already allocates its own at `:445`).
- **De-duplicate internal patterns:** the sync-pump `ManualResetEventSlim` +
  `BeginX` + `Wait()` idiom in `Accept:287-300` and `Receive:757-788`; the
  waiting-receive delivery block (`HandleIncomingPacket:551-566` vs
  `BeginReceive:596-603`); the receive-teardown `catch (ObjectDisposedException) /
  catch (SocketException)` in `BeginDemuxReceive:459-467` and `BeginReceive:612-624`.

---

## Phase 3 — Socket abstraction hygiene (ISP / LSP) — ◑ PARTIAL

**Status:**
- ✅ **3.0 LSP + Accept/EndAccept DRY** (commit): `BaseSocket.Accept`/`EndAccept`
  were `abstract`, forcing the datagram `UDPSocket` to implement passive-open by
  calling `_socket.Accept()` on a UDP socket and swallowing the throw as null.
  They're now `virtual`, throwing `NotSupportedException` by default; `UDPSocket`
  drops its bogus overrides (which also removes the TCP/UDP `Accept` duplication).
- ✅ **UTPSocket role split** (was the deferred Phase 2 headline): the 758-line
  god class is split into `UTPConnection` (data socket) and `UTPListener` (passive
  open), with `PacketType` promoted to top-level `UTPPacketType`. `UTPSocket.cs`
  deleted. Validated against the real two-instance loopback e2e tests.
- ❌ **3.1 ISocket interface-segregation + 3.2 socket factory — NOT done, by
  decision.** Having read the whole socket layer: `MessageStreamEncryption` takes
  `ISocket` in six signatures, and `PeerWireConnection<T : ISocket, new()>` /
  `LocalPeerDiscovery<T>` are deeply coupled to the fat interface (they use
  cross-cutting subsets — connect+listen, datagram+connect+options — that don't
  map onto clean single interfaces). Segregating would be a wide, high-risk
  rewrite of the generic socket-consumption model for mostly-structural gain, and
  the concrete LSP defect it targeted is already fixed by 3.0. The `new T()`
  blocker that motivated a factory turned out not to bite: the UTP split works
  because `UTPConnection`/`UTPListener` are distinct types used in their
  respective roles, and nothing drives UTP through `PeerWireConnection`'s listener
  path. Revisit only if a future feature needs the narrow interfaces.
- **3.2 (original) "remove SetSocketOption from the interface" is infeasible** as
  written: `LocalPeerDiscovery` actively uses all three overloads (ReuseAddress,
  MulticastTimeToLive, AddMembership). Left in place.

### Original 3.1–3.4 detail (for reference)

- **Split the fat `ISocket` interface** (`IO/ISocket.cs:37-70`) into capability
  interfaces: `IConnectableSocket` (Connect/Send/Receive/Begin/EndReceive),
  `IDatagramSocket` (SendTo/Begin/EndReceiveFrom), `IListenerSocket`
  (Listen/Accept/Begin/EndAccept). `TCPSocket` never needs `SendTo`; a data socket
  never needs `Accept`.
- **Remove `SetSocketOption` overloads from the interface** (`:66-68`) — they leak
  raw `System.Net.Sockets` enums into the abstraction and are essentially unused
  by consumers.
- **Fix the `BaseSocket`/`UDPSocket` LSP violation:** `BaseSocket` declares
  `Accept`/`EndAccept` abstract (`:52,123`), forcing `UDPSocket` to implement
  server semantics it can't honor — `UDPSocket.Accept` calls `_socket.Accept()` on
  a UDP socket (`:68-90`) and hides the resulting throw behind
  `catch (Exception) { return null; }`. After splitting the interface, UDP no
  longer implements the listener contract.
- **De-duplicate `Accept`/`EndAccept`:** `TCPSocket` (`:50-72`) and `UDPSocket`
  (`:68-90`) have byte-for-byte-identical try/catch bodies differing only in the
  constructed type. Also stop the swallow-all `catch (Exception) { return null; }`.

---

## Phase 4 — PeerWireConnection (SRP / DIP / DRY) — ◑ MOSTLY DONE

**Status — done:**
- Extracted `Data/PeerHandshakeCodec.cs`: the fixed BitTorrent handshake wire
  layout (build + parse + the `"BitTorrent protocol"` constant) now lives in one
  place, used by both `Handshake()` (send) and `ReceiveCallback` (receive). This
  removed the magic offsets (`+49/+8/+20/+28`), the literal protocol string, and
  the inline field slicing from the ~135-line `ReceiveCallback`. Unit-tested
  (`PeerHandshakeCodecTests`, 6 cases).
- De-duplicated socket setup into `ConfigureSocket()` (was repeated in the ctor,
  `Connect`, and `ReconnectSocket`).
- De-duplicated the MSE reset block into `ResetEncryptionState()` (was in
  `Connect` and `Disconnect`).

**Status — deferred, by decision:**
- **Socket factory (DIP):** not done — consistent with the Phase 3 decision to
  keep the `new T()` generic model (the factory solved a problem the codebase
  doesn't have in practice).
- **`IEncryptionHandshake` abstraction (DIP):** deferred to Phase 5, where
  `MessageStreamEncryption` itself is split — introducing the interface is
  natural there rather than bolting it onto the current static class.
- **Blanket `catch (Exception)` in `ReceiveCallback`:** left as-is. It runs on an
  async socket callback where an uncaught exception would crash the process, and
  the library has no logging seam to route a swallowed error to; narrowing it is
  a change better made alongside a logging abstraction. The duplicated blocking
  "read exactly N bytes" loop in `BeginOutgoingEncryption` is likewise left for
  Phase 5 (it belongs with MSE's `ReadExact`).

### Original detail (for reference)

`IO/PeerWireConnection.cs` (473 lines), especially the ~135-line
`ReceiveCallback:293-429` god-method.

- **Separate responsibilities:** connection lifecycle, MSE encryption
  orchestration, BitTorrent wire-handshake parsing, and packet framing
  (`ParsePackets:431`/`ParsePacket:451`) into collaborating types.
- **Inject the socket factory & encryption handshake (DIP):** stop `new T()`
  (`:111,246`); accept a `Func<ISocket>`/factory. Replace the direct static calls
  to `MessageStreamEncryption.CompleteOutgoing`/`CompleteIncoming` (`:284,349`)
  with an injected `IEncryptionHandshake` abstraction.
- **De-duplicate socket setup:** the `NoDelay`/`ReceiveTimeout`/`SendTimeout`
  block is repeated in the ctor (`:102-105`), `Connect` (`:112-114`),
  `ReconnectSocket` (`:247-249`), and the `Timeout` setter (`:64-79`). One
  `ConfigureSocket(socket)` method.
- **De-duplicate the encryption-state reset block** (`Connect:116-121` vs
  `Disconnect:131-135`).
- **Remove the third "read exactly N bytes" loop** in
  `BeginOutgoingEncryption:271-282` — reuse the shared `ReadExact` (see 5.2).
- **Extract protocol magic:** the `0x13`/`19` first-byte check (`:335,384`), the
  literal `"BitTorrent protocol"` string, and offset math `protocolStrLen + 49`
  / `+ 28` (`:370,380`) belong in named constants / a handshake parser. Replace the
  blanket `catch (Exception) { }` (`:421-424`).

---

## Phase 5 — MessageStreamEncryption (SRP / DRY) — ✅ DONE

**Status:** The 534-line `MessageStreamEncryption` god class is split into:
- `IO/MseCrypto.cs` — pure crypto/encoding utilities (Hash, Xor, BytesEqual,
  Ascii, Concat, big-endian BigInteger conversion, RandomBytes/RandomInt).
- `IO/MseFraming.cs` — socket read framing: `ReadExact`, the collapsed
  `ScanForNeedle` (the two near-identical needle scanners are now one method
  parameterized by a `Func<byte>` byte source), and the `StreamBuffer` FIFO.
- `IO/MseHandshake.cs` (+ `IO/IMseHandshake.cs`) — the handshake state machine
  (`CompleteOutgoing`/`CompleteIncoming`/`CreateLocalPublicKey`/`CreatePadding`),
  DH constants and the MSE prime.

Also done:
- Deleted the parallel big-endian/hex helpers (`UInt16BE`/`UInt32BE`/
  `ReadUInt16BE`/`ReadUInt32BE`/`HexToBytes`) — now routed through the Phase-0
  `PackHelper`/`UnpackHelper`/`HexCodec`.
- De-duplicated the shared-secret expression into `ComputeSharedSecret`, and
  collapsed the `Hash` accumulate-loop into `sha1.ComputeHash(Concat(parts))`.
- **`ReadExact` is now the one blocking-read helper** (`MseFraming.ReadExact`);
  `PeerWireConnection.BeginOutgoingEncryption`'s duplicate 96-byte read loop was
  replaced with it (resolving a Phase 4 deferral).
- **`IMseHandshake` DIP abstraction** (resolving the other Phase 4 deferral):
  `PeerWireConnection` now depends on `IMseHandshake` (default `new MseHandshake()`
  field) instead of the concrete static class.

Behaviour-preserving — validated by the MSE/PE handshake tests (RC4 + plaintext
negotiation, both halves, over TCP loopback). Full suite green (182).

`IO/MessageStreamEncryption.cs` (534 lines) mixes the MSE handshake state machine,
a general crypto/encoding toolbox, and socket-read framing.

- **Split into three types:** `MseHandshake` (the protocol state machine —
  `CompleteOutgoing:64`, `CompleteIncoming:131`), a crypto utility (`Hash`, `Xor`,
  `RandomBytes`, big-integer key math), and stream framing (`ReadExact`, the
  needle scanners, the `StreamBuffer` nested class).
- **Collapse the standout duplication:** `ScanRawForNeedle:262-291` and
  `ScanBufferForNeedle:295-324` are near-identical sliding-window scanners
  differing only in byte source — parameterize by a `Func<byte>`/byte-source
  delegate.
- **Delete the parallel big-endian and hex helpers** (`:403-422`, `:424`) — route
  through the Phase-0 shared codecs.
- **De-duplicate the shared-secret expression**
  `ToBigEndian(BigInteger.ModPow(...))` (identical at `:75` and `:152`) and the
  `total += p.Length` + `BlockCopy` loop in `Hash:332-346` and `Concat:386-400`.
- **`ReadExact` (`:242-258`)** becomes the one blocking-read helper used by
  `PeerWireConnection` too (see 4).

---

## Phase 6 — PeerWireClient & IPeerWireClient (SRP / ISP / OCP / DRY) — ◑ PARTIAL

**Status — done:**
- **Collapsed the nine `Send*` methods** into one `SendCommand(PeerClientCommands,
  Action<PeerMessageBuilder>)` helper. The message id now comes from the
  `PeerClientCommands` enum instead of the raw literals (`0`–`8`, `128`) that were
  a second, drift-prone encoding parallel to the receive-side switch.
- **`SendBitField(bool[])` now returns `bool`** (was `void`), matching its
  `(bool[], bool)` overload; interface updated to match.

**Status — deferred, by decision:**
- **Split the fat `IPeerWireClient` interface (ISP) + consolidate the 12
  delegates into generic `EventArgs`:** not done. Like the `ISocket` segregation
  (Phase 3), this is a wide, source-breaking change to a public API — it ripples
  through every event subscriber (Demo, tests), the protocol-extension surface
  (`IProtocolExtension.OnCommand(IPeerWireClient, …)`), and `PeerWireListener` —
  for a mostly-structural gain. Revisit as a deliberate major-version API change.
- **Make `ProcessCommand` fully table-driven (OCP):** left as the small
  enum-based `switch`. The five no-payload base commands (choke/unchoke/…) just
  raise events; routing them through `MessageDispatcher` would add a handler
  class each for negligible OCP benefit on a stable, rarely-changed set. The
  data-carrying messages already go through the dispatcher.
- **`IPAddress.Parse` in `Connect(string, int)`:** left in place — parsing an IP
  literal in a convenience overload is reasonable and not worth an abstraction.

- **De-duplicate the `Send*` methods** (`PeerWireClient.cs:297-420`): nine methods
  are `_peerConnection.Send(new PeerMessageBuilder(N)...Message()); return true;`
  differing only by a hard-coded message ID and payload. Collapse to one helper
  driven by the `PeerClientCommands` enum.
- **Replace magic message IDs with the existing `PeerClientCommands` enum.** The
  send path uses raw literals (`0`–`8`, and `128` for keep-alive at `:299`) while
  the receive path already matches `PeerClientCommands.*` (`:258-272`) — two
  encodings maintained in different places.
- **Make `ProcessCommand` (`:249-295`) fully table-driven** via `MessageDispatcher`
  instead of the hard-coded `switch` (OCP). It currently mixes three dispatch
  mechanisms (switch + dispatcher + extension loop).
- **Fix the inconsistent `SendBitField` overloads** — one returns `void` (`:339`),
  the other `bool` (`:344`).
- **Split the fat `IPeerWireClient` interface** (`IPeerWireClient.cs`): 14 events,
  12 bespoke delegate types, 10 `Send*` methods, connect/disconnect, and extension
  registration in one interface. Segregate into `IPeerConnectionControl`,
  `IPeerMessageSender`, and `IPeerEvents`. Consider consolidating the 12
  near-identical delegates into a small set of generic `EventArgs` types.
- **DIP:** inject `MessageDispatcher` (`:84`); move `IPAddress.Parse` out of
  `Connect(string,int)` (`:125-129`).

---

## Phase 7 — DHTClient god class (SRP / DIP / DRY) — ◑ PARTIAL

**Status — done:**
- Extracted `DHT/DHTTransactionRegistry.cs` (SRP): transaction-id allocation and
  the pending-request correlation table (`_txCounter` + `_pending` + `NextTxId`)
  now live in one focused type; `DHTClient` delegates via `Register`/`Abandon`/
  `Cancel`/`Complete`.
- De-duplicated the query envelope into `BuildQuery(txId, name, args)` (was
  copy-pasted in `FindNodeAsync` and `GetPeersQueryAsync`).
- De-duplicated node-list ingestion into `IngestNodes(r)` (was copy-pasted in
  `FindNodeAsync` and `QueryNodeForPeersAsync`, with divergent guarded vs
  unguarded casts — now uniformly guarded).
- (`DHTRoutingTable.IdsEqual` was already length-agnostic; the node-id `20`
  literals were removed back in Phase 0.)

Validated by the real two-instance loopback DHT round-trip tests (which exercise
send/receive, query+response, tx-id correlation and node ingestion). Suite green (182).

**Status — deferred, by decision:**
- The deeper split (a `PeerSearch` for the Kademlia iterative loop, a transport
  wrapper around `UdpClient`, a separate inbound server-query handler) and
  **DIP-injecting the transport/RNG**: deferred. This is async UDP networking
  whose automated coverage is the loopback round-trip only; a large split here
  carries real regression risk (peer-discovery breakage is exactly the class of
  bug that's hard to catch without live-network testing), for structural gain.
  The high-duplication codec was already extracted in Phase 0, and the two
  worst remaining DRY sites plus the transaction-correlation SRP concern are now
  addressed.
- The **guarded bencode accessor** (dozens of unchecked `((BString)v).ByteValue`
  casts) is deferred to Phase 8, where it's introduced as a shared helper for the
  extension work and retrofitted to DHT there (avoids double-churn).

`DHT/DHTClient.cs` (595 lines) holds six responsibilities. Beyond the compact
codec extraction (0.3):

- **Split into collaborators:** `DHTMessageCodec` (query construction +
  compact (de)serialization), `TransactionRegistry` (`NextTxId:471`, `_pending`
  correlation), a `DHTSocket`/transport wrapper, and `PeerSearch` (the Kademlia
  iterative loop `SearchLoopAsync:138`, `GetPeersIterativeAsync:162`). Inbound
  server handling (`HandlePacket:383`, `HandleQuery:409`) is a further split.
- **De-duplicate node-list ingestion** (`QueryNodeForPeersAsync:225-241` vs
  `FindNodeAsync:278-288`) and the `t/y/q/a{...}` query-dict skeleton
  (`FindNodeAsync:252-264` vs `GetPeersQueryAsync:296-308`).
- **Add a guarded bencode accessor** to replace the dozens of unchecked
  `((BString)val).ByteValue` / `(BDict)val` casts after `TryGetValue` — shared with
  the extension work in Phase 8 (see 8.3).
- **Inject collaborators (DIP):** `new UdpClient` (`:55`),
  `RandomNumberGenerator.Create()` (`:45`), `new DHTRoutingTable()` (`:26`) — none
  injected, so the class needs real UDP to test, unlike the rest of the library.
- **Replace the blanket `catch { }`** at `:243,290,379,406,468`.
- **`DHTRoutingTable.AddOrUpdate`** (`:18-36`): O(n) scan + `OrderBy` eviction under
  one lock — revisit data structure; `IdsEqual` (`:54-60`) uses the shared node-ID
  equality/constant from 0.4.

---

## Phase 8 — Unify the two dispatch models (extensions) (OCP / DIP / ISP) — ◑ PARTIAL

**Status — done:**
- Added the shared guarded bencode accessor `Data/BencodeExtensions.cs`
  (`TryGetBytes`/`TryGetString`/`TryGetDict`/`TryGetList`) — the item deferred
  from Phase 7. Applied across `DHTClient` (`HandlePacket`, `HandleQuery`,
  `QueryNodeForPeersAsync`, `FindNodeAsync`), replacing ~14 `TryGetValue` +
  unchecked-cast pairs with one guarded call each (well covered by the loopback
  round-trip tests).
- Collapsed `FastExtensions.ProcessSuggest`/`ProcessAllowFast` (byte-for-byte
  identical apart from the event) into one `ProcessSingleIndex(payload, raise)`;
  removed the now-redundant `OnSuggest`/`OnAllowFast`.

**Status — deferred, by decision:**
- **8.1 Unify the two dispatch models** (migrate `IProtocolExtension`/`IBTExtension`
  onto `IMessageHandler`/`MessageDispatcher`) and **8.2 `IExtensionHost`**: both
  are **source-breaking changes to the public extension API** (external code
  implements `IProtocolExtension`/`IBTExtension`), with high churn across the
  extension surface and comparatively thin test coverage (only `FastExtensions`
  and `UTHolepunch` have tests). Same call as the other ISP/interface splits —
  a deliberate major-version change, not a safe in-place refactor.
- **Splitting `ExtendedProtocolExtensions`/`UTMetadata` and unifying `UTMetadata`'s
  hand-rolled send**: deferred with the above (touches the same
  lightly-tested extension internals).
- Applying `BencodeExtensions` across the extensions (`UTPeerExchange`,
  `UTMetadata`, `LTTrackerExchange`, `ExtendedProtocolExtensions`): left for when
  those files are next touched — the helper now exists for incremental adoption;
  applied only to the well-tested DHT paths for now to limit risk.

The library has **two competing dispatch designs**: the clean table-driven
`IMessageHandler`/`MessageDispatcher` and the legacy `switch`/`if`-based
`IProtocolExtension`/`IBTExtension`. Converge on the former.

### 8.1 Migrate extension dispatch to the handler model
- `FastExtensions.OnCommand` (`:72-94`) and `ExtendedProtocolExtensions.OnCommand`
  (`:102-112`) and `DHTPortExtension.OnCommand` (`:59-71`) hard-code command-ID
  checks. Express Fast (13–17), DHTPort (9), and extended (20) messages as
  `IMessageHandler`s registered with the dispatcher so new messages need no edits
  to existing classes.
- Collapse `FastExtensions.ProcessSuggest`/`ProcessAllowFast` (`:96-108`,
  byte-for-byte identical apart from the event fired).

### 8.2 Introduce `IExtensionHost` (DIP + ISP)
- `IBTExtension.Init(ExtendedProtocolExtensions parent)` (`IBTExtension.cs:36`)
  makes the abstraction depend on a concretion; every extension stores a concrete
  `_parent` and calls `_parent.SendExtended` / `_parent.GetIncomingMessageID`.
  Define an `IExtensionHost` (or `IExtendedMessageSender`) interface and depend on
  that so extensions are testable/reusable.
- **Fix `IBTExtension` ISP:** it forces empty `Init`/`Deinit`/`OnHandshake` no-ops
  on several extensions (`LTTrackerExchange:47,52`, `UTHolepunchExtension:90,94`,
  `UTPeerExchange:58`) while real capabilities are *missing* from the interface
  and thus unreachable polymorphically (`LTTrackerExchange.GetAdditionalHandshake:78`,
  `UTMetadata.RequestMetaData:108`). Rebalance the interface (default methods /
  smaller interfaces / capability interfaces).

### 8.3 Shared bencode helpers
- Replace the repeated `var dict = (BDict)BencodingUtils.Decode(...)` boilerplate
  (`UTMetadata:69,85,103`, `UTPeerExchange:65,70`, `LTTrackerExchange:59,64`,
  `ExtendedProtocolExtensions.ProcessExtended:192`) with a guarded
  `TryDecodeDictionary` helper (shared with DHT, see Phase 7).
- **Unify the outbound "send extended message" path:** `UTPeerExchange.SendMessage:138`
  and `UTHolepunchExtension.Send:167` use `SendExtended`, but
  `UTMetadata.RequestMetaData:126-138` hand-rolls a third inconsistent
  implementation (manual `GetIncomingMessageID` prepend + hand-built
  `PeerWirePacket`). Route all through one `SendExtended`.
- **Split `ExtendedProtocolExtensions`** (`:38-213`) — it is a registry + a
  bidirectional protocol-ID map + handshake bencode builder + packet sender +
  inbound router. Separate the ID-map and the handshake-dictionary construction
  from the routing.
- **Split `UTMetadata`** (`:38-141`) — it mixes wire parsing with stateful
  metadata reassembly (`_metadataBuffer`, `_piecesWritten`, `_piecesReceived`).
- **Shared guard helper:** the "require N payload bytes then return/close" shape is
  duplicated with per-file magic numbers (`HaveHandler:43` `<4`,
  `PieceHandler:43` `<8`, `RequestHandler:50` `<12`, `BitfieldHandler:43`,
  `UTHolepunchExtension:100,109`). Add a `RequirePayload(payload, n)` helper.
- **Read-block-header helper:** the `{index, begin, length}` big-endian triple is
  re-read in `FastExtensions.ProcessReject:110-114`, `RequestHandler:56-58`,
  `PieceHandler:49-50`, and the single-int variants in `HaveHandler:48`,
  `FastExtensions.ProcessSuggest/AllowFast`. One `BlockHeader.Read(payload)` helper.

### 8.4 MessageDispatcher DIP polish
- `MessageDispatcher` ctor news up concrete handlers (`:47-51`) and `Dispatch`
  depends on concrete `PeerWireClient` (`:75,89`) rather than `IPeerWireClient`.
  Accept handlers via injection/config and depend on the interface. (Lower
  priority — this is already the good design; just tighten the seams.)

---

## Phase 9 — Tracker layer (SRP / OCP / DIP / DRY) — ◑ PARTIAL

**Status — done:**
- Moved the duplicated convenience `Announce(url, hash, peerId)` overload (with
  the magic `0,0,0,2,0,-1,12345,0` defaults, now named constants) and the
  multi-hash `Announce(url, string[], peerId)` fan-out into `BaseScraper`. Both
  were copy-pasted in `HTTPTrackerClient` and `UDPTrackerClient`; the full 11-arg
  `Announce` is now `abstract` on `BaseScraper` and `override` in each client.
  `UDPTrackerClient` keeps a thin multi-hash override (it validates the whole hash
  set up front, then calls `base`). Signatures preserved → non-breaking.
  (Compact-peer parsing was already de-duplicated in Phase 0.)

**Status — deferred, by decision:**
- **Promote `AnnounceInfo`/`ScrapeInfo` to top-level types** (the DIP-inversion
  fix): source-breaking — they're referenced as `BaseScraper.AnnounceInfo` by
  `ITrackerClient` and by the Demo (`Demo/Program.cs:153`). Major-version change.
- **Narrow the 11-parameter `Announce` to an options object** (ISP/LSP — HTTP
  ignores several params): source-breaking public-API change. Deferred with the
  DTO promotion.
- **`BaseScraper` SRP split** (regex validation / URL-encoding helpers / DTOs into
  separate types) and **transport DIP** (`new UdpClient`, `WebRequest.Create`):
  moderate churn touching the public base type / concrete transports; deferred.
- The fragile `url.Replace("scrape","announce")` endpoint rewriting is noted but
  left (behavioural change risk for marginal gain).

- **Move shared logic into `BaseScraper`** (or dedicated collaborators):
  - Identical default `Announce` overload with the magic arg list
    `0,0,0,2,0,-1,12345,0` (`HTTPTrackerClient:215-218` == `UDPTrackerClient:220-223`).
  - Identical multi-hash fan-out `hashes.ToDictionary(...)`
    (`HTTPTrackerClient:210-213` == `UDPTrackerClient:278-282`).
  - Compact-peer parsing (see 0.3).
  - The repeated `try { ... } catch (Exception) { return null }` policy (HTTP
    `:227-249,261-284`; UDP `:185-217,233-275`) — replace with typed handling.
  - Fragile `url.Replace("scrape","announce")` / vice-versa
    (`HTTPTrackerClient:64,254`).
- **Promote `AnnounceInfo`/`ScrapeInfo` to top-level types owned by
  `ITrackerClient`** (`BaseScraper.cs:141-181`). Today the interface returns
  nested types of a concrete abstract base (`ITrackerClient.cs:40-44`), forcing
  every implementer to inherit `BaseScraper` (DIP inversion). Replace the
  `ScraperType`-switched `ScrapeInfo` ctor and positional `a,b,c` params
  (`:148,166-180`) with distinct types or a factory (it currently leaves half its
  fields silently zero depending on type — a latent bug).
- **`BaseScraper` SRP:** separate the regex validation, URL-encoding helpers, the
  connection-ID constant, and the DTOs into distinct types. `ValidateInput`
  (`:69-112`) both validates *and* mutates `Tracker`/`Port` as a side effect, and
  its error messages say "when calling scrape" even from `Announce`.
- **`ITrackerClient` ISP/LSP:** the 11-parameter `Announce` (`:41-42`) is a fat
  contract that `HTTPTrackerClient.BuildAnnounceUrl` (`:61-78`) only partially
  honors (ignores `eventTypeFilter`, `ipAddress`, `extensions`, `key`; hardcodes
  `event=started`, `compact=1`) — so HTTP and UDP are not substitutable. Narrow to
  an options object and honor it consistently.
- **DIP (transport):** `new UdpClient` (`UDPTrackerClient:62`), `WebRequest.Create`
  (`HTTPTrackerClient:229,263`; `HTTPWebSeedClient:112`) — inject a transport
  abstraction so tracker/webseed clients are unit-testable.

---

## Phase 10 — Metadata god class (SRP) — ◑ PARTIAL

**Status — done:**
- Extracted the disk-based torrent creation into `Data/TorrentBuilder.cs`
  (file enumeration, piece hashing, info/announce dict assembly, piece-size
  heuristic). `Metadata.CreateFromPath` is now a thin facade that calls
  `TorrentBuilder.Build(...)` then `LoadRoot` — non-breaking (public API
  preserved), and it removes ~130 lines of the "build a torrent from disk"
  concern from the parse/hold/serialise class. Covered by `MetadataCreationTests`.
- Fixed the `CreationDate` epoch read: `new DateTime(1970,1,1).AddSeconds(ts)`
  (Kind=Unspecified) -> `DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime`
  (same tick value, now correctly UTC; matches the Unix-time write side).
- De-duplicated the multi-file path separator: `MetadataFileInfo.PathSeparator`
  is now the single source for the join (`Metadata`) and the split
  (`HTTPWebSeedClient`), so they can't drift.

**Status — deferred, by decision:**
- Splitting the remaining **parser** and **serializer** responsibilities into
  separate public types, and **renaming the `Load(BDict)` overload** (which really
  means "load the info dict"): source-breaking changes to the public `Metadata` /
  `IMetadata` API (the Demo and tests use `Load`/`Save`/`FromFile`/properties
  directly). Major-version change.
- The standalone **`InfoHash` value type** (deferred here from Phase 0.4): the
  `HashString` get/set already routes through the shared `HexCodec`, so the
  duplication is gone; a dedicated value type would rewire the `string Hash`
  storage the bencode load/save depends on — bundle it with the parser split.
- Making the path separator **portable** (rather than the current Windows-style
  `'\\'`) is a wire-behaviour change for existing torrents/tests; left as-is (now
  at least single-sourced).

`Data/Metadata.cs` (523 lines) conflates parsing, disk-based creation, and
serialization.

- **Split into three types:** a **parser** (`Load` overloads, `:96-292`), a
  **disk-based builder** (`CreateFromPath:363-471`, `HashPieces:485-520`,
  `RecommendedPieceSize:473-483` — all file I/O), and a **serializer**
  (`Save`/`SaveToFile:294-310`).
- **Clarify the overload set (ISP):** `Load(MagnetLink)`, `Load(Stream)`,
  `Load(BDict)` (`:211`) — the `BDict` overload actually means "load the *info*
  dictionary" and exposes an internal step publicly. Rename to explicit methods.
- **Fix the Windows-specific path separator contract:** filenames joined with
  `'\\'` (`:245`) and split on `'\\'` in `HTTPWebSeedClient.BuildFileUrl:105` —
  duplicated, platform-specific convention; use a shared join/split helper and a
  portable separator.
- **Unify epoch handling:** read via `new DateTime(1970,1,1).AddSeconds(...)`
  (`:183`) but written via `DateTimeOffset...ToUnixTimeSeconds()` (`:466`).
- Use the shared `InfoHash` type from 0.4.

---

## Phase 11 — Misc client cleanups — ◑ MOSTLY DONE

**Status — done:**
- **`LocalPeerDiscovery`**: extracted the inline BT-SEARCH parse into a pure,
  testable `TryParseSearchPacket` (now unit-tested, 5 cases) that guards the
  infohash length — the old inline `Substring(10, 40)` threw on a short/malformed
  line. The receive callback now decodes only the `length` bytes actually
  received (was decoding the whole 16 KB buffer). De-duplicated the reader/sender
  socket reuse-address setup into `EnableAddressReuse`. (`Thread.Abort` was already
  fixed in Phase 1.) Added `InternalsVisibleTo(bzTorrent.Tests)` to enable the test.
- **`PeerMessageBuilder`**: named the `5`/`1` header-size magic numbers
  (`LengthPrefixSize` + `MessageIdSize`).
- **`PeerWireListener`**: the reflection ctor lookup now throws a clear
  `InvalidOperationException` instead of a bare `NullReferenceException` when the
  connection type lacks an `ISocket` constructor.

**Status — deferred, by decision:**
- Replacing `PeerWireListener`'s reflection-based construction with an injected
  `Func<ISocket, IPeerConnection>` factory, and fixing `StopListening` (it calls
  `EndAccept` rather than stopping the socket): both are public-API/behavioural
  changes to a working, test-covered listener; deferred.
- Removing `PeerMessageBuilder`'s `IDisposable`: it's a public interface on the
  type — source-breaking for any `using` consumer. Left in place.
- The fuller `LocalPeerDiscovery` SRP split (separating thread management from
  socket setup / parsing / announce formatting) and its busy-spin receive loop:
  deferred — LPD has no automated coverage beyond the new parser test, so a
  larger restructure carries regression risk for structural gain.

- **`PeerWireListener.Callback`** (`:83-101`) constructs the connection via
  reflection (`typeof(T).GetConstructor(...)` then `Invoke`), which NREs if the
  ctor is missing, and hard-codes `new PeerWireClient(connection)` (`:98`). Replace
  with an injected `Func<ISocket, IPeerConnection>` factory and an
  `IPeerWireClient` factory. Also review `StopListening` (`:75-81`) which calls
  `EndAccept` rather than stopping the socket, and the stale `asyncResult` after
  re-arming `BeginAccept` (`:100`).
- **`LocalPeerDiscovery` SRP + DRY:** split socket setup, thread management,
  BT-SEARCH parsing, and announce formatting. `SetupReaderSocket:89-103` and
  `SetupSenderSocket:105-121` duplicate the reuse-address/membership setup. Extract
  a testable `TryParseSearchPacket` from the inline parse in `Process:158-183`
  (magic `Substring(10,40)` will throw on malformed packets). Fix the buffer/
  `isReceiving` concurrency issue and read only `length` bytes (`:158`).
- **`PeerMessageBuilder`:** reconsider `IDisposable` (`:92-95` only clears a managed
  `List<byte>`); name the `5`/`1` header-size magic numbers (`:40-41`).

---

## Suggested sequencing & rationale

1. **Phase 0 + Phase 1** together — shared codecs plus the bugs those codecs fix.
   Low risk, high payoff, unblocks everything downstream.
2. **Phase 2** (UTPSocket) — the largest structural problem; do it while the codec
   work is fresh since it consumes the pack/unpack helpers.
3. **Phases 3–6** (socket abstraction, PeerWireConnection, MSE, PeerWireClient) —
   the connection stack, in dependency order.
4. **Phase 7 (DHT) and Phase 8 (extensions)** — can run in parallel with 3–6; both
   depend only on Phase 0 codecs.
5. **Phases 9–11** (tracker, metadata, misc) — mostly independent; schedule by
   available time.

## Definition of done for each phase

- [ ] Characterization tests written **before** the refactor (capture current
      behavior, including the quirks).
- [ ] Duplicated logic reduced to a single implementation with call sites updated.
- [ ] New/changed collaborators injected behind interfaces (no `new` on
      cross-cutting dependencies).
- [ ] Blanket `catch`/`catch (Exception) { return null }` replaced with typed
      handling in touched files.
- [ ] Public API changes documented in `README.md` and the package version bumped
      (currently `3.0.0`) per semver.
- [ ] All existing tests + new tests green on every target framework.
