using System;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Comprehensive performance summary for ML operations
    /// </summary>
    public class MLPerformanceSummary
    {
        public DateTime GeneratedAt { get; set; }
        public int WindowMinutes { get; set; }

        // Timing metrics
        public TimingStatistics ModelLoadMetrics { get; set; }
        public TimingStatistics PredictionMetrics { get; set; }
        public TimingStatistics TrainingMetrics { get; set; }

        // Accuracy metrics
        public double CurrentAccuracy { get; set; }
        public int TotalPredictions { get; set; }
        public int CorrectPredictions { get; set; }
        public AccuracyTrend AccuracyTrend { get; set; }

        // Cache metrics
        public double CacheHitRatio { get; set; }
        public long CacheHits { get; set; }
        public long CacheMisses { get; set; }
        public int RecentCacheActivity { get; set; }

        // API optimization metrics
        public double ApiCallReductionPercentage { get; set; }
        public long TotalApiCallsSaved { get; set; }
        public OptimizationTrend OptimizationTrend { get; set; }

        // Memory metrics
        public long CurrentMemoryUsage { get; set; }
        public long ProcessMemoryUsage { get; set; }
        public long MemoryGrowth { get; set; }
        public long RecentMemoryPeak { get; set; }

        /// <summary>
        /// Get a formatted performance report
        /// </summary>
        public string GetFormattedReport()
        {
            return $@"ML Performance Report ({GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC)
========================================

ACCURACY METRICS:
- Current Accuracy: {CurrentAccuracy:P2} ({CorrectPredictions}/{TotalPredictions} predictions)
- Trend: {AccuracyTrend.Direction} ({AccuracyTrend.ChangePercentage:+0.0;-0.0;±0.0}%)
- Average Confidence: {AccuracyTrend.Confidence:P2}

PERFORMANCE METRICS:
- Model Load: {ModelLoadMetrics.Average:F2}ms avg (min: {ModelLoadMetrics.Min:F1}ms, max: {ModelLoadMetrics.Max:F1}ms)
- Predictions: {PredictionMetrics.Average:F2}ms avg (P95: {PredictionMetrics.P95:F1}ms, count: {PredictionMetrics.Count})
- Training: {TrainingMetrics.Average:F2}ms avg (count: {TrainingMetrics.Count})

CACHE PERFORMANCE:
- Hit Ratio: {CacheHitRatio:P1} ({CacheHits} hits, {CacheMisses} misses)
- Recent Activity: {RecentCacheActivity} hits in last {WindowMinutes} minutes

API OPTIMIZATION:
- Call Reduction: {ApiCallReductionPercentage:F1}% ({TotalApiCallsSaved:N0} calls saved)
- Recent Trend: {OptimizationTrend.TrendDirection} ({OptimizationTrend.AverageReduction:F1}% avg)
- Recent Savings: {OptimizationTrend.RecentCallsSaved:N0} calls

MEMORY USAGE:
- Current GC Memory: {CurrentMemoryUsage / 1024.0 / 1024.0:F1} MB
- Process Memory: {ProcessMemoryUsage / 1024.0 / 1024.0:F1} MB
- Growth Since Start: {MemoryGrowth / 1024.0 / 1024.0:+0.0;-0.0;±0.0} MB
- Recent Peak: {RecentMemoryPeak / 1024.0 / 1024.0:F1} MB";
        }

        /// <summary>
        /// Check if performance meets target thresholds
        /// </summary>
        public PerformanceHealth GetHealthStatus()
        {
            var issues = new System.Collections.Generic.List<string>();

            // Check critical thresholds
            if (CurrentAccuracy < 0.85 && TotalPredictions > 100)
                issues.Add($"Low accuracy: {CurrentAccuracy:P1} (target: ≥85%)");

            if (PredictionMetrics.Average > 50 && PredictionMetrics.Count > 50)
                issues.Add($"Slow predictions: {PredictionMetrics.Average:F1}ms avg (target: <50ms)");

            if (CacheHitRatio < 0.90 && (CacheHits + CacheMisses) > 100)
                issues.Add($"Low cache hit rate: {CacheHitRatio:P1} (target: ≥90%)");

            if (ApiCallReductionPercentage < 45.0 && TotalApiCallsSaved > 100)
                issues.Add($"Low API optimization: {ApiCallReductionPercentage:F1}% (target: ≥45%)");

            if (MemoryGrowth > 100 * 1024 * 1024) // 100MB growth
                issues.Add($"High memory growth: {MemoryGrowth / 1024.0 / 1024.0:F1}MB (concern: >100MB)");

            return new PerformanceHealth
            {
                Status = issues.Count == 0 ? "Healthy" :
                        issues.Count <= 2 ? "Warning" : "Critical",
                Issues = issues,
                Score = Math.Max(0, 100 - (issues.Count * 20)) // 20 points per issue
            };
        }
    }

    /// <summary>
    /// Rolling performance metrics for trend analysis
    /// </summary>
    public class RollingPerformanceMetrics
    {
        public int WindowMinutes { get; set; }
        public double AveragePredictionTime { get; set; }
        public double MedianPredictionTime { get; set; }
        public double P95PredictionTime { get; set; }
        public double RecentAccuracy { get; set; }
        public double PredictionThroughput { get; set; } // predictions per hour
        public double MemoryEfficiency { get; set; } // 0.0 to 1.0
    }

    /// <summary>
    /// Timing statistics for operations
    /// </summary>
    public class TimingStatistics
    {
        public string Operation { get; set; }
        public int Count { get; set; }
        public double Average { get; set; }
        public double Median { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double P95 { get; set; }
        public double P99 { get; set; }

        public override string ToString()
        {
            return $"{Operation}: {Average:F2}ms avg (n={Count}, p95={P95:F1}ms)";
        }
    }

    /// <summary>
    /// Model accuracy trend analysis
    /// </summary>
    public class AccuracyTrend
    {
        public string Direction { get; set; } // "improving", "declining", "stable"
        public double ChangePercentage { get; set; }
        public double Confidence { get; set; }
    }

    /// <summary>
    /// API optimization trend analysis
    /// </summary>
    public class OptimizationTrend
    {
        public double AverageReduction { get; set; }
        public string TrendDirection { get; set; } // "improving", "declining", "stable"
        public int RecentCallsSaved { get; set; }
    }

    /// <summary>
    /// Overall performance health assessment
    /// </summary>
    public class PerformanceHealth
    {
        public string Status { get; set; } // "Healthy", "Warning", "Critical"
        public System.Collections.Generic.List<string> Issues { get; set; }
        public int Score { get; set; } // 0-100 health score

        public bool IsHealthy => Status == "Healthy";
        public bool HasWarnings => Status == "Warning";
        public bool IsCritical => Status == "Critical";
    }

    /// <summary>
    /// Performance benchmarks and targets
    /// </summary>
    public static class PerformanceBenchmarks
    {
        // Target thresholds based on the baseline performance mentioned in the codebase
        public const double TARGET_ACCURACY = 0.873; // 87.3% from baseline model
        public const double TARGET_API_REDUCTION = 49.83; // 49.83% from baseline
        public const double TARGET_CACHE_HIT_RATE = 0.947; // 94.7% from baseline
        public const double TARGET_PREDICTION_TIME_MS = 10.0; // Sub-10ms inference time

        // Warning thresholds (slightly below targets)
        public const double WARNING_ACCURACY = 0.85;
        public const double WARNING_API_REDUCTION = 45.0;
        public const double WARNING_CACHE_HIT_RATE = 0.90;
        public const double WARNING_PREDICTION_TIME_MS = 25.0;

        // Critical thresholds (significant degradation)
        public const double CRITICAL_ACCURACY = 0.80;
        public const double CRITICAL_API_REDUCTION = 35.0;
        public const double CRITICAL_CACHE_HIT_RATE = 0.80;
        public const double CRITICAL_PREDICTION_TIME_MS = 50.0;

        /// <summary>
        /// Check if a metric meets the target benchmark
        /// </summary>
        public static bool MeetsTarget(string metric, double value)
        {
            return metric.ToLowerInvariant() switch
            {
                "accuracy" => value >= TARGET_ACCURACY,
                "api_reduction" => value >= TARGET_API_REDUCTION,
                "cache_hit_rate" => value >= TARGET_CACHE_HIT_RATE,
                "prediction_time" => value <= TARGET_PREDICTION_TIME_MS,
                _ => true // Unknown metrics pass by default
            };
        }

        /// <summary>
        /// Get performance level for a metric
        /// </summary>
        public static string GetPerformanceLevel(string metric, double value)
        {
            return metric.ToLowerInvariant() switch
            {
                "accuracy" => value >= TARGET_ACCURACY ? "Target" :
                             value >= WARNING_ACCURACY ? "Warning" :
                             value >= CRITICAL_ACCURACY ? "Critical" : "Poor",

                "api_reduction" => value >= TARGET_API_REDUCTION ? "Target" :
                                  value >= WARNING_API_REDUCTION ? "Warning" :
                                  value >= CRITICAL_API_REDUCTION ? "Critical" : "Poor",

                "cache_hit_rate" => value >= TARGET_CACHE_HIT_RATE ? "Target" :
                                   value >= WARNING_CACHE_HIT_RATE ? "Warning" :
                                   value >= CRITICAL_CACHE_HIT_RATE ? "Critical" : "Poor",

                "prediction_time" => value <= TARGET_PREDICTION_TIME_MS ? "Target" :
                                    value <= WARNING_PREDICTION_TIME_MS ? "Warning" :
                                    value <= CRITICAL_PREDICTION_TIME_MS ? "Critical" : "Poor",

                _ => "Unknown"
            };
        }
    }
}
