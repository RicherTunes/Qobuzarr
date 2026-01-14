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
        public static string Build(int trackNumber, string trackTitle, int formatId, int discNumber = 1, int totalDiscs = 1)
        {
            if (discNumber <= 0) discNumber = 1;
            if (totalDiscs <= 1) totalDiscs = 1;

            var extension = GetExtensionForFormat(formatId);
            return FileSystemUtilities.CreateTrackFileName(
                title: trackTitle,
                trackNumber: trackNumber,
                extension: extension,
                discNumber: discNumber,
                totalDiscs: totalDiscs);
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
