using System;
using System.Collections.Generic;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Compatibility DTO for callers that display plugin-side queue capacity.
    /// Runtime queue ownership lives in Lidarr and Common's host bridge.
    /// </summary>
    public class QueueStatus
    {
        public int ActiveDownloads { get; set; }
        public int ActiveSearches { get; set; }
        public int MaxConcurrentDownloads { get; set; }
        public int MaxConcurrentSearches { get; set; }
        public int AvailableDownloadSlots { get; set; }
        public int AvailableSearchSlots { get; set; }
        public bool IsDownloadQueueFull { get; set; }
        public bool IsSearchQueueFull { get; set; }
    }

    /// <summary>
    /// Compatibility DTO for legacy CLI queue-statistics displays.
    /// </summary>
    public class QueueStatistics
    {
        public long TotalDownloadSlotAcquisitions { get; set; }
        public long TotalSearchSlotAcquisitions { get; set; }
        public TimeSpan AverageDownloadWaitTime { get; set; }
        public TimeSpan AverageSearchWaitTime { get; set; }
        public int PeakConcurrentDownloads { get; set; }
        public int PeakConcurrentSearches { get; set; }
        public TimeSpan TotalDownloadSlotHoldTime { get; set; }
        public TimeSpan TotalSearchSlotHoldTime { get; set; }
        public int DownloadQueueSaturations { get; set; }
        public int SearchQueueSaturations { get; set; }
    }

    /// <summary>
    /// Tracks progress for a generic batch operation with time estimation.
    /// </summary>
    public interface IProgressTracker : IDisposable
    {
        int TotalItems { get; }
        int CompletedItems { get; }
        string CurrentItem { get; }
        string OperationType { get; }
        TimeSpan Elapsed { get; }
        TimeSpan EstimatedRemaining { get; }
        double PercentComplete { get; }
        void ReportProgress(string currentItem, string phase = null);
        void CompleteItem(string itemDescription = null);
        void CompleteItems(int count);
    }

    /// <summary>
    /// Tracks progress for download operations with throughput and bandwidth calculations.
    /// </summary>
    public interface IDownloadProgressTracker : IProgressTracker
    {
        long TotalBytesDownloaded { get; }
        double CurrentSpeedMBps { get; }
        double AverageSpeedMBps { get; }
        int SuccessCount { get; }
        int FailureCount { get; }
        void ReportDownloadProgress(string currentItem, long bytesDownloaded, bool isSuccess);
        void AddBytesDownloaded(long additionalBytes);
    }

    public class PerformanceMetrics
    {
        public OperationMetrics SearchMetrics { get; set; } = new();
        public OperationMetrics DownloadMetrics { get; set; } = new();
        public ThroughputMetrics ThroughputMetrics { get; set; } = new();
        public ConcurrencyMetrics ConcurrencyMetrics { get; set; } = new();
    }

    public class OperationMetrics
    {
        public long TotalAttempts { get; set; }
        public long SuccessfulOperations { get; set; }
        public long FailedOperations { get; set; }
        public double SuccessRate { get; set; }
        public TimeSpan AverageOperationTime { get; set; }
        public TimeSpan FastestOperationTime { get; set; }
        public TimeSpan SlowestOperationTime { get; set; }
        public double OperationsPerMinute { get; set; }
    }

    public class ThroughputMetrics
    {
        public long TotalBytesDownloaded { get; set; }
        public double AverageDownloadSpeedMBps { get; set; }
        public double PeakDownloadSpeedMBps { get; set; }
        public TimeSpan TotalDownloadTime { get; set; }
        public double BandwidthEfficiency { get; set; }
    }

    public class ConcurrencyMetrics
    {
        public double AverageConcurrentOperations { get; set; }
        public int PeakConcurrentOperations { get; set; }
        public double ConcurrencyUtilization { get; set; }
        public TimeSpan TimeAtMaxConcurrency { get; set; }
    }

    public class QualityStatistics
    {
        public Dictionary<string, int> QualityProfileUsage { get; set; } = new();
        public Dictionary<string, int> QobuzQualityDistribution { get; set; } = new();
        public Dictionary<string, int> QualityUpgrades { get; set; } = new();
        public Dictionary<string, int> QualityDowngrades { get; set; } = new();
        public string MostUsedQualityProfile { get; set; }
        public string MostSelectedQobuzQuality { get; set; }
    }

    public class ErrorAnalysis
    {
        public Dictionary<string, int> ErrorsByType { get; set; } = new();
        public Dictionary<string, int> ErrorsByOperation { get; set; } = new();
        public List<ErrorFrequency> MostCommonErrors { get; set; } = new();
        public List<ErrorRateTrend> ErrorTrends { get; set; } = new();
        public long TotalErrors { get; set; }
        public double OverallErrorRate { get; set; }
    }

    public class ErrorFrequency
    {
        public string ErrorType { get; set; }
        public int Count { get; set; }
        public double Percentage { get; set; }
        public string SampleMessage { get; set; }
    }

    public class ErrorRateTrend
    {
        public DateTime Period { get; set; }
        public double ErrorRate { get; set; }
        public int TotalOperations { get; set; }
    }

    public class StatisticsExport
    {
        public DateTime ExportedAt { get; set; }
        public TimeSpan CoveredPeriod { get; set; }
        public IntegrationStatistics IntegrationStats { get; set; }
        public PerformanceMetrics PerformanceMetrics { get; set; }
        public QualityStatistics QualityStats { get; set; }
        public ErrorAnalysis ErrorAnalysis { get; set; }
        public Dictionary<string, object> RawData { get; set; }
        public SystemInfo SystemInfo { get; set; }
    }

    public class SystemInfo
    {
        public int ProcessorCores { get; set; }
        public long AvailableMemoryMB { get; set; }
        public string PluginVersion { get; set; }
        public string OperatingSystem { get; set; }
        public string DotNetVersion { get; set; }
    }
}
