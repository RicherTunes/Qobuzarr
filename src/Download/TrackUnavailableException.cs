using System;

namespace Lidarr.Plugin.Qobuzarr.Download
{
    /// <summary>
    /// Exception thrown when a track cannot be downloaded due to various restrictions or availability issues.
    /// Provides detailed information about why the track is unavailable for better error handling.
    /// </summary>
    public class TrackUnavailableException : Exception
    {
        /// <summary>
        /// The Qobuz track ID that is unavailable.
        /// </summary>
        public string TrackId { get; }

        /// <summary>
        /// The specific reason why the track is unavailable.
        /// </summary>
        public TrackUnavailableReason Reason { get; }

        /// <summary>
        /// Initializes a new instance of TrackUnavailableException.
        /// </summary>
        /// <param name="trackId">The Qobuz track ID that is unavailable</param>
        /// <param name="message">A detailed message explaining why the track is unavailable</param>
        /// <param name="reason">The categorized reason for unavailability</param>
        public TrackUnavailableException(string trackId, string message, TrackUnavailableReason reason)
            : base($"Track {trackId} is unavailable: {message}")
        {
            TrackId = trackId;
            Reason = reason;
        }

        /// <summary>
        /// Initializes a new instance of TrackUnavailableException with an inner exception.
        /// </summary>
        /// <param name="trackId">The Qobuz track ID that is unavailable</param>
        /// <param name="message">A detailed message explaining why the track is unavailable</param>
        /// <param name="reason">The categorized reason for unavailability</param>
        /// <param name="innerException">The exception that caused this track to be unavailable</param>
        public TrackUnavailableException(string trackId, string message, TrackUnavailableReason reason, Exception innerException)
            : base($"Track {trackId} is unavailable: {message}", innerException)
        {
            TrackId = trackId;
            Reason = reason;
        }

        /// <summary>
        /// Gets a user-friendly description of the unavailability reason.
        /// </summary>
        /// <returns>A human-readable description suitable for displaying to users</returns>
        public string GetUserFriendlyMessage()
        {
            return Reason switch
            {
                TrackUnavailableReason.RegionalRestriction => "This track is not available in your region",
                TrackUnavailableReason.SubscriptionRestriction => "This track requires a higher subscription tier",
                TrackUnavailableReason.PreviewOnly => "Only a preview/sample of this track is available",
                TrackUnavailableReason.NoQualityAvailable => "No suitable audio quality found for this track",
                TrackUnavailableReason.NotStreamable => "This track is not available for streaming",
                TrackUnavailableReason.Restricted => "This track has download restrictions",
                TrackUnavailableReason.ApiError => "Technical error occurred while accessing this track",
                TrackUnavailableReason.Unknown => "Track is unavailable for an unknown reason",
                _ => "Track is currently unavailable"
            };
        }

        /// <summary>
        /// Determines if this exception represents a permanent or temporary unavailability.
        /// </summary>
        /// <returns>True if the issue might be temporary and worth retrying later</returns>
        public bool IsTemporary()
        {
            return Reason == TrackUnavailableReason.ApiError;
        }

        /// <summary>
        /// Determines if this exception represents a user-resolvable issue.
        /// </summary>
        /// <returns>True if the user might be able to resolve this by changing settings or subscription</returns>
        public bool IsUserResolvable()
        {
            return Reason == TrackUnavailableReason.SubscriptionRestriction;
        }
    }

    /// <summary>
    /// Categorized reasons why a track might be unavailable for download.
    /// This enum is used throughout the codebase for consistent error categorization.
    /// </summary>
    public enum TrackUnavailableReason
    {
        /// <summary>
        /// Track is geo-blocked in the user's region.
        /// </summary>
        RegionalRestriction,

        /// <summary>
        /// Track requires a higher subscription tier to access.
        /// </summary>
        SubscriptionRestriction,

        /// <summary>
        /// Only preview/sample versions are available, not full tracks.
        /// </summary>
        PreviewOnly,

        /// <summary>
        /// No suitable audio quality/format is available for this track.
        /// Also used when a specific format is requested but not available.
        /// </summary>
        NoQualityAvailable,

        /// <summary>
        /// Track is marked as not streamable.
        /// </summary>
        NotStreamable,

        /// <summary>
        /// Track has some other form of download restriction.
        /// </summary>
        Restricted,

        /// <summary>
        /// Technical error occurred while trying to access the track.
        /// </summary>
        ApiError,

        /// <summary>
        /// Unknown reason for unavailability.
        /// </summary>
        Unknown
    }

    /// <summary>
    /// Classification helpers for <see cref="TrackUnavailableReason"/>.
    /// </summary>
    public static class TrackUnavailableReasonExtensions
    {
        /// <summary>
        /// True when the reason is a Qobuz rights gate that will NEVER lift for a re-grab of the exact
        /// same catalog entry: purchase-only content or an insufficient subscription tier. Used solely to
        /// decide whether an album-download failure should cause the album's release to be suppressed
        /// from future indexer searches (see <c>RestrictedReleaseSuppressionStore</c> /
        /// <c>QobuzDownloadClient.PerformDownloadAsync</c>) — it does NOT change the album-completion
        /// decision (an incomplete album still always reports Failed; see CLAUDE.md "Album-completion
        /// contract"). Lidarr's blocklist provably does not fire for this failure mode on the live
        /// instance (55+ failures, 0 blocklist entries), so suppression happens independently, purely by
        /// the qobuz indexer refusing to offer the album again — not by relying on blocklist.
        ///
        /// <para><see cref="TrackUnavailableReason.RegionalRestriction"/> (geo) is deliberately EXCLUDED
        /// even though it is also a rights gate: geo-availability can change (VPN, catalog rollout by
        /// region), and permanently hiding a release that might become available is a worse failure mode
        /// than the bounded re-grab loop it would otherwise cause. <see cref="TrackUnavailableReason.PreviewOnly"/>
        /// and <see cref="TrackUnavailableReason.NoQualityAvailable"/> are excluded — already handled as a
        /// distinct "skip" concept elsewhere, out of scope for this fix.
        /// <see cref="TrackUnavailableReason.ApiError"/>, <see cref="TrackUnavailableReason.NotStreamable"/>,
        /// and <see cref="TrackUnavailableReason.Unknown"/> are excluded because they may be transient
        /// (network blip, temporary Qobuz-side issue) or symptomatic of a genuine edition mismatch (the
        /// Aphex-Twin case) — suppressing on them risks permanently hiding an album that could actually
        /// succeed via a different edition.</para>
        /// </summary>
        public static bool IsPermanentlyUnavailable(this TrackUnavailableReason reason) => reason switch
        {
            TrackUnavailableReason.SubscriptionRestriction => true,
            TrackUnavailableReason.Restricted => true,
            _ => false,
        };
    }
}
