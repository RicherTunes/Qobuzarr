using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Service responsible for managing download queues and concurrent operations
    /// with comprehensive resource management optimized for the *arr ecosystem.
    /// </summary>
    public class LidarrQueueManager : ILidarrQueueManager, IDisposable
    {
        private readonly Logger _logger;
        private readonly object _statsLock = new();
        private readonly QueueStatistics _statistics = new();

        // Semaphores for controlling concurrency
        private SemaphoreSlim _downloadSemaphore;
        private SemaphoreSlim _searchSemaphore;

        // Configuration
        private int _maxConcurrentDownloads;
        private int _maxConcurrentSearches;

        // Disposal tracking
        private volatile bool _disposed;

        // Configuration constants
        private const int DEFAULT_MAX_CONCURRENCY = 0; // Will use Environment.ProcessorCount
        private const int MIN_CONCURRENCY = 1;
        private const int MAX_CONCURRENCY = 20;

        /// <summary>
        /// Initializes a new instance of the LidarrQueueManager with specified concurrency limits.
        /// </summary>
        /// <param name="logger">Logger for recording operations and debugging.</param>
        /// <param name="maxConcurrentDownloads">Maximum concurrent downloads. 0 to use processor count.</param>
        /// <param name="maxConcurrentSearches">Maximum concurrent searches. 0 to use processor count.</param>
        public LidarrQueueManager(
            Logger logger,
            int maxConcurrentDownloads = DEFAULT_MAX_CONCURRENCY,
            int maxConcurrentSearches = DEFAULT_MAX_CONCURRENCY)
        {
            _logger = Guard.NotNull(logger, nameof(logger));

            _maxConcurrentDownloads = GetEffectiveConcurrency(maxConcurrentDownloads);
            _maxConcurrentSearches = GetEffectiveConcurrency(maxConcurrentSearches);

            _downloadSemaphore = new SemaphoreSlim(_maxConcurrentDownloads, _maxConcurrentDownloads);
            _searchSemaphore = new SemaphoreSlim(_maxConcurrentSearches, _maxConcurrentSearches);

            _logger.Info("LidarrQueueManager initialized - Downloads: {0}, Searches: {1}",
                _maxConcurrentDownloads, _maxConcurrentSearches);
        }

        /// <summary>
        /// Gets the current number of active download operations.
        /// </summary>
        public int ActiveDownloadCount => _maxConcurrentDownloads - _downloadSemaphore.CurrentCount;

        /// <summary>
        /// Gets the current number of active search operations.
        /// </summary>
        public int ActiveSearchCount => _maxConcurrentSearches - _searchSemaphore.CurrentCount;

        /// <summary>
        /// Gets the maximum allowed concurrent downloads.
        /// </summary>
        public int MaxConcurrentDownloads => _maxConcurrentDownloads;

        /// <summary>
        /// Gets the maximum allowed concurrent searches.
        /// </summary>
        public int MaxConcurrentSearches => _maxConcurrentSearches;

        /// <summary>
        /// Acquires a download slot, waiting if necessary until one becomes available.
        /// </summary>
        public async Task AcquireDownloadSlotAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var stopwatch = Stopwatch.StartNew();
            var wasAtCapacity = _downloadSemaphore.CurrentCount == 0;

            await _downloadSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            lock (_statsLock)
            {
                _statistics.TotalDownloadSlotAcquisitions++;

                // Update average wait time
                var totalWaitTime = _statistics.AverageDownloadWaitTime.Ticks * (_statistics.TotalDownloadSlotAcquisitions - 1) + stopwatch.Elapsed.Ticks;
                _statistics.AverageDownloadWaitTime = new TimeSpan(totalWaitTime / _statistics.TotalDownloadSlotAcquisitions);

                // Update peak concurrency
                var currentActive = ActiveDownloadCount;
                _statistics.PeakConcurrentDownloads = Math.Max(_statistics.PeakConcurrentDownloads, currentActive);

                if (wasAtCapacity)
                {
                    _statistics.DownloadQueueSaturations++;
                }
            }

            _logger.Debug("Download slot acquired - Active: {0}/{1}, Wait: {2:F1}ms",
                ActiveDownloadCount, _maxConcurrentDownloads, stopwatch.Elapsed.TotalMilliseconds);
        }

        /// <summary>
        /// Releases a previously acquired download slot.
        /// </summary>
        public void ReleaseDownloadSlot()
        {
            ThrowIfDisposed();

            try
            {
                _downloadSemaphore.Release();
            }
            catch (SemaphoreFullException ex)
            {
                _logger.Warn(ex, "ReleaseDownloadSlot called when no slot was acquired; ignoring");
            }

            _logger.Debug("Download slot released - Active: {0}/{1}", ActiveDownloadCount, _maxConcurrentDownloads);
        }

        /// <summary>
        /// Acquires a search slot, waiting if necessary until one becomes available.
        /// </summary>
        public async Task AcquireSearchSlotAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var stopwatch = Stopwatch.StartNew();
            var wasAtCapacity = _searchSemaphore.CurrentCount == 0;

            await _searchSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            lock (_statsLock)
            {
                _statistics.TotalSearchSlotAcquisitions++;

                // Update average wait time
                var totalWaitTime = _statistics.AverageSearchWaitTime.Ticks * (_statistics.TotalSearchSlotAcquisitions - 1) + stopwatch.Elapsed.Ticks;
                _statistics.AverageSearchWaitTime = new TimeSpan(totalWaitTime / _statistics.TotalSearchSlotAcquisitions);

                // Update peak concurrency
                var currentActive = ActiveSearchCount;
                _statistics.PeakConcurrentSearches = Math.Max(_statistics.PeakConcurrentSearches, currentActive);

                if (wasAtCapacity)
                {
                    _statistics.SearchQueueSaturations++;
                }
            }

            _logger.Debug("Search slot acquired - Active: {0}/{1}, Wait: {2:F1}ms",
                ActiveSearchCount, _maxConcurrentSearches, stopwatch.Elapsed.TotalMilliseconds);
        }

        /// <summary>
        /// Releases a previously acquired search slot.
        /// </summary>
        public void ReleaseSearchSlot()
        {
            ThrowIfDisposed();

            try
            {
                _searchSemaphore.Release();
            }
            catch (SemaphoreFullException ex)
            {
                _logger.Warn(ex, "ReleaseSearchSlot called when no slot was acquired; ignoring");
            }

            _logger.Debug("Search slot released - Active: {0}/{1}", ActiveSearchCount, _maxConcurrentSearches);
        }

        /// <summary>
        /// Gets the current queue status including active operations and available slots.
        /// </summary>
        public QueueStatus GetQueueStatus()
        {
            ThrowIfDisposed();

            var activeDownloads = ActiveDownloadCount;
            var activeSearches = ActiveSearchCount;

            return new QueueStatus
            {
                ActiveDownloads = activeDownloads,
                ActiveSearches = activeSearches,
                MaxConcurrentDownloads = _maxConcurrentDownloads,
                MaxConcurrentSearches = _maxConcurrentSearches,
                AvailableDownloadSlots = _maxConcurrentDownloads - activeDownloads,
                AvailableSearchSlots = _maxConcurrentSearches - activeSearches,
                IsDownloadQueueFull = activeDownloads >= _maxConcurrentDownloads,
                IsSearchQueueFull = activeSearches >= _maxConcurrentSearches
            };
        }

        /// <summary>
        /// Updates the maximum concurrency limits for downloads and searches.
        /// </summary>
        public void UpdateConcurrencyLimits(int maxDownloads, int maxSearches)
        {
            ThrowIfDisposed();

            var effectiveDownloads = GetEffectiveConcurrency(maxDownloads);
            var effectiveSearches = GetEffectiveConcurrency(maxSearches);

            if (effectiveDownloads == _maxConcurrentDownloads && effectiveSearches == _maxConcurrentSearches)
            {
                return; // No changes needed
            }

            _logger.Info("Updating concurrency limits - Downloads: {0} -> {1}, Searches: {2} -> {3}",
                _maxConcurrentDownloads, effectiveDownloads, _maxConcurrentSearches, effectiveSearches);

            // Replace semaphores with new ones having updated limits
            var oldDownloadSemaphore = _downloadSemaphore;
            var oldSearchSemaphore = _searchSemaphore;

            _maxConcurrentDownloads = effectiveDownloads;
            _maxConcurrentSearches = effectiveSearches;

            _downloadSemaphore = new SemaphoreSlim(effectiveDownloads, effectiveDownloads);
            _searchSemaphore = new SemaphoreSlim(effectiveSearches, effectiveSearches);

            // Dispose old semaphores
            oldDownloadSemaphore?.Dispose();
            oldSearchSemaphore?.Dispose();
        }

        /// <summary>
        /// Waits for all active operations to complete.
        /// </summary>
        public async Task WaitForAllOperationsToCompleteAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            _logger.Info("Waiting for all operations to complete...");

            // Wait for all slots to become available (meaning all operations finished)
            var downloadSlots = new Task[_maxConcurrentDownloads];
            var searchSlots = new Task[_maxConcurrentSearches];

            // Acquire all slots
            for (int i = 0; i < _maxConcurrentDownloads; i++)
            {
                downloadSlots[i] = AcquireDownloadSlotAsync(cancellationToken);
            }

            for (int i = 0; i < _maxConcurrentSearches; i++)
            {
                searchSlots[i] = AcquireSearchSlotAsync(cancellationToken);
            }

            // Wait for all acquisitions to complete
            await Task.WhenAll(downloadSlots).ConfigureAwait(false);
            await Task.WhenAll(searchSlots).ConfigureAwait(false);

            // Release all slots
            for (int i = 0; i < _maxConcurrentDownloads; i++)
            {
                ReleaseDownloadSlot();
            }

            for (int i = 0; i < _maxConcurrentSearches; i++)
            {
                ReleaseSearchSlot();
            }

            _logger.Info("All operations completed");
        }

        /// <summary>
        /// Gets queue statistics including operation history and performance metrics.
        /// </summary>
        public QueueStatistics GetQueueStatistics()
        {
            lock (_statsLock)
            {
                return new QueueStatistics
                {
                    TotalDownloadSlotAcquisitions = _statistics.TotalDownloadSlotAcquisitions,
                    TotalSearchSlotAcquisitions = _statistics.TotalSearchSlotAcquisitions,
                    AverageDownloadWaitTime = _statistics.AverageDownloadWaitTime,
                    AverageSearchWaitTime = _statistics.AverageSearchWaitTime,
                    PeakConcurrentDownloads = _statistics.PeakConcurrentDownloads,
                    PeakConcurrentSearches = _statistics.PeakConcurrentSearches,
                    TotalDownloadSlotHoldTime = _statistics.TotalDownloadSlotHoldTime,
                    TotalSearchSlotHoldTime = _statistics.TotalSearchSlotHoldTime,
                    DownloadQueueSaturations = _statistics.DownloadQueueSaturations,
                    SearchQueueSaturations = _statistics.SearchQueueSaturations
                };
            }
        }

        #region Private Helper Methods

        private int GetEffectiveConcurrency(int requestedConcurrency)
        {
            if (requestedConcurrency <= 0)
                return Math.Min(MAX_CONCURRENCY, Math.Max(MIN_CONCURRENCY, Environment.ProcessorCount));

            return Math.Min(MAX_CONCURRENCY, Math.Max(MIN_CONCURRENCY, requestedConcurrency));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LidarrQueueManager));
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Releases all resources used by the LidarrQueueManager.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the managed and unmanaged resources used by the LidarrQueueManager.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                try
                {
                    _downloadSemaphore?.Dispose();
                    _searchSemaphore?.Dispose();
                    _disposed = true;
                    _logger?.Info("LidarrQueueManager disposed");
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Error disposing LidarrQueueManager");
                }
            }
        }

        #endregion
    }
}
