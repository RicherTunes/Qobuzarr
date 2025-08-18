using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Service responsible for handling progress reporting with advanced time estimation and throughput calculations
    /// optimized for the *arr ecosystem with comprehensive metrics and performance tracking.
    /// </summary>
    public class LidarrProgressReporter : ILidarrProgressReporter
    {
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<Guid, IProgressTracker> _activeTrackers = new();
        private readonly object _globalStatsLock = new();

        /// <summary>
        /// Initializes a new instance of the LidarrProgressReporter.
        /// </summary>
        /// <param name="logger">Logger for recording operations and debugging.</param>
        public LidarrProgressReporter(Logger logger)
        {
            _logger = Guard.NotNull(logger, nameof(logger));
            _logger.Info("LidarrProgressReporter initialized");
        }

        /// <summary>
        /// Creates a new progress tracker for a batch operation.
        /// </summary>
        public IProgressTracker CreateTracker(int totalItems, string operationType, IProgress<ProgressReport> progress = null)
        {
            var tracker = new ProgressTracker(totalItems, operationType, progress, _logger, OnTrackerCompleted);
            _activeTrackers[tracker.Id] = tracker;
            
            _logger.Debug("Created progress tracker for {0} operation with {1} items", operationType, totalItems);
            return tracker;
        }

        /// <summary>
        /// Creates a new download progress tracker for a batch download operation.
        /// </summary>
        public IDownloadProgressTracker CreateDownloadTracker(int totalItems, string operationType, IProgress<DownloadProgressReport> progress = null)
        {
            var tracker = new DownloadProgressTracker(totalItems, operationType, progress, _logger, OnTrackerCompleted);
            _activeTrackers[tracker.Id] = tracker;
            
            _logger.Debug("Created download progress tracker for {0} operation with {1} items", operationType, totalItems);
            return tracker;
        }

        /// <summary>
        /// Gets the current progress statistics for all active trackers.
        /// </summary>
        public GlobalProgressStatistics GetGlobalStatistics()
        {
            lock (_globalStatsLock)
            {
                var trackers = _activeTrackers.Values.ToArray();
                var downloadTrackers = trackers.OfType<IDownloadProgressTracker>().ToArray();

                var totalItems = trackers.Sum(t => t.TotalItems);
                var completedItems = trackers.Sum(t => t.CompletedItems);

                return new GlobalProgressStatistics
                {
                    ActiveTrackers = trackers.Length,
                    TotalItemsAcrossAllTrackers = totalItems,
                    CompletedItemsAcrossAllTrackers = completedItems,
                    OverallPercentComplete = totalItems > 0 ? (double)completedItems / totalItems * 100 : 0,
                    CombinedDownloadSpeedMBps = downloadTrackers.Sum(dt => dt.CurrentSpeedMBps),
                    TotalBytesDownloadedAcrossAllTrackers = downloadTrackers.Sum(dt => dt.TotalBytesDownloaded),
                    LongestRunningTracker = trackers.Any() ? trackers.Max(t => t.Elapsed) : TimeSpan.Zero,
                    EstimatedTimeToCompletion = trackers.Any() ? trackers.Max(t => t.EstimatedRemaining) : TimeSpan.Zero,
                    CompletedTrackers = 0, // This would need to be tracked separately
                    RunningTrackers = trackers.Count(t => t.CompletedItems < t.TotalItems)
                };
            }
        }

        /// <summary>
        /// Clears all completed trackers and resets global statistics.
        /// </summary>
        public void Reset()
        {
            lock (_globalStatsLock)
            {
                // Dispose all active trackers
                foreach (var tracker in _activeTrackers.Values)
                {
                    try
                    {
                        tracker.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error disposing progress tracker");
                    }
                }

                _activeTrackers.Clear();
                _logger.Info("Progress reporter reset - all trackers cleared");
            }
        }

        private void OnTrackerCompleted(Guid trackerId)
        {
            _activeTrackers.TryRemove(trackerId, out _);
            _logger.Debug("Progress tracker {0} completed and removed", trackerId);
        }
    }

    /// <summary>
    /// Implementation of progress tracker for generic batch operations.
    /// </summary>
    internal class ProgressTracker : IProgressTracker
    {
        protected readonly Logger _logger;
        protected readonly IProgress<ProgressReport> _progress;
        protected readonly Action<Guid> _onCompleted;
        protected readonly Stopwatch _stopwatch;
        protected readonly object _lock = new();

        protected int _completedItems;
        protected string _currentItem = string.Empty;
        protected volatile bool _disposed;

        public Guid Id { get; } = Guid.NewGuid();
        public int TotalItems { get; }
        public string OperationType { get; }

        public int CompletedItems
        {
            get
            {
                lock (_lock)
                {
                    return _completedItems;
                }
            }
        }

        public string CurrentItem
        {
            get
            {
                lock (_lock)
                {
                    return _currentItem;
                }
            }
        }

        public TimeSpan Elapsed => _stopwatch.Elapsed;

        public TimeSpan EstimatedRemaining
        {
            get
            {
                lock (_lock)
                {
                    if (_completedItems == 0 || _completedItems >= TotalItems)
                        return TimeSpan.Zero;

                    var averageTimePerItem = _stopwatch.Elapsed.TotalMilliseconds / _completedItems;
                    var remainingItems = TotalItems - _completedItems;
                    return TimeSpan.FromMilliseconds(averageTimePerItem * remainingItems);
                }
            }
        }

        public double PercentComplete
        {
            get
            {
                lock (_lock)
                {
                    return TotalItems > 0 ? (double)_completedItems / TotalItems * 100 : 0;
                }
            }
        }

        public ProgressTracker(int totalItems, string operationType, IProgress<ProgressReport> progress, Logger logger, Action<Guid> onCompleted)
        {
            TotalItems = Guard.GreaterThan(totalItems, 0, nameof(totalItems));
            OperationType = Guard.NotNullOrEmpty(operationType, nameof(operationType));
            _progress = progress;
            _logger = Guard.NotNull(logger, nameof(logger));
            _onCompleted = onCompleted;
            _stopwatch = Stopwatch.StartNew();
        }

        public virtual void ReportProgress(string currentItem, string phase = null)
        {
            ThrowIfDisposed();

            lock (_lock)
            {
                _currentItem = currentItem ?? string.Empty;
            }

            ReportToCallback(phase);
        }

        public virtual void CompleteItem(string itemDescription = null)
        {
            ThrowIfDisposed();

            lock (_lock)
            {
                _completedItems++;
                if (!string.IsNullOrEmpty(itemDescription))
                {
                    _currentItem = itemDescription;
                }
            }

            ReportToCallback();
            
            if (_completedItems >= TotalItems)
            {
                _logger.Debug("Progress tracker completed: {0} items in {1:F1}s", TotalItems, _stopwatch.Elapsed.TotalSeconds);
                _onCompleted?.Invoke(Id);
            }
        }

        public virtual void CompleteItems(int count)
        {
            ThrowIfDisposed();

            if (count <= 0) return;

            lock (_lock)
            {
                _completedItems = Math.Min(_completedItems + count, TotalItems);
            }

            ReportToCallback();

            if (_completedItems >= TotalItems)
            {
                _logger.Debug("Progress tracker completed: {0} items in {1:F1}s", TotalItems, _stopwatch.Elapsed.TotalSeconds);
                _onCompleted?.Invoke(Id);
            }
        }

        protected virtual void ReportToCallback(string phase = null)
        {
            if (_progress == null) return;

            try
            {
                _progress.Report(new ProgressReport
                {
                    Completed = CompletedItems,
                    Total = TotalItems,
                    CurrentItem = CurrentItem,
                    Phase = phase ?? OperationType,
                    Elapsed = Elapsed,
                    EstimatedRemaining = EstimatedRemaining
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error reporting progress");
            }
        }

        protected void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ProgressTracker));
        }

        public virtual void Dispose()
        {
            if (!_disposed)
            {
                _stopwatch?.Stop();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Implementation of download progress tracker with bandwidth and throughput calculations.
    /// </summary>
    internal class DownloadProgressTracker : ProgressTracker, IDownloadProgressTracker
    {
        private readonly IProgress<DownloadProgressReport> _downloadProgress;
        private readonly object _downloadLock = new();

        private long _totalBytesDownloaded;
        private int _successCount;
        private int _failureCount;

        public long TotalBytesDownloaded
        {
            get
            {
                lock (_downloadLock)
                {
                    return _totalBytesDownloaded;
                }
            }
        }

        public double CurrentSpeedMBps
        {
            get
            {
                var elapsed = _stopwatch.Elapsed.TotalSeconds;
                if (elapsed <= 0) return 0;

                lock (_downloadLock)
                {
                    return (_totalBytesDownloaded / 1024.0 / 1024.0) / elapsed;
                }
            }
        }

        public double AverageSpeedMBps => CurrentSpeedMBps; // Same as current for this implementation

        public int SuccessCount
        {
            get
            {
                lock (_downloadLock)
                {
                    return _successCount;
                }
            }
        }

        public int FailureCount
        {
            get
            {
                lock (_downloadLock)
                {
                    return _failureCount;
                }
            }
        }

        public DownloadProgressTracker(int totalItems, string operationType, IProgress<DownloadProgressReport> progress, Logger logger, Action<Guid> onCompleted)
            : base(totalItems, operationType, null, logger, onCompleted)
        {
            _downloadProgress = progress;
        }

        public void ReportDownloadProgress(string currentItem, long bytesDownloaded, bool isSuccess)
        {
            ThrowIfDisposed();

            lock (_lock)
            {
                _currentItem = currentItem ?? string.Empty;
            }

            lock (_downloadLock)
            {
                _totalBytesDownloaded += bytesDownloaded;
                if (isSuccess)
                    _successCount++;
                else
                    _failureCount++;
            }

            ReportDownloadToCallback();
        }

        public void AddBytesDownloaded(long additionalBytes)
        {
            ThrowIfDisposed();

            if (additionalBytes <= 0) return;

            lock (_downloadLock)
            {
                _totalBytesDownloaded += additionalBytes;
            }

            ReportDownloadToCallback();
        }

        protected override void ReportToCallback(string phase = null)
        {
            ReportDownloadToCallback(phase);
        }

        private void ReportDownloadToCallback(string phase = null)
        {
            if (_downloadProgress == null) return;

            try
            {
                _downloadProgress.Report(new DownloadProgressReport
                {
                    Completed = CompletedItems,
                    Total = TotalItems,
                    SuccessCount = SuccessCount,
                    FailureCount = FailureCount,
                    BytesDownloaded = TotalBytesDownloaded,
                    CurrentSpeedMBps = CurrentSpeedMBps,
                    CurrentAlbum = CurrentItem,
                    Phase = phase ?? OperationType,
                    Elapsed = Elapsed,
                    EstimatedRemaining = EstimatedRemaining
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error reporting download progress");
            }
        }
    }
}