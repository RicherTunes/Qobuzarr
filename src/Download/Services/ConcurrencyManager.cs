using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Thread-safe concurrency manager using a single semaphore with dynamic limit adjustment.
    /// </summary>
    /// <remarks>
    /// <para><b>Permit Model:</b></para>
    /// <para>
    /// Uses a single <see cref="SemaphoreSlim"/> with <c>int.MaxValue</c> max count to avoid
    /// replacing the semaphore on limit changes (which caused race conditions and deadlocks).
    /// </para>
    /// 
    /// <para><b>Key Fields:</b></para>
    /// <list type="bullet">
    ///   <item><c>_currentLimit</c>: The logical concurrency limit exposed to callers.</item>
    ///   <item><c>_reservedPermits</c>: Permits drained from the semaphore but not yet needed.
    ///         When the limit decreases, we try to immediately drain permits; if successful,
    ///         they go here. When the limit increases, we release from here first.</item>
    ///   <item><c>_pendingPermitReductions</c>: Reduction debt that couldn't be drained immediately
    ///         (permits were in use). Each <see cref="ReleaseSlot"/> consumes one pending reduction
    ///         instead of releasing back to the semaphore.</item>
    /// </list>
    /// 
    /// <para><b>Limit Increase:</b> Cancel pending reductions → release reserved permits → release new permits.</para>
    /// <para><b>Limit Decrease:</b> Drain available permits to reserved → record remaining as pending reductions.</para>
    /// <para><b>Release:</b> If pending reductions exist, consume one; otherwise release to semaphore.</para>
    /// </remarks>
    public class ConcurrencyManager : IConcurrencyManager
    {
        private readonly Logger _logger;
        private readonly object _lockObject = new object();
        private readonly List<DateTime> _acquisitionTimes = new List<DateTime>();
        
        // Single semaphore with int.MaxValue max count - never replaced, only permit count changes
        private readonly SemaphoreSlim _semaphore;
        private int _currentLimit;
        private int _activeSlots = 0;
        private volatile bool _disposed = false;
        
        // Permit tracking for dynamic limit adjustment (see class remarks)
        private int _reservedPermits = 0;
        private int _pendingPermitReductions = 0;
        private long _totalSlotsUsed = 0;

        public ConcurrencyManager(Logger logger, int initialLimit = 4)
        {
            if (initialLimit <= 0)
                throw new ArgumentOutOfRangeException(nameof(initialLimit), "Concurrency limit must be positive");

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _currentLimit = initialLimit;
            _semaphore = new SemaphoreSlim(initialLimit, int.MaxValue);

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

                return new ConcurrencySlot(ReleaseSlot);
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

                if (newLimit > oldLimit)
                {
                    var delta = newLimit - oldLimit;
                    _currentLimit = newLimit;

                    // Increasing the limit: first cancel any pending reductions, then release reserved permits,
                    // and finally release new permits.
                    if (_pendingPermitReductions > 0)
                    {
                        var cancelReductions = Math.Min(delta, _pendingPermitReductions);
                        _pendingPermitReductions -= cancelReductions;
                        delta -= cancelReductions;
                    }

                    if (delta > 0 && _reservedPermits > 0)
                    {
                        var releaseFromReserved = Math.Min(delta, _reservedPermits);
                        _reservedPermits -= releaseFromReserved;
                        _semaphore.Release(releaseFromReserved);
                        delta -= releaseFromReserved;
                    }

                    if (delta > 0)
                    {
                        _semaphore.Release(delta);
                    }

                    _logger.Info("Updated concurrency limit: {0} -> {1} (active: {2})",
                        oldLimit, newLimit, _activeSlots);
                    return;
                }

                // Decreasing the limit: drain available permits immediately and track any remaining debt.
                _currentLimit = newLimit;

                var reduction = oldLimit - newLimit;
                for (var i = 0; i < reduction; i++)
                {
                    if (_semaphore.Wait(0))
                    {
                        _reservedPermits++;
                    }
                    else
                    {
                        _pendingPermitReductions++;
                    }
                }

                _logger.Info("Updated concurrency limit: {0} -> {1} (active: {2}, reserved: {3}, pendingReductions: {4})",
                    oldLimit, newLimit, _activeSlots, _reservedPermits, _pendingPermitReductions);
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

        private void ReleaseSlot()
        {
            lock (_lockObject)
            {
                _activeSlots = Math.Max(0, _activeSlots - 1);

                if (_pendingPermitReductions > 0)
                {
                    _pendingPermitReductions--;
                    _logger.Debug("Released concurrency slot (consumed pending reduction)");
                    return;
                }
            }

            try
            {
                _semaphore.Release();
                _logger.Debug("Released concurrency slot");
            }
            catch (SemaphoreFullException)
            {
                _logger.Debug("Semaphore full while releasing concurrency slot, ignoring");
            }
            catch (ObjectDisposedException)
            {
                // Ignore disposal races during shutdown.
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
                _semaphore.Dispose();
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
