using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Service interface for integrating Lidarr with Qobuz operations including parallel album searching,
    /// validation, and coordinated downloading with proper resource management and concurrency control.
    /// </summary>
    /// <remarks>
    /// This service provides the bridge between Lidarr's wanted albums and Qobuz's catalog,
    /// implementing parallel processing patterns optimized for the *arr ecosystem with:
    /// - Semaphore-based concurrency control
    /// - Resource limits to prevent memory exhaustion
    /// - Progress reporting and cancellation support
    /// - Integration with existing queue and download systems
    /// </remarks>
    public interface ILidarrIntegrationService
    {
        /// <summary>
        /// Retrieves wanted albums from Lidarr with filtering and pagination support.
        /// </summary>
        /// <param name="filterOptions">Optional filter criteria for narrowing the wanted albums list.</param>
        /// <param name="maxAlbums">Maximum number of albums to retrieve (resource limit).</param>
        /// <param name="progress">Optional progress reporter for tracking fetch progress.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>A collection of wanted albums from Lidarr that meet the filter criteria.</returns>
        Task<IEnumerable<LidarrAlbum>> GetFilteredWantedAlbumsAsync(
            LidarrFilterOptions filterOptions = null,
            int maxAlbums = 500,
            IProgress<ProgressReport> progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Searches Qobuz in parallel for multiple Lidarr albums with intelligent concurrency control.
        /// Uses SemaphoreSlim to limit concurrent API calls and prevent rate limiting.
        /// </summary>
        /// <param name="lidarrAlbums">The collection of Lidarr albums to search for on Qobuz.</param>
        /// <param name="maxConcurrency">Maximum number of concurrent Qobuz searches (defaults to Environment.ProcessorCount).</param>
        /// <param name="progress">Optional progress reporter for tracking search completion (0-100%).</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>A dictionary mapping Lidarr albums to their matched Qobuz albums (if found).</returns>
        Task<Dictionary<LidarrAlbum, QobuzAlbum>> SearchQobuzParallelAsync(
            IEnumerable<LidarrAlbum> lidarrAlbums,
            int maxConcurrency = 0,
            IProgress<ProgressReport> progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates albums before download to check availability, quality, and subscription restrictions.
        /// </summary>
        /// <param name="albumMatches">Dictionary of Lidarr to Qobuz album matches to validate.</param>
        /// <param name="preferredQuality">The preferred audio quality for validation checks.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>A collection of validated album matches ready for download.</returns>
        Task<IEnumerable<AlbumDownloadItem>> ValidateAlbumsAsync(
            Dictionary<LidarrAlbum, QobuzAlbum> albumMatches,
            int preferredQuality,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Orchestrates the complete download process for Lidarr albums with parallel execution and retry logic.
        /// Integrates with existing queue system and download services.
        /// </summary>
        /// <param name="downloadItems">The validated album items ready for download.</param>
        /// <param name="outputPath">The base directory where albums should be downloaded.</param>
        /// <param name="maxConcurrency">Maximum number of concurrent downloads.</param>
        /// <param name="progress">Optional progress reporter for tracking download completion.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>A summary of the download operation with success/failure counts and detailed results.</returns>
        Task<DownloadBatchResult> DownloadLidarrAlbumsAsync(
            IEnumerable<AlbumDownloadItem> downloadItems,
            string outputPath,
            int maxConcurrency = 0,
            IProgress<DownloadProgressReport> progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retries failed album downloads with exponential backoff and intelligent error handling.
        /// </summary>
        /// <param name="failedItems">The collection of failed download items to retry.</param>
        /// <param name="maxRetries">Maximum number of retry attempts per item.</param>
        /// <param name="outputPath">The base directory for downloads.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>Results of the retry operation.</returns>
        Task<DownloadBatchResult> RetryFailedDownloadsAsync(
            IEnumerable<DownloadFailureItem> failedItems,
            int maxRetries = 3,
            string outputPath = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets current integration statistics including search success rates, download metrics, and resource usage.
        /// </summary>
        /// <returns>Comprehensive statistics about the integration service performance.</returns>
        IntegrationStatistics GetStatistics();

        /// <summary>
        /// Resets integration statistics and clears internal caches.
        /// </summary>
        void ResetStatistics();

        /// <summary>
        /// Clears the quality profile cache to force fresh data on next request.
        /// </summary>
        void ClearQualityProfileCache();

        /// <summary>
        /// Gets the current queue status including active operations and available slots.
        /// </summary>
        /// <returns>Queue status information.</returns>
        QueueStatus GetQueueStatus();

        /// <summary>
        /// Gets detailed performance metrics from the statistics collector.
        /// </summary>
        /// <returns>Performance metrics with breakdowns by operation type.</returns>
        PerformanceMetrics GetPerformanceMetrics();

        /// <summary>
        /// Gets quality distribution statistics.
        /// </summary>
        /// <returns>Statistics about quality profile usage and selection.</returns>
        QualityStatistics GetQualityStatistics();

        /// <summary>
        /// Gets error analysis with categorization and frequency.
        /// </summary>
        /// <returns>Error analysis information.</returns>
        ErrorAnalysis GetErrorAnalysis();

        /// <summary>
        /// Exports comprehensive statistics for external analysis.
        /// </summary>
        /// <param name="includeRawData">Whether to include raw data points.</param>
        /// <returns>Statistics export data.</returns>
        StatisticsExport ExportStatistics(bool includeRawData = false);

        /// <summary>
        /// Creates a progress tracker for monitoring operations.
        /// </summary>
        /// <param name="totalItems">Total number of items to process.</param>
        /// <param name="operationType">Type of operation being tracked.</param>
        /// <param name="progress">Progress callback.</param>
        /// <returns>Progress tracker instance.</returns>
        IProgressTracker CreateProgressTracker(int totalItems, string operationType, IProgress<ProgressReport> progress = null);

        /// <summary>
        /// Creates a download progress tracker for monitoring download operations.
        /// </summary>
        /// <param name="totalItems">Total number of items to download.</param>
        /// <param name="operationType">Type of download operation.</param>
        /// <param name="progress">Download progress callback.</param>
        /// <returns>Download progress tracker instance.</returns>
        IDownloadProgressTracker CreateDownloadProgressTracker(int totalItems, string operationType, IProgress<DownloadProgressReport> progress = null);
    }

    /// <summary>
    /// Represents an album ready for download with all necessary metadata and validation information.
    /// </summary>
    public class AlbumDownloadItem
    {
        public LidarrAlbum LidarrAlbum { get; set; }
        public QobuzAlbum QobuzAlbum { get; set; }
        public int PreferredQuality { get; set; }
        public string OutputPath { get; set; }
        public DateTime ValidatedAt { get; set; }
        public List<string> ValidationMessages { get; set; } = new();
        
        // Quality profile integration fields
        public LidarrQualityProfile QualityProfile { get; set; }
        public string SelectedQobuzQuality { get; set; }
        public QualityRecommendation QualityRecommendation { get; set; }
    }

    /// <summary>
    /// Represents a failed download item with retry information.
    /// </summary>
    public class DownloadFailureItem
    {
        public AlbumDownloadItem OriginalItem { get; set; }
        public Exception LastException { get; set; }
        public int AttemptCount { get; set; }
        public DateTime LastAttemptAt { get; set; }
        public string FailureReason { get; set; }
    }

    /// <summary>
    /// Comprehensive result of a batch download operation.
    /// </summary>
    public class DownloadBatchResult
    {
        public int TotalItems { get; set; }
        public int SuccessfulDownloads { get; set; }
        public int FailedDownloads { get; set; }
        public int SkippedDownloads { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public List<DownloadSuccessItem> SuccessItems { get; set; } = new();
        public List<DownloadFailureItem> FailureItems { get; set; } = new();
        public List<string> SkippedItems { get; set; } = new();
        public long TotalBytesDownloaded { get; set; }
        public double AverageDownloadSpeed { get; set; } // MB/s
    }

    /// <summary>
    /// Represents a successful download with metadata.
    /// </summary>
    public class DownloadSuccessItem
    {
        public AlbumDownloadItem DownloadItem { get; set; }
        public string DownloadPath { get; set; }
        public TimeSpan DownloadDuration { get; set; }
        public long BytesDownloaded { get; set; }
        public int TracksDownloaded { get; set; }
        public DateTime CompletedAt { get; set; }
    }

    /// <summary>
    /// Progress report for general operations.
    /// </summary>
    public class ProgressReport
    {
        public int Completed { get; set; }
        public int Total { get; set; }
        public double PercentComplete => Total > 0 ? (double)Completed / Total * 100 : 0;
        public string CurrentItem { get; set; }
        public string Phase { get; set; }
        public TimeSpan Elapsed { get; set; }
        public TimeSpan EstimatedRemaining { get; set; }
    }

    /// <summary>
    /// Detailed progress report for download operations.
    /// </summary>
    public class DownloadProgressReport : ProgressReport
    {
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public int SkippedCount { get; set; }
        public long BytesDownloaded { get; set; }
        public double CurrentSpeedMBps { get; set; }
        public string CurrentAlbum { get; set; }
        public string CurrentTrack { get; set; }
    }

    /// <summary>
    /// Comprehensive statistics about integration service performance.
    /// </summary>
    public class IntegrationStatistics
    {
        public int TotalSearches { get; set; }
        public int SuccessfulSearches { get; set; }
        public int FailedSearches { get; set; }
        public double SearchSuccessRate => TotalSearches > 0 ? (double)SuccessfulSearches / TotalSearches * 100 : 0;
        
        public int TotalDownloads { get; set; }
        public int SuccessfulDownloads { get; set; }
        public int FailedDownloads { get; set; }
        public double DownloadSuccessRate => TotalDownloads > 0 ? (double)SuccessfulDownloads / TotalDownloads * 100 : 0;
        
        public long TotalBytesDownloaded { get; set; }
        public TimeSpan TotalDownloadTime { get; set; }
        public double AverageDownloadSpeedMBps => TotalDownloadTime.TotalSeconds > 0 
            ? (TotalBytesDownloaded / 1024.0 / 1024.0) / TotalDownloadTime.TotalSeconds : 0;
        
        public int CurrentConcurrentOperations { get; set; }
        public int PeakConcurrentOperations { get; set; }
        public DateTime LastOperationAt { get; set; }
        
        public Dictionary<string, int> ErrorCounts { get; set; } = new();
        public Dictionary<int, int> QualityDistribution { get; set; } = new();
    }
}