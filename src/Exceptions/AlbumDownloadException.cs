using System;
using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.Qobuzarr.Download;

namespace Lidarr.Plugin.Qobuzarr.Exceptions
{
    /// <summary>
    /// Exception thrown when an album download fails or completes with issues.
    /// </summary>
    public class AlbumDownloadException : Exception
    {
        /// <summary>
        /// The album ID that failed to download.
        /// </summary>
        public string AlbumId { get; }

        /// <summary>
        /// The album title for user-friendly messages.
        /// </summary>
        public string AlbumTitle { get; }

        /// <summary>
        /// Total number of tracks in the album.
        /// </summary>
        public int TotalTracks { get; }

        /// <summary>
        /// Number of successfully downloaded tracks.
        /// </summary>
        public int SuccessfulTracks { get; }

        /// <summary>
        /// Number of tracks skipped (e.g., preview only).
        /// </summary>
        public int SkippedTracks { get; }

        /// <summary>
        /// Number of tracks that failed to download.
        /// </summary>
        public int FailedTracks { get; }

        /// <summary>
        /// Detailed results for each track.
        /// </summary>
        public IReadOnlyList<TrackDownloadResult> TrackResults { get; }

        /// <summary>
        /// Whether any tracks were successfully downloaded.
        /// </summary>
        public bool IsPartialSuccess => SuccessfulTracks > 0;

        /// <summary>
        /// Percentage of tracks successfully downloaded.
        /// </summary>
        public double SuccessPercentage => TotalTracks > 0 ? (double)SuccessfulTracks / TotalTracks * 100 : 0;

        /// <summary>
        /// Creates an exception for a simple error case without track details.
        /// </summary>
        public AlbumDownloadException(string message, string albumId) : base(message)
        {
            AlbumId = albumId;
            AlbumTitle = "Unknown Album";
            TotalTracks = 0;
            SuccessfulTracks = 0;
            SkippedTracks = 0;
            FailedTracks = 0;
            TrackResults = new List<TrackDownloadResult>();
        }

        /// <summary>
        /// Creates an exception for a simple error case with album title.
        /// </summary>
        public AlbumDownloadException(string message, string albumId, string albumTitle) : base(message)
        {
            AlbumId = albumId;
            AlbumTitle = albumTitle;
            TotalTracks = 0;
            SuccessfulTracks = 0;
            SkippedTracks = 0;
            FailedTracks = 0;
            TrackResults = new List<TrackDownloadResult>();
        }

        /// <summary>
        /// Creates an exception for detailed download results.
        /// </summary>
        public AlbumDownloadException(
            string albumId,
            string albumTitle,
            int totalTracks,
            int successfulTracks,
            int skippedTracks,
            int failedTracks,
            IEnumerable<TrackDownloadResult> trackResults)
            : base(CreateMessage(albumTitle, totalTracks, successfulTracks, skippedTracks, failedTracks))
        {
            AlbumId = albumId;
            AlbumTitle = albumTitle;
            TotalTracks = totalTracks;
            SuccessfulTracks = successfulTracks;
            SkippedTracks = skippedTracks;
            FailedTracks = failedTracks;
            TrackResults = trackResults?.ToList() ?? new List<TrackDownloadResult>();
        }

        private static string CreateMessage(string albumTitle, int totalTracks, int successfulTracks, int skippedTracks, int failedTracks)
        {
            if (successfulTracks == 0)
            {
                return $"Failed to download any tracks from album '{albumTitle}'. " +
                       $"{skippedTracks} tracks were preview-only, {failedTracks} tracks failed.";
            }

            var issues = new List<string>();
            if (skippedTracks > 0)
                issues.Add($"{skippedTracks} preview-only");
            if (failedTracks > 0)
                issues.Add($"{failedTracks} failed");

            return $"Album '{albumTitle}' partially downloaded: {successfulTracks}/{totalTracks} tracks successful. " +
                   $"Issues: {string.Join(", ", issues)}.";
        }

        /// <summary>
        /// Gets a summary of track issues grouped by reason.
        /// </summary>
        public Dictionary<TrackUnavailableReason, List<TrackDownloadResult>> GetIssuesSummary()
        {
            return TrackResults
                .Where(r => !r.Success && r.Reason.HasValue)
                .GroupBy(r => r.Reason.Value)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        /// <summary>
        /// Determines if this exception should be treated as a failure based on policy.
        /// </summary>
        /// <param name="minimumSuccessRate">The minimum success rate (0-1) to consider the download successful</param>
        /// <param name="treatPreviewAsFailure">Whether preview-only tracks should be counted as failures</param>
        public bool ShouldTreatAsFailure(double minimumSuccessRate = 0.8, bool treatPreviewAsFailure = false)
        {
            var effectiveSuccessful = SuccessfulTracks;
            var effectiveTotal = TotalTracks;

            if (!treatPreviewAsFailure)
            {
                // Don't count preview tracks against success rate
                effectiveTotal -= SkippedTracks;
            }

            if (effectiveTotal == 0)
                return true; // No downloadable tracks

            var successRate = (double)effectiveSuccessful / effectiveTotal;
            return successRate < minimumSuccessRate;
        }
    }
}