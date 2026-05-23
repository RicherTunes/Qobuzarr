using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Intelligent concurrency management that adapts based on system performance and API behavior
    /// </summary>
    public class AdaptiveConcurrencyManager
    {
        private readonly IQobuzLogger _logger;
        private readonly object _adjustmentLock = new object();

        // Performance metrics
        private readonly ConcurrentQueue<PerformanceMetric> _recentMetrics = new();
        private readonly ConcurrentQueue<double> _recentLatencies = new();
        private readonly ConcurrentQueue<bool> _recentSuccesses = new();

        // Cached calculations to avoid expensive LINQ operations on property access
        private double _cachedAverageLatency;
        private double _cachedSuccessRate = 1.0; // intentional: 1.0 is the meaningful default (100% success rate assumed)
        private DateTime _lastCalculationTime = DateTime.UtcNow;
        private volatile bool _cacheIsStale = true;
        private readonly object _cacheLock = new object();
        private readonly TimeSpan _cacheValidityPeriod = TimeSpan.FromSeconds(5);

        // Current settings
        private volatile int _currentConcurrency;
        private volatile int _consecutiveSuccesses;
        private volatile int _consecutiveFailures;
        private DateTime _lastAdjustment = DateTime.UtcNow;

        // Shared semaphore for concurrency control
        private SemaphoreSlim _sharedSemaphore;

        // Configuration
        private readonly int _minConcurrency;
        private readonly int _maxConcurrency;
        private readonly TimeSpan _adjustmentInterval;
        private readonly double _targetLatency;
        private readonly double _maxLatency;

        public int CurrentConcurrency => _currentConcurrency;
        public double AverageLatency => GetCachedAverageLatency();
        public double SuccessRate => GetCachedSuccessRate();

        private double GetCachedAverageLatency()
        {
            if (ShouldRefreshCache())
            {
                RefreshCachedCalculations();
            }

            lock (_cacheLock)
            {
                return _cachedAverageLatency;
            }
        }

        private double GetCachedSuccessRate()
        {
            if (ShouldRefreshCache())
            {
                RefreshCachedCalculations();
            }

            lock (_cacheLock)
            {
                return _cachedSuccessRate;
            }
        }

        private bool ShouldRefreshCache()
        {
            return _cacheIsStale || (DateTime.UtcNow - _lastCalculationTime) > _cacheValidityPeriod;
        }

        private void RefreshCachedCalculations()
        {
            lock (_cacheLock)
            {
                // Double-check pattern to avoid unnecessary recalculations
                if (!_cacheIsStale && (DateTime.UtcNow - _lastCalculationTime) <= _cacheValidityPeriod)
                {
                    return;
                }

                // Calculate average latency
                var latencies = _recentLatencies.ToArray();
                _cachedAverageLatency = latencies.Length > 0 ? latencies.Average() : 0;

                // Calculate success rate
                var successes = _recentSuccesses.ToArray();
                _cachedSuccessRate = successes.Length > 0 ? successes.Count(x => x) / (double)successes.Length : 1.0;

                _lastCalculationTime = DateTime.UtcNow;
                _cacheIsStale = false;
            }
        }

        public AdaptiveConcurrencyManager(
            IQobuzLogger logger,
            int minConcurrency = 1,
            int maxConcurrency = 8,
            TimeSpan? adjustmentInterval = null,
            double targetLatency = 1000.0, // 1 second
            double maxLatency = 5000.0) // 5 seconds
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _minConcurrency = Math.Max(1, minConcurrency);
            _maxConcurrency = Math.Max(_minConcurrency, maxConcurrency);
            _adjustmentInterval = adjustmentInterval ?? TimeSpan.FromSeconds(10);
            _targetLatency = targetLatency;
            _maxLatency = maxLatency;

            // Start conservative - use minConcurrency if it's higher than processor-based calculation
            // This ensures test predictability while still being reasonable for production
            var processorBasedConcurrency = Math.Min(Environment.ProcessorCount / 2, 2);
            _currentConcurrency = Math.Max(_minConcurrency, processorBasedConcurrency);

            // Ensure we don't exceed max concurrency
            _currentConcurrency = Math.Min(_currentConcurrency, _maxConcurrency);

            // Initialize shared semaphore
            _sharedSemaphore = new SemaphoreSlim(_currentConcurrency, _currentConcurrency);

            _logger.Info("AdaptiveConcurrencyManager initialized: min={0}, max={1}, initial={2}, targetLatency={3}ms",
                _minConcurrency, _maxConcurrency, _currentConcurrency, _targetLatency);
        }

        /// <summary>
        /// Records a completed operation and adjusts concurrency if needed
        /// </summary>
        public void RecordOperation(TimeSpan latency, bool success, Exception error = null)
        {
            var latencyMs = latency.TotalMilliseconds;

            // Record metrics
            _recentLatencies.Enqueue(latencyMs);
            _recentSuccesses.Enqueue(success);

            var metric = new PerformanceMetric
            {
                Timestamp = DateTime.UtcNow,
                Latency = latencyMs,
                Success = success,
                Error = error?.GetType().Name,
                Concurrency = _currentConcurrency
            };

            _recentMetrics.Enqueue(metric);

            // Keep only recent data (last 100 operations)
            while (_recentMetrics.Count > 100) _recentMetrics.TryDequeue(out _);
            while (_recentLatencies.Count > 50) _recentLatencies.TryDequeue(out _);
            while (_recentSuccesses.Count > 50) _recentSuccesses.TryDequeue(out _);

            // Mark cache as stale since new metrics were added
            _cacheIsStale = true;

            // Update consecutive counters
            if (success)
            {
                Interlocked.Increment(ref _consecutiveSuccesses);
                Interlocked.Exchange(ref _consecutiveFailures, 0);
            }
            else
            {
                Interlocked.Increment(ref _consecutiveFailures);
                Interlocked.Exchange(ref _consecutiveSuccesses, 0);
            }

            // Consider adjustment
            ConsiderAdjustment(latencyMs, success, error);
        }

        /// <summary>
        /// Gets the shared semaphore for controlling concurrency
        /// </summary>
        public SemaphoreSlim GetConcurrencySemaphore()
        {
            return _sharedSemaphore;
        }

        /// <summary>
        /// Executes an operation with adaptive concurrency control
        /// </summary>
        public async Task<T> ExecuteWithConcurrencyAsync<T>(
            Func<Task<T>> operation,
            SemaphoreSlim semaphore = null,
            CancellationToken cancellationToken = default)
        {
            var localSemaphore = semaphore ?? GetConcurrencySemaphore();
            var shouldDispose = semaphore != null; // Only dispose if caller provided their own

            try
            {
                await localSemaphore.WaitAsync(cancellationToken);

                var stopwatch = Stopwatch.StartNew();
                Exception? operationError = null;
                T result = default(T)!; // Will be set before return
                bool success = false;

                try
                {
                    result = await operation();
                    success = true;
                    return result;
                }
                catch (Exception ex)
                {
                    operationError = ex;
                    throw;
                }
                finally
                {
                    stopwatch.Stop();
                    RecordOperation(stopwatch.Elapsed, success, operationError);
                }
            }
            finally
            {
                localSemaphore.Release();
                if (shouldDispose)
                {
                    localSemaphore.Dispose();
                }
            }
        }

        private void ConsiderAdjustment(double latencyMs, bool success, Exception error)
        {
            lock (_adjustmentLock)
            {
                var now = DateTime.UtcNow;
                var timeSinceLastAdjustment = now - _lastAdjustment;

                if (timeSinceLastAdjustment < _adjustmentInterval)
                    return;

                var decision = DetermineAdjustment(latencyMs, success, error);
                if (decision != AdjustmentDecision.NoChange)
                {
                    ApplyAdjustment(decision);
                    _lastAdjustment = now;
                }
            }
        }

        private AdjustmentDecision DetermineAdjustment(double latencyMs, bool success, Exception error)
        {
            var avgLatency = AverageLatency;
            var successRate = SuccessRate;

            // Immediate decrease for critical issues
            if (IsRateLimitError(error))
            {
                _logger.Warn("Rate limit detected, reducing concurrency immediately");
                return AdjustmentDecision.DecreaseAggressive;
            }

            if (latencyMs > _maxLatency || avgLatency > _maxLatency)
            {
                _logger.Debug("High latency detected (current: {0}ms, avg: {1}ms), reducing concurrency",
                    latencyMs, avgLatency);
                return AdjustmentDecision.Decrease;
            }

            if (_consecutiveFailures >= 5)
            {
                _logger.Debug("Multiple consecutive failures ({0}), reducing concurrency", _consecutiveFailures);
                return AdjustmentDecision.Decrease;
            }

            if (successRate < 0.8)
            {
                _logger.Debug("Low success rate ({0:P1}), reducing concurrency", successRate);
                return AdjustmentDecision.Decrease;
            }

            // Conditions for increase
            if (_consecutiveSuccesses >= 20 && avgLatency < _targetLatency && successRate > 0.95)
            {
                _logger.Debug("Good performance (successes: {0}, latency: {1}ms, rate: {2:P1}), increasing concurrency",
                    _consecutiveSuccesses, avgLatency, successRate);
                return AdjustmentDecision.Increase;
            }

            return AdjustmentDecision.NoChange;
        }

        private void ApplyAdjustment(AdjustmentDecision decision)
        {
            var oldConcurrency = _currentConcurrency;
            var newConcurrency = decision switch
            {
                AdjustmentDecision.Increase => Math.Min(_maxConcurrency, _currentConcurrency + 1),
                AdjustmentDecision.Decrease => Math.Max(_minConcurrency, _currentConcurrency - 1),
                AdjustmentDecision.DecreaseAggressive => Math.Max(_minConcurrency, _currentConcurrency / 2),
                _ => _currentConcurrency
            };

            if (newConcurrency != oldConcurrency)
            {
                _currentConcurrency = newConcurrency;
                _consecutiveSuccesses = 0;
                _consecutiveFailures = 0;

                // Update the shared semaphore with new limit
                var oldSemaphore = _sharedSemaphore;
                _sharedSemaphore = new SemaphoreSlim(newConcurrency, newConcurrency);

                // Dispose old semaphore in a background task to avoid blocking
                Task.Run(() =>
                {
                    try
                    {
                        oldSemaphore?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug("Error disposing old semaphore during concurrency adjustment: {0}", ex.Message);
                    }
                });

                _logger.Info("Concurrency adjusted: {0} → {1} (decision: {2}, avg latency: {3:F1}ms, success rate: {4:P1})",
                    oldConcurrency, newConcurrency, decision, AverageLatency, SuccessRate);
            }
        }

        private bool IsRateLimitError(Exception error)
        {
            if (error == null) return false;

            return error.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                   error.Message.Contains("429", StringComparison.OrdinalIgnoreCase) ||
                   error.Message.Contains("too many requests", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets current performance statistics
        /// </summary>
        public ConcurrencyStats GetStats()
        {
            return new ConcurrencyStats
            {
                CurrentConcurrency = _currentConcurrency,
                AverageLatency = AverageLatency,
                SuccessRate = SuccessRate,
                ConsecutiveSuccesses = _consecutiveSuccesses,
                ConsecutiveFailures = _consecutiveFailures,
                RecentOperations = _recentMetrics.Count,
                LastAdjustment = _lastAdjustment
            };
        }

        private enum AdjustmentDecision
        {
            NoChange,
            Increase,
            Decrease,
            DecreaseAggressive
        }

        private class PerformanceMetric
        {
            public DateTime Timestamp { get; set; }
            public double Latency { get; set; }
            public bool Success { get; set; }
            public string Error { get; set; }
            public int Concurrency { get; set; }
        }
    }

    public class ConcurrencyStats
    {
        public int CurrentConcurrency { get; set; }
        public double AverageLatency { get; set; }
        public double SuccessRate { get; set; }
        public int ConsecutiveSuccesses { get; set; }
        public int ConsecutiveFailures { get; set; }
        public int RecentOperations { get; set; }
        public DateTime LastAdjustment { get; set; }
    }
}
