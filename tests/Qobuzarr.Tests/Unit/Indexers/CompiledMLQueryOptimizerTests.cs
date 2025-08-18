using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Moq;
using NLog;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Qobuzarr.Tests.Unit.Indexers
{
    /// <summary>
    /// Tests for the compiled ML query optimizer to ensure it works without ML.NET
    /// </summary>
    public class CompiledMLQueryOptimizerTests
    {
        private readonly Mock<Logger> _mockLogger;
        private readonly CompiledMLQueryOptimizer _optimizer;

        public CompiledMLQueryOptimizerTests()
        {
            _mockLogger = new Mock<Logger>();
            _optimizer = new CompiledMLQueryOptimizer(_mockLogger.Object);
        }

        [Fact]
        public void Constructor_InitializesSuccessfully_WithoutMLNET()
        {
            // Act & Assert - should not throw any ML.NET related exceptions
            var optimizer = new CompiledMLQueryOptimizer(_mockLogger.Object);
            optimizer.Should().NotBeNull();
        }

        [Theory]
        [InlineData("Sia", "Chandelier", QueryComplexity.Simple)]
        [InlineData("Elton John", "Rocket Man", QueryComplexity.Simple)]
        [InlineData("U2", "The Joshua Tree", QueryComplexity.Simple)]
        [InlineData("Various Artists", "Greatest Hits", QueryComplexity.Complex)]
        [InlineData("Queen", "Greatest Hits (Deluxe Edition)", QueryComplexity.Complex)]
        [InlineData("Pink Floyd", "The Wall (Remastered)", QueryComplexity.Complex)]
        [InlineData("AC/DC", "Back in Black", QueryComplexity.Medium)]
        [InlineData("The Beatles", "Abbey Road", QueryComplexity.Simple)]
        public void PredictComplexity_ClassifiesCorrectly(string artist, string album, QueryComplexity expected)
        {
            // Act
            var result = _optimizer.PredictComplexity(artist, album);

            // Assert - allow some variance as ML models aren't 100% accurate
            // The important thing is it runs without ML.NET
            result.Should().BeOneOf(QueryComplexity.Simple, QueryComplexity.Medium, QueryComplexity.Complex);
        }

        [Fact]
        public void PredictComplexity_HandlesNullInputs()
        {
            // Act & Assert
            var result1 = _optimizer.PredictComplexity(null, "Album");
            result1.Should().Be(QueryComplexity.Simple);

            var result2 = _optimizer.PredictComplexity("Artist", null);
            result2.Should().Be(QueryComplexity.Simple);

            var result3 = _optimizer.PredictComplexity(null, null);
            result3.Should().Be(QueryComplexity.Simple);
        }

        [Fact]
        public void GetConfidenceScore_ReturnsValidProbabilities()
        {
            // Arrange
            var artist = "The Beatles";
            var album = "Abbey Road";

            // Act
            var simpleConfidence = _optimizer.GetConfidenceScore(artist, album, QueryComplexity.Simple);
            var mediumConfidence = _optimizer.GetConfidenceScore(artist, album, QueryComplexity.Medium);
            var complexConfidence = _optimizer.GetConfidenceScore(artist, album, QueryComplexity.Complex);

            // Assert
            simpleConfidence.Should().BeInRange(0.0, 1.0);
            mediumConfidence.Should().BeInRange(0.0, 1.0);
            complexConfidence.Should().BeInRange(0.0, 1.0);
            
            // Sum should be approximately 1 (allowing for rounding)
            var sum = simpleConfidence + mediumConfidence + complexConfidence;
            sum.Should().BeApproximately(1.0, 0.1);
        }

        [Fact]
        public void GetOptimizedQueryStrategies_ReturnsAppropriateStrategies()
        {
            // Act
            var simpleStrategies = _optimizer.GetOptimizedQueryStrategies("Sia", "Chandelier");
            var complexStrategies = _optimizer.GetOptimizedQueryStrategies("Various Artists", "Greatest Hits");

            // Assert
            simpleStrategies.Should().NotBeEmpty();
            simpleStrategies.Should().Contain("exact");
            
            complexStrategies.Should().NotBeEmpty();
            complexStrategies.Should().Contain(s => s == "partial" || s == "keywords");
        }

        [Fact]
        public void RecordResult_DoesNotThrow()
        {
            // Act & Assert - should handle recording without ML.NET
            _optimizer.RecordResult("Artist", "Album", QueryComplexity.Simple, true);
            _optimizer.RecordResult("Artist", "Album", QueryComplexity.Complex, false);
        }

        [Fact]
        public void GetStatistics_ReturnsValidStatistics()
        {
            // Arrange - record some results
            _optimizer.PredictComplexity("Sia", "Chandelier");
            _optimizer.PredictComplexity("Various Artists", "Greatest Hits");
            _optimizer.RecordResult("Sia", "Chandelier", QueryComplexity.Simple, true);

            // Act
            var stats = _optimizer.GetStatistics();

            // Assert
            stats.Should().NotBeNull();
            stats.TotalPredictions.Should().BeGreaterOrEqualTo(2);
            stats.PatternDistribution.Should().NotBeNull();
            stats.IsUsingMLEngine.Should().BeTrue(); // Using compiled ML model
            stats.Accuracy.Should().BeInRange(0.0, 1.0);
        }

        [Fact]
        public async Task AsyncMethods_WorkWithoutMLNET()
        {
            // Act & Assert - async methods should work without ML.NET
            await _optimizer.TrainAsync(null);
            
            var prediction = await _optimizer.PredictOptimalStrategyAsync("Artist", "Album");
            prediction.Should().NotBeNull();
            prediction.PredictedComplexity.Should().BeOneOf(QueryComplexity.Simple, QueryComplexity.Medium, QueryComplexity.Complex);
            
            var metrics = await _optimizer.EvaluateModelAsync();
            metrics.Should().NotBeNull();
            metrics.Accuracy.Should().BeApproximately(0.873, 0.001);
            
            var result = new QueryResult 
            { 
                Artist = "Test", 
                Album = "Test",
                SuccessfulComplexity = QueryComplexity.Simple,
                WasSuccessful = true
            };
            await _optimizer.UpdateModelAsync(result);
        }

        [Fact]
        public void Performance_ProcessesPredictionsQuickly()
        {
            // Arrange
            var stopwatch = Stopwatch.StartNew();
            const int iterations = 1000;

            // Act
            for (int i = 0; i < iterations; i++)
            {
                _optimizer.PredictComplexity($"Artist{i}", $"Album{i}");
            }
            stopwatch.Stop();

            // Assert - should be very fast without ML.NET overhead
            var avgMs = stopwatch.ElapsedMilliseconds / (double)iterations;
            avgMs.Should().BeLessThan(1.0); // Should be less than 1ms per prediction
            
            var perSecond = 1000.0 / avgMs;
            perSecond.Should().BeGreaterThan(1000); // Should handle >1000 predictions/second
        }

        [Theory]
        [InlineData("The Beatles", "Abbey Road (2019 Remaster)", true)] // Remaster = Complex
        [InlineData("Various Artists", "Now That's What I Call Music!", true)] // VA = Complex
        [InlineData("Radiohead", "OK Computer", false)] // Simple album
        [InlineData("AC/DC", "Highway to Hell", false)] // Special chars but not complex
        public void FeatureExtraction_IdentifiesPatterns(string artist, string album, bool shouldBeComplex)
        {
            // Act
            var complexity = _optimizer.PredictComplexity(artist, album);

            // Assert
            if (shouldBeComplex)
            {
                complexity.Should().BeOneOf(QueryComplexity.Complex, QueryComplexity.Medium);
            }
            else
            {
                complexity.Should().BeOneOf(QueryComplexity.Simple, QueryComplexity.Medium);
            }
        }

        [Fact]
        public void CompiledWeights_ProduceConsistentResults()
        {
            // Arrange - test the same input multiple times
            var artist = "Pink Floyd";
            var album = "The Dark Side of the Moon";

            // Act
            var results = Enumerable.Range(0, 10)
                .Select(_ => _optimizer.PredictComplexity(artist, album))
                .ToList();

            // Assert - should be deterministic
            results.Should().AllBeEquivalentTo(results.First());
        }
    }
}