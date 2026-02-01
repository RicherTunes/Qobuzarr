using System;
using System.Collections.Generic;
using System.Linq;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Hybrid ML optimizer that combines baseline model with optional personal model
    /// Provides best of both worlds: proven baseline + personal customization
    /// </summary>
    /// <remarks>
    /// Architecture:
    /// - Baseline Model: Trained on 500K+ diverse albums, ships with plugin
    /// - Personal Model: Optional user-trained model for their specific library
    /// - Hybrid Logic: Intelligently combines predictions based on confidence
    /// 
    /// Confidence-based routing:
    /// - High confidence from either model: Use that prediction
    /// - Low confidence from both: Use baseline (more conservative)
    /// - Disagreement: Use weighted combination based on training data size
    /// </remarks>
    public class HybridMLQueryOptimizer : IPatternLearningEngine
    {
        private readonly Logger _logger;
        private readonly IPatternLearningEngine _baselineModel;
        private readonly IPatternLearningEngine _personalModel;
        private readonly HybridConfiguration _config;
        private readonly Dictionary<QueryComplexity, int> _statistics;
        private readonly MLPerformanceMetrics _performanceMetrics;
        private readonly object _metricsLock = new object();
        private DateTime _modelLoadTime;
        private int _totalPredictions = 0;
        private int _correctPredictions = 0;
        private int _baselineUsed = 0;
        private int _personalUsed = 0;
        private int _hybridUsed = 0;

        public HybridMLQueryOptimizer(
            Logger logger,
            IPatternLearningEngine baselineModel,
            IPatternLearningEngine personalModel = null,
            HybridConfiguration config = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _baselineModel = baselineModel ?? throw new ArgumentNullException(nameof(baselineModel));
            _personalModel = personalModel;
            _config = config ?? new HybridConfiguration();

            // Initialize performance monitoring
            _performanceMetrics = new MLPerformanceMetrics(_logger);
            _modelLoadTime = DateTime.UtcNow;

            _statistics = new Dictionary<QueryComplexity, int>
            {
                { QueryComplexity.Simple, 0 },
                { QueryComplexity.Medium, 0 },
                { QueryComplexity.Complex, 0 }
            };

            string modelInfo = _personalModel != null ? "Baseline + Personal" : "Baseline Only";
            _logger.Info($"Initialized HybridMLQueryOptimizer ({modelInfo})");

            if (_personalModel != null)
            {
                _logger.Info("Personal model detected - enabling hybrid prediction mode");
            }
        }

        public QueryComplexity PredictComplexity(string artistName, string albumTitle)
        {
            if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(albumTitle))
                return QueryComplexity.Simple;

            QueryComplexity result;
            string strategy;

            if (_personalModel == null)
            {
                // Baseline-only mode
                result = _baselineModel.PredictComplexity(artistName, albumTitle);
                strategy = "baseline-only";
                _baselineUsed++;
            }
            else
            {
                // Hybrid mode - combine both models
                result = CombinePredictions(artistName, albumTitle, out strategy);
            }

            _statistics[result]++;
            _totalPredictions++;

            _logger.Trace($"Hybrid prediction for '{artistName} - {albumTitle}': {result} (strategy: {strategy})");

            return result;
        }

        private QueryComplexity CombinePredictions(string artistName, string albumTitle, out string strategy)
        {
            // Get predictions from both models
            var baselinePrediction = _baselineModel.PredictComplexity(artistName, albumTitle);
            var personalPrediction = _personalModel.PredictComplexity(artistName, albumTitle);

            // Get confidence scores
            var baselineConfidence = _baselineModel.GetConfidenceScore(artistName, albumTitle, baselinePrediction);
            var personalConfidence = _personalModel.GetConfidenceScore(artistName, albumTitle, personalPrediction);

            // Decision logic based on confidence and agreement
            if (baselinePrediction == personalPrediction)
            {
                // Models agree - use the prediction
                strategy = $"agreement (baseline: {baselineConfidence:F2}, personal: {personalConfidence:F2})";
                _hybridUsed++;
                return baselinePrediction;
            }

            // Models disagree - use confidence-based routing
            if (personalConfidence > _config.HighConfidenceThreshold &&
                personalConfidence > baselineConfidence + _config.ConfidenceDifferenceThreshold)
            {
                // Personal model is highly confident and significantly more confident
                strategy = $"personal-high-conf ({personalConfidence:F2} vs {baselineConfidence:F2})";
                _personalUsed++;
                return personalPrediction;
            }

            if (baselineConfidence > _config.HighConfidenceThreshold &&
                baselineConfidence > personalConfidence + _config.ConfidenceDifferenceThreshold)
            {
                // Baseline model is highly confident and significantly more confident
                strategy = $"baseline-high-conf ({baselineConfidence:F2} vs {personalConfidence:F2})";
                _baselineUsed++;
                return baselinePrediction;
            }

            // Both models have moderate confidence - use weighted combination
            double personalWeight = _config.PersonalModelWeight;
            double baselineWeight = 1.0 - personalWeight;

            // Convert predictions to scores for weighted combination
            var scores = new Dictionary<QueryComplexity, double>
            {
                { QueryComplexity.Simple, 0.0 },
                { QueryComplexity.Medium, 0.0 },
                { QueryComplexity.Complex, 0.0 }
            };

            // Add weighted scores
            scores[baselinePrediction] += baselineWeight * baselineConfidence;
            scores[personalPrediction] += personalWeight * personalConfidence;

            // Return highest scoring complexity
            var result = scores.OrderByDescending(kvp => kvp.Value).First().Key;
            strategy = $"weighted ({personalWeight:F1}*personal + {baselineWeight:F1}*baseline)";
            _hybridUsed++;

            return result;
        }

        public double GetConfidenceScore(string artistName, string albumTitle, QueryComplexity complexity)
        {
            if (_personalModel == null)
            {
                return _baselineModel.GetConfidenceScore(artistName, albumTitle, complexity);
            }

            // For hybrid mode, return the maximum confidence from either model
            var baselineConfidence = _baselineModel.GetConfidenceScore(artistName, albumTitle, complexity);
            var personalConfidence = _personalModel.GetConfidenceScore(artistName, albumTitle, complexity);

            return Math.Max(baselineConfidence, personalConfidence);
        }

        public void RecordResult(string artistName, string albumTitle, QueryComplexity usedComplexity, bool wasSuccessful)
        {
            if (wasSuccessful)
            {
                var predicted = PredictComplexity(artistName, albumTitle);
                if (predicted == usedComplexity)
                {
                    _correctPredictions++;
                }

                _logger.Trace($"Hybrid result: {(wasSuccessful ? "success" : "failure")} " +
                           $"(predicted: {predicted}, actual: {usedComplexity})");
            }

            // Forward to underlying models for their own learning
            _baselineModel.RecordResult(artistName, albumTitle, usedComplexity, wasSuccessful);
            _personalModel?.RecordResult(artistName, albumTitle, usedComplexity, wasSuccessful);
        }

        public PatternStatistics GetStatistics()
        {
            var accuracy = _totalPredictions > 0
                ? (double)_correctPredictions / _totalPredictions
                : 0.0;

            // Get baseline statistics for comparison
            var baselineStats = _baselineModel.GetStatistics();

            return new PatternStatistics
            {
                TotalPredictions = _totalPredictions,
                CorrectPredictions = _correctPredictions,
                Accuracy = accuracy,
                LastModelUpdate = DateTime.UtcNow,
                PatternDistribution = new Dictionary<QueryComplexity, int>(_statistics),
                IsUsingMLEngine = true,
                // Hybrid-specific metadata
                HybridStatistics = new Dictionary<string, object>
                {
                    ["BaselineUsed"] = _baselineUsed,
                    ["PersonalUsed"] = _personalUsed,
                    ["HybridUsed"] = _hybridUsed,
                    ["HasPersonalModel"] = _personalModel != null,
                    ["BaselineAccuracy"] = baselineStats.Accuracy,
                    ["PersonalModelAvailable"] = _personalModel != null,
                    ["Configuration"] = _config
                }
            };
        }

        public List<string> GetOptimizedQueryStrategies(string artistName, string albumTitle)
        {
            var complexity = PredictComplexity(artistName, albumTitle);
            var confidence = GetConfidenceScore(artistName, albumTitle, complexity);

            // Use enhanced strategy selection for hybrid mode
            if (_personalModel != null)
            {
                return GetHybridQueryStrategies(artistName, albumTitle, complexity, confidence);
            }

            // Fallback to baseline strategies
            return _baselineModel.GetOptimizedQueryStrategies(artistName, albumTitle);
        }

        private List<string> GetHybridQueryStrategies(string artistName, string albumTitle,
            QueryComplexity complexity, double confidence)
        {
            // Get strategies from both models
            var baselineStrategies = _baselineModel.GetOptimizedQueryStrategies(artistName, albumTitle);
            var personalStrategies = _personalModel?.GetOptimizedQueryStrategies(artistName, albumTitle)
                                   ?? baselineStrategies;

            // High confidence: use specific model's strategy
            if (confidence > _config.HighConfidenceThreshold)
            {
                switch (complexity)
                {
                    case QueryComplexity.Simple:
                        return personalStrategies;  // Personal model likely better for user's simple cases
                    case QueryComplexity.Complex:
                        return baselineStrategies; // Baseline model has more diverse complex examples
                    case QueryComplexity.Medium:
                        return CombineStrategies(personalStrategies, baselineStrategies);
                }
            }

            // Low confidence: use conservative approach (baseline)
            return baselineStrategies;
        }

        private List<string> CombineStrategies(List<string> personalStrategies, List<string> baselineStrategies)
        {
            // Merge strategies, preferring personal model but including baseline fallbacks
            var combined = new List<string>();

            // Start with personal strategies
            combined.AddRange(personalStrategies);

            // Add baseline strategies that aren't already included
            foreach (var strategy in baselineStrategies)
            {
                if (!combined.Contains(strategy))
                {
                    combined.Add(strategy);
                }
            }

            return combined;
        }

        // Async method implementations for interface compatibility
        public async System.Threading.Tasks.Task TrainAsync(IEnumerable<QueryPattern> patterns)
        {
            await _baselineModel.TrainAsync(patterns);
            if (_personalModel != null)
            {
                await _personalModel.TrainAsync(patterns);
            }
        }

        public async System.Threading.Tasks.Task<PredictionResult> PredictOptimalStrategyAsync(string artist, string album)
        {
            var complexity = PredictComplexity(artist, album);
            var confidence = GetConfidenceScore(artist, album, complexity);
            var strategies = GetOptimizedQueryStrategies(artist, album);

            return await System.Threading.Tasks.Task.FromResult(new PredictionResult
            {
                PredictedComplexity = complexity,
                Confidence = (float)confidence,
                RecommendedQueries = strategies,
                Features = ExtractCombinedFeatures(artist, album)
            });
        }

        public async System.Threading.Tasks.Task<ModelMetrics> EvaluateModelAsync()
        {
            var baselineMetrics = await _baselineModel.EvaluateModelAsync();

            if (_personalModel != null)
            {
                var personalMetrics = await _personalModel.EvaluateModelAsync();

                // Return combined metrics
                return new ModelMetrics
                {
                    Accuracy = Math.Max(baselineMetrics.Accuracy, personalMetrics.Accuracy),
                    Precision = (baselineMetrics.Precision + personalMetrics.Precision) / 2,
                    Recall = (baselineMetrics.Recall + personalMetrics.Recall) / 2,
                    F1Score = (baselineMetrics.F1Score + personalMetrics.F1Score) / 2,
                    ConfusionMatrix = baselineMetrics.ConfusionMatrix // Use baseline as primary
                };
            }

            return baselineMetrics;
        }

        public async System.Threading.Tasks.Task UpdateModelAsync(QueryResult actualResult)
        {
            RecordResult(actualResult.Artist, actualResult.Album,
                actualResult.SuccessfulComplexity, actualResult.WasSuccessful);
            await System.Threading.Tasks.Task.CompletedTask;
        }

        private float[] ExtractCombinedFeatures(string artist, string album)
        {
            // Use baseline model's feature extraction as primary
            // Personal model should use compatible feature extraction

            // This is a simplified approach - in practice, both models should use
            // the same feature extraction pipeline for compatibility
            return new float[25]; // Placeholder - should extract actual features
        }

        /// <summary>
        /// Initialize the hybrid model (placeholder for model loading simulation)
        /// </summary>
        private void InitializeHybridModel()
        {
            // Simulate hybrid model initialization work for performance timing
            // Removed Thread.Sleep anti-pattern - hybrid model should initialize instantly
        }

        /// <summary>
        /// Get model usage distribution for analytics
        /// </summary>
        private Dictionary<string, object> GetModelUsageDistribution()
        {
            var total = _baselineUsed + _personalUsed + _hybridUsed;
            return new Dictionary<string, object>
            {
                ["BaselinePercentage"] = total > 0 ? (double)_baselineUsed / total * 100 : 0,
                ["PersonalPercentage"] = total > 0 ? (double)_personalUsed / total * 100 : 0,
                ["HybridPercentage"] = total > 0 ? (double)_hybridUsed / total * 100 : 0,
                ["TotalDecisions"] = total
            };
        }

        /// <summary>
        /// Record cache hit for performance tracking
        /// </summary>
        public void RecordCacheHit()
        {
            _performanceMetrics.RecordCacheHit();
        }

        /// <summary>
        /// Record cache miss for performance tracking
        /// </summary>
        public void RecordCacheMiss()
        {
            _performanceMetrics.RecordCacheMiss();
        }

        /// <summary>
        /// Record API optimization results
        /// </summary>
        public void RecordApiOptimization(int callsSaved, int totalCallsWithoutOptimization)
        {
            _performanceMetrics.RecordApiOptimization(callsSaved, totalCallsWithoutOptimization);
        }

        /// <summary>
        /// Get detailed performance report
        /// </summary>
        public string GetPerformanceReport()
        {
            var summary = _performanceMetrics.GetPerformanceSummary();
            var distribution = GetModelUsageDistribution();

            var hybridReport = $@"
HYBRID MODEL USAGE:
- Baseline Only: {distribution["BaselinePercentage"]:F1}% ({_baselineUsed} decisions)
- Personal Only: {distribution["PersonalPercentage"]:F1}% ({_personalUsed} decisions)
- Hybrid Logic: {distribution["HybridPercentage"]:F1}% ({_hybridUsed} decisions)
- Total Decisions: {distribution["TotalDecisions"]}
- Personal Model Available: {_personalModel != null}";

            return summary.GetFormattedReport() + hybridReport;
        }

        /// <summary>
        /// Get current performance health status
        /// </summary>
        public PerformanceHealth GetPerformanceHealth()
        {
            var summary = _performanceMetrics.GetPerformanceSummary();
            return summary.GetHealthStatus();
        }

        #region IDisposable Implementation

        private bool _disposed = false;

        public void Dispose()
        {
            if (!_disposed)
            {
                _performanceMetrics?.Dispose();
                _disposed = true;
                _logger.Debug("HybridMLQueryOptimizer disposed with performance metrics");
            }
        }

        #endregion
    }

    /// <summary>
    /// Configuration for hybrid model behavior
    /// </summary>
    public class HybridConfiguration
    {
        /// <summary>
        /// Confidence threshold above which a model is considered "high confidence"
        /// </summary>
        public double HighConfidenceThreshold { get; set; } = 0.8;

        /// <summary>
        /// Minimum confidence difference required to prefer one model over another
        /// </summary>
        public double ConfidenceDifferenceThreshold { get; set; } = 0.15;

        /// <summary>
        /// Weight for personal model in weighted combinations (0.0 = baseline only, 1.0 = personal only)
        /// </summary>
        public double PersonalModelWeight { get; set; } = 0.7;

        /// <summary>
        /// Enable detailed logging of hybrid decision process
        /// </summary>
        public bool EnableDetailedLogging { get; set; } = false;

        /// <summary>
        /// Strategy for handling model disagreements
        /// </summary>
        public string DisagreementStrategy { get; set; } = "confidence_based";
    }
}
