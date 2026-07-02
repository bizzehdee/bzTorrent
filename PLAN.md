# bzTorrent — Protocol Coverage Plan

bzTorrent is a BitTorrent **protocol library** (not a client): tracker
communication, peer wire, metadata parsing, DHT, and BEP extensions, leaving
piece selection/choking/disk-IO/resume to the consumer. That scope boundary
is intentional and out of scope for this plan.

This plan tracks the remaining protocol-level gaps identified in review:

1. `.torrent` file creation (`Metadata` is currently read-only)
2. `UTPSocket` retransmission / out-of-order packet handling (currently
   flagged experimental in README)
3. MSE/PE support over uTP (currently throws `NotSupportedException`)
4. BEP-19 WebSeed (HTTP/FTP seeding, `url-list`)
5. BEP-55 Holepunch extension protocol
6. BEP-7 / BEP-32 IPv6 support (tracker `compact` peer lists, DHT)

## Approach

- One feature at a time, each with unit tests, verified against the real
  `Demo/TestTorrents/ubuntu-26.04-desktop-amd64.iso.torrent` fixture via the
  `Demo` console app (live network) where that's meaningful, and a commit
  after each task completes with tests green.
- No changes to existing public API surface beyond additive members unless
  called out in a task.
