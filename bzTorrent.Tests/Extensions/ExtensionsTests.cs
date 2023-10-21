using FluentAssertions;
using System;
using System.Diagnostics;
using bzTorrent.Helpers;
using Xunit;
using bzTorrent.Data;
using bzTorrent.Extensions;
using System.Collections.Generic;

namespace bzTorrent.Tests.Extensions
{
    public class ExtensionsTests
    {
        [Fact]
        public void IsNullOrEmptyReturnsTrueForEmptyArray()
        {
            Array.Empty<object>().IsNullOrEmpty().Should().BeTrue();
        }

        [Fact]
        public void IsNullOrEmptyReturnsFalseForArrayWithObjects()
        {
            var array = new List<object>() { new object(), new object(), new object() };
            array.IsNullOrEmpty().Should().BeFalse();
        }

        [Fact]
        public void IsNullOrEmptyReturnsTrueForNullCastedToArray()
        {
            var nullArray = (object[])null;
            nullArray.IsNullOrEmpty().Should().BeTrue();
        }

        [Fact]
        public void ThrowIfNullThrowsExceptionForNull()
        {
            object x = null;
            var ex = Record.Exception(() => x.ThrowIfNull(nameof(x)));
            Assert.NotNull(ex);
        }

        [Fact]
        public void ThrowIfNullDoesNotThrowExceptionForValidObject()
        {
            object x = new();
            var ex = Record.Exception(() => x.ThrowIfNull(nameof(x)));
            Assert.Null(ex);
        }
    }
}
