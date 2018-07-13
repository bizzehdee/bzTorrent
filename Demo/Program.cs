using System;
using System.Net.Torrent;
using System.Text;
using System.Threading.Tasks;

namespace Demo
{
    class Program
    {
        static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            DecodeTorrentToMeta();

            TestMagnetLink();

            TestAsyncMagnetLink();

            AnnounceTorrent();

            ScrapeTorrent();
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

        static void TestAsyncMagnetLink()
        {
            var ubuntuMagnetLink = "magnet:?xt=urn:btih:e4be9e4db876e3e3179778b03e906297be5c8dbe&dn=ubuntu-18.04-desktop-amd64.iso&tr=http://torrent.ubuntu.com:6969/announce";

            var magnetMetadata = MagnetLink.ResolveToMetadata(ubuntuMagnetLink);

            foreach (var item in magnetMetadata.AnnounceList)
            {
                Console.WriteLine(item);
            }
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
    }
}
