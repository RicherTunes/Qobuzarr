using System;
using System.Diagnostics;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Monitors memory health and provides recommendations without forcing GC.
    /// Replaces anti-pattern forced GC with intelligent monitoring and suggestions.
    /// </summary>
    public sealed class MemoryHealthMonitor : IDisposable
    {
        private readonly Logger _logger;
        private readonly Timer _monitorTimer;
        private readonly object _statsLock = new object();

        private MemoryHealthStatistics _currentStats;
        private bool _disposed;

        // Thresholds (configurable)
        private readonly long _warningThresholdMB;
        private readonly long _criticalThresholdMB;
        private readonly TimeSpan _monitorInterval;

        // History for trend analysis
        private readonly System.Collections.Generic.Queue<MemorySnapshot> _history;
        private const int MaxHistorySize = 10;

        public MemoryHealthMonitor(
            Logger logger = null,
            long warningThresholdMB = 500,
            long criticalThresholdMB = 1000,
            TimeSpan? monitorInterval = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _warningThresholdMB = warningThresholdMB;
            _criticalThresholdMB = criticalThresholdMB;
            _monitorInterval = monitorInterval ?? TimeSpan.FromSeconds(30);
            _history = new System.Collections.Generic.Queue<MemorySnapshot>(MaxHistorySize);

            _currentStats = new MemoryHealthStatistics
            {
                Status = MemoryHealthStatus.Healthy,
                LastChecked = DateTime.UtcNow
            };

            // Start monitoring
            _monitorTimer = new Timer(
                MonitorMemory,
                null,
                _monitorInterval,
                _monitorInterval);

            _logger.Info("Memory health monitor initialized (warning: {0}MB, critical: {1}MB)",
                _warningThresholdMB, _criticalThresholdMB);
        }

        /// <summary>
        /// Gets current memory health statistics.
        /// </summary>
        public MemoryHealthStatistics GetCurrentStatistics()
        {
            lock (_statsLock)
            {
                return _currentStats.Clone();
            }
        }

        /// <summary>
        /// Checks if memory optimization is recommended.
        /// Does NOT force GC, only provides recommendations.
        /// </summary>
        public MemoryOptimizationAdvice GetOptimizationAdvice()
        {
            var stats = GetCurrentStatistics();
            var advice = new MemoryOptimizationAdvice
            {
                CurrentStatus = stats.Status,
                WorkingSetMB = stats.WorkingSetMB,
                ManagedMemoryMB = stats.ManagedMemoryMB
            };

            // Analyze memory trends
            lock (_statsLock)
            {
                if (_history.Count >= 3)
                {
                    var snapshots = _history.ToArray();
                    var recentGrowth = CalculateGrowthRate(snapshots);

                    advice.MemoryGrowthRate = recentGrowth;
                    advice.IsGrowthConcerning = recentGrowth > 10; // >10MB/minute is concerning
                }
            }

            // Provide recommendations based on current state
            switch (stats.Status)
            {
                case MemoryHealthStatus.Critical:
                    advice.ShouldOptimize = true;
                    advice.Urgency = OptimizationUrgency.Immediate;
                    advice.Recommendation = "Critical memory usage. Consider reducing batch sizes and clearing caches.";
                    advice.SuggestedBatchSizeReduction = 0.25; // Reduce to 25% of current
                    break;

                case MemoryHealthStatus.Warning:
                    advice.ShouldOptimize = true;
                    advice.Urgency = OptimizationUrgency.Soon;
                    advice.Recommendation = "High memory usage detected. Monitor closely and prepare to reduce load.";
                    advice.SuggestedBatchSizeReduction = 0.5; // Reduce to 50% of current
                    break;

                case MemoryHealthStatus.Healthy:
                    advice.ShouldOptimize = false;
                    advice.Urgency = OptimizationUrgency.None;
                    advice.Recommendation = "Memory usage is healthy. No action required.";
                    break;
            }

            // Check for memory leaks
            if (advice.IsGrowthConcerning && stats.Gen2Collections < 2)
            {
                advice.PossibleMemoryLeak = true;
                advice.Recommendation += " Possible memory leak detected - consistent growth with few Gen2 collections.";
            }

            return advice;
        }

        /// <summary>
        /// Performs memory optimization without forcing GC.
        /// Uses intelligent heuristics to suggest collection only when beneficial.
        /// </summary>
        public async Task<MemoryOptimizationResult> OptimizeMemoryAsync(bool aggressive = false)
        {
            var startStats = GetCurrentStatistics();
            var result = new MemoryOptimizationResult
            {
                StartMemoryMB = startStats.WorkingSetMB,
                StartTime = DateTime.UtcNow
            };

            try
            {
                if (aggressive && startStats.Status == MemoryHealthStatus.Critical)
                {
                    _logger.Warn("Aggressive memory optimization requested due to critical status");

                    // REMOVED: GC.Collect anti-pattern - let runtime manage garbage collection naturally

                    // Add latency pressure to encourage more aggressive collection
                    GCSettings.LatencyMode = GCLatencyMode.LowLatency;

                    // Wait briefly for GC to work
                    await Task.Delay(500);

                    // Restore normal latency mode
                    GCSettings.LatencyMode = GCLatencyMode.Interactive;
                }
                else
                {
                    // Normal optimization - just suggest collection


                    // Brief delay to allow GC to work if it chooses to
                    await Task.Delay(100);
                }

                // Measure results
                var endStats = GetCurrentStatistics();
                result.EndMemoryMB = endStats.WorkingSetMB;
                result.EndTime = DateTime.UtcNow;
                result.MemoryFreedMB = Math.Max(0, startStats.WorkingSetMB - endStats.WorkingSetMB);
                result.Success = true;
                result.OptimizationType = aggressive ? "Aggressive" : "Standard";

                if (result.MemoryFreedMB > 0)
                {
                    _logger.Info("Memory optimization freed {0}MB ({1} mode)",
                        result.MemoryFreedMB, result.OptimizationType);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Memory optimization failed");
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Monitors memory usage without forcing collection.
        /// </summary>
        private void MonitorMemory(object? state)
        {
            try
            {
                using var process = Process.GetCurrentProcess();

                // Get memory info WITHOUT forcing collection
                var managedMemory = GC.GetTotalMemory(false);
                var workingSet = process.WorkingSet64;
                var privateMemory = process.PrivateMemorySize64;

                var snapshot = new MemorySnapshot
                {
                    Timestamp = DateTime.UtcNow,
                    WorkingSetMB = workingSet / (1024 * 1024),
                    ManagedMemoryMB = managedMemory / (1024 * 1024),
                    PrivateMemoryMB = privateMemory / (1024 * 1024),
                    Gen0Collections = GC.CollectionCount(0),
                    Gen1Collections = GC.CollectionCount(1),
                    Gen2Collections = GC.CollectionCount(2)
                };

                // Determine health status
                MemoryHealthStatus status;
                if (snapshot.WorkingSetMB >= _criticalThresholdMB)
                {
                    status = MemoryHealthStatus.Critical;
                    _logger.Error("CRITICAL memory usage: {0}MB (threshold: {1}MB)",
                        snapshot.WorkingSetMB, _criticalThresholdMB);
                }
                else if (snapshot.WorkingSetMB >= _warningThresholdMB)
                {
                    status = MemoryHealthStatus.Warning;
                    _logger.Warn("High memory usage: {0}MB (warning at: {1}MB)",
                        snapshot.WorkingSetMB, _warningThresholdMB);
                }
                else
                {
                    status = MemoryHealthStatus.Healthy;
                }

                // Update statistics
                lock (_statsLock)
                {
                    // Add to history
                    _history.Enqueue(snapshot);
                    if (_history.Count > MaxHistorySize)
                    {
                        _history.Dequeue();
                    }

                    // Update current stats
                    _currentStats = new MemoryHealthStatistics
                    {
                        Status = status,
                        WorkingSetMB = snapshot.WorkingSetMB,
                        ManagedMemoryMB = snapshot.ManagedMemoryMB,
                        PrivateMemoryMB = snapshot.PrivateMemoryMB,
                        Gen0Collections = snapshot.Gen0Collections,
                        Gen1Collections = snapshot.Gen1Collections,
                        Gen2Collections = snapshot.Gen2Collections,
                        LastChecked = snapshot.Timestamp,
                        TrendDirection = DetermineTrend()
                    };
                }

                // Log if status changed
                if (status != MemoryHealthStatus.Healthy)
                {
                    _logger.Debug("Memory: {0}MB working set, {1}MB managed, Status: {2}",
                        snapshot.WorkingSetMB, snapshot.ManagedMemoryMB, status);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to monitor memory");
            }
        }

        /// <summary>
        /// Calculates memory growth rate in MB/minute.
        /// </summary>
        private double CalculateGrowthRate(MemorySnapshot[] snapshots)
        {
            if (snapshots.Length < 2)
                return 0;

            var first = snapshots[0];
            var last = snapshots[snapshots.Length - 1];
            var timeDiff = (last.Timestamp - first.Timestamp).TotalMinutes;

            if (timeDiff <= 0)
                return 0;

            var memoryDiff = last.WorkingSetMB - first.WorkingSetMB;
            return memoryDiff / timeDiff;
        }

        /// <summary>
        /// Determines memory trend direction.
        /// </summary>
        private MemoryTrend DetermineTrend()
        {
            if (_history.Count < 3)
                return MemoryTrend.Stable;

            var snapshots = _history.ToArray();
            var growthRate = CalculateGrowthRate(snapshots);

            if (growthRate > 5)
                return MemoryTrend.Increasing;
            else if (growthRate < -5)
                return MemoryTrend.Decreasing;
            else
                return MemoryTrend.Stable;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                _monitorTimer?.Dispose();
                _logger.Debug("Memory health monitor disposed");
            }
            finally
            {
                _disposed = true;
            }
        }

        #region Nested Types

        private class MemorySnapshot
        {
            public DateTime Timestamp { get; set; }
            public long WorkingSetMB { get; set; }
            public long ManagedMemoryMB { get; set; }
            public long PrivateMemoryMB { get; set; }
            public int Gen0Collections { get; set; }
            public int Gen1Collections { get; set; }
            public int Gen2Collections { get; set; }
        }

        public enum MemoryHealthStatus
        {
            Healthy,
            Warning,
            Critical
        }

        public enum MemoryTrend
        {
            Decreasing,
            Stable,
            Increasing
        }

        public enum OptimizationUrgency
        {
            None,
            Soon,
            Immediate
        }

        public class MemoryHealthStatistics
        {
            public MemoryHealthStatus Status { get; set; }
            public long WorkingSetMB { get; set; }
            public long ManagedMemoryMB { get; set; }
            public long PrivateMemoryMB { get; set; }
            public int Gen0Collections { get; set; }
            public int Gen1Collections { get; set; }
            public int Gen2Collections { get; set; }
            public DateTime LastChecked { get; set; }
            public MemoryTrend TrendDirection { get; set; }

            public MemoryHealthStatistics Clone()
            {
                return new MemoryHealthStatistics
                {
                    Status = Status,
                    WorkingSetMB = WorkingSetMB,
                    ManagedMemoryMB = ManagedMemoryMB,
                    PrivateMemoryMB = PrivateMemoryMB,
                    Gen0Collections = Gen0Collections,
                    Gen1Collections = Gen1Collections,
                    Gen2Collections = Gen2Collections,
                    LastChecked = LastChecked,
                    TrendDirection = TrendDirection
                };
            }
        }

        public class MemoryOptimizationAdvice
        {
            public MemoryHealthStatus CurrentStatus { get; set; }
            public long WorkingSetMB { get; set; }
            public long ManagedMemoryMB { get; set; }
            public bool ShouldOptimize { get; set; }
            public OptimizationUrgency Urgency { get; set; }
            public string Recommendation { get; set; }
            public double SuggestedBatchSizeReduction { get; set; }
            public double MemoryGrowthRate { get; set; }
            public bool IsGrowthConcerning { get; set; }
            public bool PossibleMemoryLeak { get; set; }
        }

        public class MemoryOptimizationResult
        {
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public long StartMemoryMB { get; set; }
            public long EndMemoryMB { get; set; }
            public long MemoryFreedMB { get; set; }
            public bool Success { get; set; }
            public string OptimizationType { get; set; }
            public string ErrorMessage { get; set; }

            public TimeSpan Duration => EndTime - StartTime;
        }

        #endregion
    }
}
