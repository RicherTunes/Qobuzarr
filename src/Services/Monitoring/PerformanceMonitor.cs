using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;

namespace Lidarr.Plugin.Qobuzarr.Services.Monitoring
{
    /// <summary>
    /// Production performance monitoring service for tracking key operations.
    /// Focuses on quality detection, API calls, and caching performance.
    /// </summary>
    public class PerformanceMonitor : IPerformanceMonitor
    {
        private readonly IQobuzLogger _logger;
        private readonly ConcurrentDictionary<string, List<PerformanceMetric>> _metrics;
        private readonly object _statsLock = new();
        
        // Performance thresholds (configurable in production)
        private const double SLOW_OPERATION_THRESHOLD_MS = 1000;
        private const double VERY_SLOW_OPERATION_THRESHOLD_MS = 5000;
        private const int MAX_METRICS_PER_OPERATION = 1000; // Rolling window

        public PerformanceMonitor(IQobuzLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _metrics = new ConcurrentDictionary<string, List<PerformanceMetric>>();
        }

        public async Task<T> TrackOperationAsync<T>(string operationName, Func<Task<T>> operation, Dictionary<string, object> metadata = null)
        {
            if (string.IsNullOrWhiteSpace(operationName))
                throw new ArgumentException("Operation name cannot be null or empty", nameof(operationName));
            
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            var stopwatch = Stopwatch.StartNew();
            var startTime = DateTime.UtcNow;
            Exception? operationException = null;
            T? result = default(T);

            try
            {
                _logger.Debug("Starting operation: {0}", operationName);
                result = await operation();
                return result;
            }
            catch (Exception ex)
            {
                operationException = ex;
                throw;
            }
            finally
            {
                stopwatch.Stop();
                var executionTimeMs = stopwatch.Elapsed.TotalMilliseconds;
                
                RecordOperationMetric(operationName, executionTimeMs, operationException == null, metadata, startTime);
                LogPerformanceMetrics(operationName, executionTimeMs, operationException);
            }
        }

        public T TrackOperation<T>(string operationName, Func<T> operation, Dictionary<string, object> metadata = null)
        {
            if (string.IsNullOrWhiteSpace(operationName))
                throw new ArgumentException("Operation name cannot be null or empty", nameof(operationName));
            
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            var stopwatch = Stopwatch.StartNew();
            var startTime = DateTime.UtcNow;
            Exception? operationException = null;
            T? result = default(T);

            try
            {
                _logger.Debug("Starting operation: {0}", operationName);
                result = operation();
                return result;
            }
            catch (Exception ex)
            {
                operationException = ex;
                throw;
            }
            finally
            {
                stopwatch.Stop();
                var executionTimeMs = stopwatch.Elapsed.TotalMilliseconds;
                
                RecordOperationMetric(operationName, executionTimeMs, operationException == null, metadata, startTime);
                LogPerformanceMetrics(operationName, executionTimeMs, operationException);
            }
        }

        public void RecordMetric(string metricName, double value, Dictionary<string, object> metadata = null)
        {
            if (string.IsNullOrWhiteSpace(metricName))
                throw new ArgumentException("Metric name cannot be null or empty", nameof(metricName));

            var metric = new PerformanceMetric
            {
                OperationName = metricName,
                ExecutionTimeMs = value,
                Success = true,
                Timestamp = DateTime.UtcNow,
                Metadata = metadata ?? new Dictionary<string, object>()
            };

            AddMetricToCollection(metricName, metric);
            
            _logger.Debug("Recorded metric {0}: {1:F2}ms", metricName, value);
        }

        public PerformanceStatistics GetStatistics(string operationName = null)
        {
            if (string.IsNullOrWhiteSpace(operationName))
            {
                // Return aggregated statistics for all operations
                return GetAggregatedStatistics();
            }

            if (!_metrics.TryGetValue(operationName, out var metrics))
            {
                return new PerformanceStatistics
                {
                    OperationName = operationName,
                    LastUpdated = DateTime.UtcNow
                };
            }

            lock (_statsLock)
            {
                var successfulMetrics = metrics.Where(m => m.Success).ToList();
                var executionTimes = successfulMetrics.Select(m => m.ExecutionTimeMs).OrderBy(t => t).ToList();

                if (!executionTimes.Any())
                {
                    return new PerformanceStatistics
                    {
                        OperationName = operationName,
                        TotalOperations = metrics.Count,
                        ErrorCount = metrics.Count(m => !m.Success),
                        LastUpdated = DateTime.UtcNow
                    };
                }

                var p95Index = (int)Math.Ceiling(executionTimes.Count * 0.95) - 1;
                p95Index = Math.Max(0, Math.Min(p95Index, executionTimes.Count - 1));

                return new PerformanceStatistics
                {
                    OperationName = operationName,
                    TotalOperations = metrics.Count,
                    AverageExecutionTimeMs = executionTimes.Average(),
                    MinExecutionTimeMs = executionTimes.Min(),
                    MaxExecutionTimeMs = executionTimes.Max(),
                    P95ExecutionTimeMs = executionTimes[p95Index],
                    ErrorCount = metrics.Count(m => !m.Success),
                    LastUpdated = DateTime.UtcNow,
                    Metadata = GetAggregatedMetadata(successfulMetrics)
                };
            }
        }

        private void RecordOperationMetric(string operationName, double executionTimeMs, bool success, 
            Dictionary<string, object> metadata, DateTime startTime)
        {
            var metric = new PerformanceMetric
            {
                OperationName = operationName,
                ExecutionTimeMs = executionTimeMs,
                Success = success,
                Timestamp = startTime,
                Metadata = metadata ?? new Dictionary<string, object>()
            };

            AddMetricToCollection(operationName, metric);
        }

        private void AddMetricToCollection(string operationName, PerformanceMetric metric)
        {
            _metrics.AddOrUpdate(
                operationName,
                new List<PerformanceMetric> { metric },
                (key, existingMetrics) =>
                {
                    lock (_statsLock)
                    {
                        existingMetrics.Add(metric);
                        
                        // Keep rolling window of metrics to prevent memory growth
                        if (existingMetrics.Count > MAX_METRICS_PER_OPERATION)
                        {
                            existingMetrics.RemoveRange(0, existingMetrics.Count - MAX_METRICS_PER_OPERATION);
                        }
                        
                        return existingMetrics;
                    }
                });
        }

        private void LogPerformanceMetrics(string operationName, double executionTimeMs, Exception exception)
        {
            var logLevel = GetLogLevel(executionTimeMs, exception != null);
            
            if (exception != null)
            {
                _logger.Error(exception, "Operation {0} failed after {1:F2}ms", operationName, executionTimeMs);
            }
            else if (executionTimeMs >= VERY_SLOW_OPERATION_THRESHOLD_MS)
            {
                _logger.Warn("Very slow operation {0}: {1:F2}ms", operationName, executionTimeMs);
            }
            else if (executionTimeMs >= SLOW_OPERATION_THRESHOLD_MS)
            {
                _logger.Info("Slow operation {0}: {1:F2}ms", operationName, executionTimeMs);
            }
            else
            {
                _logger.Debug("Operation {0} completed in {1:F2}ms", operationName, executionTimeMs);
            }
        }

        private string GetLogLevel(double executionTimeMs, bool hasError)
        {
            if (hasError) return "Error";
            if (executionTimeMs >= VERY_SLOW_OPERATION_THRESHOLD_MS) return "Warn";
            if (executionTimeMs >= SLOW_OPERATION_THRESHOLD_MS) return "Info";
            return "Debug";
        }

        private PerformanceStatistics GetAggregatedStatistics()
        {
            lock (_statsLock)
            {
                var allMetrics = _metrics.Values.SelectMany(m => m).ToList();
                var successfulMetrics = allMetrics.Where(m => m.Success).ToList();
                var executionTimes = successfulMetrics.Select(m => m.ExecutionTimeMs).OrderBy(t => t).ToList();

                if (!executionTimes.Any())
                {
                    return new PerformanceStatistics
                    {
                        OperationName = "All Operations",
                        TotalOperations = allMetrics.Count,
                        ErrorCount = allMetrics.Count(m => !m.Success),
                        LastUpdated = DateTime.UtcNow
                    };
                }

                var p95Index = (int)Math.Ceiling(executionTimes.Count * 0.95) - 1;
                p95Index = Math.Max(0, Math.Min(p95Index, executionTimes.Count - 1));

                return new PerformanceStatistics
                {
                    OperationName = "All Operations",
                    TotalOperations = allMetrics.Count,
                    AverageExecutionTimeMs = executionTimes.Average(),
                    MinExecutionTimeMs = executionTimes.Min(),
                    MaxExecutionTimeMs = executionTimes.Max(),
                    P95ExecutionTimeMs = executionTimes[p95Index],
                    ErrorCount = allMetrics.Count(m => !m.Success),
                    LastUpdated = DateTime.UtcNow,
                    Metadata = GetAggregatedMetadata(successfulMetrics)
                };
            }
        }

        private Dictionary<string, object> GetAggregatedMetadata(List<PerformanceMetric> metrics)
        {
            var aggregated = new Dictionary<string, object>();
            
            if (metrics.Any())
            {
                aggregated["sample_count"] = metrics.Count;
                aggregated["time_range"] = $"{metrics.Min(m => m.Timestamp):yyyy-MM-dd HH:mm} - {metrics.Max(m => m.Timestamp):yyyy-MM-dd HH:mm}";
                
                // Aggregate common metadata keys
                var commonKeys = metrics
                    .SelectMany(m => m.Metadata.Keys)
                    .GroupBy(k => k)
                    .Where(g => g.Count() > metrics.Count * 0.1) // Present in at least 10% of samples
                    .Select(g => g.Key)
                    .ToList();

                foreach (var key in commonKeys)
                {
                    var values = metrics
                        .Where(m => m.Metadata.ContainsKey(key))
                        .Select(m => m.Metadata[key])
                        .ToList();
                    
                    if (values.Any())
                    {
                        aggregated[$"common_{key}"] = values.First(); // Take first representative value
                    }
                }
            }
            
            return aggregated;
        }
    }

    /// <summary>
    /// Internal performance metric data structure.
    /// </summary>
    internal class PerformanceMetric
    {
        public string OperationName { get; set; }
        public double ExecutionTimeMs { get; set; }
        public bool Success { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}