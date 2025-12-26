using System.IO;
using Lidarr.Plugin.Common.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    /// <summary>
    /// Builds sanitized track filenames with correct extensions based on Qobuz format.
    /// </summary>
    public static class TrackFileNameBuilder
    {
        /// <summary>
        /// Builds a sanitized track filename with the correct extension based on format.
        /// </summary>
        public static string Build(int trackNumber, string trackTitle, int formatId)
        {
            var sanitizedTitle = FileNameSanitizer.SanitizeFileName(trackTitle);
            var extension = GetExtensionForFormat(formatId);
            return $"{trackNumber:00} - {sanitizedTitle}{extension}";
        }

        /// <summary>
        /// Gets file extension for Qobuz format ID.
        /// </summary>
        public static string GetExtensionForFormat(int formatId)
        {
            return formatId switch
            {
                5 => ".mp3",
                6 or 7 or 27 => ".flac",
                _ => ".flac" // Default to FLAC for unknown formats
            };
        }
    }
}
