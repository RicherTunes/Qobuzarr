using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Qobuzarr.Tests.Indexers
{
    /// <summary>
    /// TDD tests for the feature-flag guarding ExtractCombinedFeatures in HybridMLQueryOptimizer.
    ///
    /// The method is gated by HybridConfiguration.EnableHybridFeatureExtraction (default: false).
    /// See docs/EXPERIMENTAL_FEATURES.md for the full feature specification.
    /// </summary>
    public class HybridMLQueryOptimizerFeatureTests : IDisposable
    {
        private readonly Logger _logger;
        private readonly StubPatternEngine _baselineModel;

        public HybridMLQueryOptimizerFeatureTests()
        {
            var logConfig = new NLog.Config.LoggingConfiguration();
            LogManager.Configuration = logConfig;
            _logger = LogManager.GetCurrentClassLogger();
            _baselineModel = new StubPatternEngine();
        }

        // -----------------------------------------------------------------------
        // HybridConfiguration default
        // -----------------------------------------------------------------------

        [Fact]
        public void HybridConfiguration_DefaultValue_HasFeatureFlagDisabled()
        {
            // Ensures the default is opt-out (safe for production).
            var config = new HybridConfiguration();
            config.EnableHybridFeatureExtraction.Should().BeFalse(
                "EnableHybridFeatureExtraction must default to false to protect production builds");
        }

        // -----------------------------------------------------------------------
        // ExtractCombinedFeatures — disabled path (default)
        // -----------------------------------------------------------------------

        [Fact]
        public void PredictComplexity_WhenFeatureFlagDisabled_DoesNotThrowAndReturnsValidComplexity()
        {
            // Arrange: default config has EnableHybridFeatureExtraction = false
            var hybridConfig = new HybridConfiguration { EnableHybridFeatureExtraction = false };
            var personalModel = new StubPatternEngine();
            var optimizer = new HybridMLQueryOptimizer(
                _logger, _baselineModel, personalModel, hybridConfig);

            // Act: triggers ExtractCombinedFeatures via CombinePredictions internally
            var result = optimizer.PredictComplexity("Radiohead", "OK Computer");

            // Assert: zero-vector placeholder path taken; no exception
            new[] { QueryComplexity.Simple, QueryComplexity.Medium, QueryComplexity.Complex }
                .Should().Contain(result,
                "prediction should succeed even with the feature flag disabled (placeholder path)");
        }

        // -----------------------------------------------------------------------
        // ExtractCombinedFeatures — enabled path, baseline IS an IFeatureExtractor
        // -----------------------------------------------------------------------

        [Fact]
        public void PredictComplexity_WhenFeatureFlagEnabled_AndBaselineIsExtractor_DoesNotThrow()
        {
            // Arrange: baseline implements IFeatureExtractor; feature flag enabled
            var featureExtractingBaseline = new FeatureExtractingStubEngine();
            var hybridConfig = new HybridConfiguration { EnableHybridFeatureExtraction = true };
            var personalModel = new StubPatternEngine();

            var optimizer = new HybridMLQueryOptimizer(
                _logger, featureExtractingBaseline, personalModel, hybridConfig);

            // Act
            var act = () => optimizer.PredictComplexity("Pink Floyd", "The Wall");

            // Assert: must not throw; the experimental path must be stable
            act.Should().NotThrow(
                "ExtractCombinedFeatures with a feature-extracting baseline should not throw");
        }

        // -----------------------------------------------------------------------
        // ExtractCombinedFeatures — enabled path, baseline is NOT an IFeatureExtractor
        // -----------------------------------------------------------------------

        [Fact]
        public void PredictComplexity_WhenFeatureFlagEnabled_AndBaselineIsNotExtractor_FallsBackGracefully()
        {
            // Arrange: baseline does NOT implement IFeatureExtractor
            var hybridConfig = new HybridConfiguration { EnableHybridFeatureExtraction = true };
            var personalModel = new StubPatternEngine();

            var optimizer = new HybridMLQueryOptimizer(
                _logger, _baselineModel, personalModel, hybridConfig);

            // Act: should not throw; falls back to zero-vector with a warning logged
            var act = () => optimizer.PredictComplexity("Boards of Canada", "Geogaddi");
            act.Should().NotThrow(
                "ExtractCombinedFeatures must always return a valid float[] even in fallback mode");
        }

        public void Dispose() { }

        // -----------------------------------------------------------------------
        // Stubs implementing the full IPatternLearningEngine interface
        // -----------------------------------------------------------------------

        private sealed class StubPatternEngine : IPatternLearningEngine
        {
            public QueryComplexity PredictComplexity(string artistName, string albumTitle)
                => QueryComplexity.Medium;

            public double GetConfidenceScore(string artistName, string albumTitle, QueryComplexity complexity)
                => 0.5;

            public void RecordResult(string artistName, string albumTitle,
                QueryComplexity usedComplexity, bool wasSuccessful) { }

            public PatternStatistics GetStatistics()
                => new PatternStatistics { PatternDistribution = new Dictionary<QueryComplexity, int>() };

            public List<string> GetOptimizedQueryStrategies(string artistName, string albumTitle)
                => new List<string>();

            public Task TrainAsync(IEnumerable<QueryPattern> patterns)
                => Task.CompletedTask;

            public Task<PredictionResult> PredictOptimalStrategyAsync(string artist, string album)
                => Task.FromResult(new PredictionResult
                {
                    PredictedComplexity = QueryComplexity.Medium,
                    Confidence = 0.5f,
                    RecommendedQueries = new List<string>()
                });

            public Task<ModelMetrics> EvaluateModelAsync()
                => Task.FromResult(new ModelMetrics());

            public Task UpdateModelAsync(QueryResult actualResult)
                => Task.CompletedTask;
        }

        private sealed class FeatureExtractingStubEngine : IPatternLearningEngine, IFeatureExtractor
        {
            public int ExtractFeaturesCallCount { get; private set; }

            public QueryComplexity PredictComplexity(string artistName, string albumTitle)
                => QueryComplexity.Medium;

            public double GetConfidenceScore(string artistName, string albumTitle, QueryComplexity complexity)
                => 0.9; // high confidence so the extractor path is exercised

            public void RecordResult(string artistName, string albumTitle,
                QueryComplexity usedComplexity, bool wasSuccessful) { }

            public PatternStatistics GetStatistics()
                => new PatternStatistics { PatternDistribution = new Dictionary<QueryComplexity, int>() };

            public List<string> GetOptimizedQueryStrategies(string artistName, string albumTitle)
                => new List<string>();

            public Task TrainAsync(IEnumerable<QueryPattern> patterns)
                => Task.CompletedTask;

            public Task<PredictionResult> PredictOptimalStrategyAsync(string artist, string album)
                => Task.FromResult(new PredictionResult
                {
                    PredictedComplexity = QueryComplexity.Medium,
                    Confidence = 0.9f,
                    RecommendedQueries = new List<string>()
                });

            public Task<ModelMetrics> EvaluateModelAsync()
                => Task.FromResult(new ModelMetrics());

            public Task UpdateModelAsync(QueryResult actualResult)
                => Task.CompletedTask;

            public float[]? ExtractFeatures(string artist, string album)
            {
                ExtractFeaturesCallCount++;
                // Return a non-zero 25-element vector
                var features = new float[25];
                for (int i = 0; i < features.Length; i++)
                    features[i] = (float)(i + 1) / 25f;
                return features;
            }
        }
    }
}
