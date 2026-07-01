# bzTorrent

A .NET Standard 2.0 library for building BitTorrent clients and tools. Handles the protocol plumbing — tracker communication, peer wire connections, metadata parsing, peer discovery, and DHT — so you can focus on what your application does with torrents rather than the protocol details.

## Installation

```
dotnet add package bzTorrent
```

Compatible with .NET Standard 2.0 and above (.NET 5/6/7/8/9, .NET Framework 4.6.1+, Xamarin, Unity, etc.).

## Quick start

```csharp
using bzTorrent;
using bzTorrent.Data;
using bzTorrent.IO;

// Load a .torrent file
var metadata = Metadata.FromFile("ubuntu.torrent");

// Or parse a magnet link
var metadata = MagnetLink.ResolveToMetadata("magnet:?xt=urn:btih:...");

Console.WriteLine(metadata.Name);        // "ubuntu-24.04-desktop-amd64.iso"
Console.WriteLine(metadata.HashString);  // "DAFC8C076CA2F3ED..."
Console.WriteLine(metadata.PieceHashes.Count); // number of pieces
Console.WriteLine(metadata.PieceSize);   // bytes per piece

foreach (var file in metadata.GetFileInfos())
    Console.WriteLine($"{file.Filename} ({file.FileSize} bytes, starts at byte {file.FileStartByte})");

foreach (var tracker in metadata.AnnounceList)
    Console.WriteLine(tracker);
```

## Finding peers

### Trackers (HTTP and UDP)

```csharp
using bzTorrent;

// HTTP tracker
ITrackerClient tracker = new HTTPTrackerClient(timeoutSeconds: 10);

// UDP tracker
ITrackerClient tracker = new UDPTrackerClient(timeoutSeconds: 10);

var info = tracker.Announce(
    trackerUrl:   "udp://tracker.opentrackr.org:1337/announce",
    infoHash:     metadata.HashString,
    peerId:       "-MY0001-123456789012",
    downloaded:   0,
    left:         metadata.PieceHashes.Count * metadata.PieceSize,
    uploaded:     0,
    ipAddress:    0,
    key:          0,
    numWant:      200,
    port:         6881,
    compact:      0);

Console.WriteLine($"{info.Seeders} seeders, {info.Leechers} leechers");
foreach (var peer in info.Peers)
    Console.WriteLine(peer); // IPEndPoint

// Scrape (stats only, no peer list)
var stats = tracker.Scrape(trackerUrl, metadata.HashString);
Console.WriteLine($"{stats.Seeders} seeders, {stats.Leechers} leechers, {stats.Downloaded} completed");
```

### DHT (trackerless)

```csharp
using bzTorrent.DHT;

var dht = new DHTClient();
dht.PeerFound += endpoint => Console.WriteLine($"DHT found peer: {endpoint}");
dht.Start();

// Bootstrap from the standard DHT bootstrap nodes, then search
var bootstrapNodes = new[]
{
    new IPEndPoint(Dns.GetHostAddresses("router.bittorrent.com").First(), 6881),
    new IPEndPoint(Dns.GetHostAddresses("router.utorrent.com").First(), 6881),
    new IPEndPoint(Dns.GetHostAddresses("dht.transmissionbt.com").First(), 6881),
};
await dht.BootstrapAsync(bootstrapNodes);
dht.StartSearch(metadata.Hash); // searches continuously, re-querying every 5 minutes

// When done
dht.Dispose();
```

### Local peer discovery (LPD / BEP-14)

```csharp
using bzTorrent;
using bzTorrent.IO;

var lpd = new LocalPeerDiscovery<UDPSocket>();
lpd.NewPeer += (address, port, infoHash) =>
    Console.WriteLine($"Local peer: {address}:{port}");
lpd.Open();
```

## Connecting to a peer

```csharp
using bzTorrent;
using bzTorrent.IO;

var connection = new PeerWireConnection<TCPSocket> { Timeout = 5 };
var client = new PeerWireClient(connection) { KeepConnectionAlive = true };

// Wire up events
client.HandshakeComplete += (pwc) => pwc.SendInterested();
client.BitField         += (pwc, length, bitField) => pwc.SendInterested();
client.UnChoke          += (pwc) => { /* now we can request pieces */ };
client.Choke            += (pwc) => { /* stop sending requests */ };
client.Have             += (pwc, pieceIndex) => { /* peer has this piece */ };
client.Piece            += (pwc, index, offset, data) =>
{
    // write data to disk at (index * pieceSize + offset)
};
client.DroppedConnection += (pwc) => Console.WriteLine("Disconnected");

// Connect and handshake
client.Connect(peerEndPoint);
client.Handshake(metadata.HashString, peerId);

// Drive the connection — call in a loop until it returns false
while (client.Process())
{
    if (!client.ReceivedHandshake) continue;

    // Request a piece block (pieceIndex, byteOffset, byteLength)
    client.SendRequest(pieceIndex, offset, blockSize);
}
```

### Requesting a piece in 16 KB blocks

BitTorrent pieces are typically 256 KB and must be requested in blocks of up to 16 KB:

```csharp
var blockSize = 16 * 1024;
var pieceSize = metadata.PieceSize; // or smaller for the last piece
var blocks = (int)Math.Ceiling(pieceSize / (double)blockSize);

for (int b = 0; b < blocks; b++)
{
    var offset = b * blockSize;
    var length = (int)Math.Min(pieceSize - offset, blockSize);
    client.SendRequest((uint)pieceIndex, (uint)offset, (uint)length);
}
```

## Message Stream Encryption (MSE/PE)

Obfuscates the handshake and subsequent traffic (RC4) so simple deep packet inspection can't identify it as BitTorrent. Set `EncryptionMode` on the `PeerWireConnection` before connecting/handshaking:

```csharp
using bzTorrent.IO;

var connection = new PeerWireConnection<TCPSocket>
{
    Timeout = 5,
    // PlainText          — never attempt MSE (default)
    // PreferEncryption   — attempt MSE, fall back to plaintext if the peer won't encrypt
    // RequireEncryption  — attempt MSE, refuse the connection if it can't be encrypted
    EncryptionMode = PeerEncryptionMode.PreferEncryption
};
var client = new PeerWireClient(connection);

client.Connect(peerEndPoint);
client.Handshake(metadata.HashString, peerId); // runs the MSE key exchange first, if enabled

// Check whether the negotiated payload ended up encrypted
client.Piece += (pwc, index, offset, data) =>
    Console.WriteLine(pwc.IsEncrypted ? "encrypted piece" : "plaintext piece");
```

`PeerEncryptionOptions` (on `connection.EncryptionOptions`) controls the negotiation details:

```csharp
connection.EncryptionOptions.MaxPaddingBytes = 512; // spec default
```

`AddKnownInfoHash` is required to accept encrypted incoming connections, since an incoming MSE handshake hides the infohash until matched against a known set. `PeerWireConnection<T>.Accept()` propagates `EncryptionMode` and `EncryptionOptions` (including known infohashes) to each accepted connection automatically:

```csharp
var listener = new PeerWireConnection<TCPSocket> { EncryptionMode = PeerEncryptionMode.PreferEncryption };
listener.EncryptionOptions.AddKnownInfoHash(metadata.HashString);
listener.Listen(new IPEndPoint(IPAddress.Any, 6881));

var accepted = listener.Accept(); // inherits EncryptionMode + known infohashes
```

> **Note:** the higher-level `PeerWireListener<T>` convenience class (see [Listening for incoming connections](#listening-for-incoming-connections)) does not currently use `Accept()` internally, so encryption settings aren't propagated to connections it hands you — use the pattern above directly if you need encrypted incoming connections today.

## Protocol extensions

Extensions plug into `PeerWireClient` and handle the details of their respective BEPs automatically.

### Fast extensions (BEP-6)

```csharp
using bzTorrent.ProtocolExtensions;

var fast = new FastExtensions();
fast.HaveAll      += (pwc) => pwc.SendInterested(); // seeder — has everything
fast.HaveNone     += (pwc) => { };                  // peer has nothing yet
fast.AllowedFast  += (pwc, index) => { /* request this piece even when choked */ };
fast.SuggestPiece += (pwc, index) => { /* peer recommends this piece */ };

client.RegisterBTExtension(fast);
```

### Extended protocol container (BEP-10)

BEP-10 is required to use PEX, metadata exchange, and tracker exchange. Register it once and add sub-extensions to it:

```csharp
using bzTorrent.ProtocolExtensions;

var extProtocol = new ExtendedProtocolExtensions();

// Add sub-extensions (see below)
extProtocol.RegisterProtocolExtension(client, pex);
extProtocol.RegisterProtocolExtension(client, trackerExchange);
extProtocol.RegisterProtocolExtension(client, utMetadata);

client.RegisterBTExtension(extProtocol);
```

### Peer exchange (BEP-11)

Peers share peer lists with each other. Wire up `Added` to grow your peer pool for free:

```csharp
var pex = new UTPeerExchange();
pex.Added   += (pwc, ext, endpoint, flags) => { /* add endpoint to your peer pool */ };
pex.Dropped += (pwc, ext, endpoint)        => { /* peer left the swarm */ };

extProtocol.RegisterProtocolExtension(client, pex);
```

### Metadata exchange / magnet links (BEP-9)

Fetch the full `.torrent` info dictionary from peers when you only have a magnet link:

```csharp
var utMetadata = new UTMetadata();
utMetadata.MetaDataReceived += (pwc, ext, infoDict) =>
{
    // infoDict is a BDict (bzBencode) containing the full info dictionary
    // Load it into a Metadata object:
    var metadata = new Metadata();
    metadata.Load(magnetLink); // sets hash + trackers from the magnet link
    metadata.Load(infoDict);   // populates pieces, files, name
};

extProtocol.RegisterProtocolExtension(client, utMetadata);
```

### Tracker exchange (BEP-28)

Peers share tracker URLs they know about:

```csharp
var trackerExchange = new LTTrackerExchange();
trackerExchange.TrackerAdded += (pwc, ext, trackerUrl) =>
{
    // announce to the new tracker
};

extProtocol.RegisterProtocolExtension(client, trackerExchange);
```

### DHT port advertisement (BEP-5)

Peers advertise their DHT port during the handshake:

```csharp
var dhtPort = new DHTPortExtension();
dhtPort.Port += (pwc, port) =>
{
    // peer's DHT node is listening on port
};

client.RegisterBTExtension(dhtPort);
```

## Writing piece data to files

`MetadataFileInfo.FileStartByte` gives the byte offset of each file within the overall torrent data stream. Use this to map piece data to the correct file position:

```csharp
void WritePieceData(IMetadata metadata, string outputDir, int pieceIndex, int offset, byte[] data)
{
    var absoluteStart = (long)pieceIndex * metadata.PieceSize + offset;
    var absoluteEnd   = absoluteStart + data.Length;

    foreach (var file in metadata.GetFileInfos())
    {
        var fileEnd = file.FileStartByte + file.FileSize;
        if (absoluteEnd <= file.FileStartByte || absoluteStart >= fileEnd)
            continue;

        var fileOffset  = Math.Max(absoluteStart - file.FileStartByte, 0);
        var dataOffset  = (int)Math.Max(file.FileStartByte - absoluteStart, 0);
        var writeLength = (int)Math.Min(data.Length - dataOffset, file.FileSize - fileOffset);

        var path = Path.GetFullPath(file.Filename, outputDir);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        fs.Seek(fileOffset, SeekOrigin.Begin);
        fs.Write(data, dataOffset, writeLength);
    }
}
```

## Listening for incoming connections

```csharp
using bzTorrent;
using bzTorrent.IO;

var listener = new PeerWireListener<TCPSocket>(port: 6881);
listener.NewPeer += (client) =>
{
    // client is a PeerWireClient — wire up events and call Process() as above
};
listener.Start();
```

## Namespaces

| Namespace | Contents |
|---|---|
| `bzTorrent` | `PeerWireClient`, `PeerWireListener`, `LocalPeerDiscovery`, tracker clients |
| `bzTorrent.Data` | `Metadata`, `MagnetLink`, `MetadataFileInfo`, `MetadataPieceInfo` |
| `bzTorrent.DHT` | `DHTClient`, `DHTNode`, `DHTRoutingTable` |
| `bzTorrent.IO` | `PeerWireConnection`, `TCPSocket`, `UDPSocket`, `UTPSocket` |
| `bzTorrent.ProtocolExtensions` | `FastExtensions`, `ExtendedProtocolExtensions`, `UTPeerExchange`, `UTMetadata`, `LTTrackerExchange`, `DHTPortExtension` |

## Implemented BEPs

| BEP | Description |
|---|---|
| BEP-3  | BitTorrent protocol — peer wire, tracker HTTP announce |
| BEP-5  | DHT protocol |
| BEP-6  | Fast extensions |
| BEP-9  | Extension for peers to send metadata files (magnet link metadata fetch) |
| BEP-10 | Extension protocol |
| BEP-11 | Peer exchange (PEX) |
| BEP-14 | Local service discovery |
| BEP-15 | UDP tracker protocol |
| BEP-28 | Tracker exchange protocol |
| —      | Message Stream Encryption / Protocol Encryption (MSE/PE) — not a formal BEP, but widely implemented |

## License

BSD 3-Clause. See [LICENSE](LICENSE).
