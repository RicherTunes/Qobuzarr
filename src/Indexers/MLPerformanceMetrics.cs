using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Comprehensive performance monitoring and metrics collection for ML operations
    /// Provides lightweight, production-ready monitoring without external dependencies
    /// </summary>
    public class MLPerformanceMetrics : IDisposable
    {
        private readonly Logger _logger;
        private readonly Timer _aggregationTimer;
        private readonly object _statsLock = new object();

        // Performance timing metrics
        private readonly ConcurrentQueue<double> _loadTimes = new();
        private readonly ConcurrentQueue<double> _predictionTimes = new();
        private readonly ConcurrentQueue<double> _trainingTimes = new();
        private readonly ConcurrentQueue<MemorySnapshot> _memorySnapshots = new();

        // Cache metrics
        private long _cacheHits = 0;
        private long _cacheMisses = 0;
        private readonly ConcurrentQueue<DateTime> _cacheHitTimes = new();

        // Accuracy metrics with time-based windows
        private readonly ConcurrentQueue<AccuracySnapshot> _accuracyHistory = new();
        private double _currentAccuracy = 0.0;
        private int _correctPredictions = 0;
        private int _totalPredictions = 0;

        // API optimization metrics
        private long _apiCallsSaved = 0;
        private long _totalApiCallsWithoutOptimization = 0;
        private readonly ConcurrentQueue<OptimizationSnapshot> _optimizationHistory = new();

        // Rolling window configurations
        private const int MaxHistorySize = 10000;
        private const int RollingWindowMinutes = 60;
        private const int AggregationIntervalSeconds = 30;

        // Memory monitoring
        private readonly Process _currentProcess;
        private long _initialMemoryUsage;

        public MLPerformanceMetrics(Logger logger = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _currentProcess = Process.GetCurrentProcess();
            _initialMemoryUsage = GC.GetTotalMemory(false);

            // Start periodic aggregation and cleanup
            _aggregationTimer = new Timer(PerformAggregation, null,
                TimeSpan.FromSeconds(AggregationIntervalSeconds),
                TimeSpan.FromSeconds(AggregationIntervalSeconds));

            _logger.Debug("ML Performance Metrics initialized with {0}s aggregation interval", AggregationIntervalSeconds);
        }

        #region Model Loading Metrics

        /// <summary>
        /// Start timing a model loading operation
        /// </summary>
        public IDisposable StartModelLoadTiming(string modelType)
        {
            return new OperationTimer(_loadTimes, $"Model Load ({modelType})", _logger);
        }

        /// <summary>
        /// Record model loading time manually
        /// </summary>
        public void RecordModelLoadTime(double milliseconds, string modelType = null)
        {
            _loadTimes.Enqueue(milliseconds);
            _logger.Debug("Model load time recorded: {0:F2}ms ({1})", milliseconds, modelType ?? "unknown");
        }

        #endregion

        #region Prediction Metrics

        /// <summary>
        /// Start timing a prediction operation
        /// </summary>
        public IDisposable StartPredictionTiming()
        {
            return new OperationTimer(_predictionTimes, "Prediction", _logger);
        }

        /// <summary>
        /// Record prediction time and accuracy
        /// </summary>
        public void RecordPrediction(double milliseconds, bool wasCorrect, double confidence = 0.0)
        {
            _predictionTimes.Enqueue(milliseconds);

            lock (_statsLock)
            {
                _totalPredictions++;
                if (wasCorrect)
                    _correctPredictions++;

                _currentAccuracy = _totalPredictions > 0 ? (double)_correctPredictions / _totalPredictions : 0.0;
            }

            // Record accuracy snapshot for time-series analysis
            _accuracyHistory.Enqueue(new AccuracySnapshot
            {
                Timestamp = DateTime.UtcNow,
                Accuracy = _currentAccuracy,
                Confidence = confidence,
                WasCorrect = wasCorrect
            });

            _logger.Trace("Prediction recorded: {0:F2}ms, correct: {1}, confidence: {2:F2}, current accuracy: {3:F2}%",
                milliseconds, wasCorrect, confidence, _currentAccuracy * 100);
        }

        #endregion

        #region Memory Tracking

        /// <summary>
        /// Take a memory snapshot for the current operation
        /// </summary>
        public void RecordMemorySnapshot(string operation)
        {
            var gcMemory = GC.GetTotalMemory(false);
            var processMemory = _currentProcess.WorkingSet64;

            _memorySnapshots.Enqueue(new MemorySnapshot
            {
                Timestamp = DateTime.UtcNow,
                Operation = operation,
                GCMemoryBytes = gcMemory,
                ProcessMemoryBytes = processMemory,
                MemoryDeltaBytes = gcMemory - _initialMemoryUsage
            });

            _logger.Trace("Memory snapshot for {0}: GC={1:N0} bytes, Process={2:N0} bytes, Delta={3:N0} bytes",
                operation, gcMemory, processMemory, gcMemory - _initialMemoryUsage);
        }

        #endregion

        #region Cache Metrics

        /// <summary>
        /// Record a cache hit
        /// </summary>
        public void RecordCacheHit()
        {
            Interlocked.Increment(ref _cacheHits);
            _cacheHitTimes.Enqueue(DateTime.UtcNow);
        }

        /// <summary>
        /// Record a cache miss
        /// </summary>
        public void RecordCacheMiss()
        {
            Interlocked.Increment(ref _cacheMisses);
        }

        /// <summary>
        /// Get current cache hit ratio
        /// </summary>
        public double GetCacheHitRatio()
        {
            var hits = Interlocked.Read(ref _cacheHits);
            var misses = Interlocked.Read(ref _cacheMisses);
            var total = hits + misses;

            return total > 0 ? (double)hits / total : 0.0;
        }

        #endregion

        #region API Optimization Metrics

        /// <summary>
        /// Record API calls saved through optimization
        /// </summary>
        public void RecordApiOptimization(int callsSaved, int totalCallsWithoutOptimization)
        {
            Interlocked.Add(ref _apiCallsSaved, callsSaved);
            Interlocked.Add(ref _totalApiCallsWithoutOptimization, totalCallsWithoutOptimization);

            _optimizationHistory.Enqueue(new OptimizationSnapshot
            {
                Timestamp = DateTime.UtcNow,
                CallsSaved = callsSaved,
                TotalCalls = totalCallsWithoutOptimization,
                ReductionPercentage = totalCallsWithoutOptimization > 0 ? (double)callsSaved / totalCallsWithoutOptimization : 0.0
            });

            var totalSaved = Interlocked.Read(ref _apiCallsSaved);
            var totalWithoutOpt = Interlocked.Read(ref _totalApiCallsWithoutOptimization);
            var overallReduction = totalWithoutOpt > 0 ? (double)totalSaved / totalWithoutOpt * 100 : 0.0;

            _logger.Debug("API optimization recorded: {0} calls saved out of {1} ({2:F1}% reduction). Overall: {3:F1}% reduction",
                callsSaved, totalCallsWithoutOptimization, (double)callsSaved / totalCallsWithoutOptimization * 100, overallReduction);
        }

        /// <summary>
        /// Get current API call reduction percentage
        /// </summary>
        public double GetApiCallReductionPercentage()
        {
            var saved = Interlocked.Read(ref _apiCallsSaved);
            var total = Interlocked.Read(ref _totalApiCallsWithoutOptimization);

            return total > 0 ? (double)saved / total * 100 : 0.0;
        }

        #endregion

        #region Training Metrics

        /// <summary>
        /// Start timing a training operation
        /// </summary>
        public IDisposable StartTrainingTiming()
        {
            return new OperationTimer(_trainingTimes, "Training", _logger);
        }

        /// <summary>
        /// Record training time manually
        /// </summary>
        public void RecordTrainingTime(double milliseconds)
        {
            _trainingTimes.Enqueue(milliseconds);
            _logger.Debug("Training time recorded: {0:F2}ms", milliseconds);
        }

        #endregion

        #region Metrics Retrieval

        /// <summary>
        /// Get comprehensive performance summary
        /// </summary>
        public MLPerformanceSummary GetPerformanceSummary()
        {
            lock (_statsLock)
            {
                var now = DateTime.UtcNow;
                var cutoff = now.AddMinutes(-RollingWindowMinutes);

                return new MLPerformanceSummary
                {
                    GeneratedAt = now,
                    WindowMinutes = RollingWindowMinutes,

                    // Timing metrics
                    ModelLoadMetrics = CalculateTimingStats(_loadTimes, "Model Load"),
                    PredictionMetrics = CalculateTimingStats(_predictionTimes, "Prediction"),
                    TrainingMetrics = CalculateTimingStats(_trainingTimes, "Training"),

                    // Accuracy metrics
                    CurrentAccuracy = _currentAccuracy,
                    TotalPredictions = _totalPredictions,
                    CorrectPredictions = _correctPredictions,
                    AccuracyTrend = CalculateAccuracyTrend(cutoff),

                    // Cache metrics
                    CacheHitRatio = GetCacheHitRatio(),
                    CacheHits = Interlocked.Read(ref _cacheHits),
                    CacheMisses = Interlocked.Read(ref _cacheMisses),
                    RecentCacheActivity = GetRecentCacheActivity(cutoff),

                    // API optimization metrics
                    ApiCallReductionPercentage = GetApiCallReductionPercentage(),
                    TotalApiCallsSaved = Interlocked.Read(ref _apiCallsSaved),
                    OptimizationTrend = CalculateOptimizationTrend(cutoff),

                    // Memory metrics
                    CurrentMemoryUsage = GC.GetTotalMemory(false),
                    ProcessMemoryUsage = _currentProcess.WorkingSet64,
                    MemoryGrowth = GC.GetTotalMemory(false) - _initialMemoryUsage,
                    RecentMemoryPeak = GetRecentMemoryPeak(cutoff)
                };
            }
        }

        /// <summary>
        /// Get rolling average performance for the last N minutes
        /// </summary>
        public RollingPerformanceMetrics GetRollingMetrics(int windowMinutes = 15)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-windowMinutes);

            var recentPredictions = _predictionTimes.ToArray().TakeLast(1000).ToArray();
            var recentAccuracy = _accuracyHistory.Where(a => a.Timestamp >= cutoff).ToArray();

            return new RollingPerformanceMetrics
            {
                WindowMinutes = windowMinutes,
                AveragePredictionTime = recentPredictions.Length > 0 ? recentPredictions.Average() : 0,
                MedianPredictionTime = recentPredictions.Length > 0 ? CalculateMedian(recentPredictions) : 0,
                P95PredictionTime = recentPredictions.Length > 0 ? CalculatePercentile(recentPredictions, 0.95) : 0,
                RecentAccuracy = recentAccuracy.Length > 0 ? recentAccuracy.Average(a => a.Accuracy) : _currentAccuracy,
                PredictionThroughput = recentPredictions.Length / Math.Max(windowMinutes, 1) * 60, // predictions per hour
                MemoryEfficiency = CalculateMemoryEfficiency(cutoff)
            };
        }

        #endregion

        #region Private Helper Methods

        private TimingStatistics CalculateTimingStats(ConcurrentQueue<double> times, string operation)
        {
            var timeArray = times.ToArray();

            if (timeArray.Length == 0)
            {
                return new TimingStatistics { Operation = operation, Count = 0 };
            }

            return new TimingStatistics
            {
                Operation = operation,
                Count = timeArray.Length,
                Average = timeArray.Average(),
                Median = CalculateMedian(timeArray),
                Min = timeArray.Min(),
                Max = timeArray.Max(),
                P95 = CalculatePercentile(timeArray, 0.95),
                P99 = CalculatePercentile(timeArray, 0.99)
            };
        }

        private double CalculateMedian(double[] values)
        {
            if (values.Length == 0) return 0;

            var sorted = values.OrderBy(x => x).ToArray();
            var mid = sorted.Length / 2;

            return sorted.Length % 2 == 0
                ? (sorted[mid - 1] + sorted[mid]) / 2.0
                : sorted[mid];
        }

        private double CalculatePercentile(double[] values, double percentile)
        {
            if (values.Length == 0) return 0;

            var sorted = values.OrderBy(x => x).ToArray();
            var index = (int)Math.Ceiling(sorted.Length * percentile) - 1;
            return sorted[Math.Max(0, Math.Min(index, sorted.Length - 1))];
        }

        private AccuracyTrend CalculateAccuracyTrend(DateTime since)
        {
            var recentAccuracy = _accuracyHistory.Where(a => a.Timestamp >= since).ToArray();

            if (recentAccuracy.Length < 2)
            {
                return new AccuracyTrend
                {
                    Direction = "stable",
                    ChangePercentage = 0,
                    Confidence = 0
                };
            }

            var firstHalf = recentAccuracy.Take(recentAccuracy.Length / 2).Average(a => a.Accuracy);
            var secondHalf = recentAccuracy.Skip(recentAccuracy.Length / 2).Average(a => a.Accuracy);
            var change = secondHalf - firstHalf;

            return new AccuracyTrend
            {
                Direction = Math.Abs(change) < 0.01 ? "stable" : (change > 0 ? "improving" : "declining"),
                ChangePercentage = change * 100,
                Confidence = recentAccuracy.Average(a => a.Confidence)
            };
        }

        private int GetRecentCacheActivity(DateTime since)
        {
            return _cacheHitTimes.Count(t => t >= since);
        }

        private OptimizationTrend CalculateOptimizationTrend(DateTime since)
        {
            var recentOptimizations = _optimizationHistory.Where(o => o.Timestamp >= since).ToArray();

            if (recentOptimizations.Length == 0)
            {
                return new OptimizationTrend
                {
                    AverageReduction = GetApiCallReductionPercentage(),
                    TrendDirection = "stable",
                    RecentCallsSaved = 0
                };
            }

            var avgReduction = recentOptimizations.Average(o => o.ReductionPercentage) * 100;
            var totalSaved = recentOptimizations.Sum(o => o.CallsSaved);

            return new OptimizationTrend
            {
                AverageReduction = avgReduction,
                TrendDirection = avgReduction >= GetApiCallReductionPercentage() ? "improving" : "declining",
                RecentCallsSaved = totalSaved
            };
        }

        private long GetRecentMemoryPeak(DateTime since)
        {
            var recentSnapshots = _memorySnapshots.Where(s => s.Timestamp >= since).ToArray();
            return recentSnapshots.Length > 0 ? recentSnapshots.Max(s => s.GCMemoryBytes) : GC.GetTotalMemory(false);
        }

        private double CalculateMemoryEfficiency(DateTime since)
        {
            var recentSnapshots = _memorySnapshots.Where(s => s.Timestamp >= since).ToArray();

            if (recentSnapshots.Length < 2)
                return 1.0; // Perfect efficiency if no data

            var memoryGrowth = recentSnapshots.Max(s => s.GCMemoryBytes) - recentSnapshots.Min(s => s.GCMemoryBytes);
            var operations = recentSnapshots.Length;

            // Lower memory growth per operation = higher efficiency
            return operations > 0 ? Math.Max(0, 1.0 - (memoryGrowth / operations / 1024.0 / 1024.0)) : 1.0;
        }

        private void PerformAggregation(object? state)
        {
            try
            {
                // Clean up old data beyond rolling window
                var cutoff = DateTime.UtcNow.AddMinutes(-RollingWindowMinutes * 2); // Keep 2x window for safety

                CleanupOldData(_accuracyHistory, cutoff, MaxHistorySize);
                CleanupOldData(_optimizationHistory, cutoff, MaxHistorySize);
                CleanupOldData(_memorySnapshots, cutoff, MaxHistorySize);
                CleanupOldQueue(_cacheHitTimes, cutoff, MaxHistorySize);
                CleanupTimingQueue(_loadTimes, MaxHistorySize);
                CleanupTimingQueue(_predictionTimes, MaxHistorySize);
                CleanupTimingQueue(_trainingTimes, MaxHistorySize);

                // Log periodic summary
                var summary = GetPerformanceSummary();
                _logger.Debug("ML Performance Summary - Accuracy: {0:F1}%, Cache Hit Rate: {1:F1}%, API Reduction: {2:F1}%, Avg Prediction: {3:F2}ms",
                    summary.CurrentAccuracy * 100,
                    summary.CacheHitRatio * 100,
                    summary.ApiCallReductionPercentage,
                    summary.PredictionMetrics.Average);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error during ML performance metrics aggregation");
            }
        }

        private void CleanupOldData<T>(ConcurrentQueue<T> queue, DateTime cutoff, int maxSize) where T : ITimestamped
        {
            var count = 0;
            while (queue.TryPeek(out var item) && (item.Timestamp < cutoff || count > maxSize))
            {
                queue.TryDequeue(out _);
                count++;
            }
        }

        private void CleanupOldQueue(ConcurrentQueue<DateTime> queue, DateTime cutoff, int maxSize)
        {
            var count = 0;
            while (queue.TryPeek(out var item) && (item < cutoff || count > maxSize))
            {
                queue.TryDequeue(out _);
                count++;
            }
        }

        private void CleanupTimingQueue(ConcurrentQueue<double> queue, int maxSize)
        {
            while (queue.Count > maxSize)
            {
                queue.TryDequeue(out _);
            }
        }

        #endregion

        #region IDisposable Implementation

        private bool _disposed = false;

        public void Dispose()
        {
            if (!_disposed)
            {
                _aggregationTimer?.Dispose();
                _currentProcess?.Dispose();
                _disposed = true;

                _logger.Debug("ML Performance Metrics disposed");
            }
        }

        #endregion
    }

    #region Helper Classes and Data Structures

    /// <summary>
    /// Disposable timer for measuring operation durations
    /// </summary>
    internal class OperationTimer : IDisposable
    {
        private readonly Stopwatch _stopwatch;
        private readonly ConcurrentQueue<double> _targetQueue;
        private readonly string _operation;
        private readonly Logger _logger;

        public OperationTimer(ConcurrentQueue<double> targetQueue, string operation, Logger logger)
        {
            _targetQueue = targetQueue;
            _operation = operation;
            _logger = logger;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            var elapsed = _stopwatch.Elapsed.TotalMilliseconds;
            _targetQueue.Enqueue(elapsed);
            _logger.Trace("{0} completed in {1:F2}ms", _operation, elapsed);
        }
    }

    /// <summary>
    /// Interface for timestamped data points
    /// </summary>
    internal interface ITimestamped
    {
        DateTime Timestamp { get; }
    }

    /// <summary>
    /// Memory usage snapshot
    /// </summary>
    internal class MemorySnapshot : ITimestamped
    {
        public DateTime Timestamp { get; set; }
        public string Operation { get; set; }
        public long GCMemoryBytes { get; set; }
        public long ProcessMemoryBytes { get; set; }
        public long MemoryDeltaBytes { get; set; }
    }

    /// <summary>
    /// Model accuracy snapshot for time-series analysis
    /// </summary>
    internal class AccuracySnapshot : ITimestamped
    {
        public DateTime Timestamp { get; set; }
        public double Accuracy { get; set; }
        public double Confidence { get; set; }
        public bool WasCorrect { get; set; }
    }

    /// <summary>
    /// API optimization snapshot
    /// </summary>
    internal class OptimizationSnapshot : ITimestamped
    {
        public DateTime Timestamp { get; set; }
        public int CallsSaved { get; set; }
        public int TotalCalls { get; set; }
        public double ReductionPercentage { get; set; }
    }

    #endregion
}
