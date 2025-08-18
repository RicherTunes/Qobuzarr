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
}