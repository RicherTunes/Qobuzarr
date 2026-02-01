using System.Collections.Generic;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Service for validating stream availability before attempting downloads.
    /// Helps prevent "no quality available" errors by pre-checking track accessibility.
    /// </summary>
    public interface IQobuzStreamAvailabilityService
    {
        /// <summary>
        /// Check if a track has any downloadable streams available.
        /// Returns the available qualities or empty list if none available.
        /// </summary>
        /// <param name="trackId">The Qobuz track ID to check</param>
        /// <returns>List of available quality format IDs, empty if none available</returns>
        Task<List<int>> GetAvailableQualitiesAsync(string trackId);

        /// <summary>
        /// Validate if a track is available in a specific quality.
        /// Includes comprehensive restriction checking.
        /// </summary>
        /// <param name="trackId">The Qobuz track ID</param>
        /// <param name="qualityFormatId">The quality format ID to check</param>
        /// <returns>StreamAvailabilityResult with details about availability</returns>
        Task<StreamAvailabilityResult> ValidateStreamAvailabilityAsync(string trackId, int qualityFormatId);

        /// <summary>
        /// Pre-validate an entire album to identify problematic tracks.
        /// Useful for showing warnings before starting bulk downloads.
        /// </summary>
        /// <param name="album">The album to validate</param>
        /// <param name="preferredQuality">The preferred quality to check for</param>
        /// <returns>Album validation result with per-track details</returns>
        Task<AlbumAvailabilityResult> ValidateAlbumAvailabilityAsync(QobuzAlbum album, int preferredQuality);
    }

    /// <summary>
    /// Result of stream availability validation for a single track/quality combination.
    /// </summary>
    public class StreamAvailabilityResult
    {
        public bool IsAvailable { get; set; }
        public string TrackId { get; set; }
        public int QualityFormatId { get; set; }
        public TrackUnavailableReason? UnavailableReason { get; set; }
        public string RestrictionMessage { get; set; }
        public bool IsPreviewOnly { get; set; }
        public List<int> AlternativeQualities { get; set; } = new List<int>();
    }

    /// <summary>
    /// Result of album-wide availability validation.
    /// </summary>
    public class AlbumAvailabilityResult
    {
        public string AlbumId { get; set; }
        public int RequestedQuality { get; set; }
        public int TotalTracks { get; set; }
        public int AvailableTracks { get; set; }
        public int UnavailableTracks { get; set; }
        public List<TrackAvailabilityInfo> TrackResults { get; set; } = new List<TrackAvailabilityInfo>();

        /// <summary>
        /// True if at least some tracks are downloadable in the requested quality.
        /// </summary>
        public bool IsPartiallyAvailable => AvailableTracks > 0;

        /// <summary>
        /// True if all tracks are available in the requested quality.
        /// </summary>
        public bool IsFullyAvailable => AvailableTracks == TotalTracks;

        /// <summary>
        /// Percentage of tracks available (0-100).
        /// </summary>
        public double AvailabilityPercentage => TotalTracks > 0 ? (double)AvailableTracks / TotalTracks * 100 : 0;
    }

    /// <summary>
    /// Availability information for a single track within an album.
    /// </summary>
    public class TrackAvailabilityInfo
    {
        public string TrackId { get; set; }
        public string TrackTitle { get; set; }
        public int TrackNumber { get; set; }
        public bool IsAvailable { get; set; }
        public TrackUnavailableReason? UnavailableReason { get; set; }
        public string RestrictionMessage { get; set; }
        public List<int> AvailableQualities { get; set; } = new List<int>();
    }
}
