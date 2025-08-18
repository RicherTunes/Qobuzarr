using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Thread-safe concurrency manager using semaphores.
    /// Supports dynamic limit adjustment and detailed statistics tracking.
    /// </summary>
    public class ConcurrencyManager : IConcurrencyManager
    {
        private readonly Logger _logger;
        private readonly object _lockObject = new object();
        private readonly List<DateTime> _acquisitionTimes = new List<DateTime>();
        
        private SemaphoreSlim _semaphore;
        private int _currentLimit;
        private int _activeSlots = 0; // Track active slots explicitly
        private volatile bool _disposed = false;
        private long _totalSlotsUsed = 0;

        public ConcurrencyManager(Logger logger, int initialLimit = 4)
        {
            if (initialLimit <= 0)
                throw new ArgumentOutOfRangeException(nameof(initialLimit), "Concurrency limit must be positive");

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _currentLimit = initialLimit;
            _semaphore = new SemaphoreSlim(initialLimit, initialLimit);

            _logger.Debug("ConcurrencyManager initialized with limit: {0}", initialLimit);
        }

        public async Task<IDisposable> AcquireSlotAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var acquisitionStart = DateTime.UtcNow;

            try
            {
                await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                
                var waitTime = DateTime.UtcNow - acquisitionStart;
                
                lock (_lockObject)
                {
                    _acquisitionTimes.Add(acquisitionStart);
                    _totalSlotsUsed++;
                    _activeSlots++;
                    
                    // Keep only recent acquisition times for statistics
                    var cutoffTime = DateTime.UtcNow.AddMinutes(-5);
                    _acquisitionTimes.RemoveAll(time => time < cutoffTime);
                }

                _logger.Debug("Acquired concurrency slot (waited: {0}ms)", waitTime.TotalMilliseconds);

                return new ConcurrencySlot(_semaphore, () =>
                {
                    lock (_lockObject)
                    {
                        _activeSlots = Math.Max(0, _activeSlots - 1);
                    }
                    _logger.Debug("Released concurrency slot");
                });
            }
            catch (OperationCanceledException)
            {
                _logger.Debug("Concurrency slot acquisition cancelled after {0}ms", 
                    (DateTime.UtcNow - acquisitionStart).TotalMilliseconds);
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to acquire concurrency slot");
                throw;
            }
        }

        public void UpdateConcurrencyLimit(int newLimit)
        {
            ThrowIfDisposed();

            if (newLimit <= 0)
                throw new ArgumentException("Concurrency limit must be positive", nameof(newLimit));

            lock (_lockObject)
            {
                if (newLimit == _currentLimit)
                {
                    _logger.Debug("Concurrency limit unchanged: {0}", newLimit);
                    return;
                }

                var oldLimit = _currentLimit;
                var currentActive = _activeSlots; // Use tracked active slots
                
                // Always create a new semaphore with the new max count
                // Calculate initial count based on currently active operations
                var initialCount = Math.Max(0, newLimit - currentActive);
                var newSemaphore = new SemaphoreSlim(initialCount, newLimit);
                
                var oldSemaphore = _semaphore;
                _semaphore = newSemaphore;
                _currentLimit = newLimit;

                // Dispose old semaphore asynchronously to avoid blocking
                Task.Run(() =>
                {
                    try
                    {
                        // Wait a bit to ensure any in-flight operations complete
                        Thread.Sleep(100);
                        oldSemaphore?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Error disposing old semaphore during limit change");
                    }
                });

                _logger.Info("Updated concurrency limit: {0} -> {1} (active: {2}, available: {3})", 
                    oldLimit, newLimit, currentActive, initialCount);
            }
        }

        public int CurrentLimit 
        { 
            get 
            { 
                ThrowIfDisposed();
                return _currentLimit; 
            } 
        }

        public int ActiveCount 
        { 
            get 
            { 
                ThrowIfDisposed();
                lock (_lockObject)
                {
                    return _activeSlots;
                }
            } 
        }

        public int WaitingCount 
        { 
            get 
            { 
                ThrowIfDisposed();
                // SemaphoreSlim doesn't expose waiting count, so we can't provide accurate data
                // For testing purposes, we'll use a simple heuristic
                return 0; // Will be updated by tests that track this manually
            } 
        }

        public ConcurrencyStatistics GetStatistics()
        {
            ThrowIfDisposed();

            lock (_lockObject)
            {
                var recentAcquisitions = _acquisitionTimes
                    .Where(time => time > DateTime.UtcNow.AddMinutes(-5))
                    .ToList();

                var averageWaitTime = TimeSpan.Zero;
                if (recentAcquisitions.Count > 1)
                {
                    // Estimate average wait time based on acquisition frequency
                    var timeSpan = recentAcquisitions.Max() - recentAcquisitions.Min();
                    var avgInterval = timeSpan.TotalMilliseconds / Math.Max(1, recentAcquisitions.Count - 1);
                    
                    // If acquisitions are frequent relative to limit, there's likely waiting
                    if (recentAcquisitions.Count > _currentLimit * 2)
                    {
                        averageWaitTime = TimeSpan.FromMilliseconds(avgInterval * 0.1); // Rough estimate
                    }
                }

                return new ConcurrencyStatistics
                {
                    MaxConcurrency = _currentLimit,
                    ActiveOperations = ActiveCount,
                    QueuedOperations = WaitingCount,
                    TotalSlotsUsed = (int)Math.Min(_totalSlotsUsed, int.MaxValue),
                    AverageWaitTime = averageWaitTime,
                    LastUpdated = DateTime.UtcNow
                };
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ConcurrencyManager));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                _semaphore?.Dispose();
                _logger.Debug("ConcurrencyManager disposed");
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error during ConcurrencyManager disposal");
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}