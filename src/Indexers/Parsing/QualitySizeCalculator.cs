using System;
using System.Linq;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Indexers.Parsing
{
    /// <summary>
    /// Static utility for calculating estimated file sizes based on audio quality.
    /// Extracted from QobuzParser for direct testability.
    /// </summary>
    public static class QualitySizeCalculator
    {
        /// <summary>
        /// Minimum file size to prevent issues in Lidarr (1MB)
        /// </summary>
        public const long MinimumSizeBytes = 1024 * 1024;

        /// <summary>
        /// Calculate estimated file size for an album at a specific quality.
        /// </summary>
        /// <param name="durationSeconds">Total album duration in seconds</param>
        /// <param name="quality">The audio quality/format</param>
        /// <returns>Estimated size in bytes (minimum 1MB)</returns>
        public static long CalculateSize(double durationSeconds, QobuzAudioQuality quality)
        {
            if (durationSeconds <= 0)
            {
                return MinimumSizeBytes;
            }

            var bitrate = quality.GetEstimatedBitrate();

            // Convert bits per second to bytes per second, then multiply by duration
            var estimatedSize = (long)(durationSeconds * (bitrate / 8.0));

            // Ensure we don't return 0 size (causes issues in Lidarr)
            return Math.Max(estimatedSize, MinimumSizeBytes);
        }

        /// <summary>
        /// Calculate estimated file size for a QobuzAlbum at a specific quality.
        /// </summary>
        public static long CalculateSize(QobuzAlbum album, QobuzAudioQuality quality)
        {
            if (album == null)
            {
                return MinimumSizeBytes;
            }

            var durationSeconds = CalculateReliableDuration(album);
            return CalculateSize(durationSeconds, quality);
        }

        /// <summary>
        /// Calculate the most reliable duration estimate for an album.
        /// Uses multiple fallback strategies.
        /// </summary>
        public static double CalculateReliableDuration(QobuzAlbum album)
        {
            if (album == null)
            {
                return 30; // Minimum fallback
            }

            // Tier 1: Use album duration if available
            if (album.Duration.TotalSeconds > 0)
            {
                return album.Duration.TotalSeconds;
            }

            // Tier 2: Sum track durations if available
            var tracks = album.GetTracks();
            if (tracks.Any())
            {
                var trackSum = tracks.Sum(t => t.Duration.TotalSeconds);
                if (trackSum > 0)
                {
                    return trackSum;
                }
            }

            // Tier 3: Smart estimation based on singles vs albums
            var trackCount = Math.Max(album.TracksCount > 0 ? album.TracksCount : tracks.Count, 1);
            var isSingle = IsLikelySingle(album);
            var avgDuration = isSingle ? 3.25 * 60 : 3.5 * 60; // Singles: 3.25min, Albums: 3.5min

            return Math.Max(trackCount * avgDuration, 30); // 30 second minimum
        }

        /// <summary>
        /// Determine if an album is likely a single based on track count and duration.
        /// </summary>
        private static bool IsLikelySingle(QobuzAlbum album)
        {
            return album.TracksCount <= Configuration.QobuzConstants.Parser.SingleTrackMinCount &&
                   album.Duration < Configuration.QobuzConstants.Parser.SingleTrackMinDuration;
        }
    }
}
