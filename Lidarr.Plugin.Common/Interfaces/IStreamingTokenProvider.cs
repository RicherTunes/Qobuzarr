using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Models;

namespace Lidarr.Plugin.Common.Interfaces
{
    /// <summary>
    /// Interface for streaming service token providers.
    /// Handles authentication token management for streaming services.
    /// </summary>
    public interface IStreamingTokenProvider
    {
        /// <summary>
        /// Gets a valid access token for API calls.
        /// </summary>
        /// <returns>Valid access token or null if authentication failed</returns>
        Task<string> GetAccessTokenAsync();

        /// <summary>
        /// Refreshes an expired token if the service supports it.
        /// </summary>
        /// <returns>New access token or null if refresh failed/not supported</returns>
        Task<string> RefreshTokenAsync();

        /// <summary>
        /// Validates if a token is still valid.
        /// </summary>
        /// <param name="token">Token to validate</param>
        /// <returns>True if token is valid and can be used for API calls</returns>
        Task<bool> ValidateTokenAsync(string token);

        /// <summary>
        /// Gets token expiration time if known.
        /// </summary>
        /// <param name="token">Token to check</param>
        /// <returns>Expiration time or null if unknown</returns>
        DateTime? GetTokenExpiration(string token);

        /// <summary>
        /// Clears any cached authentication data.
        /// </summary>
        void ClearAuthenticationCache();

        /// <summary>
        /// Whether this provider supports token refresh.
        /// </summary>
        bool SupportsRefresh { get; }

        /// <summary>
        /// Service name for this token provider.
        /// </summary>
        string ServiceName { get; }
    }

    /// <summary>
    /// Interface for streaming service download orchestration.
    /// Coordinates download operations across streaming services.
    /// </summary>
    public interface IStreamingDownloadOrchestrator
    {
        /// <summary>
        /// Downloads an album from the streaming service.
        /// </summary>
        /// <param name="albumId">Album identifier</param>
        /// <param name="outputDirectory">Directory to save files</param>
        /// <param name="quality">Preferred quality (optional)</param>
        /// <param name="progress">Progress callback (optional)</param>
        /// <returns>Download result with success status and file paths</returns>
        Task<DownloadResult> DownloadAlbumAsync(
            string albumId, 
            string outputDirectory, 
            StreamingQuality quality = null,
            IProgress<DownloadProgress> progress = null);

        /// <summary>
        /// Downloads a single track from the streaming service.
        /// </summary>
        /// <param name="trackId">Track identifier</param>
        /// <param name="outputPath">Path to save the file</param>
        /// <param name="quality">Preferred quality (optional)</param>
        /// <returns>Download result with success status and file path</returns>
        Task<TrackDownloadResult> DownloadTrackAsync(
            string trackId, 
            string outputPath, 
            StreamingQuality quality = null);

        /// <summary>
        /// Gets available qualities for a specific album or track.
        /// </summary>
        /// <param name="contentId">Album or track identifier</param>
        /// <returns>List of available qualities</returns>
        Task<List<StreamingQuality>> GetAvailableQualitiesAsync(string contentId);

        /// <summary>
        /// Estimates download size for planning purposes.
        /// </summary>
        /// <param name="albumId">Album identifier</param>
        /// <param name="quality">Target quality</param>
        /// <returns>Estimated size in bytes</returns>
        Task<long> EstimateDownloadSizeAsync(string albumId, StreamingQuality quality = null);

        /// <summary>
        /// Cancels an ongoing download operation.
        /// </summary>
        /// <param name="downloadId">Download operation identifier</param>
        Task CancelDownloadAsync(string downloadId);

        /// <summary>
        /// Service name for this orchestrator.
        /// </summary>
        string ServiceName { get; }
    }

    /// <summary>
    /// Result of a download operation.
    /// </summary>
    public class DownloadResult
    {
        /// <summary>
        /// Whether the download was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Downloaded file paths.
        /// </summary>
        public List<string> FilePaths { get; set; } = new List<string>();

        /// <summary>
        /// Total download size in bytes.
        /// </summary>
        public long TotalSize { get; set; }

        /// <summary>
        /// Time taken for the download.
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Error message if download failed.
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Additional metadata about the download.
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Individual track download results.
        /// </summary>
        public List<TrackDownloadResult> TrackResults { get; set; } = new List<TrackDownloadResult>();
    }

    /// <summary>
    /// Result of downloading a single track.
    /// </summary>
    public class TrackDownloadResult
    {
        /// <summary>
        /// Track identifier.
        /// </summary>
        public string TrackId { get; set; }

        /// <summary>
        /// Whether the track download was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Path to the downloaded file.
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// File size in bytes.
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// Actual quality of the downloaded file.
        /// </summary>
        public StreamingQuality ActualQuality { get; set; }

        /// <summary>
        /// Time taken to download this track.
        /// </summary>
        public TimeSpan DownloadTime { get; set; }

        /// <summary>
        /// Error message if download failed.
        /// </summary>
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Progress information for download operations.
    /// </summary>
    public class DownloadProgress
    {
        /// <summary>
        /// Number of tracks completed.
        /// </summary>
        public int CompletedTracks { get; set; }

        /// <summary>
        /// Total number of tracks.
        /// </summary>
        public int TotalTracks { get; set; }

        /// <summary>
        /// Progress percentage (0-100).
        /// </summary>
        public double PercentComplete { get; set; }

        /// <summary>
        /// Currently downloading track.
        /// </summary>
        public string CurrentTrack { get; set; }

        /// <summary>
        /// Estimated time remaining.
        /// </summary>
        public TimeSpan? EstimatedTimeRemaining { get; set; }

        /// <summary>
        /// Current download speed in bytes per second.
        /// </summary>
        public long BytesPerSecond { get; set; }
    }
}