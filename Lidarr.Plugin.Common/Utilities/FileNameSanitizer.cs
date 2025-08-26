using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lidarr.Plugin.Common.Utilities
{
    /// <summary>
    /// Utility for sanitizing file and path names to be safe for various file systems.
    /// </summary>
    public static class FileNameSanitizer
    {
        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();
        private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();
        
        // Additional characters that can cause issues
        private static readonly char[] ProblematicChars = { ':', '*', '?', '"', '<', '>', '|' };
        
        // Zero-width characters that should be removed
        private static readonly char[] ZeroWidthChars = { '\u200B', '\u200C', '\u200D', '\uFEFF' };

        /// <summary>
        /// Sanitizes a file name to be safe for file system storage.
        /// </summary>
        /// <param name="fileName">The file name to sanitize</param>
        /// <param name="replacement">Character to replace invalid characters with</param>
        /// <returns>Sanitized file name</returns>
        public static string SanitizeFileName(string fileName, char replacement = ' ')
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "Unknown";

            var sanitized = fileName;

            // Remove zero-width characters
            foreach (var zwChar in ZeroWidthChars)
            {
                sanitized = sanitized.Replace(zwChar.ToString(), "");
            }

            // Replace invalid file name characters
            foreach (var invalidChar in InvalidFileNameChars.Concat(ProblematicChars))
            {
                sanitized = sanitized.Replace(invalidChar, replacement);
            }

            // Handle special cases
            sanitized = sanitized.Replace("..", " "); // Parent directory traversal
            sanitized = sanitized.Replace("/", " "); // Forward slash
            sanitized = sanitized.Replace("\\", " "); // Backslash
            sanitized = sanitized.Replace("\n", " "); // Newlines
            sanitized = sanitized.Replace("\r", " "); // Carriage returns
            sanitized = sanitized.Replace("\t", " "); // Tabs

            // Collapse multiple spaces
            sanitized = Regex.Replace(sanitized, @"\s+", " ");

            // Trim whitespace
            sanitized = sanitized.Trim();

            // Handle reserved Windows names
            var reservedNames = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
            if (reservedNames.Contains(sanitized.ToUpperInvariant()))
            {
                sanitized = $"_{sanitized}";
            }

            // Ensure not empty after sanitization
            if (string.IsNullOrWhiteSpace(sanitized))
                return "Unknown";

            return sanitized;
        }

        /// <summary>
        /// Sanitizes a full path to be safe for file system storage.
        /// </summary>
        /// <param name="path">The path to sanitize</param>
        /// <returns>Sanitized path</returns>
        public static string SanitizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            var sanitized = path;

            // Remove path traversal attempts
            sanitized = sanitized.Replace("..", "");
            sanitized = sanitized.Replace("~", "");

            // Remove leading path separators to prevent absolute path issues
            while (sanitized.StartsWith("/") || sanitized.StartsWith("\\"))
            {
                sanitized = sanitized.Substring(1);
            }

            // Split into parts and sanitize each
            var parts = sanitized.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            var sanitizedParts = parts.Select(part => SanitizeFileName(part)).ToArray();

            return string.Join(Path.DirectorySeparatorChar.ToString(), sanitizedParts);
        }
    }
}
