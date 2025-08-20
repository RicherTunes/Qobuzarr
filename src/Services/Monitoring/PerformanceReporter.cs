using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lidarr.Plugin.Qobuzarr.Abstractions;

namespace Lidarr.Plugin.Qobuzarr.Services.Monitoring
{
    /// <summary>
    /// Performance reporting service for generating production performance insights.
    /// Provides formatted reports for monitoring production path performance.
    /// </summary>
    public class PerformanceReporter
    {
        private readonly IPerformanceMonitor _performanceMonitor;
        private readonly IQobuzLogger _logger;

        public PerformanceReporter(IPerformanceMonitor performanceMonitor, IQobuzLogger logger)
        {
            _performanceMonitor = performanceMonitor ?? throw new ArgumentNullException(nameof(performanceMonitor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Generates a comprehensive performance report for all monitored operations.
        /// </summary>
        public PerformanceReport GenerateReport()
        {
            var allStats = _performanceMonitor.GetStatistics();
            var operationNames = GetTrackedOperations();
            
            var report = new PerformanceReport
            {
                GeneratedAt = DateTime.UtcNow,
                OverallStatistics = allStats,
                OperationStatistics = new List<PerformanceStatistics>(),
                Recommendations = new List<string>(),
                PerformanceAlerts = new List<PerformanceAlert>()
            };

            // Collect per-operation statistics
            foreach (var operationName in operationNames)
            {
                var opStats = _performanceMonitor.GetStatistics(operationName);
                if (opStats.TotalOperations > 0)
                {
                    report.OperationStatistics.Add(opStats);
                    
                    // Generate alerts for problematic operations
                    var alerts = AnalyzeOperationPerformance(opStats);
                    report.PerformanceAlerts.AddRange(alerts);
                }
            }

            // Generate recommendations based on performance data
            report.Recommendations.AddRange(GenerateRecommendations(report.OperationStatistics));

            return report;
        }

        /// <summary>
        /// Logs a formatted performance summary to the application logs.
        /// </summary>
        public void LogPerformanceSummary()
        {
            var report = GenerateReport();
            
            _logger.Info("=== Qobuzarr Performance Summary ===");
            _logger.Info("Generated at: {0}", report.GeneratedAt);
            
            if (report.OverallStatistics.TotalOperations > 0)
            {
                _logger.Info("Overall: {0} operations, {1:F2}ms avg, {2:F2}ms P95", 
                    report.OverallStatistics.TotalOperations,
                    report.OverallStatistics.AverageExecutionTimeMs,
                    report.OverallStatistics.P95ExecutionTimeMs);
            }

            // Log top slowest operations
            var slowestOps = report.OperationStatistics
                .OrderByDescending(s => s.AverageExecutionTimeMs)
                .Take(5)
                .ToList();

            if (slowestOps.Any())
            {
                _logger.Info("Top slowest operations:");
                foreach (var op in slowestOps)
                {
                    _logger.Info("  {0}: {1:F2}ms avg ({2} ops)", 
                        op.OperationName, op.AverageExecutionTimeMs, op.TotalOperations);
                }
            }

            // Log performance alerts
            if (report.PerformanceAlerts.Any())
            {
                _logger.Warn("Performance alerts ({0}):", report.PerformanceAlerts.Count);
                foreach (var alert in report.PerformanceAlerts.Take(5))
                {
                    _logger.Warn("  {0}: {1}", alert.Severity, alert.Message);
                }
            }

            // Log recommendations
            if (report.Recommendations.Any())
            {
                _logger.Info("Performance recommendations:");
                foreach (var recommendation in report.Recommendations.Take(3))
                {
                    _logger.Info("  - {0}", recommendation);
                }
            }
        }

        /// <summary>
        /// Formats a detailed performance report as a readable string.
        /// </summary>
        public string FormatDetailedReport(PerformanceReport report = null)
        {
            report ??= GenerateReport();
            var sb = new StringBuilder();

            sb.AppendLine("=== Qobuzarr Performance Report ===");
            sb.AppendLine($"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine();

            // Overall statistics
            if (report.OverallStatistics.TotalOperations > 0)
            {
                sb.AppendLine("Overall Performance:");
                sb.AppendLine($"  Total Operations: {report.OverallStatistics.TotalOperations:N0}");
                sb.AppendLine($"  Average Time: {report.OverallStatistics.AverageExecutionTimeMs:F2}ms");
                sb.AppendLine($"  P95 Time: {report.OverallStatistics.P95ExecutionTimeMs:F2}ms");
                sb.AppendLine($"  Error Rate: {GetErrorRate(report.OverallStatistics):F1}%");
                sb.AppendLine();
            }

            // Operation-level statistics
            var groupedOps = report.OperationStatistics
                .GroupBy(s => GetOperationCategory(s.OperationName))
                .OrderBy(g => g.Key)
                .ToList();

            foreach (var group in groupedOps)
            {
                sb.AppendLine($"{group.Key} Operations:");
                var operations = group.OrderByDescending(s => s.AverageExecutionTimeMs).ToList();
                
                foreach (var op in operations)
                {
                    sb.AppendLine($"  {op.OperationName}:");
                    sb.AppendLine($"    Operations: {op.TotalOperations:N0}");
                    sb.AppendLine($"    Avg Time: {op.AverageExecutionTimeMs:F2}ms");
                    sb.AppendLine($"    P95 Time: {op.P95ExecutionTimeMs:F2}ms");
                    sb.AppendLine($"    Error Rate: {GetErrorRate(op):F1}%");
                }
                sb.AppendLine();
            }

            // Performance alerts
            if (report.PerformanceAlerts.Any())
            {
                sb.AppendLine("Performance Alerts:");
                foreach (var alert in report.PerformanceAlerts)
                {
                    sb.AppendLine($"  [{alert.Severity}] {alert.Message}");
                    if (!string.IsNullOrEmpty(alert.Recommendation))
                    {
                        sb.AppendLine($"    → {alert.Recommendation}");
                    }
                }
                sb.AppendLine();
            }

            // Recommendations
            if (report.Recommendations.Any())
            {
                sb.AppendLine("Recommendations:");
                foreach (var rec in report.Recommendations)
                {
                    sb.AppendLine($"  - {rec}");
                }
            }

            return sb.ToString();
        }

        private List<string> GetTrackedOperations()
        {
            // These are the key operations we're monitoring
            return new List<string>
            {
                "QualityDetection.SingleTrack",
                "QualityDetection.Album", 
                "QualityDetection.SampleTrack",
                "QualityMapping.LidarrProfile",
                "QualitySelection.BestQuality",
                "API.GetStreamInfo",
                "Cache.AlbumQuality.Read",
                "Cache.AlbumQuality.Write"
            };
        }

        private List<PerformanceAlert> AnalyzeOperationPerformance(PerformanceStatistics stats)
        {
            var alerts = new List<PerformanceAlert>();

            // High average execution time
            if (stats.AverageExecutionTimeMs > 2000)
            {
                alerts.Add(new PerformanceAlert
                {
                    Severity = "HIGH",
                    Operation = stats.OperationName,
                    Message = $"High average execution time: {stats.AverageExecutionTimeMs:F2}ms",
                    Recommendation = "Consider optimizing this operation or adding more caching"
                });
            }

            // High error rate
            var errorRate = GetErrorRate(stats);
            if (errorRate > 10)
            {
                alerts.Add(new PerformanceAlert
                {
                    Severity = "HIGH",
                    Operation = stats.OperationName,
                    Message = $"High error rate: {errorRate:F1}%",
                    Recommendation = "Investigate error causes and improve error handling"
                });
            }

            // High P95 latency
            if (stats.P95ExecutionTimeMs > 5000)
            {
                alerts.Add(new PerformanceAlert
                {
                    Severity = "MEDIUM",
                    Operation = stats.OperationName,
                    Message = $"High P95 latency: {stats.P95ExecutionTimeMs:F2}ms",
                    Recommendation = "Some operations are very slow, investigate outliers"
                });
            }

            return alerts;
        }

        private List<string> GenerateRecommendations(List<PerformanceStatistics> operationStats)
        {
            var recommendations = new List<string>();

            // API call optimization
            var apiStats = operationStats.FirstOrDefault(s => s.OperationName == "API.GetStreamInfo");
            if (apiStats != null && apiStats.AverageExecutionTimeMs > 1000)
            {
                recommendations.Add("API calls are slow - consider implementing connection pooling or request batching");
            }

            // Cache hit rate analysis
            var cacheReadStats = operationStats.FirstOrDefault(s => s.OperationName == "Cache.AlbumQuality.Read");
            if (cacheReadStats != null && cacheReadStats.TotalOperations > 0)
            {
                recommendations.Add("Monitor cache hit rates to ensure effective caching strategy");
            }

            // Quality detection optimization
            var albumQualityStats = operationStats.FirstOrDefault(s => s.OperationName == "QualityDetection.Album");
            if (albumQualityStats != null && albumQualityStats.AverageExecutionTimeMs > 3000)
            {
                recommendations.Add("Album quality detection is slow - consider reducing sample size or parallel processing");
            }

            return recommendations;
        }

        private double GetErrorRate(PerformanceStatistics stats)
        {
            if (stats.TotalOperations == 0) return 0;
            return (double)stats.ErrorCount / stats.TotalOperations * 100;
        }

        private string GetOperationCategory(string operationName)
        {
            if (operationName.StartsWith("QualityDetection")) return "Quality Detection";
            if (operationName.StartsWith("QualityMapping")) return "Quality Mapping";
            if (operationName.StartsWith("QualitySelection")) return "Quality Selection";
            if (operationName.StartsWith("API")) return "API Operations";
            if (operationName.StartsWith("Cache")) return "Cache Operations";
            return "Other";
        }
    }

    /// <summary>
    /// Comprehensive performance report containing all monitoring data.
    /// </summary>
    public class PerformanceReport
    {
        public DateTime GeneratedAt { get; set; }
        public PerformanceStatistics OverallStatistics { get; set; }
        public List<PerformanceStatistics> OperationStatistics { get; set; } = new();
        public List<PerformanceAlert> PerformanceAlerts { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }

    /// <summary>
    /// Performance alert for monitoring issues.
    /// </summary>
    public class PerformanceAlert
    {
        public string Severity { get; set; } // HIGH, MEDIUM, LOW
        public string Operation { get; set; }
        public string Message { get; set; }
        public string Recommendation { get; set; }
    }
}