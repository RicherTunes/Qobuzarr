using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Service responsible for collecting and reporting comprehensive statistics about Lidarr integration operations
    /// with advanced metrics collection and analysis optimized for the *arr ecosystem.
    /// </summary>
    public class LidarrStatisticsCollector : ILidarrStatisticsCollector
    {
        private readonly Logger _logger;
        private readonly object _statsLock = new();
        private readonly DateTime _startTime = DateTime.UtcNow;

        // Core statistics storage
        private readonly IntegrationStatistics _statistics = new();
        private readonly List<OperationRecord> _operationHistory = new();
        private readonly Dictionary<string, List<ErrorRecord>> _errorHistory = new();
        private readonly Dictionary<int, QualityProfileRecord> _qualityProfileUsage = new();

        // Performance tracking
        private readonly List<double> _concurrencyMeasurements = new();
        private readonly List<ThroughputMeasurement> _throughputHistory = new();

        // Configuration
        private const int MAX_HISTORY_RECORDS = 10000;
        private const int PERFORMANCE_WINDOW_MINUTES = 60;

        /// <summary>
        /// Initializes a new instance of the LidarrStatisticsCollector.
        /// </summary>
        /// <param name="logger">Logger for recording operations and debugging.</param>
        public LidarrStatisticsCollector(Logger logger)
        {
            _logger = Guard.NotNull(logger, nameof(logger));
            _logger.Info("LidarrStatisticsCollector initialized");
        }

        /// <summary>
        /// Records a search attempt with success/failure information.
        /// </summary>
        public void RecordSearchAttempt(bool success, int concurrency = 0)
        {
            lock (_statsLock)
            {
                _statistics.TotalSearches++;
                _statistics.LastOperationAt = DateTime.UtcNow;

                if (success)
                {
                    _statistics.SuccessfulSearches++;
                }
                else
                {
                    _statistics.FailedSearches++;
                }

                if (concurrency > 0)
                {
                    _statistics.CurrentConcurrentOperations = concurrency;
                    _statistics.PeakConcurrentOperations = Math.Max(_statistics.PeakConcurrentOperations, concurrency);
                    _concurrencyMeasurements.Add(concurrency);
                }

                // Record operation for detailed analysis
                RecordOperation("Search", success, 0, TimeSpan.Zero);
            }
        }

        /// <summary>
        /// Records a download attempt with success/failure and throughput information.
        /// </summary>
        public void RecordDownloadAttempt(bool success, long bytesDownloaded, int concurrency = 0)
        {
            lock (_statsLock)
            {
                _statistics.TotalDownloads++;
                _statistics.LastOperationAt = DateTime.UtcNow;

                if (success)
                {
                    _statistics.SuccessfulDownloads++;
                    _statistics.TotalBytesDownloaded += bytesDownloaded;
                }
                else
                {
                    _statistics.FailedDownloads++;
                }

                if (concurrency > 0)
                {
                    _statistics.CurrentConcurrentOperations = concurrency;
                    _statistics.PeakConcurrentOperations = Math.Max(_statistics.PeakConcurrentOperations, concurrency);
                    _concurrencyMeasurements.Add(concurrency);
                }

                // Record throughput measurement
                if (success && bytesDownloaded > 0)
                {
                    _throughputHistory.Add(new ThroughputMeasurement
                    {
                        Timestamp = DateTime.UtcNow,
                        BytesTransferred = bytesDownloaded,
                        OperationType = "Download"
                    });
                }

                // Record operation for detailed analysis
                RecordOperation("Download", success, bytesDownloaded, TimeSpan.Zero);
            }
        }

        /// <summary>
        /// Records an error with categorization for troubleshooting.
        /// </summary>
        public void RecordError(Exception exception, string operationType)
        {
            if (exception == null) return;

            lock (_statsLock)
            {
                var errorType = exception.GetType().Name;
                _statistics.ErrorCounts[errorType] = _statistics.ErrorCounts.GetValueOrDefault(errorType, 0) + 1;

                // Record detailed error information
                if (!_errorHistory.ContainsKey(operationType))
                {
                    _errorHistory[operationType] = new List<ErrorRecord>();
                }

                var errorRecord = new ErrorRecord
                {
                    Timestamp = DateTime.UtcNow,
                    ErrorType = errorType,
                    Message = exception.Message,
                    StackTrace = exception.StackTrace,
                    OperationType = operationType
                };

                _errorHistory[operationType].Add(errorRecord);

                // Maintain history size
                if (_errorHistory[operationType].Count > MAX_HISTORY_RECORDS / 10)
                {
                    _errorHistory[operationType].RemoveAt(0);
                }
            }

            _logger.Debug("Recorded error: {0} in {1} operation", exception.GetType().Name, operationType);
        }

        /// <summary>
        /// Records quality profile usage statistics.
        /// </summary>
        public void RecordQualityProfileUsage(int qualityProfileId, string qualityProfileName, string selectedQuality)
        {
            lock (_statsLock)
            {
                if (!_qualityProfileUsage.ContainsKey(qualityProfileId))
                {
                    _qualityProfileUsage[qualityProfileId] = new QualityProfileRecord
                    {
                        Id = qualityProfileId,
                        Name = qualityProfileName ?? "Unknown",
                        UsageCount = 0,
                        QualitySelections = new Dictionary<string, int>()
                    };
                }

                var record = _qualityProfileUsage[qualityProfileId];
                record.UsageCount++;
                record.LastUsed = DateTime.UtcNow;

                if (!string.IsNullOrEmpty(selectedQuality))
                {
                    record.QualitySelections[selectedQuality] = record.QualitySelections.GetValueOrDefault(selectedQuality, 0) + 1;

                    // Update global quality distribution
                    var qualityKey = ConvertQobuzQualityToInt(selectedQuality);
                    _statistics.QualityDistribution[qualityKey] = _statistics.QualityDistribution.GetValueOrDefault(qualityKey, 0) + 1;
                }
            }
        }

        /// <summary>
        /// Records completion of a batch operation.
        /// </summary>
        public void RecordBatchComplete(TimeSpan duration, string operationType = "Download")
        {
            lock (_statsLock)
            {
                if (operationType == "Download")
                {
                    _statistics.TotalDownloadTime = _statistics.TotalDownloadTime.Add(duration);
                }

                _statistics.LastOperationAt = DateTime.UtcNow;
            }

            _logger.Debug("Recorded batch operation completion: {0} in {1:F1}s", operationType, duration.TotalSeconds);
        }

        /// <summary>
        /// Records rate limiting events.
        /// </summary>
        public void RecordRateLimit(string endpoint, int delayMs)
        {
            lock (_statsLock)
            {
                // This could be expanded to track rate limiting statistics
                _logger.Debug("Rate limit recorded for {0}: {1}ms delay", endpoint, delayMs);
            }
        }

        /// <summary>
        /// Gets current integration statistics.
        /// </summary>
        public IntegrationStatistics GetStatistics()
        {
            lock (_statsLock)
            {
                return new IntegrationStatistics
                {
                    TotalSearches = _statistics.TotalSearches,
                    SuccessfulSearches = _statistics.SuccessfulSearches,
                    FailedSearches = _statistics.FailedSearches,
                    TotalDownloads = _statistics.TotalDownloads,
                    SuccessfulDownloads = _statistics.SuccessfulDownloads,
                    FailedDownloads = _statistics.FailedDownloads,
                    TotalBytesDownloaded = _statistics.TotalBytesDownloaded,
                    TotalDownloadTime = _statistics.TotalDownloadTime,
                    CurrentConcurrentOperations = _statistics.CurrentConcurrentOperations,
                    PeakConcurrentOperations = _statistics.PeakConcurrentOperations,
                    LastOperationAt = _statistics.LastOperationAt,
                    ErrorCounts = new Dictionary<string, int>(_statistics.ErrorCounts),
                    QualityDistribution = new Dictionary<int, int>(_statistics.QualityDistribution)
                };
            }
        }

        /// <summary>
        /// Gets detailed performance metrics.
        /// </summary>
        public PerformanceMetrics GetPerformanceMetrics()
        {
            lock (_statsLock)
            {
                var searchOps = _operationHistory.Where(o => o.OperationType == "Search").ToList();
                var downloadOps = _operationHistory.Where(o => o.OperationType == "Download").ToList();

                return new PerformanceMetrics
                {
                    SearchMetrics = CalculateOperationMetrics(searchOps),
                    DownloadMetrics = CalculateOperationMetrics(downloadOps),
                    ThroughputMetrics = CalculateThroughputMetrics(),
                    ConcurrencyMetrics = CalculateConcurrencyMetrics()
                };
            }
        }

        /// <summary>
        /// Gets quality distribution statistics.
        /// </summary>
        public QualityStatistics GetQualityStatistics()
        {
            lock (_statsLock)
            {
                var qualityStats = new QualityStatistics();

                foreach (var profile in _qualityProfileUsage.Values)
                {
                    qualityStats.QualityProfileUsage[profile.Name] = profile.UsageCount;

                    foreach (var quality in profile.QualitySelections)
                    {
                        qualityStats.QobuzQualityDistribution[quality.Key] =
                            qualityStats.QobuzQualityDistribution.GetValueOrDefault(quality.Key, 0) + quality.Value;
                    }
                }

                qualityStats.MostUsedQualityProfile = qualityStats.QualityProfileUsage
                    .OrderByDescending(kvp => kvp.Value)
                    .FirstOrDefault().Key;

                qualityStats.MostSelectedQobuzQuality = qualityStats.QobuzQualityDistribution
                    .OrderByDescending(kvp => kvp.Value)
                    .FirstOrDefault().Key;

                return qualityStats;
            }
        }

        /// <summary>
        /// Gets error analysis with categorization and frequency.
        /// </summary>
        public ErrorAnalysis GetErrorAnalysis()
        {
            lock (_statsLock)
            {
                var analysis = new ErrorAnalysis
                {
                    ErrorsByType = new Dictionary<string, int>(_statistics.ErrorCounts)
                };

                // Calculate errors by operation
                foreach (var opErrors in _errorHistory)
                {
                    analysis.ErrorsByOperation[opErrors.Key] = opErrors.Value.Count;
                }

                // Calculate total errors and overall rate
                analysis.TotalErrors = _statistics.ErrorCounts.Values.Sum();
                var totalOperations = _statistics.TotalSearches + _statistics.TotalDownloads;
                analysis.OverallErrorRate = totalOperations > 0 ? (double)analysis.TotalErrors / totalOperations * 100 : 0;

                // Most common errors
                analysis.MostCommonErrors = _statistics.ErrorCounts
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(10)
                    .Select(kvp => new ErrorFrequency
                    {
                        ErrorType = kvp.Key,
                        Count = kvp.Value,
                        Percentage = analysis.TotalErrors > 0 ? (double)kvp.Value / analysis.TotalErrors * 100 : 0,
                        SampleMessage = GetSampleErrorMessage(kvp.Key)
                    })
                    .ToList();

                return analysis;
            }
        }

        /// <summary>
        /// Resets all statistics and clears collected data.
        /// </summary>
        public void ResetStatistics()
        {
            lock (_statsLock)
            {
                _statistics.TotalSearches = 0;
                _statistics.SuccessfulSearches = 0;
                _statistics.FailedSearches = 0;
                _statistics.TotalDownloads = 0;
                _statistics.SuccessfulDownloads = 0;
                _statistics.FailedDownloads = 0;
                _statistics.TotalBytesDownloaded = 0;
                _statistics.TotalDownloadTime = TimeSpan.Zero;
                _statistics.CurrentConcurrentOperations = 0;
                _statistics.PeakConcurrentOperations = 0;
                _statistics.LastOperationAt = DateTime.MinValue;
                _statistics.ErrorCounts.Clear();
                _statistics.QualityDistribution.Clear();

                _operationHistory.Clear();
                _errorHistory.Clear();
                _qualityProfileUsage.Clear();
                _concurrencyMeasurements.Clear();
                _throughputHistory.Clear();
            }

            _logger.Info("Statistics collector reset - all data cleared");
        }

        /// <summary>
        /// Exports statistics to a structured format for external analysis.
        /// </summary>
        public StatisticsExport ExportStatistics(bool includeRawData = false)
        {
            lock (_statsLock)
            {
                var export = new StatisticsExport
                {
                    ExportedAt = DateTime.UtcNow,
                    CoveredPeriod = DateTime.UtcNow - _startTime,
                    IntegrationStats = GetStatistics(),
                    PerformanceMetrics = GetPerformanceMetrics(),
                    QualityStats = GetQualityStatistics(),
                    ErrorAnalysis = GetErrorAnalysis(),
                    SystemInfo = GetSystemInfo()
                };

                if (includeRawData)
                {
                    export.RawData = new Dictionary<string, object>
                    {
                        ["OperationHistory"] = _operationHistory.ToList(),
                        ["ErrorHistory"] = _errorHistory.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList()),
                        ["QualityProfileUsage"] = _qualityProfileUsage.Values.ToList(),
                        ["ConcurrencyMeasurements"] = _concurrencyMeasurements.ToList(),
                        ["ThroughputHistory"] = _throughputHistory.ToList()
                    };
                }

                return export;
            }
        }

        #region Private Helper Methods

        private void RecordOperation(string operationType, bool success, long bytesTransferred, TimeSpan duration)
        {
            var record = new OperationRecord
            {
                Timestamp = DateTime.UtcNow,
                OperationType = operationType,
                Success = success,
                BytesTransferred = bytesTransferred,
                Duration = duration
            };

            _operationHistory.Add(record);

            // Maintain history size
            if (_operationHistory.Count > MAX_HISTORY_RECORDS)
            {
                _operationHistory.RemoveAt(0);
            }
        }

        private OperationMetrics CalculateOperationMetrics(List<OperationRecord> operations)
        {
            if (!operations.Any())
            {
                return new OperationMetrics();
            }

            var successful = operations.Where(o => o.Success).ToList();
            var totalTime = DateTime.UtcNow - _startTime;

            return new OperationMetrics
            {
                TotalAttempts = operations.Count,
                SuccessfulOperations = successful.Count,
                FailedOperations = operations.Count - successful.Count,
                SuccessRate = (double)successful.Count / operations.Count * 100,
                AverageOperationTime = successful.Any() ?
                    TimeSpan.FromTicks((long)successful.Average(o => o.Duration.Ticks)) : TimeSpan.Zero,
                FastestOperationTime = successful.Any() ?
                    successful.Min(o => o.Duration) : TimeSpan.Zero,
                SlowestOperationTime = successful.Any() ?
                    successful.Max(o => o.Duration) : TimeSpan.Zero,
                OperationsPerMinute = totalTime.TotalMinutes > 0 ?
                    operations.Count / totalTime.TotalMinutes : 0
            };
        }

        private ThroughputMetrics CalculateThroughputMetrics()
        {
            var recentMeasurements = _throughputHistory
                .Where(m => m.Timestamp > DateTime.UtcNow.AddMinutes(-PERFORMANCE_WINDOW_MINUTES))
                .ToList();

            if (!recentMeasurements.Any())
            {
                return new ThroughputMetrics
                {
                    TotalBytesDownloaded = _statistics.TotalBytesDownloaded
                };
            }

            var totalBytes = recentMeasurements.Sum(m => m.BytesTransferred);
            var timeSpan = recentMeasurements.Max(m => m.Timestamp) - recentMeasurements.Min(m => m.Timestamp);
            var averageSpeed = timeSpan.TotalSeconds > 0 ? (totalBytes / 1024.0 / 1024.0) / timeSpan.TotalSeconds : 0;

            return new ThroughputMetrics
            {
                TotalBytesDownloaded = _statistics.TotalBytesDownloaded,
                AverageDownloadSpeedMBps = averageSpeed,
                PeakDownloadSpeedMBps = averageSpeed, // Simplified for this implementation
                TotalDownloadTime = _statistics.TotalDownloadTime,
                BandwidthEfficiency = 85.0 // Placeholder - would need more sophisticated calculation
            };
        }

        private ConcurrencyMetrics CalculateConcurrencyMetrics()
        {
            if (!_concurrencyMeasurements.Any())
            {
                return new ConcurrencyMetrics();
            }

            return new ConcurrencyMetrics
            {
                AverageConcurrentOperations = _concurrencyMeasurements.Average(),
                PeakConcurrentOperations = _statistics.PeakConcurrentOperations,
                ConcurrencyUtilization = _statistics.PeakConcurrentOperations > 0 ?
                    (_concurrencyMeasurements.Average() / _statistics.PeakConcurrentOperations) * 100 : 0,
                TimeAtMaxConcurrency = TimeSpan.Zero // Would need more sophisticated tracking
            };
        }

        private string GetSampleErrorMessage(string errorType)
        {
            foreach (var opErrors in _errorHistory.Values)
            {
                var sample = opErrors.FirstOrDefault(e => e.ErrorType == errorType);
                if (sample != null)
                {
                    return sample.Message;
                }
            }
            return "No sample message available";
        }

        private int ConvertQobuzQualityToInt(string qobuzQuality)
        {
            return qobuzQuality?.ToLower() switch
            {
                "flac-hires" => 27,  // Hi-Res FLAC
                "flac-cd" => 6,      // CD Quality FLAC
                "mp3-320" => 5,      // 320kbps MP3
                _ => 6               // Default to CD quality
            };
        }

        private SystemInfo GetSystemInfo()
        {
            return new SystemInfo
            {
                ProcessorCores = Environment.ProcessorCount,
                AvailableMemoryMB = GC.GetTotalMemory(false) / 1024 / 1024,
                PluginVersion = GetType().Assembly.GetName().Version?.ToString() ?? "Unknown",
                OperatingSystem = RuntimeInformation.OSDescription,
                DotNetVersion = RuntimeInformation.FrameworkDescription
            };
        }

        #endregion

        #region Data Classes

        private class OperationRecord
        {
            public DateTime Timestamp { get; set; }
            public string OperationType { get; set; }
            public bool Success { get; set; }
            public long BytesTransferred { get; set; }
            public TimeSpan Duration { get; set; }
        }

        private class ErrorRecord
        {
            public DateTime Timestamp { get; set; }
            public string ErrorType { get; set; }
            public string Message { get; set; }
            public string StackTrace { get; set; }
            public string OperationType { get; set; }
        }

        private class QualityProfileRecord
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int UsageCount { get; set; }
            public DateTime LastUsed { get; set; }
            public Dictionary<string, int> QualitySelections { get; set; } = new();
        }

        private class ThroughputMeasurement
        {
            public DateTime Timestamp { get; set; }
            public long BytesTransferred { get; set; }
            public string OperationType { get; set; }
        }

        #endregion
    }
}
