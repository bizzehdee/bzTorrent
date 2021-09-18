namespace Demo
{
    using System;
    using System.Net.Torrent;
    using System.Net.Torrent.Data;
    using System.Net.Torrent.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    class Program
    {
        static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            /*
            DecodeTorrentToMeta();

            TestMagnetLink();

            TestMagnetLinkUDP();

            TestAsyncMagnetLinkUDP();

            TestAsyncMagnetLink();

            AnnounceTorrentUDP();
            AnnounceTorrentUDP();

            AnnounceTorrent();

            ScrapeTorrent();
            */
            TestPeerWireClient();
        }

        static void DecodeTorrentToMeta()
        {
            var meta = Metadata.FromFile("TestTorrents\\ubuntu-18.04-desktop-amd64.iso.torrent");
        }

        static void TestMagnetLink()
        {
            var ubuntuMagnetLink = "magnet:?xt=urn:btih:e4be9e4db876e3e3179778b03e906297be5c8dbe&dn=ubuntu-18.04-desktop-amd64.iso&tr=http://torrent.ubuntu.com:6969/announce";

            var magnetLink = MagnetLink.Resolve(ubuntuMagnetLink);
        }
        static void TestMagnetLinkUDP()
        {
            var ubuntuMagnetLink = "magnet:?xt=urn:btih:e4be9e4db876e3e3179778b03e906297be5c8dbe&dn=ubuntu-18.04-desktop-amd64.iso&tr=udp://tracker.opentrackr.org:1337/announce";

            var magnetLink = MagnetLink.Resolve(ubuntuMagnetLink);
        }

        static void TestAsyncMagnetLinkUDP()
        {
            var ubuntuMagnetLink = "magnet:?xt=urn:btih:e4be9e4db876e3e3179778b03e906297be5c8dbe&dn=ubuntu-18.04-desktop-amd64.iso&tr=udp://tracker.opentrackr.org:1337/announce";

            var magnetMetadata = MagnetLink.ResolveToMetadata(ubuntuMagnetLink);

            foreach (var item in magnetMetadata.AnnounceList)
            {
                Console.WriteLine(item);
            }
        }

        static void TestAsyncMagnetLink()
        {
            var ubuntuMagnetLink = "magnet:?xt=urn:btih:e4be9e4db876e3e3179778b03e906297be5c8dbe&dn=ubuntu-18.04-desktop-amd64.iso&tr=http://torrent.ubuntu.com:6969/announce";

            var magnetMetadata = MagnetLink.ResolveToMetadata(ubuntuMagnetLink);

            foreach (var item in magnetMetadata.AnnounceList)
            {
                Console.WriteLine(item);
            }
        }

        static void AnnounceTorrentUDP()
        {
            var scraper = new UDPTrackerClient(15);
            var peers = scraper.Announce("udp://tracker.opentrackr.org:1337/announce", "e4be9e4db876e3e3179778b03e906297be5c8dbe", "-LW2222-011345223110");
        }

        static void AnnounceTorrent()
        {
            var scraper = new HTTPTrackerClient(15);
            var peers = scraper.Announce("http://torrent.ubuntu.com:6969/announce", "e4be9e4db876e3e3179778b03e906297be5c8dbe", "-LW2222-011345223110");
        }

        static void ScrapeTorrent()
        {
            var scraper = new HTTPTrackerClient(15);
            var announce = scraper.Scrape("http://torrent.ubuntu.com:6969/announce", new string[] { "e4be9e4db876e3e3179778b03e906297be5c8dbe" });
        }

        static void TestPeerWireClient()
        {
            //create a socket with chosen protocol
            var socket = new PeerWireTCPConnection();

            //create a client with that socket
            var client = new PeerWireClient(socket);

            //connect to the remote host
            client.Connect("127.0.0.1", 63516);

            //perform handshake
            client.Handshake("e4be9e4db876e3e3179778b03e906297be5c8dbe", "-LN2222-011345223110");

            //implement events

            //process until return false
            while(client.Process())
            {
                Thread.Sleep(10);
            }
        }
    }
}
