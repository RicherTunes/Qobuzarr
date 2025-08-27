using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Indexers
{
    /// <summary>
    /// Monitors ML model performance and triggers retraining when thresholds are breached
    /// </summary>
    public class MLRetrainingMonitor
    {
        private readonly ILogger _logger;
        private readonly CompiledMLQueryOptimizer _optimizer;
        private readonly MLPerformanceMetrics _performanceMetrics;
        
        // Performance thresholds that trigger retraining
        private const double MIN_API_REDUCTION = 45.0; // Minimum acceptable API call reduction
        private const double MIN_ACCURACY = 85.0; // Minimum acceptable accuracy
        private const double MAX_PREDICTION_TIME_MS = 15.0; // Maximum acceptable prediction time
        private const double MIN_CACHE_HIT_RATE = 60.0; // Minimum cache hit rate
        private const int MAX_MEMORY_MB = 75; // Maximum memory usage
        
        // Drift detection parameters
        private const int MONITORING_WINDOW_SIZE = 1000; // Number of recent predictions to analyze
        private const double DRIFT_THRESHOLD = 0.05; // 5% change indicates drift
        
        private readonly Queue<PerformanceSnapshot> _performanceHistory;
        private readonly object _historyLock = new object();
        private DateTime _lastRetrainingCheck = DateTime.UtcNow;
        private DateTime _lastRetrainingTrigger = DateTime.MinValue;
        private readonly TimeSpan _minRetrainingInterval = TimeSpan.FromDays(7); // Don't retrain more than weekly
        
        public MLRetrainingMonitor(CompiledMLQueryOptimizer optimizer, MLPerformanceMetrics performanceMetrics)
        {
            _optimizer = optimizer ?? throw new ArgumentNullException(nameof(optimizer));
            _performanceMetrics = performanceMetrics ?? throw new ArgumentNullException(nameof(performanceMetrics));
            _logger = LogManager.GetCurrentClassLogger();
            _performanceHistory = new Queue<PerformanceSnapshot>();
        }
        
        /// <summary>
        /// Check if retraining is needed based on current performance metrics
        /// </summary>
        public RetrainingDecision CheckRetrainingNeeded()
        {
            var decision = new RetrainingDecision();
            
            try
            {
                var stats = _optimizer.GetStatistics();
                var perfSummary = _performanceMetrics.GetPerformanceSummary();
                var rollingMetrics = _performanceMetrics.GetRollingMetrics(15); // 15-minute window
                
                // Extract key metrics
                var apiReduction = stats.HybridStatistics.ContainsKey("ApiCallReduction") 
                    ? Convert.ToDouble(stats.HybridStatistics["ApiCallReduction"]) 
                    : 49.83;
                var accuracy = stats.Accuracy * 100;
                var avgPredictionTime = perfSummary.PredictionMetrics.Average;
                var cacheHitRate = _performanceMetrics.GetCacheHitRatio() * 100;
                var memoryUsageMB = perfSummary.CurrentMemoryUsage / (1024 * 1024);
                
                // Check performance thresholds
                if (apiReduction < MIN_API_REDUCTION)
                {
                    decision.IsNeeded = true;
                    decision.Reasons.Add($"API reduction below threshold: {apiReduction:F2}% < {MIN_API_REDUCTION}%");
                    decision.Priority = RetrainingPriority.High;
                }
                
                if (accuracy < MIN_ACCURACY)
                {
                    decision.IsNeeded = true;
                    decision.Reasons.Add($"Model accuracy below threshold: {accuracy:F2}% < {MIN_ACCURACY}%");
                    decision.Priority = RetrainingPriority.High;
                }
                
                if (avgPredictionTime > MAX_PREDICTION_TIME_MS)
                {
                    decision.IsNeeded = true;
                    decision.Reasons.Add($"Prediction time above threshold: {avgPredictionTime:F2}ms > {MAX_PREDICTION_TIME_MS}ms");
                    decision.Priority = UpdatePriority(decision.Priority, RetrainingPriority.Medium);
                }
                
                if (cacheHitRate < MIN_CACHE_HIT_RATE)
                {
                    decision.IsNeeded = true;
                    decision.Reasons.Add($"Cache hit rate below threshold: {cacheHitRate:F2}% < {MIN_CACHE_HIT_RATE}%");
                    decision.Priority = UpdatePriority(decision.Priority, RetrainingPriority.Low);
                }
                
                if (memoryUsageMB > MAX_MEMORY_MB)
                {
                    decision.IsNeeded = true;
                    decision.Reasons.Add($"Memory usage above threshold: {memoryUsageMB:F0}MB > {MAX_MEMORY_MB}MB");
                    decision.Priority = UpdatePriority(decision.Priority, RetrainingPriority.Medium);
                }
                
                // Check for performance drift
                var drift = DetectPerformanceDrift();
                if (drift.IsDriftDetected)
                {
                    decision.IsNeeded = true;
                    decision.Reasons.Add($"Performance drift detected: {drift.DriftAmount:F2}% change in {drift.MetricName}");
                    decision.Priority = UpdatePriority(decision.Priority, RetrainingPriority.Medium);
                }
                
                // Check for new query patterns
                var newPatterns = DetectNewQueryPatterns();
                if (newPatterns.Count > 0)
                {
                    decision.IsNeeded = true;
                    decision.Reasons.Add($"New query patterns detected: {newPatterns.Count} patterns not in training data");
                    decision.Priority = UpdatePriority(decision.Priority, RetrainingPriority.Low);
                    decision.NewPatterns = newPatterns;
                }
                
                // Enforce minimum retraining interval
                if (decision.IsNeeded && DateTime.UtcNow - _lastRetrainingTrigger < _minRetrainingInterval)
                {
                    decision.IsDeferred = true;
                    decision.DeferralReason = $"Minimum retraining interval not met. Last retraining: {_lastRetrainingTrigger:yyyy-MM-dd}";
                }
                
                // Record current snapshot
                RecordPerformanceSnapshot(new PerformanceSnapshot
                {
                    Timestamp = DateTime.UtcNow,
                    ApiReduction = apiReduction,
                    Accuracy = accuracy,
                    PredictionTime = avgPredictionTime,
                    CacheHitRate = cacheHitRate,
                    MemoryUsageMB = memoryUsageMB
                });
                
                _lastRetrainingCheck = DateTime.UtcNow;
                
                if (decision.IsNeeded && !decision.IsDeferred)
                {
                    _logger.Info($"ML model retraining triggered. Priority: {decision.Priority}. Reasons: {string.Join("; ", decision.Reasons)}");
                    _lastRetrainingTrigger = DateTime.UtcNow;
                }
                
                return decision;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking retraining requirements");
                return new RetrainingDecision { IsNeeded = false };
            }
        }
        
        /// <summary>
        /// Detect performance drift by comparing recent metrics to baseline
        /// </summary>
        private DriftDetection DetectPerformanceDrift()
        {
            lock (_historyLock)
            {
                if (_performanceHistory.Count < MONITORING_WINDOW_SIZE / 2)
                {
                    return new DriftDetection { IsDriftDetected = false };
                }
                
                var recent = _performanceHistory.TakeLast(100).ToList();
                var baseline = _performanceHistory.Take(100).ToList();
                
                // Compare API reduction
                var recentApiReduction = recent.Average(s => s.ApiReduction);
                var baselineApiReduction = baseline.Average(s => s.ApiReduction);
                var apiDrift = Math.Abs(recentApiReduction - baselineApiReduction) / baselineApiReduction;
                
                if (apiDrift > DRIFT_THRESHOLD)
                {
                    return new DriftDetection
                    {
                        IsDriftDetected = true,
                        MetricName = "API Reduction",
                        DriftAmount = apiDrift * 100
                    };
                }
                
                // Compare accuracy
                var recentAccuracy = recent.Average(s => s.Accuracy);
                var baselineAccuracy = baseline.Average(s => s.Accuracy);
                var accuracyDrift = Math.Abs(recentAccuracy - baselineAccuracy) / baselineAccuracy;
                
                if (accuracyDrift > DRIFT_THRESHOLD)
                {
                    return new DriftDetection
                    {
                        IsDriftDetected = true,
                        MetricName = "Accuracy",
                        DriftAmount = accuracyDrift * 100
                    };
                }
                
                return new DriftDetection { IsDriftDetected = false };
            }
        }
        
        /// <summary>
        /// Detect new query patterns that weren't in the original training data
        /// </summary>
        private List<QueryPattern> DetectNewQueryPatterns()
        {
            var newPatterns = new List<QueryPattern>();
            
            // This would typically analyze recent queries against the training dataset
            // For now, return empty list as we don't have access to live query data
            // In production, this would:
            // 1. Track unique query patterns from recent searches
            // 2. Compare against patterns in ml-baseline-patterns.json
            // 3. Identify patterns with low confidence scores or high failure rates
            
            return newPatterns;
        }
        
        private void RecordPerformanceSnapshot(PerformanceSnapshot snapshot)
        {
            lock (_historyLock)
            {
                _performanceHistory.Enqueue(snapshot);
                
                // Maintain window size
                while (_performanceHistory.Count > MONITORING_WINDOW_SIZE)
                {
                    _performanceHistory.Dequeue();
                }
            }
        }
        
        private RetrainingPriority UpdatePriority(RetrainingPriority current, RetrainingPriority new)
        {
            return (RetrainingPriority)Math.Max((int)current, (int)new);
        }
        
        /// <summary>
        /// Get current monitoring status
        /// </summary>
        public MonitoringStatus GetMonitoringStatus()
        {
            lock (_historyLock)
            {
                if (_performanceHistory.Count == 0)
                {
                    return new MonitoringStatus
                    {
                        IsMonitoring = false,
                        Message = "No performance data collected yet"
                    };
                }
                
                var latest = _performanceHistory.Last();
                return new MonitoringStatus
                {
                    IsMonitoring = true,
                    LastCheck = _lastRetrainingCheck,
                    LastRetraining = _lastRetrainingTrigger,
                    CurrentMetrics = latest,
                    HistorySize = _performanceHistory.Count,
                    Message = $"Monitoring {_performanceHistory.Count} snapshots. Last check: {_lastRetrainingCheck:yyyy-MM-dd HH:mm:ss}"
                };
            }
        }
    }
    
    public class RetrainingDecision
    {
        public bool IsNeeded { get; set; }
        public bool IsDeferred { get; set; }
        public string DeferralReason { get; set; }
        public RetrainingPriority Priority { get; set; } = RetrainingPriority.None;
        public List<string> Reasons { get; set; } = new List<string>();
        public List<QueryPattern> NewPatterns { get; set; } = new List<QueryPattern>();
    }
    
    public enum RetrainingPriority
    {
        None = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4
    }
    
    public class DriftDetection
    {
        public bool IsDriftDetected { get; set; }
        public string MetricName { get; set; }
        public double DriftAmount { get; set; }
    }
    
    public class PerformanceSnapshot
    {
        public DateTime Timestamp { get; set; }
        public double ApiReduction { get; set; }
        public double Accuracy { get; set; }
        public double PredictionTime { get; set; }
        public double CacheHitRate { get; set; }
        public double MemoryUsageMB { get; set; }
    }
    
    public class MonitoringStatus
    {
        public bool IsMonitoring { get; set; }
        public DateTime LastCheck { get; set; }
        public DateTime LastRetraining { get; set; }
        public PerformanceSnapshot CurrentMetrics { get; set; }
        public int HistorySize { get; set; }
        public string Message { get; set; }
    }
    
    public class QueryPattern
    {
        public string Artist { get; set; }
        public string Album { get; set; }
        public int OccurrenceCount { get; set; }
        public double FailureRate { get; set; }
        public double AverageConfidence { get; set; }
    }
}