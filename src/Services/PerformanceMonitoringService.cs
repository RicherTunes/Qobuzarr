using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Serilog;
using Serilog.Events;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Constants;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Production telemetry service for validating performance claims
    /// Tracks API call reduction, cache hit rates, and ML optimization effectiveness
    /// </summary>
    public class PerformanceMonitoringService : IPerformanceMonitoringService
    {
        private readonly ILogger _performanceLogger;
        private readonly IQobuzLogger _logger;
        private readonly Timer _metricsFlushTimer;
        private readonly object _metricsLock = new object();

        // Performance counters
        private long _totalApiCalls = 0;
        private long _cachedApiCalls = 0;
        private long _mlOptimizedQueries = 0;
        private long _standardQueries = 0;
        
        // Cache performance tracking
        private readonly ConcurrentDictionary<string, CacheMetrics> _cacheMetrics = new();
        
        // API call tracking
        private readonly ConcurrentQueue<ApiCallMetric> _recentApiCalls = new();
        private readonly ConcurrentQueue<CacheHitMetric> _recentCacheHits = new();

        public PerformanceMonitoringService(IQobuzLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Initialize Serilog for structured performance logging
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                                      "Qobuzarr", "performance", "performance-.log");
            
            _performanceLogger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.WithProperty("Component", "Qobuzarr")
                .Enrich.WithProperty("Version", QobuzarrConstants.Plugin.Version)
                .WriteTo.File(
                    path: logPath,
                    formatter: new Serilog.Formatting.Compact.CompactJsonFormatter(),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    buffered: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(30))
                .CreateLogger();

            // Flush metrics every 5 minutes
            _metricsFlushTimer = new Timer(FlushMetrics, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            
            _logger.Info("Performance monitoring initialized - metrics will be logged to {0}", logPath);
        }

        #region API Call Tracking

        /// <summary>
        /// Records an API call for performance tracking
        /// </summary>
        public void RecordApiCall(string endpoint, TimeSpan duration, bool wasCached, string cacheKey = null)
        {
            Interlocked.Increment(ref _totalApiCalls);
            if (wasCached)
            {
                Interlocked.Increment(ref _cachedApiCalls);
            }

            var metric = new ApiCallMetric
            {
                Timestamp = DateTime.UtcNow,
                Endpoint = endpoint,
                Duration = duration,
                WasCached = wasCached,
                CacheKey = cacheKey
            };

            _recentApiCalls.Enqueue(metric);
            
            // Keep only recent data (last 1000 calls)
            while (_recentApiCalls.Count > 1000)
            {
                _recentApiCalls.TryDequeue(out _);
            }
        }

        /// <summary>
        /// Records cache hit/miss for performance tracking
        /// </summary>
        public void RecordCacheHit(string cacheType, string key, bool hit, TimeSpan? lookupDuration = null)
        {
            var metric = new CacheHitMetric
            {
                Timestamp = DateTime.UtcNow,
                CacheType = cacheType,
                Key = key,
                Hit = hit,
                LookupDuration = lookupDuration ?? TimeSpan.Zero
            };

            _recentCacheHits.Enqueue(metric);
            
            // Update cache type metrics
            _cacheMetrics.AddOrUpdate(cacheType, 
                new CacheMetrics { Hits = hit ? 1 : 0, Misses = hit ? 0 : 1 },
                (key, existing) => new CacheMetrics 
                { 
                    Hits = existing.Hits + (hit ? 1 : 0), 
                    Misses = existing.Misses + (hit ? 0 : 1) 
                });

            // Keep only recent data (last 1000 cache operations)
            while (_recentCacheHits.Count > 1000)
            {
                _recentCacheHits.TryDequeue(out _);
            }
        }

        #endregion

        #region ML Optimization Tracking

        /// <summary>
        /// Records ML query optimization usage
        /// </summary>
        public void RecordMLOptimization(string originalQuery, string optimizedQuery, bool successful, double confidenceScore)
        {
            if (successful)
            {
                Interlocked.Increment(ref _mlOptimizedQueries);
            }
            else
            {
                Interlocked.Increment(ref _standardQueries);
            }

            _performanceLogger.Information("ML_Optimization: {Timestamp} {OriginalQuery} {OptimizedQuery} {Successful} {ConfidenceScore}",
                DateTime.UtcNow,
                originalQuery,
                optimizedQuery,
                successful,
                confidenceScore);
        }

        /// <summary>
        /// Records API call reduction metrics
        /// </summary>
        public void RecordApiReduction(int originalCalls, int actualCalls, string optimization)
        {
            var reductionPercentage = originalCalls > 0 ? (double)(originalCalls - actualCalls) / originalCalls * 100 : 0;
            
            _performanceLogger.Information("API_Reduction: {Timestamp} {OriginalCalls} {ActualCalls} {ReductionPercentage:F1}% {Optimization}",
                DateTime.UtcNow,
                originalCalls,
                actualCalls,
                reductionPercentage,
                optimization);
        }

        #endregion

        #region Performance Metrics

        /// <summary>
        /// Gets current API call reduction percentage
        /// </summary>
        public double GetApiReductionPercentage()
        {
            var total = _totalApiCalls;
            var cached = _cachedApiCalls;
            
            if (total == 0) return 0;
            return (double)cached / total * 100;
        }

        /// <summary>
        /// Gets current cache hit rate
        /// </summary>
        public double GetCacheHitRate(string cacheType = null)
        {
            if (cacheType != null && _cacheMetrics.TryGetValue(cacheType, out var specific))
            {
                var totalOps = specific.Hits + specific.Misses;
                return totalOps > 0 ? (double)specific.Hits / totalOps * 100 : 0;
            }

            // Overall cache hit rate
            var totalHits = _cacheMetrics.Values.Sum(m => m.Hits);
            var totalMisses = _cacheMetrics.Values.Sum(m => m.Misses);
            var totalOperations = totalHits + totalMisses;
            
            return totalOperations > 0 ? (double)totalHits / totalOperations * 100 : 0;
        }

        /// <summary>
        /// Gets ML optimization effectiveness percentage
        /// </summary>
        public double GetMLOptimizationRate()
        {
            var total = _mlOptimizedQueries + _standardQueries;
            if (total == 0) return 0;
            return (double)_mlOptimizedQueries / total * 100;
        }

        /// <summary>
        /// Gets comprehensive performance metrics
        /// </summary>
        public ProductionMetrics GetCurrentMetrics()
        {
            lock (_metricsLock)
            {
                return new ProductionMetrics
                {
                    Timestamp = DateTime.UtcNow,
                    TotalApiCalls = _totalApiCalls,
                    CachedApiCalls = _cachedApiCalls,
                    ApiReductionPercentage = GetApiReductionPercentage(),
                    CacheHitRate = GetCacheHitRate(),
                    MLOptimizationRate = GetMLOptimizationRate(),
                    TotalMLOptimizedQueries = _mlOptimizedQueries,
                    TotalStandardQueries = _standardQueries,
                    CacheMetricsByType = _cacheMetrics.ToDictionary(kv => kv.Key, kv => kv.Value)
                };
            }
        }

        #endregion

        #region Metrics Flushing

        private void FlushMetrics(object state)
        {
            try
            {
                var metrics = GetCurrentMetrics();
                
                _performanceLogger.Information("Performance_Summary: {@Metrics}", metrics);
                
                _logger.Info("Performance metrics flushed - API Reduction: {0:F1}%, Cache Hit Rate: {1:F1}%, ML Optimization: {2:F1}%",
                    metrics.ApiReductionPercentage,
                    metrics.CacheHitRate,
                    metrics.MLOptimizationRate);
                    
                // Validate against claimed performance targets
                ValidatePerformanceTargets(metrics);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error flushing performance metrics");
            }
        }

        private void ValidatePerformanceTargets(ProductionMetrics metrics)
        {
            // Tech lead feedback: Validate claimed "65.8% API reduction" and "94.7% cache hit rates"
            const double TARGET_API_REDUCTION = 65.8;
            const double TARGET_CACHE_HIT_RATE = 94.7;

            if (metrics.TotalApiCalls >= 100) // Only validate with sufficient data
            {
                if (metrics.ApiReductionPercentage >= TARGET_API_REDUCTION)
                {
                    _performanceLogger.Information("Performance_Target_Met: API_Reduction {ActualPercentage:F1}% >= {TargetPercentage:F1}%",
                        metrics.ApiReductionPercentage, TARGET_API_REDUCTION);
                }
                else
                {
                    _performanceLogger.Warning("Performance_Target_Missed: API_Reduction {ActualPercentage:F1}% < {TargetPercentage:F1}%",
                        metrics.ApiReductionPercentage, TARGET_API_REDUCTION);
                }

                if (metrics.CacheHitRate >= TARGET_CACHE_HIT_RATE)
                {
                    _performanceLogger.Information("Performance_Target_Met: Cache_Hit_Rate {ActualPercentage:F1}% >= {TargetPercentage:F1}%",
                        metrics.CacheHitRate, TARGET_CACHE_HIT_RATE);
                }
                else
                {
                    _performanceLogger.Warning("Performance_Target_Missed: Cache_Hit_Rate {ActualPercentage:F1}% < {TargetPercentage:F1}%",
                        metrics.CacheHitRate, TARGET_CACHE_HIT_RATE);
                }
            }
        }

        #endregion

        public void Dispose()
        {
            _metricsFlushTimer?.Dispose();
            if (_performanceLogger is IDisposable disposableLogger)
            {
                disposableLogger.Dispose();
            }
        }
    }

    #region Data Models

    public class ProductionMetrics
    {
        public DateTime Timestamp { get; set; }
        public long TotalApiCalls { get; set; }
        public long CachedApiCalls { get; set; }
        public double ApiReductionPercentage { get; set; }
        public double CacheHitRate { get; set; }
        public double MLOptimizationRate { get; set; }
        public long TotalMLOptimizedQueries { get; set; }
        public long TotalStandardQueries { get; set; }
        public Dictionary<string, CacheMetrics> CacheMetricsByType { get; set; } = new();
    }

    public class CacheMetrics
    {
        public long Hits { get; set; }
        public long Misses { get; set; }
        public double HitRate => (Hits + Misses) > 0 ? (double)Hits / (Hits + Misses) * 100 : 0;
    }

    public class ApiCallMetric
    {
        public DateTime Timestamp { get; set; }
        public string Endpoint { get; set; } = "";
        public TimeSpan Duration { get; set; }
        public bool WasCached { get; set; }
        public string CacheKey { get; set; } = "";
    }

    public class CacheHitMetric
    {
        public DateTime Timestamp { get; set; }
        public string CacheType { get; set; } = "";
        public string Key { get; set; } = "";
        public bool Hit { get; set; }
        public TimeSpan LookupDuration { get; set; }
    }

    #endregion
}