using FluentAssertions;
using System;
using System.Diagnostics;
using bzTorrent.Helpers;
using Xunit;
using bzTorrent.Data;


namespace bzTorrent.Tests.Data
{
    public class MagnetLinkTests
    {
        private readonly string magnetLink = "magnet:?xt=urn:btih:C1463792A1FF36A237E3A0F68BADEB0D3764E9BB&dn=ubuntu-23.10-live-server-amd64.iso&xl=2662275072&tr=https%3A%2F%2Ftorrent.ubuntu.com%2Fannounce";
        private readonly string invalidMagnetLink = "magnet:?C1463792A1FF36A237E3A0F68BADEB0D3764E9BB";
        private readonly string invalidMagnetLink2 = "magnet:?xt=urnC1463792A1FF36A237E3A0F68BADEB0D3764E9BB";

        [Fact]
        public void HashShouldBeCorrectWhenParsed()
        {
            var magnet = MagnetLink.Resolve(magnetLink);

            magnet.HashString.Should().Be("C1463792A1FF36A237E3A0F68BADEB0D3764E9BB");
        }

        [Fact]
        public void NameShouldBeCorrectWhenParsed()
        {
            var magnet = MagnetLink.Resolve(magnetLink);

            magnet.Name.Should().Be("ubuntu-23.10-live-server-amd64.iso");
        }

        [Fact]
        public void HasExactlyOneTrackerWhenResolved()
        {
            var magnet = MagnetLink.Resolve(magnetLink);

            magnet.Trackers.Should().HaveCount(1);
        }

        [Fact]
        public void InvalidMagnetShouldGiveBlankMagnetDTO()
        {
            var magnet = MagnetLink.Resolve(invalidMagnetLink);

            magnet.Hash.Should().BeNull();
            magnet.Trackers.Should().HaveCount(0);
        }

        [Fact]
        public void SemiInvalidMagnetShouldGiveBlankMagnetDTO()
        {
            var magnet = MagnetLink.Resolve(invalidMagnetLink2);

            magnet.Hash.Should().BeNull();
            magnet.Trackers.Should().HaveCount(0);
        }
    }
}
