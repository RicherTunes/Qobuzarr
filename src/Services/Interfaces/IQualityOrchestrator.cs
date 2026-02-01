using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Services.Consolidated;

namespace Lidarr.Plugin.Qobuzarr.Services.Interfaces
{
    /// <summary>
    /// Interface for orchestrating quality-related services.
    /// </summary>
    /// <remarks>
    /// This interface coordinates quality detection, selection, fallback, and streaming
    /// URL generation across the quality domain services.
    /// 
    /// Key Features:
    /// - Quality selection with fallback
    /// - Batch quality processing
    /// - Quality detection coordination
    /// - Stream URL generation with quality management
    /// - Fallback chain management
    /// 
    /// This orchestrator provides a unified interface for all quality-related
    /// operations while coordinating multiple underlying services.
    /// </remarks>
    public interface IQualityOrchestrator
    {
        /// <summary>
        /// Selects the best available quality for a track with fallback.
        /// </summary>
        /// <param name="trackId">The Qobuz track ID</param>
        /// <param name="preferredQuality">The preferred quality ID</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Quality selection result with stream URL</returns>
        Task<QualitySelectionResult> SelectBestQualityAsync(string trackId, int preferredQuality, CancellationToken cancellationToken = default);

        /// <summary>
        /// Detects all available qualities for a track.
        /// </summary>
        /// <param name="trackId">The Qobuz track ID</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Quality detection result</returns>
        Task<QualityDetectionResult> DetectAvailableQualitiesAsync(string trackId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Processes quality selection for multiple tracks in batch.
        /// </summary>
        /// <param name="trackIds">List of Qobuz track IDs</param>
        /// <param name="preferredQuality">The preferred quality ID</param>
        /// <param name="maxConcurrency">Maximum concurrent operations</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Dictionary mapping track ID to quality selection results</returns>
        Task<Dictionary<string, QualitySelectionResult>> ProcessBatchQualityAsync(IReadOnlyList<string> trackIds, int preferredQuality, int maxConcurrency = 5, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the quality fallback chain for a preferred quality.
        /// </summary>
        /// <param name="preferredQuality">The preferred quality ID</param>
        /// <returns>List of quality IDs in fallback order</returns>
        List<int> GetFallbackChain(int preferredQuality);

        /// <summary>
        /// Maps a Lidarr quality to a Qobuz quality ID.
        /// </summary>
        /// <param name="lidarrQuality">The Lidarr quality object</param>
        /// <returns>The corresponding Qobuz quality ID</returns>
        int MapLidarrQualityToQobuz(object lidarrQuality);

        /// <summary>
        /// Gets streaming information for a track with quality management.
        /// </summary>
        /// <param name="trackId">The Qobuz track ID</param>
        /// <param name="preferredQuality">The preferred quality ID</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Stream information with selected quality</returns>
        Task<StreamInfo> GetStreamInfoAsync(string trackId, int preferredQuality, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Result of quality selection operation.
    /// </summary>
    public class QualitySelectionResult
    {
        public bool Success { get; set; }
        public int QualityId { get; set; }
        public string QualityName { get; set; }
        public string StreamUrl { get; set; }
        public bool IsFallbackQuality { get; set; }
        public int OriginalPreferredQuality { get; set; }
        public string Error { get; set; }
        public System.TimeSpan ProcessingTime { get; set; }
    }

    /// <summary>
    /// Result of quality detection operation.
    /// </summary>
    public class QualityDetectionResult
    {
        public bool Success { get; set; }
        public List<int> AvailableQualities { get; set; } = new();
        public int? HighestAvailableQuality { get; set; }
        public string Error { get; set; }
        public System.TimeSpan DetectionTime { get; set; }
    }

    /// <summary>
    /// Streaming information with quality details.
    /// </summary>
    public class StreamInfo
    {
        public string Url { get; set; }
        public int QualityId { get; set; }
        public string QualityName { get; set; }
        public long? FileSizeBytes { get; set; }
        public System.TimeSpan? Duration { get; set; }
        public System.DateTime? ExpiresAt { get; set; }
    }
}
