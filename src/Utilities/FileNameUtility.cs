using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    /// <summary>
    /// Utility class for safe file and folder name generation
    /// Now uses the shared library's FileNameSanitizer for consistency.
    /// </summary>
    public static class FileNameUtility
    {
        private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();

        /// <summary>
        /// Sanitize a string for use as a file name using the shared library.
        /// </summary>
        public static string SanitizeFileName(string fileName)
        {
            return Lidarr.Plugin.Common.Utilities.FileNameSanitizer.SanitizeFileName(fileName);
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