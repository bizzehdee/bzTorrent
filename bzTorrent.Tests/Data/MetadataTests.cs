using FluentAssertions;
using System;
using System.Diagnostics;
using bzTorrent.Helpers;
using Xunit;
using bzTorrent.Data;
using System.IO;
using System.Text;

namespace bzTorrent.Tests.Data
{
    public class MetadataTests
    {
        public MetadataTests() 
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        [Fact]
        public void ValidFileCreatesValidMetadata()
        {
            var metadata = new Metadata();

            var file = File.OpenRead("TestFiles\\ubuntu-23.10-live-server-amd64.iso.torrent");

            metadata.Load(file).Should().BeTrue();

            metadata.HashString.Should().Be("C1463792A1FF36A237E3A0F68BADEB0D3764E9BB");
            metadata.Name.Should().Be("ubuntu-23.10-live-server-amd64.iso");
            metadata.CreationDate.Should().Be(DateTime.Parse("12/10/2023 14:24:45"));

            file.Close();
        }
    }
}
