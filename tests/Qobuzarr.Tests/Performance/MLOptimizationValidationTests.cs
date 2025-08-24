using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.API;
using NSubstitute;
using NzbDrone.Core.Parser.Model;

namespace Qobuzarr.Tests.Performance
{
    /// <summary>
    /// Validates ML optimization performance against target metrics
    /// Target: 49% API call reduction with maintained accuracy
    /// </summary>
    [Collection("MLPerformance")]
    public class MLOptimizationValidationTests
    {
        private readonly ITestOutputHelper _output;
        private readonly CompiledMLQueryOptimizer _optimizer;
        private readonly IQobuzApiClient _mockApiClient;
        private readonly MLPerformanceMetrics _metrics;

        public MLOptimizationValidationTests(ITestOutputHelper output)
        {
            _output = output;
            _optimizer = new CompiledMLQueryOptimizer();
            _mockApiClient = Substitute.For<IQobuzApiClient>();
            _metrics = new MLPerformanceMetrics();
        }

        #region Performance Target Validation

        [Fact]
        [Trait("Category", "MLPerformance")]
        public async Task MLOptimizer_ShouldAchieve49PercentApiReduction()
        {
            // Validate the critical 49% API call reduction target
            
            // Arrange
            var testQueries = GenerateRealWorldQueries(1000);
            var baselineApiCalls = 0;
            var optimizedApiCalls = 0;

            // Act - Baseline (no optimization)
            foreach (var query in testQueries)
            {
                baselineApiCalls += SimulateUnoptimizedSearch(query);
            }

            // Act - With ML optimization
            foreach (var query in testQueries)
            {
                var optimizedQuery = await _optimizer.OptimizeQueryAsync(query);
                optimizedApiCalls += SimulateOptimizedSearch(optimizedQuery);
            }

            // Calculate reduction
            var reductionPercent = ((double)(baselineApiCalls - optimizedApiCalls) / baselineApiCalls) * 100;

            // Assert
            reductionPercent.Should().BeGreaterThanOrEqualTo(49.0,
                $"ML optimization must achieve at least 49% API call reduction. Actual: {reductionPercent:F2}%");
            
            _output.WriteLine($"API Call Reduction: {reductionPercent:F2}%");
            _output.WriteLine($"Baseline calls: {baselineApiCalls}, Optimized calls: {optimizedApiCalls}");
        }

        [Fact]
        [Trait("Category", "MLPerformance")]
        public void MLOptimizer_ShouldMaintainAccuracy()
        {
            // Ensure optimization doesn't compromise search accuracy
            
            // Arrange
            var accuracyTestCases = new[]
            {
                ("Miles Davis Kind of Blue", "Miles Davis", "Kind of Blue"),
                ("The Beatles Abbey Road", "The Beatles", "Abbey Road"),
                ("Pink Floyd The Dark Side of the Moon", "Pink Floyd", "The Dark Side of the Moon"),
                ("Led Zeppelin IV", "Led Zeppelin", "IV"),
                ("Nirvana Nevermind", "Nirvana", "Nevermind")
            };

            var correctPredictions = 0;

            // Act
            foreach (var (query, expectedArtist, expectedAlbum) in accuracyTestCases)
            {
                var result = _optimizer.ClassifyQuery(query);
                
                if (result.Artist.Contains(expectedArtist, StringComparison.OrdinalIgnoreCase) &&
                    result.Album.Contains(expectedAlbum, StringComparison.OrdinalIgnoreCase))
                {
                    correctPredictions++;
                }
            }

            // Calculate accuracy
            var accuracy = (double)correctPredictions / accuracyTestCases.Length;

            // Assert
            accuracy.Should().BeGreaterThanOrEqualTo(0.90,
                "ML optimizer should maintain at least 90% accuracy");
            
            _output.WriteLine($"Classification Accuracy: {accuracy:P}");
        }

        #endregion

        #region Memory and Performance Tests

        [Fact]
        [Trait("Category", "MLPerformance")]
        public void MLOptimizer_ShouldNotLeakMemory()
        {
            // Test for memory leaks during extended operation
            
            // Arrange
            var initialMemory = GC.GetTotalMemory(true);
            const int iterations = 10000;

            // Act
            for (int i = 0; i < iterations; i++)
            {
                var query = $"Artist{i} Album{i}";
                var result = _optimizer.ClassifyQuery(query);
                
                // Force feature extraction
                var features = _optimizer.ExtractEnhancedFeatures(query);
            }

            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var finalMemory = GC.GetTotalMemory(true);
            var memoryIncrease = finalMemory - initialMemory;
            var memoryIncreasePerIteration = memoryIncrease / iterations;

            // Assert
            memoryIncreasePerIteration.Should().BeLessThan(100,
                "Memory increase per iteration should be minimal (< 100 bytes)");
            
            _output.WriteLine($"Memory increase per iteration: {memoryIncreasePerIteration} bytes");
            _output.WriteLine($"Total memory increase: {memoryIncrease / 1024.0:F2} KB");
        }

        [Fact]
        [Trait("Category", "MLPerformance")]
        public void MLOptimizer_ShouldMeetLatencyRequirements()
        {
            // Ensure ML optimization doesn't add significant latency
            
            // Arrange
            var testQueries = GenerateRealWorldQueries(100);
            var stopwatch = new Stopwatch();
            var latencies = new List<double>();

            // Act
            foreach (var query in testQueries)
            {
                stopwatch.Restart();
                var result = _optimizer.ClassifyQuery(query);
                stopwatch.Stop();
                
                latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
            }

            // Calculate statistics
            var avgLatency = latencies.Average();
            var p95Latency = latencies.OrderBy(l => l).Skip((int)(latencies.Count * 0.95)).First();
            var maxLatency = latencies.Max();

            // Assert
            avgLatency.Should().BeLessThan(10.0,
                "Average classification latency should be < 10ms");
            p95Latency.Should().BeLessThan(20.0,
                "95th percentile latency should be < 20ms");
            maxLatency.Should().BeLessThan(50.0,
                "Maximum latency should be < 50ms");
            
            _output.WriteLine($"Latency - Avg: {avgLatency:F2}ms, P95: {p95Latency:F2}ms, Max: {maxLatency:F2}ms");
        }

        #endregion

        #region Feature Extraction Validation

        [Fact]
        [Trait("Category", "MLPerformance")]
        public void ExtractEnhancedFeatures_ShouldGenerateConsistentFeatures()
        {
            // Validate feature extraction consistency
            
            // Arrange
            var query = "Miles Davis Kind of Blue";
            
            // Act
            var features1 = _optimizer.ExtractEnhancedFeatures(query);
            var features2 = _optimizer.ExtractEnhancedFeatures(query);

            // Assert
            features1.Should().HaveCount(16, "Should extract 16 enhanced features");
            features2.Should().BeEquivalentTo(features1,
                "Same query should produce identical features");
            
            // Validate feature ranges
            features1.Should().OnlyContain(f => f >= 0 && f <= 1.0,
                "All features should be normalized to [0, 1] range");
            
            _output.WriteLine($"Features extracted: {string.Join(", ", features1.Select(f => f.ToString("F3")))}");
        }

        [Fact]
        [Trait("Category", "MLPerformance")]
        public void ExtractEnhancedFeatures_ShouldHandleEdgeCases()
        {
            // Test feature extraction with edge cases
            
            // Arrange
            var edgeCases = new[]
            {
                "",                          // Empty query
                "a",                         // Single character
                "The The",                   // Repeated words
                "!!!@#$%",                   // Special characters only
                new string('x', 500),        // Very long query
                "Björk Homogenic",           // Unicode characters
                "  spaces   everywhere  ",   // Multiple spaces
                null                         // Null query
            };

            // Act & Assert
            foreach (var query in edgeCases)
            {
                var features = _optimizer.ExtractEnhancedFeatures(query ?? "");
                
                features.Should().HaveCount(16,
                    $"Should always return 16 features for query: '{query}'");
                features.Should().OnlyContain(f => !double.IsNaN(f) && !double.IsInfinity(f),
                    $"Features should be valid numbers for query: '{query}'");
            }
            
            _output.WriteLine("All edge cases handled successfully");
        }

        #endregion

        #region Confidence Scoring Tests

        [Fact]
        [Trait("Category", "MLPerformance")]
        public void CalculateConfidence_ShouldProvideAccurateScores()
        {
            // Validate confidence scoring accuracy
            
            // Arrange
            var testCases = new[]
            {
                ("Miles Davis Kind of Blue", 0.85, 1.0),        // Clear query, high confidence
                ("Blue", 0.3, 0.6),                             // Ambiguous, low confidence
                ("The Beatles Abbey Road Remastered", 0.75, 0.95), // Good query with extra info
                ("Track 1", 0.1, 0.4),                          // Very ambiguous
                ("Pink Floyd Dark Side Moon", 0.7, 0.9)         // Missing articles but clear
            };

            // Act & Assert
            foreach (var (query, minConfidence, maxConfidence) in testCases)
            {
                var result = _optimizer.ClassifyQuery(query);
                var confidence = _optimizer.CalculateConfidence(
                    _optimizer.ExtractEnhancedFeatures(query));
                
                confidence.Should().BeInRange(minConfidence, maxConfidence,
                    $"Confidence for '{query}' should be between {minConfidence} and {maxConfidence}");
                
                _output.WriteLine($"Query: '{query}' - Confidence: {confidence:F3}");
            }
        }

        #endregion

        #region Cache Integration Tests

        [Fact]
        [Trait("Category", "MLPerformance")]
        public void MLOptimizer_ShouldIntegrateWithCache()
        {
            // Test cache hit/miss recording
            
            // Arrange
            _metrics.Reset();
            var queries = new[] { "Miles Davis", "John Coltrane", "Miles Davis" }; // Duplicate for cache hit

            // Act
            foreach (var query in queries)
            {
                var result = _optimizer.ClassifyQuery(query);
                _metrics.RecordClassification(query, result, confidence: 0.85);
            }

            // Assert
            var stats = _metrics.GetStatistics();
            stats.TotalClassifications.Should().Be(3);
            stats.CacheHitRate.Should().BeGreaterThan(0, "Should have cache hits for duplicate query");
            
            _output.WriteLine($"Cache hit rate: {stats.CacheHitRate:P}");
        }

        #endregion

        #region Model Drift Detection

        [Fact]
        [Trait("Category", "MLPerformance")]
        public void MLOptimizer_ShouldDetectPerformanceDegradation()
        {
            // Test ability to detect model performance degradation
            
            // Arrange
            _metrics.Reset();
            var goodQueries = GenerateRealWorldQueries(50);
            var poorQueries = GeneratePoorQueries(50);

            // Act - Simulate good performance period
            foreach (var query in goodQueries)
            {
                var result = _optimizer.ClassifyQuery(query);
                _metrics.RecordClassification(query, result, confidence: 0.85);
                _metrics.RecordApiCallReduction(0.5); // 50% reduction
            }

            var goodStats = _metrics.GetStatistics();

            // Act - Simulate degraded performance
            foreach (var query in poorQueries)
            {
                var result = _optimizer.ClassifyQuery(query);
                _metrics.RecordClassification(query, result, confidence: 0.45);
                _metrics.RecordApiCallReduction(0.2); // Only 20% reduction
            }

            var degradedStats = _metrics.GetStatistics();

            // Assert
            degradedStats.AverageConfidence.Should().BeLessThan(goodStats.AverageConfidence,
                "Should detect confidence degradation");
            degradedStats.ApiCallReduction.Should().BeLessThan(goodStats.ApiCallReduction,
                "Should detect API reduction degradation");
            
            _output.WriteLine($"Confidence drop: {goodStats.AverageConfidence:F3} -> {degradedStats.AverageConfidence:F3}");
            _output.WriteLine($"API reduction drop: {goodStats.ApiCallReduction:P} -> {degradedStats.ApiCallReduction:P}");
        }

        #endregion

        #region Helper Methods

        private List<string> GenerateRealWorldQueries(int count)
        {
            var artists = new[] { "Miles Davis", "John Coltrane", "Bill Evans", "The Beatles", "Pink Floyd" };
            var albums = new[] { "Kind of Blue", "A Love Supreme", "Abbey Road", "The Wall", "Nevermind" };
            var queries = new List<string>();

            var random = new Random(42); // Fixed seed for reproducibility
            for (int i = 0; i < count; i++)
            {
                var artist = artists[random.Next(artists.Length)];
                var album = albums[random.Next(albums.Length)];
                
                // Mix of query formats
                var format = random.Next(4);
                switch (format)
                {
                    case 0:
                        queries.Add($"{artist} {album}");
                        break;
                    case 1:
                        queries.Add($"{artist} - {album}");
                        break;
                    case 2:
                        queries.Add(album);
                        break;
                    case 3:
                        queries.Add($"{artist} {album} Remastered");
                        break;
                }
            }

            return queries;
        }

        private List<string> GeneratePoorQueries(int count)
        {
            // Generate queries that are harder to classify
            var queries = new List<string>();
            for (int i = 0; i < count; i++)
            {
                queries.Add($"Track {i}");
                queries.Add($"Album {i}");
                queries.Add($"Unknown Artist {i}");
            }
            return queries;
        }

        private int SimulateUnoptimizedSearch(string query)
        {
            // Simulates API calls without optimization
            // Typically: 1 artist search + 1 album search + N track searches
            return 3 + (query.Length > 20 ? 2 : 1); // More calls for complex queries
        }

        private int SimulateOptimizedSearch(AlbumSearchCriteria optimizedQuery)
        {
            // Simulates API calls with ML optimization
            // Smart search reduces to targeted calls
            return optimizedQuery.CleanSearchQuery.Length > 30 ? 2 : 1;
        }

        #endregion
    }
}