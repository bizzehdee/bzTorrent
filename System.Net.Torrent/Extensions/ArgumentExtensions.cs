namespace System.Net.Torrent.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public static class ArgumentExtensions
    {
        public static void ThrowIfNull<T>(this T source, string paramName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(paramName);
            }
        }

        public static void ThrowIfNullOrEmpty<T>(this IEnumerable<T> source, string paramName)
        {
            if (source.IsNullOrEmpty())
            {
                throw new ArgumentNullException("Enumerable cannot be null or empty", paramName);
            }
        }
    }
}
