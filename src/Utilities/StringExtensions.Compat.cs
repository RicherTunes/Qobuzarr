using System;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    // Backwards-compatible string extensions used by tests and older call sites.
    // Delegates to shared library implementations where appropriate.
    public static class StringExtensions
    {
        public static string ToSafeFileName(this string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Unknown";
            return Lidarr.Plugin.Common.Utilities.FileNameSanitizer.SanitizeFileName(value);
        }

        public static string SafeSubstring(this string value, int startIndex, int length)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (startIndex >= value.Length) return string.Empty;
            if (startIndex < 0) startIndex = 0;
            if (length < 0) length = 0;
            if (startIndex + length > value.Length) length = value.Length - startIndex;
            return value.Substring(startIndex, length);
        }

        public static string Truncate(this string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || maxLength <= 0) return string.Empty;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }
}

