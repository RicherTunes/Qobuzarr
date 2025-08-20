using System;
using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.Qobuzarr.Abstractions;

namespace Lidarr.Plugin.Qobuzarr.Services.Monitoring
{
    /// <summary>
    /// Week 3 ML/Rate Limiting recommendation analyzer based on production performance data.
    /// Provides evidence-based recommendations for ML optimization and rate limiting needs.
    /// </summary>
    public class MLRecommendationAnalyzer
    {
        private readonly IPerformanceMonitor _performanceMonitor;
        private readonly IQobuzLogger _logger;

        // Evidence-based thresholds for ML recommendation
        private const double HIGH_API_CALL_RATE_THRESHOLD = 100; // calls per hour
        private const double API_INEFFICIENCY_THRESHOLD = 0.3; // 30% redundant calls
        private const double QUERY_COMPLEXITY_BENEFIT_THRESHOLD = 0.15; // 15% improvement potential
        private const int MIN_DATA_POINTS_FOR_ANALYSIS = 50;

        public MLRecommendationAnalyzer(IPerformanceMonitor performanceMonitor, IQobuzLogger logger)
        {
            _performanceMonitor = performanceMonitor ?? throw new ArgumentNullException(nameof(performanceMonitor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Analyzes production performance data to determine ML optimization needs.
        /// </summary>
        public MLRecommendationReport AnalyzeMLNeed()
        {
            _logger.Info("Starting Week 3 ML recommendation analysis based on production data");

            var report = new MLRecommendationReport
            {
                GeneratedAt = DateTime.UtcNow,
                AnalysisPeriod = "Post-Consolidation Performance Data",
                DataSources = new List<string>
                {
                    "QualityDetection.SingleTrack metrics",
                    "QualityDetection.Album metrics", 
                    "API.GetStreamInfo call patterns",
                    "Cache hit/miss ratios",
                    "Quality mapping performance"
                }
            };

            // Analyze API call efficiency
            var apiEfficiency = AnalyzeApiEfficiency();
            report.ApiEfficiencyAnalysis = apiEfficiency;

            // Analyze query complexity patterns
            var queryComplexity = AnalyzeQueryComplexity();
            report.QueryComplexityAnalysis = queryComplexity;

            // Analyze caching effectiveness
            var cachingAnalysis = AnalyzeCachingEffectiveness();
            report.CachingAnalysis = cachingAnalysis;

            // Generate final recommendation
            report.Recommendation = GenerateMLRecommendation(apiEfficiency, queryComplexity, cachingAnalysis);
            report.ConfidenceLevel = CalculateRecommendationConfidence(report);

            _logger.Info("ML recommendation analysis complete: {0}", report.Recommendation.Decision);
            return report;
        }

        private ApiEfficiencyAnalysis AnalyzeApiEfficiency()
        {
            var apiStats = _performanceMonitor.GetStatistics("API.GetStreamInfo");
            var qualityDetectionStats = _performanceMonitor.GetStatistics("QualityDetection.SingleTrack");

            var analysis = new ApiEfficiencyAnalysis
            {
                TotalApiCalls = apiStats.TotalOperations,
                AverageCallsPerHour = CalculateCallsPerHour(apiStats),
                RedundantCallsRate = EstimateRedundantCalls(apiStats, qualityDetectionStats),
                CurrentLatency = apiStats.AverageExecutionTimeMs
            };

            // Determine if API efficiency is a concern
            analysis.RequiresOptimization = 
                analysis.AverageCallsPerHour > HIGH_API_CALL_RATE_THRESHOLD ||
                analysis.RedundantCallsRate > API_INEFFICIENCY_THRESHOLD ||
                analysis.CurrentLatency > 2000; // >2s average

            return analysis;
        }

        private QueryComplexityAnalysis AnalyzeQueryComplexity()
        {
            var qualityMappingStats = _performanceMonitor.GetStatistics("QualityMapping.LidarrProfile");
            var albumDetectionStats = _performanceMonitor.GetStatistics("QualityDetection.Album");

            var analysis = new QueryComplexityAnalysis
            {
                TotalQueries = qualityMappingStats.TotalOperations + albumDetectionStats.TotalOperations,
                AverageProcessingTime = (qualityMappingStats.AverageExecutionTimeMs + albumDetectionStats.AverageExecutionTimeMs) / 2,
                ComplexQueryRatio = EstimateComplexQueryRatio(albumDetectionStats),
                OptimizationPotential = CalculateOptimizationPotential(qualityMappingStats, albumDetectionStats)
            };

            analysis.WouldBenefitFromML = 
                analysis.OptimizationPotential > QUERY_COMPLEXITY_BENEFIT_THRESHOLD &&
                analysis.TotalQueries > MIN_DATA_POINTS_FOR_ANALYSIS;

            return analysis;
        }

        private CachingEffectivenessAnalysis AnalyzeCachingEffectiveness()
        {
            var cacheReadStats = _performanceMonitor.GetStatistics("Cache.AlbumQuality.Read");
            var qualityDetectionStats = _performanceMonitor.GetStatistics("QualityDetection.Album");

            var analysis = new CachingEffectivenessAnalysis
            {
                TotalCacheOperations = cacheReadStats.TotalOperations,
                EstimatedHitRate = EstimateCacheHitRate(cacheReadStats, qualityDetectionStats),
                CacheLatency = cacheReadStats.AverageExecutionTimeMs
            };

            analysis.IsEffective = 
                analysis.EstimatedHitRate > 0.6 && // >60% hit rate
                analysis.CacheLatency < 50; // <50ms cache access

            return analysis;
        }

        private MLRecommendation GenerateMLRecommendation(
            ApiEfficiencyAnalysis apiEfficiency,
            QueryComplexityAnalysis queryComplexity, 
            CachingEffectivenessAnalysis caching)
        {
            var recommendation = new MLRecommendation
            {
                Decision = DetermineMLDecision(apiEfficiency, queryComplexity, caching),
                PrimaryReasons = new List<string>(),
                ActionItems = new List<string>(),
                ExpectedBenefits = new List<string>(),
                ImplementationEffort = "Low",
                Timeline = "Not recommended at this time"
            };

            // Build evidence-based reasoning
            if (apiEfficiency.RequiresOptimization)
            {
                recommendation.PrimaryReasons.Add($"High API call rate: {apiEfficiency.AverageCallsPerHour:F1} calls/hour (>{HIGH_API_CALL_RATE_THRESHOLD} threshold)");
                recommendation.PrimaryReasons.Add($"Redundant calls: {apiEfficiency.RedundantCallsRate:P1} (>{API_INEFFICIENCY_THRESHOLD:P0} threshold)");
            }

            if (queryComplexity.WouldBenefitFromML)
            {
                recommendation.PrimaryReasons.Add($"Query optimization potential: {queryComplexity.OptimizationPotential:P1} improvement possible");
                recommendation.ActionItems.Add("Implement ML-based query complexity prediction");
                recommendation.ExpectedBenefits.Add($"Reduce processing time by ~{queryComplexity.OptimizationPotential:P1}");
                recommendation.ImplementationEffort = "Medium";
                recommendation.Timeline = "2-3 weeks";
            }

            if (!caching.IsEffective)
            {
                recommendation.PrimaryReasons.Add($"Caching inefficiency: {caching.EstimatedHitRate:P1} hit rate, {caching.CacheLatency:F1}ms latency");
                recommendation.ActionItems.Add("Improve caching strategy before considering ML");
            }

            // Override decision based on consolidated analysis
            if (recommendation.Decision == MLDecision.NotRecommended && caching.IsEffective && !apiEfficiency.RequiresOptimization)
            {
                recommendation.PrimaryReasons.Add("Current performance is acceptable with existing optimizations");
                recommendation.PrimaryReasons.Add("Service consolidation has already achieved significant improvements");
                recommendation.ActionItems.Add("Continue monitoring with current performance infrastructure");
                recommendation.ExpectedBenefits.Add("Maintain current performance without additional complexity");
            }

            return recommendation;
        }

        private MLDecision DetermineMLDecision(
            ApiEfficiencyAnalysis apiEfficiency,
            QueryComplexityAnalysis queryComplexity,
            CachingEffectivenessAnalysis caching)
        {
            // Evidence-based decision tree
            if (apiEfficiency.RequiresOptimization && queryComplexity.WouldBenefitFromML)
            {
                return MLDecision.HighlyRecommended;
            }

            if (queryComplexity.WouldBenefitFromML && caching.IsEffective)
            {
                return MLDecision.Recommended;
            }

            if (apiEfficiency.RequiresOptimization || !caching.IsEffective)
            {
                return MLDecision.ConditionallyRecommended;
            }

            return MLDecision.NotRecommended;
        }

        private double CalculateRecommendationConfidence(MLRecommendationReport report)
        {
            var confidence = 0.0;

            // Base confidence on data quantity
            var totalDataPoints = report.ApiEfficiencyAnalysis.TotalApiCalls + 
                                 report.QueryComplexityAnalysis.TotalQueries;
            
            if (totalDataPoints > MIN_DATA_POINTS_FOR_ANALYSIS * 2)
                confidence += 0.4; // High data confidence
            else if (totalDataPoints > MIN_DATA_POINTS_FOR_ANALYSIS)
                confidence += 0.2; // Medium data confidence

            // Confidence from clear performance patterns
            if (report.ApiEfficiencyAnalysis.RequiresOptimization)
                confidence += 0.3;

            if (report.QueryComplexityAnalysis.WouldBenefitFromML)
                confidence += 0.3;

            if (report.CachingAnalysis.IsEffective)
                confidence += 0.2; // Stable baseline

            return Math.Min(confidence, 1.0);
        }

        // Helper methods for metrics calculation
        private double CalculateCallsPerHour(PerformanceStatistics stats)
        {
            // Estimate based on total operations (simplified for demonstration)
            return stats.TotalOperations * 3.6; // Assuming 10-second sampling window
        }

        private double EstimateRedundantCalls(PerformanceStatistics apiStats, PerformanceStatistics detectionStats)
        {
            if (detectionStats.TotalOperations == 0) return 0.0;
            
            // Estimate redundancy based on API calls vs quality detections
            var expectedCalls = detectionStats.TotalOperations * 2.5; // Average calls per detection
            var actualCalls = apiStats.TotalOperations;
            
            return actualCalls > expectedCalls ? (actualCalls - expectedCalls) / actualCalls : 0.0;
        }

        private double EstimateComplexQueryRatio(PerformanceStatistics albumStats)
        {
            // Estimate based on processing time distribution (P95 vs average)
            if (albumStats.AverageExecutionTimeMs == 0) return 0.0;
            
            var complexityRatio = albumStats.P95ExecutionTimeMs / albumStats.AverageExecutionTimeMs;
            return Math.Min(complexityRatio / 3.0, 1.0); // Normalize to 0-1 range
        }

        private double CalculateOptimizationPotential(PerformanceStatistics mappingStats, PerformanceStatistics albumStats)
        {
            // Estimate optimization potential based on variance and complexity
            if (mappingStats.TotalOperations == 0 && albumStats.TotalOperations == 0) return 0.0;
            
            var avgVariance = (mappingStats.P95ExecutionTimeMs - mappingStats.AverageExecutionTimeMs) +
                             (albumStats.P95ExecutionTimeMs - albumStats.AverageExecutionTimeMs);
            
            return Math.Min(avgVariance / 5000.0, 0.5); // Cap at 50% potential improvement
        }

        private double EstimateCacheHitRate(PerformanceStatistics cacheStats, PerformanceStatistics detectionStats)
        {
            if (detectionStats.TotalOperations == 0) return 0.0;
            
            // Estimate hit rate based on cache operations vs detection operations
            var hitRate = (double)cacheStats.TotalOperations / (cacheStats.TotalOperations + detectionStats.TotalOperations);
            return Math.Min(hitRate, 1.0);
        }
    }

    // Data structures for ML recommendation analysis
    public class MLRecommendationReport
    {
        public DateTime GeneratedAt { get; set; }
        public string AnalysisPeriod { get; set; }
        public List<string> DataSources { get; set; } = new();
        public ApiEfficiencyAnalysis ApiEfficiencyAnalysis { get; set; }
        public QueryComplexityAnalysis QueryComplexityAnalysis { get; set; }
        public CachingEffectivenessAnalysis CachingAnalysis { get; set; }
        public MLRecommendation Recommendation { get; set; }
        public double ConfidenceLevel { get; set; }
    }

    public class ApiEfficiencyAnalysis
    {
        public long TotalApiCalls { get; set; }
        public double AverageCallsPerHour { get; set; }
        public double RedundantCallsRate { get; set; }
        public double CurrentLatency { get; set; }
        public bool RequiresOptimization { get; set; }
    }

    public class QueryComplexityAnalysis
    {
        public long TotalQueries { get; set; }
        public double AverageProcessingTime { get; set; }
        public double ComplexQueryRatio { get; set; }
        public double OptimizationPotential { get; set; }
        public bool WouldBenefitFromML { get; set; }
    }

    public class CachingEffectivenessAnalysis
    {
        public long TotalCacheOperations { get; set; }
        public double EstimatedHitRate { get; set; }
        public double CacheLatency { get; set; }
        public bool IsEffective { get; set; }
    }

    public class MLRecommendation
    {
        public MLDecision Decision { get; set; }
        public List<string> PrimaryReasons { get; set; } = new();
        public List<string> ActionItems { get; set; } = new();
        public List<string> ExpectedBenefits { get; set; } = new();
        public string ImplementationEffort { get; set; }
        public string Timeline { get; set; }
    }

    public enum MLDecision
    {
        NotRecommended,
        ConditionallyRecommended,
        Recommended,
        HighlyRecommended
    }
}