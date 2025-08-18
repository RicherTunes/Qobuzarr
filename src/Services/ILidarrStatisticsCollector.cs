using System;
using System.Collections.Generic;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Service responsible for collecting and reporting comprehensive statistics about Lidarr integration operations.
    /// </summary>
    public interface ILidarrStatisticsCollector
    {
        /// <summary>
        /// Records a search attempt with success/failure information.
        /// </summary>
        /// <param name="success">Whether the search was successful.</param>
        /// <param name="concurrency">Current concurrency level during the search.</param>
        void RecordSearchAttempt(bool success, int concurrency = 0);

        /// <summary>
        /// Records a download attempt with success/failure and throughput information.
        /// </summary>
        /// <param name="success">Whether the download was successful.</param>
        /// <param name="bytesDownloaded">Number of bytes downloaded (0 if failed).</param>
        /// <param name="concurrency">Current concurrency level during the download.</param>
        void RecordDownloadAttempt(bool success, long bytesDownloaded, int concurrency = 0);

        /// <summary>
        /// Records an error with categorization for troubleshooting.
        /// </summary>
        /// <param name="exception">Exception that occurred.</param>
        /// <param name="operationType">Type of operation where error occurred.</param>
        void RecordError(Exception exception, string operationType);

        /// <summary>
        /// Records quality profile usage statistics.
        /// </summary>
        /// <param name="qualityProfileId">Quality profile ID.</param>
        /// <param name="qualityProfileName">Quality profile name.</param>
        /// <param name="selectedQuality">Selected Qobuz quality.</param>
        void RecordQualityProfileUsage(int qualityProfileId, string qualityProfileName, string selectedQuality);

        /// <summary>
        /// Records completion of a batch operation.
        /// </summary>
        /// <param name="duration">Total duration of the batch operation.</param>
        /// <param name="operationType">Type of batch operation.</param>
        void RecordBatchComplete(TimeSpan duration, string operationType = "Download");

        /// <summary>
        /// Records rate limiting events.
        /// </summary>
        /// <param name="endpoint">API endpoint that was rate limited.</param>
        /// <param name="delayMs">Delay imposed by rate limiting.</param>
        void RecordRateLimit(string endpoint, int delayMs);

        /// <summary>
        /// Gets current integration statistics.
        /// </summary>
        /// <returns>Complete integration statistics.</returns>
        IntegrationStatistics GetStatistics();

        /// <summary>
        /// Gets detailed performance metrics.
        /// </summary>
        /// <returns>Performance metrics with breakdowns by operation type.</returns>
        PerformanceMetrics GetPerformanceMetrics();

        /// <summary>
        /// Gets quality distribution statistics.
        /// </summary>
        /// <returns>Statistics about quality profile usage and selection.</returns>
        QualityStatistics GetQualityStatistics();

        /// <summary>
        /// Gets error analysis with categorization and frequency.
        /// </summary>
        /// <returns>Error analysis information.</returns>
        ErrorAnalysis GetErrorAnalysis();

        /// <summary>
        /// Resets all statistics and clears collected data.
        /// </summary>
        void ResetStatistics();

        /// <summary>
        /// Exports statistics to a structured format for external analysis.
        /// </summary>
        /// <param name="includeRawData">Whether to include raw data points.</param>
        /// <returns>Statistics export data.</returns>
        StatisticsExport ExportStatistics(bool includeRawData = false);
    }

    /// <summary>
    /// Contains detailed performance metrics broken down by operation type.
    /// </summary>
    public class PerformanceMetrics
    {
        /// <summary>
        /// Search operation metrics.
        /// </summary>
        public OperationMetrics SearchMetrics { get; set; } = new();

        /// <summary>
        /// Download operation metrics.
        /// </summary>
        public OperationMetrics DownloadMetrics { get; set; } = new();

        /// <summary>
        /// Overall throughput statistics.
        /// </summary>
        public ThroughputMetrics ThroughputMetrics { get; set; } = new();

        /// <summary>
        /// Concurrency utilization statistics.
        /// </summary>
        public ConcurrencyMetrics ConcurrencyMetrics { get; set; } = new();
    }

    /// <summary>
    /// Metrics for a specific operation type.
    /// </summary>
    public class OperationMetrics
    {
        /// <summary>
        /// Total number of operations attempted.
        /// </summary>
        public long TotalAttempts { get; set; }

        /// <summary>
        /// Number of successful operations.
        /// </summary>
        public long SuccessfulOperations { get; set; }

        /// <summary>
        /// Number of failed operations.
        /// </summary>
        public long FailedOperations { get; set; }

        /// <summary>
        /// Success rate as a percentage.
        /// </summary>
        public double SuccessRate { get; set; }

        /// <summary>
        /// Average operation duration.
        /// </summary>
        public TimeSpan AverageOperationTime { get; set; }

        /// <summary>
        /// Fastest operation time recorded.
        /// </summary>
        public TimeSpan FastestOperationTime { get; set; }

        /// <summary>
        /// Slowest operation time recorded.
        /// </summary>
        public TimeSpan SlowestOperationTime { get; set; }

        /// <summary>
        /// Operations per minute rate.
        /// </summary>
        public double OperationsPerMinute { get; set; }
    }

    /// <summary>
    /// Throughput and bandwidth metrics.
    /// </summary>
    public class ThroughputMetrics
    {
        /// <summary>
        /// Total bytes downloaded across all operations.
        /// </summary>
        public long TotalBytesDownloaded { get; set; }

        /// <summary>
        /// Average download speed in MB/s.
        /// </summary>
        public double AverageDownloadSpeedMBps { get; set; }

        /// <summary>
        /// Peak download speed recorded in MB/s.
        /// </summary>
        public double PeakDownloadSpeedMBps { get; set; }

        /// <summary>
        /// Total time spent downloading.
        /// </summary>
        public TimeSpan TotalDownloadTime { get; set; }

        /// <summary>
        /// Bandwidth efficiency percentage.
        /// </summary>
        public double BandwidthEfficiency { get; set; }
    }

    /// <summary>
    /// Concurrency utilization metrics.
    /// </summary>
    public class ConcurrencyMetrics
    {
        /// <summary>
        /// Average concurrent operations maintained.
        /// </summary>
        public double AverageConcurrentOperations { get; set; }

        /// <summary>
        /// Peak concurrent operations observed.
        /// </summary>
        public int PeakConcurrentOperations { get; set; }

        /// <summary>
        /// Concurrency utilization as a percentage of maximum.
        /// </summary>
        public double ConcurrencyUtilization { get; set; }

        /// <summary>
        /// Time spent at maximum concurrency.
        /// </summary>
        public TimeSpan TimeAtMaxConcurrency { get; set; }
    }

    /// <summary>
    /// Statistics about quality profile usage and selection patterns.
    /// </summary>
    public class QualityStatistics
    {
        /// <summary>
        /// Quality profile usage frequency.
        /// </summary>
        public Dictionary<string, int> QualityProfileUsage { get; set; } = new();

        /// <summary>
        /// Selected Qobuz quality distribution.
        /// </summary>
        public Dictionary<string, int> QobuzQualityDistribution { get; set; } = new();

        /// <summary>
        /// Quality upgrade statistics (when higher quality was selected).
        /// </summary>
        public Dictionary<string, int> QualityUpgrades { get; set; } = new();

        /// <summary>
        /// Quality downgrade statistics (when lower quality had to be selected).
        /// </summary>
        public Dictionary<string, int> QualityDowngrades { get; set; } = new();

        /// <summary>
        /// Most frequently used quality profile.
        /// </summary>
        public string MostUsedQualityProfile { get; set; }

        /// <summary>
        /// Most frequently selected Qobuz quality.
        /// </summary>
        public string MostSelectedQobuzQuality { get; set; }
    }

    /// <summary>
    /// Comprehensive error analysis with categorization and troubleshooting insights.
    /// </summary>
    public class ErrorAnalysis
    {
        /// <summary>
        /// Error frequency by exception type.
        /// </summary>
        public Dictionary<string, int> ErrorsByType { get; set; } = new();

        /// <summary>
        /// Error frequency by operation type.
        /// </summary>
        public Dictionary<string, int> ErrorsByOperation { get; set; } = new();

        /// <summary>
        /// Most common error types with descriptions.
        /// </summary>
        public List<ErrorFrequency> MostCommonErrors { get; set; } = new();

        /// <summary>
        /// Error rate trends over time.
        /// </summary>
        public List<ErrorRateTrend> ErrorTrends { get; set; } = new();

        /// <summary>
        /// Total error count across all operations.
        /// </summary>
        public long TotalErrors { get; set; }

        /// <summary>
        /// Overall error rate as a percentage.
        /// </summary>
        public double OverallErrorRate { get; set; }
    }

    /// <summary>
    /// Represents error frequency data for analysis.
    /// </summary>
    public class ErrorFrequency
    {
        /// <summary>
        /// Error type name.
        /// </summary>
        public string ErrorType { get; set; }

        /// <summary>
        /// Number of occurrences.
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// Percentage of total errors.
        /// </summary>
        public double Percentage { get; set; }

        /// <summary>
        /// Sample error message.
        /// </summary>
        public string SampleMessage { get; set; }
    }

    /// <summary>
    /// Represents error rate trends over time.
    /// </summary>
    public class ErrorRateTrend
    {
        /// <summary>
        /// Time period for this trend data.
        /// </summary>
        public DateTime Period { get; set; }

        /// <summary>
        /// Error rate during this period.
        /// </summary>
        public double ErrorRate { get; set; }

        /// <summary>
        /// Total operations during this period.
        /// </summary>
        public int TotalOperations { get; set; }
    }

    /// <summary>
    /// Comprehensive statistics export for external analysis.
    /// </summary>
    public class StatisticsExport
    {
        /// <summary>
        /// Export timestamp.
        /// </summary>
        public DateTime ExportedAt { get; set; }

        /// <summary>
        /// Time period covered by these statistics.
        /// </summary>
        public TimeSpan CoveredPeriod { get; set; }

        /// <summary>
        /// Basic integration statistics.
        /// </summary>
        public IntegrationStatistics IntegrationStats { get; set; }

        /// <summary>
        /// Detailed performance metrics.
        /// </summary>
        public PerformanceMetrics PerformanceMetrics { get; set; }

        /// <summary>
        /// Quality usage statistics.
        /// </summary>
        public QualityStatistics QualityStats { get; set; }

        /// <summary>
        /// Error analysis data.
        /// </summary>
        public ErrorAnalysis ErrorAnalysis { get; set; }

        /// <summary>
        /// Raw data points (only included if requested).
        /// </summary>
        public Dictionary<string, object> RawData { get; set; }

        /// <summary>
        /// System information at time of export.
        /// </summary>
        public SystemInfo SystemInfo { get; set; }
    }

    /// <summary>
    /// System information for statistics context.
    /// </summary>
    public class SystemInfo
    {
        /// <summary>
        /// Number of processor cores.
        /// </summary>
        public int ProcessorCores { get; set; }

        /// <summary>
        /// Available system memory in MB.
        /// </summary>
        public long AvailableMemoryMB { get; set; }

        /// <summary>
        /// Plugin version information.
        /// </summary>
        public string PluginVersion { get; set; }

        /// <summary>
        /// Operating system information.
        /// </summary>
        public string OperatingSystem { get; set; }

        /// <summary>
        /// .NET runtime version.
        /// </summary>
        public string DotNetVersion { get; set; }
    }
}