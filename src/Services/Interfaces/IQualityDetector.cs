using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Services.Consolidated;

namespace Lidarr.Plugin.Qobuzarr.Services.Interfaces
{
    /// <summary>
    /// Interface for detecting available audio qualities for tracks.
    /// </summary>
    /// <remarks>
    /// This interface provides quality detection capabilities to determine
    /// which audio formats and bitrates are available for specific tracks.
    /// 
    /// Key Features:
    /// - Track-level quality detection
    /// - Album-level batch quality detection
    /// - Availability checking for specific qualities
    /// - Format support validation
    /// - Efficient batch processing
    /// 
    /// Quality detection helps optimize downloads by determining the best
    /// available quality before attempting stream URL generation.
    /// </remarks>
    public interface IQualityDetector
    {
        /// <summary>
        /// Detects all available qualities for a track.
        /// </summary>
        /// <param name="trackId">The Qobuz track ID</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>List of available quality IDs</returns>
        Task<List<int>> DetectAvailableQualitiesAsync(string trackId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a specific quality is available for a track.
        /// </summary>
        /// <param name="trackId">The Qobuz track ID</param>
        /// <param name="qualityId">The quality ID to check</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>True if the quality is available</returns>
        Task<bool> IsQualityAvailableAsync(string trackId, int qualityId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the highest available quality for a track.
        /// </summary>
        /// <param name="trackId">The Qobuz track ID</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>The highest available quality ID or null if none available</returns>
        Task<int?> GetHighestAvailableQualityAsync(string trackId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Detects available qualities for multiple tracks in batch.
        /// </summary>
        /// <param name="trackIds">List of Qobuz track IDs</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Dictionary mapping track ID to list of available quality IDs</returns>
        Task<Dictionary<string, List<int>>> DetectBatchQualitiesAsync(IReadOnlyList<string> trackIds, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets quality availability summary for tracks.
        /// </summary>
        /// <param name="trackIds">List of Qobuz track IDs</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Quality detection summary</returns>
        Task<QualityDetectionSummary> GetQualityAvailabilitySummaryAsync(IReadOnlyList<string> trackIds, CancellationToken cancellationToken = default);

        /// <summary>
        /// Detects album-level quality availability with intelligent sampling.
        /// </summary>
        /// <param name="album">The album to analyze</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Album quality detection result</returns>
        Task<AlbumQualityResult> DetectAlbumQualityAsync(QobuzAlbum album, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets available qualities for a track as QualityFormat objects.
        /// </summary>
        /// <param name="trackId">The Qobuz track ID</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>List of available quality formats</returns>
        Task<List<QualityFormat>> GetAvailableQualitiesAsync(string trackId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Summary of quality detection results.
    /// </summary>
    public class QualityDetectionSummary
    {
        public int TotalTracks { get; set; }
        public int TracksWithHighRes { get; set; }
        public int TracksWithLossless { get; set; }
        public int TracksWithLossy { get; set; }
        public Dictionary<int, int> QualityAvailabilityCounts { get; set; } = new();
        public string MostCommonQuality { get; set; }
        public List<string> UnavailableTracks { get; set; } = new();
    }
}
