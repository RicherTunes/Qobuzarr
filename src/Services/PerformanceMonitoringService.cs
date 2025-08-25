using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using NLog;
using Newtonsoft.Json;
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
        private readonly Logger _performanceLogger;
        private readonly IQobuzLogger _logger;
        private readonly Timer _metricsFlushTimer;
        private readonly object _metricsLock = new object();
        private readonly string _logDirectory;
        private readonly StreamWriter _logWriter;

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
            
            // Initialize NLog for performance logging (using Lidarr's existing NLog infrastructure)
            _performanceLogger = LogManager.GetLogger("Qobuzarr.Performance");
            
            // Set up file-based performance logging
            // Use Lidarr's AppData folder which is writable in Docker containers
            try
            {
                // Try to use Lidarr's config/data directory (usually /config in Docker)
                var appDataFolder = Environment.GetEnvironmentVariable("APP_DATA") ?? 
                                   Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ??
                                   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lidarr");
                
                _logDirectory = Path.Combine(appDataFolder, "Qobuzarr", "performance");
                
                // Only create directory and file writer if we have write permissions
                if (!string.IsNullOrEmpty(_logDirectory))
                {
                    try
                    {
                        Directory.CreateDirectory(_logDirectory);
                        var logPath = Path.Combine(_logDirectory, $"performance-{DateTime.UtcNow:yyyyMMdd}.json");
                        _logWriter = new StreamWriter(logPath, append: true) { AutoFlush = false };
                        _logger.Debug("Performance logging initialized to: {0}", logPath);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        _logger.Warn("Cannot create performance log directory at {0}, performance file logging disabled", _logDirectory);
                        _logWriter = null;
                    }
                    catch (IOException)
                    {
                        _logger.Warn("Cannot access performance log directory at {0}, performance file logging disabled", _logDirectory);
                        _logWriter = null;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to initialize performance file logging, continuing without file output");
                _logWriter = null;
            }

            // Flush metrics every 5 minutes
            _metricsFlushTimer = new Timer(FlushMetrics, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            
            _logger.Info("Performance monitoring initialized - metrics will be logged to NLog");
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

            // Write structured JSON log entry
            var logEntry = new
            {
                Type = "ML_Optimization",
                Timestamp = DateTime.UtcNow,
                OriginalQuery = originalQuery,
                OptimizedQuery = optimizedQuery,
                Successful = successful,
                ConfidenceScore = confidenceScore
            };
            
            lock (_metricsLock)
            {
                _logWriter?.WriteLine(JsonConvert.SerializeObject(logEntry));
            }
            
            _performanceLogger.Info("ML Optimization: {0} -> {1} (Success: {2}, Confidence: {3:F2})",
                originalQuery, optimizedQuery, successful, confidenceScore);
        }

        /// <summary>
        /// Records API call reduction metrics
        /// </summary>
        public void RecordApiReduction(int originalCalls, int actualCalls, string optimization)
        {
            var reductionPercentage = originalCalls > 0 ? (double)(originalCalls - actualCalls) / originalCalls * 100 : 0;
            
            // Write structured JSON log entry
            var logEntry = new
            {
                Type = "API_Reduction",
                Timestamp = DateTime.UtcNow,
                OriginalCalls = originalCalls,
                ActualCalls = actualCalls,
                ReductionPercentage = reductionPercentage,
                Optimization = optimization
            };
            
            lock (_metricsLock)
            {
                _logWriter?.WriteLine(JsonConvert.SerializeObject(logEntry));
            }
            
            _performanceLogger.Info("API Reduction: {0} -> {1} calls ({2:F1}% reduction via {3})",
                originalCalls, actualCalls, reductionPercentage, optimization);
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
                
                // Write summary to JSON log
                lock (_metricsLock)
                {
                    if (_logWriter != null)
                    {
                        _logWriter.WriteLine(JsonConvert.SerializeObject(new
                        {
                            Type = "Performance_Summary",
                            Timestamp = DateTime.UtcNow,
                            Metrics = metrics
                        }));
                        _logWriter.Flush();
                    }
                }
                
                _performanceLogger.Info("Performance Summary: API Calls: {0}, Cache Hits: {1:F1}%, ML Optimized: {2:F1}%",
                    metrics.TotalApiCalls, metrics.CacheHitRate, metrics.MLOptimizationRate);
                
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
                    _performanceLogger.Info("Performance Target Met: API Reduction {0:F1}% >= {1:F1}%",
                        metrics.ApiReductionPercentage, TARGET_API_REDUCTION);
                }
                else
                {
                    _performanceLogger.Warn("Performance Target Missed: API Reduction {0:F1}% < {1:F1}%",
                        metrics.ApiReductionPercentage, TARGET_API_REDUCTION);
                }

                if (metrics.CacheHitRate >= TARGET_CACHE_HIT_RATE)
                {
                    _performanceLogger.Info("Performance Target Met: Cache Hit Rate {0:F1}% >= {1:F1}%",
                        metrics.CacheHitRate, TARGET_CACHE_HIT_RATE);
                }
                else
                {
                    _performanceLogger.Warn("Performance Target Missed: Cache Hit Rate {0:F1}% < {1:F1}%",
                        metrics.CacheHitRate, TARGET_CACHE_HIT_RATE);
                }
            }
        }

        #endregion

        public void Dispose()
        {
            _metricsFlushTimer?.Dispose();
            
            // Flush and close the log writer
            lock (_metricsLock)
            {
                _logWriter?.Flush();
                _logWriter?.Dispose();
            }
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