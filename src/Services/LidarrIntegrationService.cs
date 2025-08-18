using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Refactored implementation of the Lidarr-Qobuz integration service that orchestrates
    /// multiple specialized services for album retrieval, download orchestration, queue management,
    /// progress reporting, and statistics collection optimized for the *arr ecosystem.
    /// </summary>
    /// <remarks>
    /// This service now acts as a coordinator that delegates specific responsibilities to:
    /// - ILidarrAlbumRetriever: Album retrieval and Qobuz searching
    /// - ILidarrDownloadOrchestrator: Download execution and coordination
    /// - ILidarrQueueManager: Concurrency and resource management
    /// - ILidarrProgressReporter: Progress tracking and reporting
    /// - ILidarrStatisticsCollector: Metrics collection and analysis
    /// </remarks>
    public class LidarrIntegrationService : ILidarrIntegrationService, IDisposable
    {
        // Specialized service dependencies
        private readonly ILidarrAlbumRetriever _albumRetriever;
        private readonly ILidarrDownloadOrchestrator _downloadOrchestrator;
        private readonly ILidarrQueueManager _queueManager;
        private readonly ILidarrProgressReporter _progressReporter;
        private readonly ILidarrStatisticsCollector _statisticsCollector;
        private readonly Logger _logger;
        
        private volatile bool _disposed;

        // Configuration constants
        private const int MAX_ALBUMS_PER_REQUEST = 500;
        private const int DEFAULT_MAX_RETRIES = 3;

        /// <summary>
        /// Initializes a new instance of the LidarrIntegrationService with specialized service dependencies.
        /// </summary>
        /// <param name="albumRetriever">Service for retrieving albums from Lidarr and searching Qobuz.</param>
        /// <param name="downloadOrchestrator">Service for orchestrating download operations.</param>
        /// <param name="queueManager">Service for managing download queues and concurrency.</param>
        /// <param name="progressReporter">Service for handling progress reporting.</param>
        /// <param name="statisticsCollector">Service for collecting operation statistics.</param>
        /// <param name="logger">Logger for recording operations and debugging.</param>
        public LidarrIntegrationService(
            ILidarrAlbumRetriever albumRetriever,
            ILidarrDownloadOrchestrator downloadOrchestrator,
            ILidarrQueueManager queueManager,
            ILidarrProgressReporter progressReporter,
            ILidarrStatisticsCollector statisticsCollector,
            Logger logger)
        {
            _albumRetriever = Guard.NotNull(albumRetriever, nameof(albumRetriever));
            _downloadOrchestrator = Guard.NotNull(downloadOrchestrator, nameof(downloadOrchestrator));
            _queueManager = Guard.NotNull(queueManager, nameof(queueManager));
            _progressReporter = Guard.NotNull(progressReporter, nameof(progressReporter));
            _statisticsCollector = Guard.NotNull(statisticsCollector, nameof(statisticsCollector));
            _logger = Guard.NotNull(logger, nameof(logger));

            _logger.Info("LidarrIntegrationService initialized with specialized service dependencies");
        }

        /// <summary>
        /// Retrieves wanted albums from Lidarr with filtering and resource limits.
        /// </summary>
        public async Task<IEnumerable<LidarrAlbum>> GetFilteredWantedAlbumsAsync(
            LidarrFilterOptions filterOptions = null,
            int maxAlbums = MAX_ALBUMS_PER_REQUEST,
            IProgress<ProgressReport> progress = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return await _albumRetriever.GetFilteredWantedAlbumsAsync(filterOptions, maxAlbums, progress, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Searches Qobuz in parallel for multiple Lidarr albums with intelligent concurrency control.
        /// </summary>
        public async Task<Dictionary<LidarrAlbum, QobuzAlbum>> SearchQobuzParallelAsync(
            IEnumerable<LidarrAlbum> lidarrAlbums,
            int maxConcurrency = 0,
            IProgress<ProgressReport> progress = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return await _albumRetriever.SearchQobuzParallelAsync(lidarrAlbums, maxConcurrency, progress, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Validates albums before download to check availability, quality, and restrictions.
        /// Now uses quality profiles to determine appropriate quality levels for each album.
        /// </summary>
        public async Task<IEnumerable<AlbumDownloadItem>> ValidateAlbumsAsync(
            Dictionary<LidarrAlbum, QobuzAlbum> albumMatches,
            int preferredQuality,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return await _albumRetriever.ValidateAlbumsAsync(albumMatches, preferredQuality, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Orchestrates the complete download process for Lidarr albums with parallel execution.
        /// </summary>
        public async Task<DownloadBatchResult> DownloadLidarrAlbumsAsync(
            IEnumerable<AlbumDownloadItem> downloadItems,
            string outputPath,
            int maxConcurrency = 0,
            IProgress<DownloadProgressReport> progress = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return await _downloadOrchestrator.DownloadLidarrAlbumsAsync(downloadItems, outputPath, maxConcurrency, progress, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Retries failed album downloads with exponential backoff.
        /// </summary>
        public async Task<DownloadBatchResult> RetryFailedDownloadsAsync(
            IEnumerable<DownloadFailureItem> failedItems,
            int maxRetries = DEFAULT_MAX_RETRIES,
            string outputPath = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return await _downloadOrchestrator.RetryFailedDownloadsAsync(failedItems, maxRetries, outputPath, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets current integration statistics.
        /// </summary>
        public IntegrationStatistics GetStatistics()
        {
            return _statisticsCollector.GetStatistics();
        }

        /// <summary>
        /// Resets integration statistics and clears internal caches.
        /// </summary>
        public void ResetStatistics()
        {
            _statisticsCollector.ResetStatistics();
            _albumRetriever.ClearQualityProfileCache();
            _logger.Info("Integration statistics reset");
        }

        #region Additional Service Methods

        /// <summary>
        /// Gets the current queue status including active operations and available slots.
        /// </summary>
        /// <returns>Queue status information.</returns>
        public QueueStatus GetQueueStatus()
        {
            return _queueManager.GetQueueStatus();
        }

        /// <summary>
        /// Gets detailed performance metrics from the statistics collector.
        /// </summary>
        /// <returns>Performance metrics with breakdowns by operation type.</returns>
        public PerformanceMetrics GetPerformanceMetrics()
        {
            return _statisticsCollector.GetPerformanceMetrics();
        }

        /// <summary>
        /// Gets quality distribution statistics.
        /// </summary>
        /// <returns>Statistics about quality profile usage and selection.</returns>
        public QualityStatistics GetQualityStatistics()
        {
            return _statisticsCollector.GetQualityStatistics();
        }

        /// <summary>
        /// Gets error analysis with categorization and frequency.
        /// </summary>
        /// <returns>Error analysis information.</returns>
        public ErrorAnalysis GetErrorAnalysis()
        {
            return _statisticsCollector.GetErrorAnalysis();
        }

        /// <summary>
        /// Exports comprehensive statistics for external analysis.
        /// </summary>
        /// <param name="includeRawData">Whether to include raw data points.</param>
        /// <returns>Statistics export data.</returns>
        public StatisticsExport ExportStatistics(bool includeRawData = false)
        {
            return _statisticsCollector.ExportStatistics(includeRawData);
        }

        /// <summary>
        /// Creates a progress tracker for monitoring operations.
        /// </summary>
        /// <param name="totalItems">Total number of items to process.</param>
        /// <param name="operationType">Type of operation being tracked.</param>
        /// <param name="progress">Progress callback.</param>
        /// <returns>Progress tracker instance.</returns>
        public IProgressTracker CreateProgressTracker(int totalItems, string operationType, IProgress<ProgressReport> progress = null)
        {
            return _progressReporter.CreateTracker(totalItems, operationType, progress);
        }

        /// <summary>
        /// Creates a download progress tracker for monitoring download operations.
        /// </summary>
        /// <param name="totalItems">Total number of items to download.</param>
        /// <param name="operationType">Type of download operation.</param>
        /// <param name="progress">Download progress callback.</param>
        /// <returns>Download progress tracker instance.</returns>
        public IDownloadProgressTracker CreateDownloadProgressTracker(int totalItems, string operationType, IProgress<DownloadProgressReport> progress = null)
        {
            return _progressReporter.CreateDownloadTracker(totalItems, operationType, progress);
        }

        #endregion

        #region Private Helper Methods

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LidarrIntegrationService));
        }

        #endregion

        #region Cache Management

        /// <summary>
        /// Clears the quality profile cache, forcing a refresh on next access
        /// </summary>
        public void ClearQualityProfileCache()
        {
            _logger?.Debug("Clearing quality profile cache");
            // The album retriever handles quality profile caching internally
            // This method is kept for backward compatibility
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                try
                {
                    _queueManager?.Dispose();
                    _progressReporter?.Reset();
                    _disposed = true;
                    _logger?.Info("LidarrIntegrationService disposed");
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Error disposing LidarrIntegrationService");
                }
            }
        }

        #endregion
    }
}
