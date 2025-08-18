using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    /// <summary>
    /// Utility class for safe file and folder name generation
    /// </summary>
    public static class FileNameUtility
    {
        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();
        private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();

        /// <summary>
        /// Sanitize a string for use as a file name
        /// </summary>
        public static string SanitizeFileName(string fileName)
        {
            return FileNameSanitizer.SanitizeFileName(fileName);
        }

        /// <summary>
        /// Sanitize a string for use as a folder path
        /// </summary>
        public static string SanitizeFolderPath(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return "Unknown";

            var sanitized = folderPath;
            
            // Replace invalid characters
            foreach (var c in InvalidPathChars)
            {
                sanitized = sanitized.Replace(c, '_');
            }

            return sanitized.Trim();
        }
    }
}