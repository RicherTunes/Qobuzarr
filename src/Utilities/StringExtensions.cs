using System;
using NzbDrone.Common.Extensions;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    /// <summary>
    /// File naming utilities for Qobuz plugin
    /// Note: Uses Lidarr's built-in StringExtensions for common operations
    /// </summary>
    public static class FileNamingUtils
    {
        // Static readonly collections to avoid repeated allocations - performance optimization
        private static readonly char[] IllegalChars = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
        private static readonly string[] ReservedNames = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", 
                                                          "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", 
                                                          "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        /// <summary>
        /// Clean string for use in file paths with comprehensive edge case handling
        /// This method is specific to our plugin and not available in Lidarr
        /// </summary>
        public static string ToSafeFileName(this string value)
        {
            if (value.IsNullOrWhiteSpace())
                return "Unknown";

            var safeName = value;

            // Handle Unicode normalization to prevent issues with accented characters
            safeName = safeName.Normalize(System.Text.NormalizationForm.FormC);

            // Replace illegal filename characters
            foreach (var c in IllegalChars)
            {
                safeName = safeName.Replace(c, '_');
            }

            // Replace problematic Unicode characters that cause issues on different filesystems
            safeName = safeName.Replace('\u00A0', ' '); // Non-breaking space
            safeName = safeName.Replace('\u2013', '-'); // En dash
            safeName = safeName.Replace('\u2014', '-'); // Em dash
            safeName = safeName.Replace('\u2018', '\''); // Left single quotation mark
            safeName = safeName.Replace('\u2019', '\''); // Right single quotation mark
            safeName = safeName.Replace('\u201C', '"'); // Left double quotation mark
            safeName = safeName.Replace('\u201D', '"'); // Right double quotation mark
            safeName = safeName.Replace("\u2026", "..."); // Horizontal ellipsis

            // Remove or replace control characters (0x00-0x1F, 0x7F)
            for (int i = 0; i < 32; i++)
            {
                safeName = safeName.Replace((char)i, '_');
            }
            safeName = safeName.Replace((char)127, '_'); // DEL character

            // Handle Windows reserved names (CON, PRN, AUX, NUL, COM1-9, LPT1-9)
            var upperName = safeName.ToUpperInvariant();
            foreach (var reserved in ReservedNames)
            {
                if (upperName == reserved || upperName.StartsWith(reserved + "."))
                {
                    safeName = "_" + safeName;
                    break;
                }
            }

            // Remove leading/trailing dots and spaces (Windows restriction)
            safeName = safeName.Trim(' ', '.');

            // Handle excessive length (keep under 200 chars to leave room for extensions and paths)
            if (safeName.Length > 200)
            {
                safeName = safeName.Substring(0, 200).TrimEnd(' ', '.');
            }

            // Remove multiple consecutive spaces/underscores and clean up
            while (safeName.Contains("  "))
                safeName = safeName.Replace("  ", " ");
            while (safeName.Contains("__"))
                safeName = safeName.Replace("__", "_");

            // Final safety check - if we ended up with empty string, use fallback
            if (safeName.IsNullOrWhiteSpace())
                return "Unknown";

            return safeName.Trim();
        }

        /// <summary>
        /// Safe substring that won't throw exceptions
        /// </summary>
        public static string SafeSubstring(this string value, int startIndex, int length)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (startIndex >= value.Length)
                return string.Empty;

            if (startIndex + length > value.Length)
                length = value.Length - startIndex;

            return value.Substring(startIndex, length);
        }

        /// <summary>
        /// Truncate string to specified length
        /// </summary>
        public static string Truncate(this string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value;

            return value.Substring(0, maxLength);
        }
    }
}