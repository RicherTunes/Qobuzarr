using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Lidarr.Plugin.Qobuzarr.Telemetry
{
    public class QobuzarrTelemetry : IDisposable
    {
        private readonly Meter _meter;
        private readonly ActivitySource _activitySource;
        private readonly ILogger<QobuzarrTelemetry> _logger;
        
        // Metrics instruments
        private readonly Counter<long> _searchRequests;
        private readonly Counter<long> _downloadRequests;
        private readonly Counter<long> _apiCalls;
        private readonly Counter<long> _cacheHits;
        private readonly Counter<long> _cacheMisses;
        private readonly Counter<long> _errors;
        private readonly Histogram<double> _searchDuration;
        private readonly Histogram<double> _downloadDuration;
        private readonly Histogram<double> _apiCallDuration;
        private readonly ObservableGauge<int> _activeDownloads;
        private readonly ObservableGauge<long> _totalBytesDownloaded;
        private readonly ObservableGauge<double> _cacheHitRate;
        
        // Internal counters
        private long _totalSearches = 0;
        private long _totalDownloads = 0;
        private long _totalApiCalls = 0;
        private long _totalCacheHits = 0;
        private long _totalCacheMisses = 0;
        private long _totalErrors = 0;
        private long _totalBytes = 0;
        private int _currentActiveDownloads = 0;
        
        // Performance tracking
        private readonly Dictionary<string, PerformanceMetrics> _performanceMetrics;
        private readonly Timer _metricsFlushTimer;
        
        public QobuzarrTelemetry(ILogger<QobuzarrTelemetry> logger)
        {
            _logger = logger;
            _performanceMetrics = new Dictionary<string, PerformanceMetrics>();
            
            // Initialize OpenTelemetry meter
            _meter = new Meter("Qobuzarr", "1.0.0");
            _activitySource = new ActivitySource("Qobuzarr");
            
            // Create counter instruments
            _searchRequests = _meter.CreateCounter<long>(
                "qobuzarr.search.requests",
                unit: "requests",
                description: "Total number of search requests");
                
            _downloadRequests = _meter.CreateCounter<long>(
                "qobuzarr.download.requests",
                unit: "requests",
                description: "Total number of download requests");
                
            _apiCalls = _meter.CreateCounter<long>(
                "qobuzarr.api.calls",
                unit: "calls",
                description: "Total number of Qobuz API calls");
                
            _cacheHits = _meter.CreateCounter<long>(
                "qobuzarr.cache.hits",
                unit: "hits",
                description: "Number of cache hits");
                
            _cacheMisses = _meter.CreateCounter<long>(
                "qobuzarr.cache.misses",
                unit: "misses",
                description: "Number of cache misses");
                
            _errors = _meter.CreateCounter<long>(
                "qobuzarr.errors",
                unit: "errors",
                description: "Total number of errors");
            
            // Create histogram instruments
            _searchDuration = _meter.CreateHistogram<double>(
                "qobuzarr.search.duration",
                unit: "ms",
                description: "Duration of search operations");
                
            _downloadDuration = _meter.CreateHistogram<double>(
                "qobuzarr.download.duration",
                unit: "s",
                description: "Duration of download operations");
                
            _apiCallDuration = _meter.CreateHistogram<double>(
                "qobuzarr.api.duration",
                unit: "ms",
                description: "Duration of API calls");
            
            // Create observable gauge instruments
            _activeDownloads = _meter.CreateObservableGauge(
                "qobuzarr.download.active",
                () => _currentActiveDownloads,
                unit: "downloads",
                description: "Current number of active downloads");
                
            _totalBytesDownloaded = _meter.CreateObservableGauge(
                "qobuzarr.download.bytes",
                () => _totalBytes,
                unit: "bytes",
                description: "Total bytes downloaded");
                
            _cacheHitRate = _meter.CreateObservableGauge(
                "qobuzarr.cache.hit_rate",
                () => CalculateCacheHitRate(),
                unit: "ratio",
                description: "Cache hit rate");
            
            // Start metrics flush timer (every 30 seconds)
            _metricsFlushTimer = new Timer(
                FlushMetrics,
                null,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30));
            
            _logger.LogInformation("Qobuzarr telemetry initialized");
        }
        
        // Search operation tracking
        public Activity StartSearchOperation(string query, string searchType = "general")
        {
            var activity = _activitySource.StartActivity("Search", ActivityKind.Internal);
            activity?.SetTag("search.query", query);
            activity?.SetTag("search.type", searchType);
            
            Interlocked.Increment(ref _totalSearches);
            _searchRequests.Add(1, 
                new KeyValuePair<string, object>("type", searchType));
            
            return activity;
        }
        
        public void EndSearchOperation(Activity activity, int resultCount, double durationMs)
        {
            activity?.SetTag("search.results", resultCount);
            activity?.SetTag("search.duration_ms", durationMs);
            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.Dispose();
            
            _searchDuration.Record(durationMs,
                new KeyValuePair<string, object>("result_count", resultCount > 0 ? "found" : "not_found"));
            
            RecordPerformanceMetric("search", durationMs);
        }
        
        // Download operation tracking
        public Activity StartDownloadOperation(string itemId, string itemType, long sizeBytes)
        {
            var activity = _activitySource.StartActivity("Download", ActivityKind.Internal);
            activity?.SetTag("download.item_id", itemId);
            activity?.SetTag("download.item_type", itemType);
            activity?.SetTag("download.size_bytes", sizeBytes);
            
            Interlocked.Increment(ref _totalDownloads);
            Interlocked.Increment(ref _currentActiveDownloads);
            _downloadRequests.Add(1,
                new KeyValuePair<string, object>("type", itemType));
            
            return activity;
        }
        
        public void EndDownloadOperation(Activity activity, bool success, double durationSeconds, long bytesDownloaded)
        {
            activity?.SetTag("download.success", success);
            activity?.SetTag("download.duration_s", durationSeconds);
            activity?.SetTag("download.bytes", bytesDownloaded);
            activity?.SetStatus(success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
            activity?.Dispose();
            
            Interlocked.Decrement(ref _currentActiveDownloads);
            Interlocked.Add(ref _totalBytes, bytesDownloaded);
            
            _downloadDuration.Record(durationSeconds,
                new KeyValuePair<string, object>("status", success ? "success" : "failed"));
            
            RecordPerformanceMetric("download", durationSeconds * 1000);
        }
        
        // API call tracking
        public Activity StartApiCall(string endpoint, string method = "GET")
        {
            var activity = _activitySource.StartActivity("ApiCall", ActivityKind.Client);
            activity?.SetTag("http.method", method);
            activity?.SetTag("http.url", endpoint);
            
            Interlocked.Increment(ref _totalApiCalls);
            _apiCalls.Add(1,
                new KeyValuePair<string, object>("endpoint", endpoint),
                new KeyValuePair<string, object>("method", method));
            
            return activity;
        }
        
        public void EndApiCall(Activity activity, int statusCode, double durationMs)
        {
            activity?.SetTag("http.status_code", statusCode);
            activity?.SetTag("api.duration_ms", durationMs);
            activity?.SetStatus(statusCode < 400 ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
            activity?.Dispose();
            
            _apiCallDuration.Record(durationMs,
                new KeyValuePair<string, object>("status", statusCode < 400 ? "success" : "error"));
            
            RecordPerformanceMetric("api_call", durationMs);
        }
        
        // Cache tracking
        public void RecordCacheHit(string cacheKey)
        {
            Interlocked.Increment(ref _totalCacheHits);
            _cacheHits.Add(1,
                new KeyValuePair<string, object>("key", SanitizeCacheKey(cacheKey)));
        }
        
        public void RecordCacheMiss(string cacheKey)
        {
            Interlocked.Increment(ref _totalCacheMisses);
            _cacheMisses.Add(1,
                new KeyValuePair<string, object>("key", SanitizeCacheKey(cacheKey)));
        }
        
        // Error tracking
        public void RecordError(string errorType, string errorMessage, string context = null)
        {
            Interlocked.Increment(ref _totalErrors);
            _errors.Add(1,
                new KeyValuePair<string, object>("type", errorType),
                new KeyValuePair<string, object>("context", context ?? "unknown"));
            
            _logger.LogWarning("Error recorded: Type={ErrorType}, Message={ErrorMessage}, Context={Context}",
                errorType, errorMessage, context);
        }
        
        // Performance metrics tracking
        private void RecordPerformanceMetric(string operation, double durationMs)
        {
            lock (_performanceMetrics)
            {
                if (!_performanceMetrics.ContainsKey(operation))
                {
                    _performanceMetrics[operation] = new PerformanceMetrics();
                }
                
                var metrics = _performanceMetrics[operation];
                metrics.Count++;
                metrics.TotalDuration += durationMs;
                metrics.MinDuration = Math.Min(metrics.MinDuration, durationMs);
                metrics.MaxDuration = Math.Max(metrics.MaxDuration, durationMs);
                
                // Update moving average
                metrics.MovingAverage = (metrics.MovingAverage * (metrics.Count - 1) + durationMs) / metrics.Count;
            }
        }
        
        // Calculate cache hit rate
        private double CalculateCacheHitRate()
        {
            var total = _totalCacheHits + _totalCacheMisses;
            return total > 0 ? (double)_totalCacheHits / total : 0.0;
        }
        
        // Sanitize cache key for metrics
        private string SanitizeCacheKey(string cacheKey)
        {
            // Remove sensitive data from cache keys
            if (cacheKey.Contains("auth") || cacheKey.Contains("token"))
                return "auth_related";
            if (cacheKey.Contains("search"))
                return "search_query";
            if (cacheKey.Contains("album"))
                return "album_data";
            if (cacheKey.Contains("track"))
                return "track_data";
            return "other";
        }
        
        // Flush metrics periodically
        private void FlushMetrics(object state)
        {
            try
            {
                var metrics = new Dictionary<string, object>
                {
                    ["timestamp"] = DateTime.UtcNow,
                    ["total_searches"] = _totalSearches,
                    ["total_downloads"] = _totalDownloads,
                    ["total_api_calls"] = _totalApiCalls,
                    ["cache_hit_rate"] = CalculateCacheHitRate(),
                    ["active_downloads"] = _currentActiveDownloads,
                    ["total_bytes"] = _totalBytes,
                    ["total_errors"] = _totalErrors
                };
                
                // Add performance metrics
                lock (_performanceMetrics)
                {
                    foreach (var kvp in _performanceMetrics)
                    {
                        metrics[$"perf_{kvp.Key}_avg_ms"] = kvp.Value.MovingAverage;
                        metrics[$"perf_{kvp.Key}_min_ms"] = kvp.Value.MinDuration;
                        metrics[$"perf_{kvp.Key}_max_ms"] = kvp.Value.MaxDuration;
                        metrics[$"perf_{kvp.Key}_count"] = kvp.Value.Count;
                    }
                }
                
                _logger.LogDebug("Metrics snapshot: {Metrics}", metrics);
                
                // Send to external monitoring if configured
                SendToMonitoring(metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing metrics");
            }
        }
        
        // Send metrics to external monitoring
        private async void SendToMonitoring(Dictionary<string, object> metrics)
        {
            // This would integrate with your monitoring backend
            // Example: Send to OpenTelemetry collector, Prometheus, etc.
            
            var monitoringEndpoint = Environment.GetEnvironmentVariable("QOBUZARR_MONITORING_ENDPOINT");
            if (!string.IsNullOrEmpty(monitoringEndpoint))
            {
                try
                {
                    // Implement actual monitoring integration here
                    await Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send metrics to monitoring");
                }
            }
        }
        
        // Get current performance summary
        public PerformanceSummary GetPerformanceSummary()
        {
            lock (_performanceMetrics)
            {
                return new PerformanceSummary
                {
                    TotalSearches = _totalSearches,
                    TotalDownloads = _totalDownloads,
                    TotalApiCalls = _totalApiCalls,
                    CacheHitRate = CalculateCacheHitRate(),
                    ActiveDownloads = _currentActiveDownloads,
                    TotalBytesDownloaded = _totalBytes,
                    TotalErrors = _totalErrors,
                    PerformanceMetrics = new Dictionary<string, PerformanceMetrics>(_performanceMetrics)
                };
            }
        }
        
        public void Dispose()
        {
            _metricsFlushTimer?.Dispose();
            _meter?.Dispose();
            _activitySource?.Dispose();
        }
        
        // Internal classes
        public class PerformanceMetrics
        {
            public int Count { get; set; }
            public double TotalDuration { get; set; }
            public double MinDuration { get; set; } = double.MaxValue;
            public double MaxDuration { get; set; } = double.MinValue;
            public double MovingAverage { get; set; }
        }
        
        public class PerformanceSummary
        {
            public long TotalSearches { get; set; }
            public long TotalDownloads { get; set; }
            public long TotalApiCalls { get; set; }
            public double CacheHitRate { get; set; }
            public int ActiveDownloads { get; set; }
            public long TotalBytesDownloaded { get; set; }
            public long TotalErrors { get; set; }
            public Dictionary<string, PerformanceMetrics> PerformanceMetrics { get; set; }
        }
    }
}