using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Services.Monitoring
{
    /// <summary>
    /// Collects and aggregates metrics for search operations
    /// </summary>
    public class SearchMetricsCollector : ISearchMetricsCollector
    {
        private readonly Logger _logger;
        private readonly ConcurrentQueue<SearchMetric> _metrics;
        private readonly Timer _reportingTimer;
        private readonly object _aggregateLock = new object();
        
        // Aggregated stats
        private long _totalSearches;
        private long _successfulSearches;
        private long _failedSearches;
        private long _totalApiCalls;
        private long _totalResultsReturned;
        private double _totalSearchDuration;
        private readonly Dictionary<string, long> _errorCounts;
        private readonly Dictionary<SearchComplexity, long> _complexityCounts;

        public SearchMetricsCollector(Logger logger)
        {
            _logger = logger;
            _metrics = new ConcurrentQueue<SearchMetric>();
            _errorCounts = new Dictionary<string, long>();
            _complexityCounts = new Dictionary<SearchComplexity, long>();
            
            // Report metrics every 5 minutes
            _reportingTimer = new Timer(ReportMetrics, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public void RecordSearch(SearchMetric metric)
        {
            _metrics.Enqueue(metric);
            
            // Keep only last 1000 metrics in memory
            while (_metrics.Count > 1000 && _metrics.TryDequeue(out _))
            {
                // Discard old metrics
            }
            
            UpdateAggregates(metric);
        }

        public SearchMetric StartSearch(string artist, string album)
        {
            return new SearchMetric
            {
                Id = Guid.NewGuid(),
                Artist = artist,
                Album = album,
                StartTime = DateTime.UtcNow,
                Stopwatch = Stopwatch.StartNew()
            };
        }

        public void CompleteSearch(SearchMetric metric, bool success, int resultsCount, int apiCallsCount)
        {
            metric.Stopwatch.Stop();
            metric.EndTime = DateTime.UtcNow;
            metric.Duration = metric.Stopwatch.Elapsed;
            metric.Success = success;
            metric.ResultsCount = resultsCount;
            metric.ApiCallsCount = apiCallsCount;
            
            RecordSearch(metric);
        }

        public void RecordError(SearchMetric metric, Exception exception)
        {
            metric.Success = false;
            metric.ErrorType = exception.GetType().Name;
            metric.ErrorMessage = exception.Message;
            
            CompleteSearch(metric, false, 0, metric.ApiCallsCount);
        }

        public SearchStatistics GetStatistics()
        {
            lock (_aggregateLock)
            {
                return new SearchStatistics
                {
                    TotalSearches = _totalSearches,
                    SuccessfulSearches = _successfulSearches,
                    FailedSearches = _failedSearches,
                    SuccessRate = _totalSearches > 0 ? (double)_successfulSearches / _totalSearches : 0,
                    TotalApiCalls = _totalApiCalls,
                    AverageApiCallsPerSearch = _totalSearches > 0 ? (double)_totalApiCalls / _totalSearches : 0,
                    TotalResultsReturned = _totalResultsReturned,
                    AverageResultsPerSearch = _totalSearches > 0 ? (double)_totalResultsReturned / _totalSearches : 0,
                    AverageSearchDuration = _totalSearches > 0 ? TimeSpan.FromMilliseconds(_totalSearchDuration / _totalSearches) : TimeSpan.Zero,
                    ErrorCounts = new Dictionary<string, long>(_errorCounts),
                    ComplexityCounts = new Dictionary<SearchComplexity, long>(_complexityCounts),
                    RecentMetrics = _metrics.TakeLast(100).ToList()
                };
            }
        }

        private void UpdateAggregates(SearchMetric metric)
        {
            lock (_aggregateLock)
            {
                _totalSearches++;
                
                if (metric.Success)
                {
                    _successfulSearches++;
                }
                else
                {
                    _failedSearches++;
                    
                    if (!string.IsNullOrEmpty(metric.ErrorType))
                    {
                        if (_errorCounts.ContainsKey(metric.ErrorType))
                        {
                            _errorCounts[metric.ErrorType]++;
                        }
                        else
                        {
                            _errorCounts[metric.ErrorType] = 1;
                        }
                    }
                }
                
                _totalApiCalls += metric.ApiCallsCount;
                _totalResultsReturned += metric.ResultsCount;
                _totalSearchDuration += metric.Duration.TotalMilliseconds;
                
                if (_complexityCounts.ContainsKey(metric.Complexity))
                {
                    _complexityCounts[metric.Complexity]++;
                }
                else
                {
                    _complexityCounts[metric.Complexity] = 1;
                }
            }
        }

        private void ReportMetrics(object state)
        {
            try
            {
                var stats = GetStatistics();
                
                _logger.Info("Search Metrics Report:");
                _logger.Info("  Total Searches: {0} (Success: {1:P1})", stats.TotalSearches, stats.SuccessRate);
                _logger.Info("  Average Duration: {0:F2}ms", stats.AverageSearchDuration.TotalMilliseconds);
                _logger.Info("  Average API Calls: {0:F1}", stats.AverageApiCallsPerSearch);
                _logger.Info("  Average Results: {0:F1}", stats.AverageResultsPerSearch);
                
                if (stats.ErrorCounts.Any())
                {
                    _logger.Info("  Top Errors:");
                    foreach (var error in stats.ErrorCounts.OrderByDescending(e => e.Value).Take(5))
                    {
                        _logger.Info("    {0}: {1}", error.Key, error.Value);
                    }
                }
                
                if (stats.ComplexityCounts.Any())
                {
                    _logger.Info("  Search Complexity Distribution:");
                    foreach (var complexity in stats.ComplexityCounts.OrderBy(c => c.Key))
                    {
                        var percentage = (double)complexity.Value / stats.TotalSearches * 100;
                        _logger.Info("    {0}: {1} ({2:F1}%)", complexity.Key, complexity.Value, percentage);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error reporting metrics");
            }
        }

        public void Dispose()
        {
            _reportingTimer?.Dispose();
            ReportMetrics(null); // Final report
        }
    }

    public class SearchMetric
    {
        public Guid Id { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public bool Success { get; set; }
        public int ResultsCount { get; set; }
        public int ApiCallsCount { get; set; }
        public SearchComplexity Complexity { get; set; }
        public string ErrorType { get; set; }
        public string ErrorMessage { get; set; }
        public Stopwatch Stopwatch { get; set; }
    }

    public class SearchStatistics
    {
        public long TotalSearches { get; set; }
        public long SuccessfulSearches { get; set; }
        public long FailedSearches { get; set; }
        public double SuccessRate { get; set; }
        public long TotalApiCalls { get; set; }
        public double AverageApiCallsPerSearch { get; set; }
        public long TotalResultsReturned { get; set; }
        public double AverageResultsPerSearch { get; set; }
        public TimeSpan AverageSearchDuration { get; set; }
        public Dictionary<string, long> ErrorCounts { get; set; }
        public Dictionary<SearchComplexity, long> ComplexityCounts { get; set; }
        public List<SearchMetric> RecentMetrics { get; set; }
    }

    public enum SearchComplexity
    {
        Simple,
        Medium,
        Complex
    }
}