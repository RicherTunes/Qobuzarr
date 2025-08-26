using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Common.Services.Performance
{
    /// <summary>
    /// Generic performance monitoring service for streaming plugins.
    /// Tracks API calls, cache performance, and operation timings.
    /// </summary>
    public class PerformanceMonitor : IDisposable
    {
        private readonly ConcurrentDictionary<string, OperationMetrics> _metrics = new();
        private readonly ConcurrentQueue<PerformanceEvent> _events = new();
        private readonly Timer _flushTimer;
        private readonly object _flushLock = new object();
        private bool _disposed = false;

        public PerformanceMonitor(TimeSpan? flushInterval = null)
        {
            var interval = flushInterval ?? TimeSpan.FromMinutes(5);
            _flushTimer = new Timer(FlushMetrics, null, interval, interval);
        }

        /// <summary>
        /// Records an API call timing and result.
        /// </summary>
        public void RecordApiCall(string endpoint, TimeSpan duration, bool fromCache = false, int? statusCode = null)
        {
            var operationKey = $"api_{endpoint}";
            var metrics = _metrics.GetOrAdd(operationKey, _ => new OperationMetrics { Name = endpoint, Type = MetricType.ApiCall });

            lock (metrics.Lock)
            {
                metrics.TotalCalls++;
                metrics.TotalDuration += duration;
                
                if (fromCache)
                    metrics.CacheHits++;
                
                if (statusCode.HasValue && statusCode >= 400)
                    metrics.ErrorCount++;
                
                metrics.LastUpdated = DateTime.UtcNow;
            }

            RecordEvent(new PerformanceEvent
            {
                Type = EventType.ApiCall,
                Name = endpoint,
                Duration = duration,
                Success = statusCode < 400,
                Metadata = new Dictionary<string, object>
                {
                    ["fromCache"] = fromCache,
                    ["statusCode"] = statusCode
                }
            });
        }

        /// <summary>
        /// Records a cache operation (hit or miss).
        /// </summary>
        public void RecordCacheOperation(string cacheType, string key, bool hit, TimeSpan? duration = null)
        {
            var operationKey = $"cache_{cacheType}";
            var metrics = _metrics.GetOrAdd(operationKey, _ => new OperationMetrics { Name = cacheType, Type = MetricType.Cache });

            lock (metrics.Lock)
            {
                metrics.TotalCalls++;
                
                if (hit)
                    metrics.CacheHits++;
                
                if (duration.HasValue)
                    metrics.TotalDuration += duration.Value;
                
                metrics.LastUpdated = DateTime.UtcNow;
            }

            RecordEvent(new PerformanceEvent
            {
                Type = EventType.CacheOperation,
                Name = $"{cacheType}_{(hit ? "hit" : "miss")}",
                Duration = duration ?? TimeSpan.Zero,
                Success = true,
                Metadata = new Dictionary<string, object>
                {
                    ["cacheType"] = cacheType,
                    ["key"] = key,
                    ["hit"] = hit
                }
            });
        }

        /// <summary>
        /// Records a download operation.
        /// </summary>
        public void RecordDownload(string trackId, TimeSpan duration, long fileSize, bool success, string errorMessage = null)
        {
            var operationKey = "download_track";
            var metrics = _metrics.GetOrAdd(operationKey, _ => new OperationMetrics { Name = "Track Downloads", Type = MetricType.Download });

            lock (metrics.Lock)
            {
                metrics.TotalCalls++;
                metrics.TotalDuration += duration;
                metrics.TotalBytes += fileSize;
                
                if (!success)
                    metrics.ErrorCount++;
                
                metrics.LastUpdated = DateTime.UtcNow;
            }

            RecordEvent(new PerformanceEvent
            {
                Type = EventType.Download,
                Name = $"track_{trackId}",
                Duration = duration,
                Success = success,
                Metadata = new Dictionary<string, object>
                {
                    ["trackId"] = trackId,
                    ["fileSize"] = fileSize,
                    ["errorMessage"] = errorMessage
                }
            });
        }

        /// <summary>
        /// Records a custom operation timing.
        /// </summary>
        public void RecordOperation(string operationName, TimeSpan duration, bool success = true, Dictionary<string, object> metadata = null)
        {
            var operationKey = $"custom_{operationName}";
            var metrics = _metrics.GetOrAdd(operationKey, _ => new OperationMetrics { Name = operationName, Type = MetricType.Custom });

            lock (metrics.Lock)
            {
                metrics.TotalCalls++;
                metrics.TotalDuration += duration;
                
                if (!success)
                    metrics.ErrorCount++;
                
                metrics.LastUpdated = DateTime.UtcNow;
            }

            RecordEvent(new PerformanceEvent
            {
                Type = EventType.Custom,
                Name = operationName,
                Duration = duration,
                Success = success,
                Metadata = metadata ?? new Dictionary<string, object>()
            });
        }

        /// <summary>
        /// Gets current performance summary.
        /// </summary>
        public PerformanceSummary GetSummary()
        {
            var summary = new PerformanceSummary
            {
                CollectionStartTime = GetEarliestMetricTime(),
                LastUpdated = DateTime.UtcNow,
                TotalOperations = _metrics.Values.Sum(m => m.TotalCalls),
                TotalErrors = _metrics.Values.Sum(m => m.ErrorCount),
                Operations = new Dictionary<string, OperationSummary>()
            };

            foreach (var metric in _metrics.Values)
            {
                lock (metric.Lock)
                {
                    summary.Operations[metric.Name] = new OperationSummary
                    {
                        Type = metric.Type,
                        TotalCalls = metric.TotalCalls,
                        TotalDuration = metric.TotalDuration,
                        AverageDuration = metric.TotalCalls > 0 ? metric.TotalDuration.TotalMilliseconds / metric.TotalCalls : 0,
                        ErrorCount = metric.ErrorCount,
                        ErrorRate = metric.TotalCalls > 0 ? (double)metric.ErrorCount / metric.TotalCalls * 100 : 0,
                        CacheHitRate = metric.TotalCalls > 0 ? (double)metric.CacheHits / metric.TotalCalls * 100 : 0,
                        TotalBytes = metric.TotalBytes,
                        LastUpdated = metric.LastUpdated
                    };
                }
            }

            return summary;
        }

        /// <summary>
        /// Gets recent performance events.
        /// </summary>
        public IEnumerable<PerformanceEvent> GetRecentEvents(int count = 100)
        {
            return _events.TakeLast(count);
        }

        /// <summary>
        /// Clears all performance data.
        /// </summary>
        public void Reset()
        {
            _metrics.Clear();
            
            while (_events.TryDequeue(out _))
            {
                // Clear queue
            }
        }

        private void RecordEvent(PerformanceEvent evt)
        {
            evt.Timestamp = DateTime.UtcNow;
            _events.Enqueue(evt);

            // Keep only last 1000 events to prevent memory growth
            while (_events.Count > 1000)
            {
                _events.TryDequeue(out _);
            }
        }

        private DateTime GetEarliestMetricTime()
        {
            return _metrics.Values.Any() 
                ? _metrics.Values.Min(m => m.LastUpdated)
                : DateTime.UtcNow;
        }

        private void FlushMetrics(object state)
        {
            if (_disposed) return;

            lock (_flushLock)
            {
                try
                {
                    OnMetricsFlush(GetSummary());
                }
                catch
                {
                    // Ignore flush errors to prevent affecting plugin operation
                }
            }
        }

        /// <summary>
        /// Called when metrics are periodically flushed.
        /// Override in derived classes for custom metrics handling.
        /// </summary>
        protected virtual void OnMetricsFlush(PerformanceSummary summary) { }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _flushTimer?.Dispose();
                
                // Final flush
                try
                {
                    OnMetricsFlush(GetSummary());
                }
                catch { }
            }
        }
    }

    /// <summary>
    /// Metrics for a specific operation type.
    /// </summary>
    public class OperationMetrics
    {
        public string Name { get; set; }
        public MetricType Type { get; set; }
        public long TotalCalls { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public long ErrorCount { get; set; }
        public long CacheHits { get; set; }
        public long TotalBytes { get; set; }
        public DateTime LastUpdated { get; set; }
        public object Lock { get; } = new object();
    }

    /// <summary>
    /// Summary of operation performance.
    /// </summary>
    public class OperationSummary
    {
        public MetricType Type { get; set; }
        public long TotalCalls { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public double AverageDuration { get; set; }
        public long ErrorCount { get; set; }
        public double ErrorRate { get; set; }
        public double CacheHitRate { get; set; }
        public long TotalBytes { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    /// <summary>
    /// Individual performance event.
    /// </summary>
    public class PerformanceEvent
    {
        public DateTime Timestamp { get; set; }
        public EventType Type { get; set; }
        public string Name { get; set; }
        public TimeSpan Duration { get; set; }
        public bool Success { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Overall performance summary.
    /// </summary>
    public class PerformanceSummary
    {
        public DateTime CollectionStartTime { get; set; }
        public DateTime LastUpdated { get; set; }
        public long TotalOperations { get; set; }
        public long TotalErrors { get; set; }
        public double OverallErrorRate => TotalOperations > 0 ? (double)TotalErrors / TotalOperations * 100 : 0;
        public Dictionary<string, OperationSummary> Operations { get; set; } = new Dictionary<string, OperationSummary>();
    }

    /// <summary>
    /// Types of performance metrics.
    /// </summary>
    public enum MetricType
    {
        ApiCall,
        Cache,
        Download,
        Authentication,
        Search,
        Custom
    }

    /// <summary>
    /// Types of performance events.
    /// </summary>
    public enum EventType
    {
        ApiCall,
        CacheOperation,
        Download,
        Authentication,
        Search,
        Error,
        Custom
    }
}