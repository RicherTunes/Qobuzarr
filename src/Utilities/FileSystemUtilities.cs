using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    /// <summary>
    /// Centralized utilities for file system operations including file name sanitization.
    /// This consolidates all file sanitization logic to prevent duplication and ensure consistency.
    /// </summary>
    public static class FileSystemUtilities
    {
        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();
        private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();
        
        /// <summary>
        /// Sanitizes a filename by replacing invalid characters while preserving readability.
        /// This now delegates to the centralized FileNameSanitizer for consistency.
        /// </summary>
        /// <param name="fileName">The filename to sanitize</param>
        /// <param name="maxLength">Maximum length for the filename (default: 255)</param>
        /// <returns>A sanitized filename safe for the file system</returns>
        public static string SanitizeFileName(string fileName, int maxLength = 255)
        {
            var sanitized = FileNameSanitizer.SanitizeFileName(fileName);
            
            // Apply maxLength constraint if needed
            if (sanitized.Length > maxLength)
            {
                sanitized = sanitized.Substring(0, maxLength);
                var lastSpace = sanitized.LastIndexOf(' ');
                if (lastSpace > maxLength / 2) // Only truncate at word boundary if it's not too early
                {
                    sanitized = sanitized.Substring(0, lastSpace);
                }
                sanitized = sanitized.TrimEnd(' ', '.', '_', '-');
            }

            return sanitized;
        }

        /// <summary>
        /// Sanitizes a directory path by sanitizing each component separately.
        /// </summary>
        /// <param name="path">The path to sanitize</param>
        /// <param name="maxLength">Maximum length for each path component</param>
        /// <returns>A sanitized path safe for the file system</returns>
        public static string SanitizeDirectoryPath(string path, int maxLength = 255)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "Unknown";

            var pathComponents = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            var sanitizedComponents = pathComponents.Select(component => SanitizeFileName(component, maxLength));
            
            return string.Join(Path.DirectorySeparatorChar.ToString(), sanitizedComponents);
        }

        /// <summary>
        /// Creates a safe filename for a track with track number prefix.
        /// </summary>
        /// <param name="title">Track title</param>
        /// <param name="trackNumber">Track number</param>
        /// <param name="extension">File extension (without dot)</param>
        /// <param name="maxLength">Maximum length excluding extension</param>
        /// <returns>Formatted safe filename</returns>
        public static string CreateTrackFileName(string title, int trackNumber, string extension = "flac", int maxLength = 200)
        {
            var trackNum = trackNumber.ToString("D2");
            var safeTitle = SanitizeFileName(title, maxLength - trackNum.Length - 4); // Reserve space for track number and " - "
            return $"{trackNum} - {safeTitle}.{extension}";
        }

        /// <summary>
        /// Creates a safe directory name for an album with year.
        /// </summary>
        /// <param name="albumTitle">Album title</param>
        /// <param name="year">Release year (optional)</param>
        /// <param name="maxLength">Maximum length for the directory name</param>
        /// <returns>Formatted safe directory name</returns>
        public static string CreateAlbumDirectoryName(string albumTitle, int? year = null, int maxLength = 200)
        {
            var yearSuffix = year.HasValue ? $" ({year})" : "";
            var availableLength = maxLength - yearSuffix.Length;
            var safeTitle = SanitizeFileName(albumTitle, availableLength);
            return $"{safeTitle}{yearSuffix}";
        }
    }
}