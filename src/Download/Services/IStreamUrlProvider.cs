using System.Collections.Generic;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Interface for obtaining streaming URLs from Qobuz API with quality fallback
    /// </summary>
    public interface IStreamUrlProvider
    {
        /// <summary>
        /// Gets streaming URL for a track with automatic quality fallback
        /// </summary>
        /// <param name="trackId">Qobuz track ID</param>
        /// <param name="preferredQuality">Preferred quality format ID</param>
        /// <returns>Streaming URL or null if track is unavailable</returns>
        Task<string> GetStreamUrlAsync(string trackId, int preferredQuality);

        /// <summary>
        /// Gets streaming URL for a track with enhanced logging using track and album context
        /// </summary>
        /// <param name="track">Full track object for enhanced logging</param>
        /// <param name="album">Album context for enhanced logging</param>
        /// <param name="preferredQuality">Preferred quality format ID</param>
        /// <returns>Streaming URL or null if track is unavailable</returns>
        Task<string> GetStreamUrlAsync(QobuzTrack track, QobuzAlbum album, int preferredQuality);

        /// <summary>
        /// Determines if a URL is a preview/sample (not full track)
        /// </summary>
        bool IsPreviewOrSampleUrl(string url);

        /// <summary>
        /// Probes a set of qualities and returns the first playable URL or a categorized reason.
        /// Does not throw for rights/availability outcomes; callers can decide on alternates.
        /// </summary>
        /// <param name="trackId">Qobuz track id</param>
        /// <param name="qualityChain">Ordered quality ids to try</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Probe result with url or unavailability reason</returns>
        Task<StreamProbeResult> TryGetStreamUrlAsync(string trackId, IReadOnlyList<int> qualityChain, System.Threading.CancellationToken cancellationToken);
    }

    /// <summary>
    /// Non-throwing probe result for stream URL resolution.
    /// </summary>
    public sealed class StreamProbeResult
    {
        public bool Success { get; set; }
        public string? Url { get; set; }
        public int? FormatId { get; set; }
        public Lidarr.Plugin.Qobuzarr.Download.TrackUnavailableReason Reason { get; set; } = Lidarr.Plugin.Qobuzarr.Download.TrackUnavailableReason.Unknown;
        public string? Detail { get; set; }
    }
}
