using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Constants;

namespace Lidarr.Plugin.Qobuzarr.Services.Observability
{
    /// <summary>
    /// Prometheus metrics collection service for comprehensive observability
    /// Provides actionable metrics following Prometheus naming conventions
    /// </summary>
    public class MetricsCollector : IMetricsCollector, IDisposable
    {
        private readonly IQobuzLogger _logger;
        private readonly Logger _metricsLogger;
        
        // Prometheus-style metric collectors
        private readonly Dictionary<string, PrometheusCounter> _counters;
        private readonly Dictionary<string, PrometheusHistogram> _histograms;
        private readonly Dictionary<string, PrometheusGauge> _gauges;
        private readonly object _metricsLock = new();

        // Metric names following Prometheus conventions
        private const string API_REQUESTS_TOTAL = "qobuzarr_api_requests_total";
        private const string API_REQUEST_DURATION_SECONDS = "qobuzarr_api_request_duration_seconds";
        private const string CACHE_OPERATIONS_TOTAL = "qobuzarr_cache_operations_total";
        private const string CACHE_HIT_RATIO = "qobuzarr_cache_hit_ratio";
        private const string QUALITY_FALLBACKS_TOTAL = "qobuzarr_quality_fallbacks_total";
        private const string AUTHENTICATION_ATTEMPTS_TOTAL = "qobuzarr_authentication_attempts_total";
        private const string DOWNLOAD_OPERATIONS_TOTAL = "qobuzarr_download_operations_total";
        private const string DOWNLOAD_DURATION_SECONDS = "qobuzarr_download_duration_seconds";
        private const string ACTIVE_DOWNLOADS_GAUGE = "qobuzarr_active_downloads_current";
        private const string ML_OPTIMIZATIONS_TOTAL = "qobuzarr_ml_optimizations_total";
        private const string SERVICE_HEALTH_STATUS = "qobuzarr_service_health_status";

        public MetricsCollector(IQobuzLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _metricsLogger = LogManager.GetLogger("Qobuzarr.Metrics");
            
            _counters = new Dictionary<string, PrometheusCounter>();
            _histograms = new Dictionary<string, PrometheusHistogram>();
            _gauges = new Dictionary<string, PrometheusGauge>();
            
            InitializeMetrics();
            
            _logger.Info("Prometheus metrics collector initialized with {0} counters, {1} histograms, {2} gauges",
                _counters.Count, _histograms.Count, _gauges.Count);
        }

        #region Initialization

        private void InitializeMetrics()
        {
            // API request metrics
            _counters[API_REQUESTS_TOTAL] = new PrometheusCounter(
                API_REQUESTS_TOTAL,
                "Total number of API requests made to Qobuz",
                new[] { "endpoint", "status", "method" });

            _histograms[API_REQUEST_DURATION_SECONDS] = new PrometheusHistogram(
                API_REQUEST_DURATION_SECONDS,
                "Duration of API requests in seconds",
                new[] { "endpoint", "status" },
                new[] { 0.1, 0.5, 1.0, 2.5, 5.0, 10.0 });

            // Cache performance metrics
            _counters[CACHE_OPERATIONS_TOTAL] = new PrometheusCounter(
                CACHE_OPERATIONS_TOTAL,
                "Total cache operations performed",
                new[] { "cache_type", "operation", "result" });

            _gauges[CACHE_HIT_RATIO] = new PrometheusGauge(
                CACHE_HIT_RATIO,
                "Cache hit ratio by cache type",
                new[] { "cache_type" });

            // Quality fallback tracking
            _counters[QUALITY_FALLBACKS_TOTAL] = new PrometheusCounter(
                QUALITY_FALLBACKS_TOTAL,
                "Total quality fallbacks executed",
                new[] { "original_quality", "fallback_quality", "reason" });

            // Authentication metrics
            _counters[AUTHENTICATION_ATTEMPTS_TOTAL] = new PrometheusCounter(
                AUTHENTICATION_ATTEMPTS_TOTAL,
                "Total authentication attempts",
                new[] { "type", "result" });

            // Download performance metrics
            _counters[DOWNLOAD_OPERATIONS_TOTAL] = new PrometheusCounter(
                DOWNLOAD_OPERATIONS_TOTAL,
                "Total download operations",
                new[] { "type", "status", "quality" });

            _histograms[DOWNLOAD_DURATION_SECONDS] = new PrometheusHistogram(
                DOWNLOAD_DURATION_SECONDS,
                "Duration of download operations in seconds",
                new[] { "type", "quality" },
                new[] { 1.0, 5.0, 10.0, 30.0, 60.0, 180.0, 300.0 });

            _gauges[ACTIVE_DOWNLOADS_GAUGE] = new PrometheusGauge(
                ACTIVE_DOWNLOADS_GAUGE,
                "Current number of active downloads",
                new[] { "type" });

            // ML optimization metrics
            _counters[ML_OPTIMIZATIONS_TOTAL] = new PrometheusCounter(
                ML_OPTIMIZATIONS_TOTAL,
                "Total ML query optimizations performed",
                new[] { "strategy", "result" });

            // Service health metrics
            _gauges[SERVICE_HEALTH_STATUS] = new PrometheusGauge(
                SERVICE_HEALTH_STATUS,
                "Service health status (1=healthy, 0=unhealthy)",
                new[] { "service_name", "component" });
        }

        #endregion

        #region API Call Metrics

        /// <summary>
        /// Records API request metrics with comprehensive labeling
        /// </summary>
        public void RecordApiRequest(string endpoint, TimeSpan duration, int statusCode, string method = "GET")
        {
            var status = GetStatusLabel(statusCode);
            
            lock (_metricsLock)
            {
                _counters[API_REQUESTS_TOTAL].WithLabels(endpoint, status, method).Inc();
                _histograms[API_REQUEST_DURATION_SECONDS].WithLabels(endpoint, status).Observe(duration.TotalSeconds);
            }

            _metricsLogger.Debug("API request recorded: {0} {1} - {2}ms - {3}",
                method, endpoint, duration.TotalMilliseconds, statusCode);
        }

        /// <summary>
        /// Records API call with additional context for monitoring
        /// </summary>
        public void RecordApiCall(string endpoint, TimeSpan duration, bool wasCached, string cacheKey = null)
        {
            var status = wasCached ? "cached" : "fresh";
            
            RecordApiRequest(endpoint, duration, wasCached ? 200 : 200, "GET");
            
            if (wasCached)
            {
                RecordCacheOperation("api", "get", true);
            }
        }

        #endregion

        #region Cache Metrics

        /// <summary>
        /// Records cache operation metrics
        /// </summary>
        public void RecordCacheOperation(string cacheType, string operation, bool success)
        {
            var result = success ? "hit" : "miss";
            
            lock (_metricsLock)
            {
                _counters[CACHE_OPERATIONS_TOTAL].WithLabels(cacheType, operation, result).Inc();
                
                // Update cache hit ratio
                UpdateCacheHitRatio(cacheType);
            }

            _metricsLogger.Debug("Cache operation: {0}.{1} = {2}", cacheType, operation, result);
        }

        /// <summary>
        /// Records cache hit/miss with timing information
        /// </summary>
        public void RecordCacheHit(string cacheType, string key, bool hit, TimeSpan? lookupDuration = null)
        {
            RecordCacheOperation(cacheType, "lookup", hit);
        }

        private void UpdateCacheHitRatio(string cacheType)
        {
            // Calculate hit ratio from counter values
            if (_counters[CACHE_OPERATIONS_TOTAL].TryGetValue($"{cacheType}.lookup.hit", out var hits) &&
                _counters[CACHE_OPERATIONS_TOTAL].TryGetValue($"{cacheType}.lookup.miss", out var misses))
            {
                var total = hits + misses;
                var ratio = total > 0 ? (double)hits / total : 0.0;
                _gauges[CACHE_HIT_RATIO].WithLabels(cacheType).Set(ratio);
            }
        }

        #endregion

        #region Quality Management Metrics

        /// <summary>
        /// Records quality fallback operations for monitoring quality issues
        /// </summary>
        public void RecordQualityFallback(string originalQuality, string fallbackQuality, string reason)
        {
            lock (_metricsLock)
            {
                _counters[QUALITY_FALLBACKS_TOTAL].WithLabels(originalQuality, fallbackQuality, reason).Inc();
            }

            _metricsLogger.Info("Quality fallback: {0} -> {1} (reason: {2})",
                originalQuality, fallbackQuality, reason);
        }

        #endregion

        #region Authentication Metrics

        /// <summary>
        /// Records authentication attempts for security monitoring
        /// </summary>
        public void RecordAuthenticationAttempt(string authenticationType, bool success, string failureReason = null)
        {
            var result = success ? "success" : failureReason ?? "failure";
            
            lock (_metricsLock)
            {
                _counters[AUTHENTICATION_ATTEMPTS_TOTAL].WithLabels(authenticationType, result).Inc();
            }

            if (!success)
            {
                _metricsLogger.Warn("Authentication failure: {0} - {1}", authenticationType, failureReason);
            }
        }

        #endregion

        #region Download Performance Metrics

        /// <summary>
        /// Records download operation metrics
        /// </summary>
        public void RecordDownloadOperation(string downloadType, TimeSpan duration, bool success, string quality = "unknown")
        {
            var status = success ? "completed" : "failed";
            
            lock (_metricsLock)
            {
                _counters[DOWNLOAD_OPERATIONS_TOTAL].WithLabels(downloadType, status, quality).Inc();
                _histograms[DOWNLOAD_DURATION_SECONDS].WithLabels(downloadType, quality).Observe(duration.TotalSeconds);
            }

            _metricsLogger.Debug("Download operation: {0} ({1}) - {2} - {3:F2}s",
                downloadType, quality, status, duration.TotalSeconds);
        }

        /// <summary>
        /// Updates active downloads gauge
        /// </summary>
        public void SetActiveDownloads(string downloadType, int count)
        {
            lock (_metricsLock)
            {
                _gauges[ACTIVE_DOWNLOADS_GAUGE].WithLabels(downloadType).Set(count);
            }
        }

        #endregion

        #region ML Optimization Metrics

        /// <summary>
        /// Records ML query optimization metrics
        /// </summary>
        public void RecordMLOptimization(string strategy, bool successful, double confidenceScore = 0.0)
        {
            var result = successful ? "applied" : "skipped";
            
            lock (_metricsLock)
            {
                _counters[ML_OPTIMIZATIONS_TOTAL].WithLabels(strategy, result).Inc();
            }

            _metricsLogger.Debug("ML optimization: {0} - {1} (confidence: {2:F2})",
                strategy, result, confidenceScore);
        }

        #endregion

        #region Service Health Metrics

        /// <summary>
        /// Updates service health status for monitoring
        /// </summary>
        public void SetServiceHealth(string serviceName, string component, bool healthy)
        {
            var healthValue = healthy ? 1.0 : 0.0;
            
            lock (_metricsLock)
            {
                _gauges[SERVICE_HEALTH_STATUS].WithLabels(serviceName, component).Set(healthValue);
            }

            if (!healthy)
            {
                _metricsLogger.Warn("Service unhealthy: {0}.{1}", serviceName, component);
            }
        }

        #endregion

        #region Metrics Export

        /// <summary>
        /// Exports all metrics in Prometheus format
        /// </summary>
        public async Task<string> ExportPrometheusMetricsAsync()
        {
            return await Task.FromResult(ExportMetrics());
        }

        /// <summary>
        /// Gets current metrics summary for monitoring dashboards
        /// </summary>
        public MetricsSummary GetMetricsSummary()
        {
            lock (_metricsLock)
            {
                return new MetricsSummary
                {
                    Timestamp = DateTime.UtcNow,
                    TotalApiRequests = GetCounterValue(API_REQUESTS_TOTAL),
                    CacheHitRatio = GetAverageCacheHitRatio(),
                    ActiveDownloads = GetGaugeValue(ACTIVE_DOWNLOADS_GAUGE),
                    TotalDownloads = GetCounterValue(DOWNLOAD_OPERATIONS_TOTAL),
                    AuthenticationFailures = GetCounterValue(AUTHENTICATION_ATTEMPTS_TOTAL, "failure"),
                    QualityFallbacks = GetCounterValue(QUALITY_FALLBACKS_TOTAL),
                    MLOptimizations = GetCounterValue(ML_OPTIMIZATIONS_TOTAL, "applied"),
                    UnhealthyServices = CountUnhealthyServices()
                };
            }
        }

        private string ExportMetrics()
        {
            var export = new System.Text.StringBuilder();
            
            foreach (var counter in _counters.Values)
            {
                export.AppendLine(counter.Export());
            }
            
            foreach (var histogram in _histograms.Values)
            {
                export.AppendLine(histogram.Export());
            }
            
            foreach (var gauge in _gauges.Values)
            {
                export.AppendLine(gauge.Export());
            }
            
            return export.ToString();
        }

        private double GetCounterValue(string metricName, string labelFilter = null)
        {
            if (_counters.TryGetValue(metricName, out var counter))
            {
                return counter.GetValue(labelFilter);
            }
            return 0.0;
        }

        private double GetGaugeValue(string metricName, string labelFilter = null)
        {
            if (_gauges.TryGetValue(metricName, out var gauge))
            {
                return gauge.GetValue(labelFilter);
            }
            return 0.0;
        }

        private double GetAverageCacheHitRatio()
        {
            if (_gauges.TryGetValue(CACHE_HIT_RATIO, out var gauge))
            {
                return gauge.GetAverageValue();
            }
            return 0.0;
        }

        private int CountUnhealthyServices()
        {
            if (_gauges.TryGetValue(SERVICE_HEALTH_STATUS, out var gauge))
            {
                return gauge.CountUnhealthyServices();
            }
            return 0;
        }

        #endregion

        #region Helper Methods

        private static string GetStatusLabel(int statusCode)
        {
            return statusCode switch
            {
                >= 200 and < 300 => "success",
                >= 400 and < 500 => "client_error", 
                >= 500 => "server_error",
                _ => "unknown"
            };
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _logger.Info("Metrics collector shutting down");
            
            // Export final metrics before shutdown
            try
            {
                var finalMetrics = GetMetricsSummary();
                _metricsLogger.Info("Final metrics - API: {0}, Downloads: {1}, Cache Hit: {2:F2}%",
                    finalMetrics.TotalApiRequests, finalMetrics.TotalDownloads, finalMetrics.CacheHitRatio * 100);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error exporting final metrics");
            }
        }

        #endregion
    }

    #region Supporting Interfaces and Models

    public interface IMetricsCollector
    {
        void RecordApiRequest(string endpoint, TimeSpan duration, int statusCode, string method = "GET");
        void RecordApiCall(string endpoint, TimeSpan duration, bool wasCached, string cacheKey = null);
        void RecordCacheOperation(string cacheType, string operation, bool success);
        void RecordCacheHit(string cacheType, string key, bool hit, TimeSpan? lookupDuration = null);
        void RecordQualityFallback(string originalQuality, string fallbackQuality, string reason);
        void RecordAuthenticationAttempt(string authenticationType, bool success, string failureReason = null);
        void RecordDownloadOperation(string downloadType, TimeSpan duration, bool success, string quality = "unknown");
        void SetActiveDownloads(string downloadType, int count);
        void RecordMLOptimization(string strategy, bool successful, double confidenceScore = 0.0);
        void SetServiceHealth(string serviceName, string component, bool healthy);
        Task<string> ExportPrometheusMetricsAsync();
        MetricsSummary GetMetricsSummary();
    }

    public class MetricsSummary
    {
        public DateTime Timestamp { get; set; }
        public double TotalApiRequests { get; set; }
        public double CacheHitRatio { get; set; }
        public double ActiveDownloads { get; set; }
        public double TotalDownloads { get; set; }
        public double AuthenticationFailures { get; set; }
        public double QualityFallbacks { get; set; }
        public double MLOptimizations { get; set; }
        public int UnhealthyServices { get; set; }
    }

    #endregion

    #region Prometheus Metric Implementations (Simplified)
    
    // Note: In production, these would use prometheus-net library classes
    // This implementation provides the interface for the observability layer

    internal class PrometheusCounter
    {
        public string Name { get; }
        public string Help { get; }
        public string[] LabelNames { get; }
        
        public PrometheusCounter(string name, string help, string[] labelNames)
        {
            Name = name;
            Help = help;
            LabelNames = labelNames;
        }

        public PrometheusCounter WithLabels(params string[] labels) => this;
        public void Inc() { }
        public double GetValue(string labelFilter = null) => 0.0;
        public bool TryGetValue(string labelFilter, out double value) { value = 0.0; return false; }
        public string Export() => $"# HELP {Name} {Help}\n# TYPE {Name} counter\n";
    }

    internal class PrometheusHistogram
    {
        public string Name { get; }
        public string Help { get; }
        public string[] LabelNames { get; }
        public double[] Buckets { get; }
        
        public PrometheusHistogram(string name, string help, string[] labelNames, double[] buckets)
        {
            Name = name;
            Help = help;
            LabelNames = labelNames;
            Buckets = buckets;
        }

        public PrometheusHistogram WithLabels(params string[] labels) => this;
        public void Observe(double value) { }
        public string Export() => $"# HELP {Name} {Help}\n# TYPE {Name} histogram\n";
    }

    internal class PrometheusGauge
    {
        public string Name { get; }
        public string Help { get; }
        public string[] LabelNames { get; }
        
        public PrometheusGauge(string name, string help, string[] labelNames)
        {
            Name = name;
            Help = help;
            LabelNames = labelNames;
        }

        public PrometheusGauge WithLabels(params string[] labels) => this;
        public void Set(double value) { }
        public double GetValue(string labelFilter = null) => 0.0;
        public double GetAverageValue() => 0.0;
        public int CountUnhealthyServices() => 0;
        public string Export() => $"# HELP {Name} {Help}\n# TYPE {Name} gauge\n";
    }

    #endregion
}