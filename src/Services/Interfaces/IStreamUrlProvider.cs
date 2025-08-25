using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Services.Interfaces
{
    /// <summary>
    /// Interface for providing validated streaming URLs from Qobuz.
    /// </summary>
    /// <remarks>
    /// This interface combines API calls, quality fallback, and URL validation
    /// to provide reliable streaming URLs for tracks.
    /// 
    /// Key Features:
    /// - Validated streaming URL generation
    /// - Quality fallback integration
    /// - Batch URL generation
    /// - URL caching and reuse
    /// - Error handling and retries
    /// 
    /// This service is the primary interface for obtaining streaming URLs
    /// with built-in reliability and validation.
    /// </remarks>
    public interface IStreamUrlProvider
    {
        /// <summary>
        /// Gets a validated streaming URL for a track with quality fallback.
        /// </summary>
        /// <param name="trackId">The Qobuz track ID</param>
        /// <param name="preferredQuality">The preferred quality ID</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Stream URL result with selected quality</returns>
        Task<StreamUrlResult> GetStreamUrlAsync(string trackId, int preferredQuality, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets streaming URLs for multiple tracks with quality fallback.
        /// </summary>
        /// <param name="trackIds">List of Qobuz track IDs</param>
        /// <param name="preferredQuality">The preferred quality ID</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Dictionary mapping track ID to stream URL results</returns>
        Task<Dictionary<string, StreamUrlResult>> GetBatchStreamUrlsAsync(IReadOnlyList<string> trackIds, int preferredQuality, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a streaming URL for a specific quality without fallback.
        /// </summary>
        /// <param name="trackId">The Qobuz track ID</param>
        /// <param name="qualityId">The exact quality ID required</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Stream URL result or null if quality not available</returns>
        Task<StreamUrlResult?> GetExactQualityStreamUrlAsync(string trackId, int qualityId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates and refreshes an existing streaming URL if needed.
        /// </summary>
        /// <param name="url">The existing streaming URL</param>
        /// <param name="trackId">The associated track ID</param>
        /// <param name="qualityId">The associated quality ID</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Refreshed URL result or null if refresh not possible</returns>
        Task<StreamUrlResult?> RefreshStreamUrlAsync(string url, string trackId, int qualityId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a streaming URL with fallback using a provided fallback chain.
        /// </summary>
        /// <param name="trackId">The Qobuz track ID</param>
        /// <param name="fallbackChain">List of quality formats to try in order</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Stream URL result with quality that worked</returns>
        Task<StreamUrlResult> GetStreamUrlWithFallbackAsync(string trackId, IReadOnlyList<QualityFormat> fallbackChain, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Result of streaming URL generation.
    /// </summary>
    public class StreamUrlResult
    {
        public bool Success { get; set; }
        public string StreamUrl { get; set; }
        public int QualityId { get; set; }
        public string QualityName { get; set; }
        public long? FileSizeBytes { get; set; }
        public System.TimeSpan? Duration { get; set; }
        public System.DateTime? ExpiresAt { get; set; }
        public bool IsFallbackQuality { get; set; }
        public int OriginalPreferredQuality { get; set; }
        public string Error { get; set; }
        public System.TimeSpan GenerationTime { get; set; }
    }
}