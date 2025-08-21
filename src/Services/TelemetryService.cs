using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NzbDrone.Core.Messaging.Events;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    public interface ITelemetryService
    {
        void RecordMetric(string name, double value, Dictionary<string, string> tags = null);
        void RecordDuration(string operation, TimeSpan duration, Dictionary<string, string> tags = null);
        void RecordEvent(string eventName, Dictionary<string, string> properties = null);
        void RecordException(Exception exception, Dictionary<string, string> properties = null);
        Task<TelemetrySnapshot> GetSnapshotAsync();
        Task<HealthStatus> GetHealthStatusAsync();
    }

    public class TelemetryService : ITelemetryService, IHandle<ApplicationStartedEvent>
    {
        private readonly ILogger<TelemetryService> _logger;
        private readonly ConcurrentDictionary<string, MetricAggregator> _metrics;
        private readonly ConcurrentQueue<TelemetryEvent> _events;
        private readonly Timer _flushTimer;
        private readonly Stopwatch _uptime;
        
        private const int MaxEventsInMemory = 10000;
        private const int FlushIntervalSeconds = 60;
        
        public TelemetryService(ILogger<TelemetryService> logger)
        {
            _logger = logger;
            _metrics = new ConcurrentDictionary<string, MetricAggregator>();
            _events = new ConcurrentQueue<TelemetryEvent>();
            _uptime = Stopwatch.StartNew();
            
            _flushTimer = new Timer(
                FlushMetrics,
                null,
                TimeSpan.FromSeconds(FlushIntervalSeconds),
                TimeSpan.FromSeconds(FlushIntervalSeconds));
        }

        public void RecordMetric(string name, double value, Dictionary<string, string> tags = null)
        {
            var key = GenerateMetricKey(name, tags);
            var aggregator = _metrics.GetOrAdd(key, k => new MetricAggregator(name, tags));
            aggregator.Record(value);
            
            _logger.LogDebug("Recorded metric {MetricName}={Value} with tags {Tags}", 
                name, value, tags != null ? string.Join(", ", tags) : "none");
        }

        public void RecordDuration(string operation, TimeSpan duration, Dictionary<string, string> tags = null)
        {
            RecordMetric($"{operation}.duration_ms", duration.TotalMilliseconds, tags);
            
            // Record performance buckets for SLA tracking
            var bucket = GetDurationBucket(duration);
            var bucketTags = new Dictionary<string, string>(tags ?? new Dictionary<string, string>())
            {
                ["bucket"] = bucket
            };
            RecordMetric($"{operation}.duration_bucket", 1, bucketTags);
        }

        public void RecordEvent(string eventName, Dictionary<string, string> properties = null)
        {
            var telemetryEvent = new TelemetryEvent
            {
                Name = eventName,
                Timestamp = DateTime.UtcNow,
                Properties = properties ?? new Dictionary<string, string>()
            };
            
            _events.Enqueue(telemetryEvent);
            
            // Prevent unbounded memory growth
            while (_events.Count > MaxEventsInMemory)
            {
                _events.TryDequeue(out _);
            }
            
            _logger.LogDebug("Recorded event {EventName} with properties {Properties}",
                eventName, properties != null ? string.Join(", ", properties) : "none");
        }

        public void RecordException(Exception exception, Dictionary<string, string> properties = null)
        {
            var exceptionProperties = new Dictionary<string, string>(properties ?? new Dictionary<string, string>())
            {
                ["exception_type"] = exception.GetType().FullName,
                ["exception_message"] = exception.Message,
                ["stack_trace"] = exception.StackTrace?.Substring(0, Math.Min(exception.StackTrace.Length, 1000))
            };
            
            RecordEvent("exception", exceptionProperties);
            RecordMetric("exceptions.total", 1, new Dictionary<string, string> { ["type"] = exception.GetType().Name });
            
            _logger.LogError(exception, "Recorded exception in telemetry");
        }

        public async Task<TelemetrySnapshot> GetSnapshotAsync()
        {
            return await Task.Run(() =>
            {
                var snapshot = new TelemetrySnapshot
                {
                    Timestamp = DateTime.UtcNow,
                    UptimeSeconds = _uptime.Elapsed.TotalSeconds,
                    Metrics = new Dictionary<string, MetricSummary>(),
                    RecentEvents = new List<TelemetryEvent>()
                };
                
                // Aggregate metrics
                foreach (var kvp in _metrics)
                {
                    var aggregator = kvp.Value;
                    snapshot.Metrics[kvp.Key] = aggregator.GetSummary();
                }
                
                // Get recent events
                snapshot.RecentEvents = _events.ToList().OrderByDescending(e => e.Timestamp).Take(100).ToList();
                
                // Calculate key performance indicators
                snapshot.KeyPerformanceIndicators = CalculateKPIs(snapshot.Metrics);
                
                return snapshot;
            });
        }

        public async Task<HealthStatus> GetHealthStatusAsync()
        {
            var snapshot = await GetSnapshotAsync();
            var status = new HealthStatus
            {
                IsHealthy = true,
                UptimeSeconds = snapshot.UptimeSeconds,
                Checks = new List<HealthCheck>()
            };
            
            // Check error rate
            var errorRate = CalculateErrorRate(snapshot.Metrics);
            status.Checks.Add(new HealthCheck
            {
                Name = "Error Rate",
                Passed = errorRate < 0.01, // Less than 1%
                Value = $"{errorRate:P2}",
                Message = errorRate < 0.01 ? "Error rate is acceptable" : "Error rate is too high"
            });
            
            // Check response time
            var avgResponseTime = CalculateAverageResponseTime(snapshot.Metrics);
            status.Checks.Add(new HealthCheck
            {
                Name = "Average Response Time",
                Passed = avgResponseTime < 1000, // Less than 1 second
                Value = $"{avgResponseTime:F0}ms",
                Message = avgResponseTime < 1000 ? "Response time is good" : "Response time is slow"
            });
            
            // Check memory usage
            var memoryUsageMB = GC.GetTotalMemory(false) / (1024 * 1024);
            status.Checks.Add(new HealthCheck
            {
                Name = "Memory Usage",
                Passed = memoryUsageMB < 500, // Less than 500MB
                Value = $"{memoryUsageMB}MB",
                Message = memoryUsageMB < 500 ? "Memory usage is normal" : "Memory usage is high"
            });
            
            // Overall health
            status.IsHealthy = status.Checks.All(c => c.Passed);
            status.Summary = status.IsHealthy 
                ? "All health checks passed" 
                : $"{status.Checks.Count(c => !c.Passed)} health checks failed";
            
            return status;
        }

        public void Handle(ApplicationStartedEvent message)
        {
            RecordEvent("application_started", new Dictionary<string, string>
            {
                ["version"] = GetType().Assembly.GetName().Version?.ToString() ?? "unknown",
                ["environment"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "production"
            });
            
            _logger.LogInformation("Telemetry service started and recording metrics");
        }

        private void FlushMetrics(object state)
        {
            try
            {
                var metricsToFlush = _metrics.Values
                    .Where(m => m.HasData)
                    .Select(m => m.GetSummary())
                    .ToList();
                
                if (metricsToFlush.Any())
                {
                    _logger.LogInformation("Flushing {Count} metrics to telemetry backend", metricsToFlush.Count);
                    
                    // In production, this would send to Application Insights, Datadog, etc.
                    // For now, just log summary
                    foreach (var metric in metricsToFlush.Take(5))
                    {
                        _logger.LogDebug("Metric: {Name} - Count: {Count}, Avg: {Avg:F2}, P95: {P95:F2}",
                            metric.Name, metric.Count, metric.Average, metric.Percentile95);
                    }
                    
                    // Reset aggregators after flush
                    foreach (var aggregator in _metrics.Values)
                    {
                        aggregator.Reset();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing telemetry metrics");
            }
        }

        private string GenerateMetricKey(string name, Dictionary<string, string> tags)
        {
            if (tags == null || !tags.Any())
                return name;
            
            var tagString = string.Join(",", tags.OrderBy(t => t.Key).Select(t => $"{t.Key}={t.Value}"));
            return $"{name}#{tagString}";
        }

        private string GetDurationBucket(TimeSpan duration)
        {
            var ms = duration.TotalMilliseconds;
            return ms switch
            {
                < 100 => "0-100ms",
                < 250 => "100-250ms",
                < 500 => "250-500ms",
                < 1000 => "500-1000ms",
                < 2500 => "1-2.5s",
                < 5000 => "2.5-5s",
                _ => "5s+"
            };
        }

        private Dictionary<string, double> CalculateKPIs(Dictionary<string, MetricSummary> metrics)
        {
            var kpis = new Dictionary<string, double>();
            
            // Calculate average API response time
            var apiMetrics = metrics.Where(m => m.Key.Contains("api") && m.Key.Contains("duration"));
            if (apiMetrics.Any())
            {
                kpis["api_response_time_avg_ms"] = apiMetrics.Average(m => m.Value.Average);
                kpis["api_response_time_p95_ms"] = apiMetrics.Max(m => m.Value.Percentile95);
            }
            
            // Calculate throughput
            var requestMetrics = metrics.Where(m => m.Key.Contains("requests.total"));
            if (requestMetrics.Any())
            {
                var totalRequests = requestMetrics.Sum(m => m.Value.Count);
                var uptimeMinutes = _uptime.Elapsed.TotalMinutes;
                kpis["requests_per_minute"] = uptimeMinutes > 0 ? totalRequests / uptimeMinutes : 0;
            }
            
            // Calculate error rate
            kpis["error_rate"] = CalculateErrorRate(metrics);
            
            return kpis;
        }

        private double CalculateErrorRate(Dictionary<string, MetricSummary> metrics)
        {
            var totalRequests = metrics.Where(m => m.Key.Contains("requests.total"))
                .Sum(m => m.Value.Count);
            var errors = metrics.Where(m => m.Key.Contains("exceptions.total"))
                .Sum(m => m.Value.Count);
            
            return totalRequests > 0 ? (double)errors / totalRequests : 0;
        }

        private double CalculateAverageResponseTime(Dictionary<string, MetricSummary> metrics)
        {
            var responseTimeMetrics = metrics.Where(m => m.Key.Contains("duration_ms"));
            return responseTimeMetrics.Any() ? responseTimeMetrics.Average(m => m.Value.Average) : 0;
        }
    }

    public class MetricAggregator
    {
        private readonly object _lock = new object();
        private readonly List<double> _values = new List<double>();
        private readonly string _name;
        private readonly Dictionary<string, string> _tags;
        
        public bool HasData => _values.Any();
        
        public MetricAggregator(string name, Dictionary<string, string> tags)
        {
            _name = name;
            _tags = tags ?? new Dictionary<string, string>();
        }
        
        public void Record(double value)
        {
            lock (_lock)
            {
                _values.Add(value);
            }
        }
        
        public MetricSummary GetSummary()
        {
            lock (_lock)
            {
                if (!_values.Any())
                {
                    return new MetricSummary
                    {
                        Name = _name,
                        Tags = _tags,
                        Count = 0
                    };
                }
                
                var sorted = _values.OrderBy(v => v).ToList();
                return new MetricSummary
                {
                    Name = _name,
                    Tags = _tags,
                    Count = sorted.Count,
                    Sum = sorted.Sum(),
                    Average = sorted.Average(),
                    Min = sorted.First(),
                    Max = sorted.Last(),
                    Percentile50 = GetPercentile(sorted, 0.5),
                    Percentile95 = GetPercentile(sorted, 0.95),
                    Percentile99 = GetPercentile(sorted, 0.99)
                };
            }
        }
        
        public void Reset()
        {
            lock (_lock)
            {
                _values.Clear();
            }
        }
        
        private double GetPercentile(List<double> sorted, double percentile)
        {
            var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
            return sorted[Math.Max(0, Math.Min(index, sorted.Count - 1))];
        }
    }

    public class TelemetrySnapshot
    {
        public DateTime Timestamp { get; set; }
        public double UptimeSeconds { get; set; }
        public Dictionary<string, MetricSummary> Metrics { get; set; }
        public List<TelemetryEvent> RecentEvents { get; set; }
        public Dictionary<string, double> KeyPerformanceIndicators { get; set; }
    }

    public class MetricSummary
    {
        public string Name { get; set; }
        public Dictionary<string, string> Tags { get; set; }
        public long Count { get; set; }
        public double Sum { get; set; }
        public double Average { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double Percentile50 { get; set; }
        public double Percentile95 { get; set; }
        public double Percentile99 { get; set; }
    }

    public class TelemetryEvent
    {
        public string Name { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, string> Properties { get; set; }
    }

    public class HealthStatus
    {
        public bool IsHealthy { get; set; }
        public string Summary { get; set; }
        public double UptimeSeconds { get; set; }
        public List<HealthCheck> Checks { get; set; }
    }

    public class HealthCheck
    {
        public string Name { get; set; }
        public bool Passed { get; set; }
        public string Value { get; set; }
        public string Message { get; set; }
    }
}