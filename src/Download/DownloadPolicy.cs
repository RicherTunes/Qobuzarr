using System;
using Lidarr.Plugin.Common.Services.Download;

namespace Lidarr.Plugin.Qobuzarr.Download
{
    /// <summary>
    /// Configurable policy for album download behavior and success criteria.
    /// </summary>
    public class DownloadPolicy
    {
        /// <summary>
        /// Minimum percentage of tracks that must be successfully downloaded
        /// for an album to be considered successfully downloaded.
        /// Range: 0.0 (0%) to 1.0 (100%)
        /// Default: 0.8 (80%)
        /// </summary>
        public double MinimumSuccessRate { get; set; } = 0.8;

        /// <summary>
        /// Whether preview-only tracks should be counted as failures
        /// when calculating the success rate.
        /// Default: false (previews are not counted against success rate)
        /// </summary>
        public bool TreatPreviewAsFailure { get; set; } = false;

        /// <summary>
        /// Whether to fail the entire album download if no tracks are available.
        /// Default: true
        /// </summary>
        public bool FailOnNoTracksAvailable { get; set; } = true;

        /// <summary>
        /// Maximum number of concurrent track downloads per album.
        /// Range: 1-10
        /// Default: 3
        /// </summary>
        public int MaxConcurrentTrackDownloads { get; set; } = 3;

        /// <summary>
        /// Whether to continue downloading remaining tracks after encountering failures.
        /// Default: true (best effort - download what's available)
        /// </summary>
        public bool ContinueOnTrackFailure { get; set; } = true;

        /// <summary>
        /// Whether to automatically retry failed tracks with lower quality.
        /// Default: true
        /// </summary>
        public bool EnableQualityFallback { get; set; } = true;

        /// <summary>
        /// Whether to skip tracks that are only available as previews/samples.
        /// Default: true (skip preview-only tracks)
        /// </summary>
        public bool SkipPreviewTracks { get; set; } = true;

        /// <summary>
        /// Action to take when regional restrictions are encountered.
        /// </summary>
        public RegionalRestrictionAction RegionalRestrictionAction { get; set; } = RegionalRestrictionAction.Fail;

        /// <summary>
        /// Creates a strict policy that requires all tracks to be downloaded successfully.
        /// </summary>
        public static DownloadPolicy CreateStrict()
        {
            return new DownloadPolicy
            {
                MinimumSuccessRate = 1.0,
                TreatPreviewAsFailure = true,
                FailOnNoTracksAvailable = true,
                ContinueOnTrackFailure = false,
                SkipPreviewTracks = false,
                RegionalRestrictionAction = RegionalRestrictionAction.Fail
            };
        }

        /// <summary>
        /// Creates a lenient policy that downloads whatever is available.
        /// </summary>
        public static DownloadPolicy CreateLenient()
        {
            return new DownloadPolicy
            {
                MinimumSuccessRate = 0.5,
                TreatPreviewAsFailure = false,
                FailOnNoTracksAvailable = false,
                ContinueOnTrackFailure = true,
                SkipPreviewTracks = true,
                RegionalRestrictionAction = RegionalRestrictionAction.Skip
            };
        }

        /// <summary>
        /// Validates the policy configuration.
        /// </summary>
        public void Validate()
        {
            if (MinimumSuccessRate < 0 || MinimumSuccessRate > 1)
                throw new ArgumentOutOfRangeException(nameof(MinimumSuccessRate), "Must be between 0.0 and 1.0");

            if (MaxConcurrentTrackDownloads < 1 || MaxConcurrentTrackDownloads > 10)
                throw new ArgumentOutOfRangeException(nameof(MaxConcurrentTrackDownloads), "Must be between 1 and 10");
        }

        /// <summary>
        /// Determines if an album download should be considered successful based on the results.
        /// </summary>
        /// <remarks>
        /// An album is successful ONLY when every track ended up on disk
        /// (<paramref name="successfulTracks"/> == <paramref name="totalTracks"/>). Any deficit —
        /// whether a track failed outright or was sample/preview-skipped — leaves the album
        /// incomplete, and Lidarr's NoMissingOrUnmatchedTracksSpecification permanently rejects
        /// an incomplete release ("Has missing tracks"), so it would never import anyway.
        ///
        /// This was a live-confirmed defect: a single unfetchable track (sample stream /
        /// removed / geo-locked) on a 30-track album left 29 good FLACs marked "successful"
        /// (96.7% >= the 80% MinimumSuccessRate), Lidarr then permanently rejected the import,
        /// and nothing retried — the whole album was silently wasted. Reporting failure instead
        /// lets Lidarr blocklist the release and re-search across indexers (fall back to a
        /// complete source). This mirrors Tidalarr's download client, which marks the item
        /// Failed whenever any track failed.
        ///
        /// The threshold knobs (<see cref="MinimumSuccessRate"/>, <see cref="TreatPreviewAsFailure"/>)
        /// are retained for the empty-album edge case and forward compatibility, but they can
        /// only ever gate a COMPLETE album — they can never rescue an incomplete one.
        /// </remarks>
        public bool IsAlbumDownloadSuccessful(int totalTracks, int successfulTracks, int skippedTracks)
            => AlbumCompletionPolicy.IsAlbumDownloadSuccessful(
                totalTracks,
                successfulTracks,
                skippedTracks,
                MinimumSuccessRate,
                TreatPreviewAsFailure,
                FailOnNoTracksAvailable);
    }

    /// <summary>
    /// Action to take when encountering regional restrictions.
    /// </summary>
    public enum RegionalRestrictionAction
    {
        /// <summary>
        /// Fail the download when regional restrictions are encountered.
        /// </summary>
        Fail,

        /// <summary>
        /// Skip regionally restricted tracks and continue with others.
        /// </summary>
        Skip,

        /// <summary>
        /// Log a warning but treat as successful if other tracks download.
        /// </summary>
        Warn
    }
}
