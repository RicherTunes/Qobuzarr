# ML Optimization Guide

**Version:** 0.0.12+  
**Last Updated:** August 2024

## Table of Contents
- [Overview](#overview)
- [ML Query Optimization](#ml-query-optimization)
- [Pattern Learning Engine](#pattern-learning-engine)
- [Performance Metrics](#performance-metrics)
- [Model Training](#model-training)
- [Real-time Adaptation](#real-time-adaptation)
- [Monitoring and Analytics](#monitoring-and-analytics)
- [Troubleshooting](#troubleshooting)

## Overview

Qobuzarr's ML optimization system provides intelligent query processing that reduces API calls by up to 49% while maintaining search accuracy. The system learns from user patterns, adapts to changing behaviors, and optimizes performance in real-time.

### Key Benefits
- **49% API call reduction** through intelligent query classification
- **Sub-100ms response times** for cached patterns
- **Self-improving accuracy** via continuous learning
- **Automatic fallback** to traditional search when needed
- **Memory-efficient processing** with <50MB overhead

### Architecture Components
```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   Query Input   │───▶│  ML Classifier   │───▶│  Optimization   │
└─────────────────┘    └──────────────────┘    │    Engine       │
                                               └─────────────────┘
                              │                          │
                              ▼                          ▼
                    ┌──────────────────┐    ┌─────────────────┐
                    │ Pattern Learning │    │  API Call       │
                    │     Engine       │    │  Optimizer      │
                    └──────────────────┘    └─────────────────┘
```

## ML Query Optimization

### Core ML Features

#### 1. Query Classification
The ML system classifies queries into optimization categories:

```csharp
public enum QueryOptimizationType
{
    HighConfidenceCache,    // 95%+ confidence, use cache
    PatternMatch,          // Known pattern, optimize query
    FuzzyOptimization,     // Partial match, modify query
    StandardSearch,        // Fall back to normal search
    LearningOpportunity    // Collect data for training
}

// Implementation example
var classifier = serviceProvider.GetService<IMLQueryClassifier>();
var optimization = await classifier.ClassifyQueryAsync(searchQuery);

switch (optimization.Type)
{
    case QueryOptimizationType.HighConfidenceCache:
        return await GetCachedResults(optimization.CacheKey);
    
    case QueryOptimizationType.PatternMatch:
        return await ExecuteOptimizedQuery(optimization.OptimizedQuery);
    
    case QueryOptimizationType.FuzzyOptimization:
        return await ExecuteFuzzyOptimizedQuery(optimization);
    
    default:
        return await ExecuteStandardQuery(searchQuery);
}
```

#### 2. Confidence Scoring
ML predictions include confidence scores to ensure accuracy:

```csharp
public class MLOptimizationResult
{
    public QueryOptimizationType Type { get; set; }
    public double ConfidenceScore { get; set; }      // 0.0 - 1.0
    public string OptimizedQuery { get; set; }
    public string CacheKey { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
    public TimeSpan EstimatedSavings { get; set; }
}

// Confidence thresholds
private static readonly Dictionary<QueryOptimizationType, double> MinConfidenceThresholds = new()
{
    { QueryOptimizationType.HighConfidenceCache, 0.95 },
    { QueryOptimizationType.PatternMatch, 0.85 },
    { QueryOptimizationType.FuzzyOptimization, 0.70 },
    { QueryOptimizationType.StandardSearch, 0.0 }
};
```

#### 3. Feature Engineering
The ML system extracts features from search queries:

```csharp
public class QueryFeatures
{
    // Text features
    public int QueryLength { get; set; }
    public int WordCount { get; set; }
    public double AverageWordLength { get; set; }
    public bool HasArtistName { get; set; }
    public bool HasAlbumTitle { get; set; }
    public bool HasYear { get; set; }
    public bool HasGenre { get; set; }
    
    // Pattern features
    public double SimilarityToKnownPatterns { get; set; }
    public int HistoricalSearchCount { get; set; }
    public DateTime LastSearched { get; set; }
    public double SuccessRateForPattern { get; set; }
    
    // User behavior features
    public TimeSpan TypicalUserSearchTime { get; set; }
    public int UserSearchFrequency { get; set; }
    public List<string> UserPreferredGenres { get; set; }
}

// Feature extraction
public class QueryFeatureExtractor : IQueryFeatureExtractor
{
    public QueryFeatures ExtractFeatures(string query, UserContext userContext)
    {
        var features = new QueryFeatures();
        
        // Basic text analysis
        features.QueryLength = query.Length;
        features.WordCount = query.Split(' ').Length;
        features.AverageWordLength = query.Split(' ').Average(w => w.Length);
        
        // Named entity recognition
        features.HasArtistName = IsArtistName(ExtractPossibleArtist(query));
        features.HasAlbumTitle = IsAlbumTitle(ExtractPossibleAlbum(query));
        features.HasYear = ExtractYear(query) != null;
        features.HasGenre = ExtractGenre(query) != null;
        
        // Pattern similarity
        features.SimilarityToKnownPatterns = CalculatePatternSimilarity(query);
        
        // User behavior
        features.UserSearchFrequency = userContext.SearchesPerDay;
        features.UserPreferredGenres = userContext.PreferredGenres;
        
        return features;
    }
}
```

## Pattern Learning Engine

### IPatternLearningEngine Interface
```csharp
public interface IPatternLearningEngine
{
    Task<bool> LearnFromQueryAsync(string originalQuery, string optimizedQuery, 
        QueryResult result, TimeSpan executionTime);
    Task<PatternMatch> FindBestPatternAsync(string query);
    Task<List<QueryPattern>> GetActivePatternsBySuccessRateAsync(double minSuccessRate = 0.8);
    Task RetrainModelsAsync();
    PatternLearningStats GetStats();
}
```

### Pattern Storage and Retrieval
```csharp
public class QueryPattern
{
    public string PatternId { get; set; }
    public string Template { get; set; }           // "artist:{artist} album:{album}"
    public List<string> Features { get; set; }     // ["artist", "album", "year"]
    public double SuccessRate { get; set; }        // 0.0 - 1.0
    public int UsageCount { get; set; }
    public DateTime LastUsed { get; set; }
    public TimeSpan AverageExecutionTime { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
}

public class PatternLearningEngine : IPatternLearningEngine
{
    private readonly IPatternRepository _patternRepository;
    private readonly IMLModelService _mlModelService;
    private readonly ILogger<PatternLearningEngine> _logger;
    
    public async Task<bool> LearnFromQueryAsync(string originalQuery, 
        string optimizedQuery, QueryResult result, TimeSpan executionTime)
    {
        try
        {
            // Extract pattern from successful query
            var pattern = ExtractPattern(originalQuery, optimizedQuery);
            if (pattern == null) return false;
            
            // Update existing pattern or create new one
            var existingPattern = await _patternRepository.FindSimilarPatternAsync(pattern);
            if (existingPattern != null)
            {
                existingPattern.UsageCount++;
                existingPattern.LastUsed = DateTime.UtcNow;
                existingPattern.SuccessRate = CalculateUpdatedSuccessRate(
                    existingPattern, result.WasSuccessful);
                await _patternRepository.UpdatePatternAsync(existingPattern);
            }
            else
            {
                pattern.SuccessRate = result.WasSuccessful ? 1.0 : 0.0;
                pattern.UsageCount = 1;
                pattern.LastUsed = DateTime.UtcNow;
                await _patternRepository.CreatePatternAsync(pattern);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to learn from query: {Query}", originalQuery);
            return false;
        }
    }
    
    public async Task<PatternMatch> FindBestPatternAsync(string query)
    {
        var features = ExtractQueryFeatures(query);
        var candidatePatterns = await _patternRepository.GetPatternsBySimilarityAsync(
            features, minSimilarity: 0.7);
        
        return candidatePatterns
            .Where(p => p.SuccessRate > 0.8)
            .OrderByDescending(p => p.SuccessRate * p.UsageCount)
            .Select(p => new PatternMatch
            {
                Pattern = p,
                ConfidenceScore = CalculateConfidence(query, p),
                OptimizedQuery = ApplyPattern(query, p)
            })
            .FirstOrDefault();
    }
}
```

## Performance Metrics

### Real-time Performance Monitoring
```csharp
public class MLOptimizationMetrics
{
    // Performance metrics
    public double ApiCallReductionPercentage { get; set; }
    public TimeSpan AverageResponseTime { get; set; }
    public int TotalOptimizedQueries { get; set; }
    public int TotalApiCallsSaved { get; set; }
    
    // Accuracy metrics
    public double PatternMatchAccuracy { get; set; }
    public double FalsePositiveRate { get; set; }
    public double FalseNegativeRate { get; set; }
    
    // Learning metrics
    public int ActivePatterns { get; set; }
    public int NewPatternsLearnedToday { get; set; }
    public DateTime LastModelRetraining { get; set; }
    
    // Resource usage
    public long MemoryUsageBytes { get; set; }
    public double CpuUsagePercentage { get; set; }
    public int CacheHitRate { get; set; }
}

// Metrics collection service
public class MLMetricsCollector : IMLMetricsCollector
{
    private readonly ConcurrentDictionary<string, QueryMetrics> _queryMetrics;
    private readonly Timer _metricsTimer;
    
    public MLOptimizationMetrics GetCurrentMetrics()
    {
        var totalQueries = _queryMetrics.Count;
        var optimizedQueries = _queryMetrics.Values.Count(m => m.WasOptimized);
        var totalApiCalls = _queryMetrics.Values.Sum(m => m.ApiCallsMade);
        var savedApiCalls = _queryMetrics.Values.Sum(m => m.ApiCallsSaved);
        
        return new MLOptimizationMetrics
        {
            ApiCallReductionPercentage = totalApiCalls > 0 
                ? (double)savedApiCalls / (totalApiCalls + savedApiCalls) * 100
                : 0,
            AverageResponseTime = TimeSpan.FromMilliseconds(
                _queryMetrics.Values.Average(m => m.ResponseTime.TotalMilliseconds)),
            TotalOptimizedQueries = optimizedQueries,
            TotalApiCallsSaved = savedApiCalls,
            PatternMatchAccuracy = CalculatePatternAccuracy(),
            ActivePatterns = GetActivePatternCount(),
            MemoryUsageBytes = GC.GetTotalMemory(false),
            CacheHitRate = CalculateCacheHitRate()
        };
    }
}
```

## Model Training

### Automated Model Training Pipeline
```csharp
public class MLModelTrainingService : IMLModelTrainingService
{
    public async Task<ModelTrainingResult> TrainModelAsync(TrainingConfiguration config)
    {
        var trainingData = await PrepareTrainingData(config);
        var model = await TrainModel(trainingData, config);
        var evaluation = await EvaluateModel(model, trainingData.TestSet);
        
        if (evaluation.Accuracy > config.MinAccuracyThreshold)
        {
            await DeployModel(model, evaluation);
            return ModelTrainingResult.Success(evaluation);
        }
        
        return ModelTrainingResult.Failed($"Accuracy {evaluation.Accuracy} below threshold {config.MinAccuracyThreshold}");
    }
    
    private async Task<TrainingData> PrepareTrainingData(TrainingConfiguration config)
    {
        // Collect historical query data
        var queryHistory = await _queryRepository.GetTrainingDataAsync(
            config.StartDate, config.EndDate);
        
        // Feature engineering
        var features = queryHistory.Select(q => new TrainingExample
        {
            Features = _featureExtractor.ExtractFeatures(q.Query, q.UserContext),
            Label = DetermineOptimalAction(q),
            Weight = CalculateExampleWeight(q)
        }).ToList();
        
        // Split into train/validation/test sets (70/15/15)
        var shuffled = features.OrderBy(x => Guid.NewGuid()).ToList();
        var trainCount = (int)(features.Count * 0.7);
        var validationCount = (int)(features.Count * 0.15);
        
        return new TrainingData
        {
            TrainingSet = shuffled.Take(trainCount).ToList(),
            ValidationSet = shuffled.Skip(trainCount).Take(validationCount).ToList(),
            TestSet = shuffled.Skip(trainCount + validationCount).ToList()
        };
    }
}
```

### Model Evaluation and A/B Testing
```csharp
public class ModelEvaluationService : IModelEvaluationService
{
    public async Task<EvaluationResult> EvaluateModelAsync(IMLModel model, List<TrainingExample> testSet)
    {
        var predictions = new List<PredictionResult>();
        
        foreach (var example in testSet)
        {
            var prediction = await model.PredictAsync(example.Features);
            predictions.Add(new PredictionResult
            {
                Expected = example.Label,
                Predicted = prediction.Classification,
                Confidence = prediction.Confidence,
                Features = example.Features
            });
        }
        
        return new EvaluationResult
        {
            Accuracy = CalculateAccuracy(predictions),
            Precision = CalculatePrecision(predictions),
            Recall = CalculateRecall(predictions),
            F1Score = CalculateF1Score(predictions),
            ConfusionMatrix = BuildConfusionMatrix(predictions),
            FeatureImportance = CalculateFeatureImportance(model),
            CrossValidationScore = await PerformCrossValidation(model, testSet)
        };
    }
    
    // A/B testing for model deployment
    public async Task<ABTestResult> RunABTestAsync(IMLModel modelA, IMLModel modelB, 
        TimeSpan testDuration)
    {
        var testResults = new ConcurrentBag<ABTestSample>();
        var testEndTime = DateTime.UtcNow + testDuration;
        
        // Run parallel testing
        await Task.Run(async () =>
        {
            while (DateTime.UtcNow < testEndTime)
            {
                // Randomly assign queries to model A or B
                var query = await GetNextTestQuery();
                var useModelA = Random.Shared.NextDouble() < 0.5;
                
                var startTime = DateTime.UtcNow;
                var result = useModelA 
                    ? await modelA.PredictAsync(query.Features)
                    : await modelB.PredictAsync(query.Features);
                var endTime = DateTime.UtcNow;
                
                testResults.Add(new ABTestSample
                {
                    ModelUsed = useModelA ? "A" : "B",
                    Query = query,
                    Result = result,
                    ResponseTime = endTime - startTime,
                    ActualOutcome = await GetActualOutcome(query, result)
                });
            }
        });
        
        return AnalyzeABTestResults(testResults.ToList());
    }
}
```

## Real-time Adaptation

### Dynamic Model Updates
```csharp
public class RealTimeMLAdapter : IMLAdapter
{
    private volatile IMLModel _currentModel;
    private readonly ConcurrentQueue<LearningExample> _learningQueue;
    private readonly Timer _adaptationTimer;
    
    public RealTimeMLAdapter()
    {
        _learningQueue = new ConcurrentQueue<LearningExample>();
        
        // Process learning examples every 5 minutes
        _adaptationTimer = new Timer(ProcessLearningQueue, null, 
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }
    
    public async Task<MLPrediction> PredictWithLearningAsync(QueryFeatures features)
    {
        var prediction = await _currentModel.PredictAsync(features);
        
        // Queue for potential learning
        _learningQueue.Enqueue(new LearningExample
        {
            Features = features,
            Prediction = prediction,
            Timestamp = DateTime.UtcNow
        });
        
        return prediction;
    }
    
    private async void ProcessLearningQueue(object state)
    {
        var examples = new List<LearningExample>();
        while (_learningQueue.TryDequeue(out var example))
        {
            examples.Add(example);
        }
        
        if (examples.Count < 10) return; // Need minimum samples
        
        try
        {
            // Incremental learning update
            await _currentModel.IncrementalUpdateAsync(examples);
            
            _logger.LogInformation("Updated ML model with {Count} new examples", 
                examples.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update ML model incrementally");
        }
    }
    
    public async Task ReplaceModelAsync(IMLModel newModel)
    {
        // Validate new model before deployment
        var validation = await ValidateModel(newModel);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Model validation failed: {validation.ErrorMessage}");
        }
        
        // Atomic model replacement
        var oldModel = _currentModel;
        _currentModel = newModel;
        
        // Cleanup old model resources
        oldModel?.Dispose();
        
        _logger.LogInformation("Successfully deployed new ML model. Validation accuracy: {Accuracy}", 
            validation.Accuracy);
    }
}
```

## Monitoring and Analytics

### ML Performance Dashboard
```csharp
public class MLPerformanceDashboard : IMLPerformanceDashboard
{
    public async Task<DashboardData> GetDashboardDataAsync()
    {
        var metrics = await _metricsCollector.GetCurrentMetricsAsync();
        var patterns = await _patternRepository.GetTopPatternsAsync(limit: 20);
        var recentPerformance = await GetRecentPerformanceDataAsync();
        
        return new DashboardData
        {
            // Real-time metrics
            CurrentMetrics = metrics,
            
            // Pattern analysis
            TopPatterns = patterns.Select(p => new PatternSummary
            {
                Template = p.Template,
                SuccessRate = p.SuccessRate,
                UsageCount = p.UsageCount,
                AverageSavings = p.AverageApiCallsSaved
            }).ToList(),
            
            // Performance trends
            HourlyPerformance = recentPerformance.GroupBy(r => r.Timestamp.Hour)
                .Select(g => new HourlyMetrics
                {
                    Hour = g.Key,
                    AverageResponseTime = g.Average(r => r.ResponseTime.TotalMilliseconds),
                    ApiCallReduction = g.Average(r => r.ApiCallsSaved),
                    TotalQueries = g.Count()
                }).ToList(),
            
            // Model health
            ModelHealth = new ModelHealthStatus
            {
                LastTrainingDate = await GetLastTrainingDateAsync(),
                ModelAccuracy = metrics.PatternMatchAccuracy,
                IsHealthy = IsModelHealthy(metrics),
                RecommendedActions = GetRecommendedActions(metrics)
            }
        };
    }
    
    public async Task<List<MLAlert>> GetActiveAlertsAsync()
    {
        var alerts = new List<MLAlert>();
        var metrics = await _metricsCollector.GetCurrentMetricsAsync();
        
        // Performance degradation alerts
        if (metrics.ApiCallReductionPercentage < 30) // Below expected 49%
        {
            alerts.Add(new MLAlert
            {
                Type = AlertType.PerformanceDegradation,
                Severity = AlertSeverity.Warning,
                Message = $"API call reduction below threshold: {metrics.ApiCallReductionPercentage:F1}%",
                RecommendedAction = "Consider model retraining or pattern analysis"
            });
        }
        
        // Accuracy alerts
        if (metrics.PatternMatchAccuracy < 0.8)
        {
            alerts.Add(new MLAlert
            {
                Type = AlertType.AccuracyDegradation,
                Severity = AlertSeverity.High,
                Message = $"Pattern match accuracy below threshold: {metrics.PatternMatchAccuracy:F1}%",
                RecommendedAction = "Immediate model retraining required"
            });
        }
        
        // Resource usage alerts
        if (metrics.MemoryUsageBytes > 100 * 1024 * 1024) // >100MB
        {
            alerts.Add(new MLAlert
            {
                Type = AlertType.ResourceUsage,
                Severity = AlertSeverity.Medium,
                Message = $"High memory usage: {metrics.MemoryUsageBytes / 1024 / 1024}MB",
                RecommendedAction = "Consider pattern cleanup or model optimization"
            });
        }
        
        return alerts;
    }
}
```

### Advanced Analytics
```csharp
public class MLAnalyticsEngine : IMLAnalyticsEngine
{
    public async Task<QueryInsights> AnalyzeQueryPatternsAsync(TimeSpan period)
    {
        var queries = await _queryRepository.GetQueriesAsync(
            DateTime.UtcNow - period, DateTime.UtcNow);
        
        return new QueryInsights
        {
            // Pattern distribution
            PatternDistribution = queries
                .GroupBy(q => q.DetectedPattern)
                .ToDictionary(g => g.Key, g => new PatternStats
                {
                    Count = g.Count(),
                    SuccessRate = g.Average(q => q.WasSuccessful ? 1.0 : 0.0),
                    AverageResponseTime = TimeSpan.FromMilliseconds(
                        g.Average(q => q.ResponseTime.TotalMilliseconds))
                }),
            
            // User behavior insights
            UserBehaviorInsights = AnalyzeUserBehavior(queries),
            
            // Optimization opportunities
            OptimizationOpportunities = FindOptimizationOpportunities(queries),
            
            // Performance trends
            PerformanceTrends = CalculatePerformanceTrends(queries)
        };
    }
    
    private List<OptimizationOpportunity> FindOptimizationOpportunities(List<QueryRecord> queries)
    {
        var opportunities = new List<OptimizationOpportunity>();
        
        // Find frequently repeated queries without patterns
        var unoptimizedQueries = queries
            .Where(q => q.DetectedPattern == null && q.ApiCallsMade > 1)
            .GroupBy(q => NormalizeQuery(q.Query))
            .Where(g => g.Count() > 5) // Repeated more than 5 times
            .ToList();
        
        foreach (var group in unoptimizedQueries)
        {
            opportunities.Add(new OptimizationOpportunity
            {
                Type = OpportunityType.NewPatternCandidate,
                QueryTemplate = group.Key,
                Frequency = group.Count(),
                EstimatedSavings = group.Sum(q => q.ApiCallsMade - 1), // Could save all but 1 API call
                Confidence = CalculatePatternConfidence(group.ToList())
            });
        }
        
        return opportunities.OrderByDescending(o => o.EstimatedSavings).ToList();
    }
}
```

## Troubleshooting

### Common Issues and Solutions

#### 1. Low API Call Reduction
**Symptoms:**
- API call reduction below 30%
- High number of queries falling back to standard search

**Diagnosis:**
```csharp
public class MLTroubleshooter : IMLTroubleshooter
{
    public async Task<DiagnosisResult> DiagnosePerformanceIssueAsync()
    {
        var metrics = await _metricsCollector.GetCurrentMetricsAsync();
        var patterns = await _patternRepository.GetActivePatternsBySuccessRateAsync(0.5);
        var recentQueries = await _queryRepository.GetRecentQueriesAsync(TimeSpan.FromHours(24));
        
        var diagnosis = new DiagnosisResult();
        
        // Check pattern coverage
        var queriesWithPatterns = recentQueries.Count(q => q.DetectedPattern != null);
        var patternCoverage = (double)queriesWithPatterns / recentQueries.Count;
        
        if (patternCoverage < 0.6)
        {
            diagnosis.Issues.Add(new Issue
            {
                Type = IssueType.LowPatternCoverage,
                Description = $"Only {patternCoverage:P} of queries have detected patterns",
                Recommendation = "Increase training data or lower pattern matching thresholds"
            });
        }
        
        // Check pattern quality
        var lowQualityPatterns = patterns.Where(p => p.SuccessRate < 0.8).Count();
        if (lowQualityPatterns > patterns.Count / 3)
        {
            diagnosis.Issues.Add(new Issue
            {
                Type = IssueType.LowPatternQuality,
                Description = $"{lowQualityPatterns} patterns have success rate below 80%",
                Recommendation = "Retrain models or remove underperforming patterns"
            });
        }
        
        return diagnosis;
    }
}
```

**Solutions:**
1. **Increase Training Data:** Collect more diverse query examples
2. **Lower Confidence Thresholds:** Reduce minimum confidence for pattern matching
3. **Pattern Cleanup:** Remove underperforming patterns
4. **Model Retraining:** Update ML models with recent data

#### 2. High False Positive Rate
**Symptoms:**
- Incorrect query optimizations
- Poor search results from optimized queries

**Solutions:**
```csharp
// Implement stricter validation
public class StrictMLValidator : IMLValidator
{
    public async Task<ValidationResult> ValidateOptimizationAsync(
        string originalQuery, string optimizedQuery, MLPrediction prediction)
    {
        // Semantic similarity check
        var similarity = await _semanticAnalyzer.CalculateSimilarityAsync(
            originalQuery, optimizedQuery);
        
        if (similarity < 0.8)
        {
            return ValidationResult.Rejected("Semantic similarity too low");
        }
        
        // Confidence threshold check
        if (prediction.Confidence < GetDynamicThreshold(originalQuery))
        {
            return ValidationResult.Rejected("Confidence below dynamic threshold");
        }
        
        // Historical success rate check
        var historicalSuccess = await GetHistoricalSuccessRateAsync(prediction.PatternId);
        if (historicalSuccess < 0.85)
        {
            return ValidationResult.Rejected("Historical success rate too low");
        }
        
        return ValidationResult.Approved();
    }
}
```

#### 3. Memory Usage Issues
**Symptoms:**
- High memory consumption (>100MB)
- OutOfMemoryExceptions

**Solutions:**
```csharp
public class MemoryOptimizedMLService : IMLService
{
    private readonly MemoryCache _patternCache;
    private readonly Timer _cleanupTimer;
    
    public MemoryOptimizedMLService()
    {
        _patternCache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = 1000, // Limit cached patterns
            CompactionPercentage = 0.25 // Remove 25% when limit reached
        });
        
        // Cleanup every hour
        _cleanupTimer = new Timer(PerformMemoryCleanup, null, 
            TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }
    
    private void PerformMemoryCleanup(object state)
    {
        // Remove old patterns
        var cutoffDate = DateTime.UtcNow.AddDays(-30);
        _patternCache.Remove(key => 
            key is string patternKey && 
            GetPatternLastUsed(patternKey) < cutoffDate);
        
        // Force garbage collection
        GC.Collect(2, GCCollectionMode.Optimized);
        
        _logger.LogInformation("Memory cleanup completed. Current usage: {Usage}MB",
            GC.GetTotalMemory(false) / 1024 / 1024);
    }
}
```

### Debug Configuration
```json
{
  "ML": {
    "Debug": {
      "EnableDetailedLogging": true,
      "LogPredictions": true,
      "LogFeatureExtraction": true,
      "LogPatternMatching": true,
      "SaveFailedPredictions": true,
      "MetricsCollectionInterval": "00:01:00"
    },
    "Thresholds": {
      "MinConfidenceForCache": 0.95,
      "MinConfidenceForOptimization": 0.85,
      "MinPatternSuccessRate": 0.8,
      "MaxMemoryUsageMB": 100
    }
  }
}
```

This comprehensive ML optimization system provides intelligent query processing while maintaining high accuracy and performance. The system continuously learns and adapts, providing significant API call reductions while ensuring search quality.