using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Qobuzarr.Tests.Performance
{
    /// <summary>
    /// Performance tests for ML Query Optimization
    /// Validates API call reduction targets and query optimization effectiveness
    /// </summary>
    [Collection("Performance")]
    [Trait("Category", "Performance")]
    [Trait("Component", "MLOptimization")]
    public class MLOptimizationPerformanceTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly CompiledMLQueryOptimizer _mlOptimizer;
        private readonly Mock<IQobuzApiClient> _mockApiClient;
        private readonly Mock<ILogger<CompiledMLQueryOptimizer>> _mockLogger;
        private readonly PerformanceMetrics _metrics;

        // Performance targets from requirements
        private const double TARGET_API_CALL_REDUCTION = 0.49; // 49% reduction target
        private const int MAX_RESPONSE_TIME_MS = 500; // Sub-500ms response time
        private const double MIN_CACHE_HIT_RATE = 0.70; // 70% cache hit rate
        private const int CONCURRENT_QUERY_LOAD = 100; // Handle 100 concurrent queries

        public MLOptimizationPerformanceTests(ITestOutputHelper output)
        {
            _output = output;
            _mockApiClient = new Mock<IQobuzApiClient>();
            _mockLogger = new Mock<ILogger<CompiledMLQueryOptimizer>>();
            _metrics = new PerformanceMetrics();
            
            _mlOptimizer = new CompiledMLQueryOptimizer(
                _mockApiClient.Object,
                _mockLogger.Object);

            SetupMockApiResponses();
        }

        private void SetupMockApiResponses()
        {
            // Setup mock API to track calls
            _mockApiClient.Setup(x => x.SearchAlbumsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync((string query, int limit, int offset) =>
                {
                    _metrics.ApiCallCount++;
                    return GenerateMockSearchResult(query);
                });
        }

        [Fact]
        [Trait("Priority", "Critical")]
        public async Task OptimizeQuery_ShouldAchieveTargetApiReduction()
        {
            // Arrange
            var testQueries = GenerateTestQueries(1000);
            var baselineApiCalls = 0;
            var optimizedApiCalls = 0;

            // Act - Baseline without optimization
            foreach (var query in testQueries)
            {
                _metrics.Reset();
                await SimulateUnoptimizedSearch(query);
                baselineApiCalls += _metrics.ApiCallCount;
            }

            // Act - With ML optimization
            _metrics.Reset();
            foreach (var query in testQueries)
            {
                await _mlOptimizer.OptimizeAndSearchAsync(query);
            }
            optimizedApiCalls = _metrics.ApiCallCount;

            // Calculate reduction
            var apiReduction = 1.0 - ((double)optimizedApiCalls / baselineApiCalls);

            // Assert
            apiReduction.Should().BeGreaterThanOrEqualTo(TARGET_API_CALL_REDUCTION,
                $"ML optimization should achieve at least {TARGET_API_CALL_REDUCTION:P0} API call reduction");

            _output.WriteLine($"API Call Reduction: {apiReduction:P2}");
            _output.WriteLine($"Baseline calls: {baselineApiCalls}, Optimized calls: {optimizedApiCalls}");
            _output.WriteLine($"Target met: {apiReduction >= TARGET_API_CALL_REDUCTION}");
        }

        [Fact]
        [Trait("Priority", "Critical")]
        public async Task QueryOptimization_ShouldMeetResponseTimeTargets()
        {
            // Arrange
            var testQueries = GenerateTestQueries(100);
            var responseTimes = new List<long>();

            // Act
            foreach (var query in testQueries)
            {
                var stopwatch = Stopwatch.StartNew();
                await _mlOptimizer.OptimizeAndSearchAsync(query);
                stopwatch.Stop();
                responseTimes.Add(stopwatch.ElapsedMilliseconds);
            }

            // Assert
            var avgResponseTime = responseTimes.Average();
            var p95ResponseTime = responseTimes.OrderBy(x => x).Skip((int)(responseTimes.Count * 0.95)).First();
            var maxResponseTime = responseTimes.Max();

            avgResponseTime.Should().BeLessThan(MAX_RESPONSE_TIME_MS,
                $"Average response time should be under {MAX_RESPONSE_TIME_MS}ms");
            p95ResponseTime.Should().BeLessThan(MAX_RESPONSE_TIME_MS * 2,
                "95th percentile should be under 2x target");

            _output.WriteLine($"Response Times - Avg: {avgResponseTime}ms, P95: {p95ResponseTime}ms, Max: {maxResponseTime}ms");
        }

        [Fact]
        [Trait("Priority", "High")]
        public async Task SmartCache_ShouldAchieveTargetHitRate()
        {
            // Arrange
            var queries = GenerateRealisticQueryPattern(500); // Mix of unique and repeated queries
            var cacheHits = 0;
            var totalQueries = queries.Count;

            // Act
            foreach (var query in queries)
            {
                var result = await _mlOptimizer.OptimizeAndSearchAsync(query);
                if (result.FromCache)
                {
                    cacheHits++;
                }
            }

            // Calculate hit rate
            var cacheHitRate = (double)cacheHits / totalQueries;

            // Assert
            cacheHitRate.Should().BeGreaterThanOrEqualTo(MIN_CACHE_HIT_RATE,
                $"Cache hit rate should be at least {MIN_CACHE_HIT_RATE:P0}");

            _output.WriteLine($"Cache Hit Rate: {cacheHitRate:P2} ({cacheHits}/{totalQueries})");
            _output.WriteLine($"Target met: {cacheHitRate >= MIN_CACHE_HIT_RATE}");
        }

        [Fact]
        [Trait("Priority", "High")]
        public async Task ConcurrentQueryOptimization_ShouldHandleLoad()
        {
            // Arrange
            var queries = GenerateTestQueries(CONCURRENT_QUERY_LOAD);
            var tasks = new List<Task<OptimizedSearchResult>>();
            var stopwatch = Stopwatch.StartNew();

            // Act - Submit all queries concurrently
            foreach (var query in queries)
            {
                tasks.Add(Task.Run(() => _mlOptimizer.OptimizeAndSearchAsync(query)));
            }

            var results = await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            results.Should().HaveCount(CONCURRENT_QUERY_LOAD);
            results.Should().AllSatisfy(r => r.Should().NotBeNull());
            
            var avgTimePerQuery = stopwatch.ElapsedMilliseconds / (double)CONCURRENT_QUERY_LOAD;
            avgTimePerQuery.Should().BeLessThan(MAX_RESPONSE_TIME_MS,
                $"Average time per query under load should be under {MAX_RESPONSE_TIME_MS}ms");

            _output.WriteLine($"Processed {CONCURRENT_QUERY_LOAD} concurrent queries in {stopwatch.ElapsedMilliseconds}ms");
            _output.WriteLine($"Average time per query: {avgTimePerQuery:F2}ms");
        }

        [Fact]
        [Trait("Priority", "High")]
        public async Task QueryClassification_ShouldCorrectlyIdentifyPatterns()
        {
            // Arrange
            var testCases = new Dictionary<string, QueryClassification>
            {
                { "Miles Davis Kind of Blue", QueryClassification.ArtistAlbum },
                { "Kind of Blue", QueryClassification.AlbumOnly },
                { "Miles Davis", QueryClassification.ArtistOnly },
                { "Jazz 1959", QueryClassification.GenreYear },
                { "Blue Note Records", QueryClassification.Label },
                { "Remastered 2013", QueryClassification.Edition },
                { "track:So What", QueryClassification.TrackSearch }
            };

            var correctClassifications = 0;

            // Act
            foreach (var testCase in testCases)
            {
                var classification = _mlOptimizer.ClassifyQuery(testCase.Key);
                if (classification == testCase.Value)
                {
                    correctClassifications++;
                }
                else
                {
                    _output.WriteLine($"Misclassification: '{testCase.Key}' classified as {classification}, expected {testCase.Value}");
                }
            }

            // Assert
            var accuracy = (double)correctClassifications / testCases.Count;
            accuracy.Should().BeGreaterThanOrEqualTo(0.8, "Query classification accuracy should be at least 80%");

            _output.WriteLine($"Classification Accuracy: {accuracy:P2} ({correctClassifications}/{testCases.Count})");
        }

        [Fact]
        [Trait("Priority", "Medium")]
        public async Task MemoryUsage_UnderLoad_ShouldRemainStable()
        {
            // Arrange
            var initialMemory = GC.GetTotalMemory(true);
            var memorySnapshots = new List<long>();
            var iterations = 10;
            var queriesPerIteration = 100;

            // Act
            for (int i = 0; i < iterations; i++)
            {
                var queries = GenerateTestQueries(queriesPerIteration);
                
                var tasks = queries.Select(q => _mlOptimizer.OptimizeAndSearchAsync(q)).ToArray();
                await Task.WhenAll(tasks);
                
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                var currentMemory = GC.GetTotalMemory(false);
                memorySnapshots.Add(currentMemory);
                
                await Task.Delay(100); // Brief pause between iterations
            }

            // Assert
            var finalMemory = memorySnapshots.Last();
            var memoryGrowth = finalMemory - initialMemory;
            var avgMemoryPerQuery = memoryGrowth / (iterations * queriesPerIteration);

            // Memory should not grow excessively
            avgMemoryPerQuery.Should().BeLessThan(10_000, "Average memory per query should be under 10KB");
            
            // Check for memory leaks (memory should stabilize)
            var lastHalfSnapshots = memorySnapshots.Skip(iterations / 2).ToList();
            var memoryVariance = lastHalfSnapshots.Max() - lastHalfSnapshots.Min();
            memoryVariance.Should().BeLessThan(50_000_000, "Memory variance should be under 50MB");

            _output.WriteLine($"Memory Growth: {memoryGrowth / 1_000_000.0:F2}MB");
            _output.WriteLine($"Avg per query: {avgMemoryPerQuery:N0} bytes");
            _output.WriteLine($"Memory variance: {memoryVariance / 1_000_000.0:F2}MB");
        }

        [Fact]
        [Trait("Priority", "Medium")]
        public async Task PatternLearning_ShouldImproveOverTime()
        {
            // Arrange
            var trainingQueries = GenerateTrainingSet(200);
            var testQueries = GenerateTestSet(50);
            
            // Measure baseline performance
            _metrics.Reset();
            foreach (var query in testQueries.Take(25))
            {
                await _mlOptimizer.OptimizeAndSearchAsync(query);
            }
            var baselineApiCalls = _metrics.ApiCallCount;

            // Act - Train the model with more queries
            foreach (var query in trainingQueries)
            {
                await _mlOptimizer.OptimizeAndSearchAsync(query);
                _mlOptimizer.UpdatePatternWeights(query, success: true);
            }

            // Measure improved performance
            _metrics.Reset();
            foreach (var query in testQueries.Skip(25))
            {
                await _mlOptimizer.OptimizeAndSearchAsync(query);
            }
            var improvedApiCalls = _metrics.ApiCallCount;

            // Assert
            var improvement = 1.0 - ((double)improvedApiCalls / baselineApiCalls);
            improvement.Should().BeGreaterThan(0.1, "Pattern learning should improve optimization by at least 10%");

            _output.WriteLine($"Performance Improvement: {improvement:P2}");
            _output.WriteLine($"Baseline API calls: {baselineApiCalls}, After training: {improvedApiCalls}");
        }

        [Fact]
        [Trait("Priority", "High")]
        public void CompiledModel_ShouldLoadSuccessfully()
        {
            // Arrange & Act
            var loadTime = Stopwatch.StartNew();
            var optimizer = new CompiledMLQueryOptimizer(
                _mockApiClient.Object,
                _mockLogger.Object);
            loadTime.Stop();

            // Assert
            optimizer.Should().NotBeNull();
            optimizer.IsModelLoaded.Should().BeTrue("Compiled model should load successfully");
            loadTime.ElapsedMilliseconds.Should().BeLessThan(1000, "Model should load in under 1 second");

            _output.WriteLine($"Model loaded in {loadTime.ElapsedMilliseconds}ms");
        }

        [Fact]
        [Trait("Priority", "Medium")]
        public async Task EdgeCaseQueries_ShouldHandleGracefully()
        {
            // Arrange
            var edgeCases = new[]
            {
                "", // Empty query
                " ", // Whitespace only
                "a", // Single character
                new string('x', 500), // Very long query
                "!!!@@@###$$$", // Special characters only
                "年代記", // Non-Latin characters
                "null", // Reserved words
                "<script>alert('xss')</script>", // Potential injection
                "SELECT * FROM albums", // SQL-like query
                string.Join(" ", Enumerable.Repeat("word", 100)) // Repetitive query
            };

            var failures = new List<string>();

            // Act
            foreach (var query in edgeCases)
            {
                try
                {
                    var result = await _mlOptimizer.OptimizeAndSearchAsync(query);
                    result.Should().NotBeNull();
                }
                catch (Exception ex)
                {
                    failures.Add($"Query '{query}' failed: {ex.Message}");
                }
            }

            // Assert
            failures.Should().BeEmpty("All edge case queries should be handled gracefully");
            
            if (failures.Any())
            {
                _output.WriteLine("Failed edge cases:");
                failures.ForEach(f => _output.WriteLine(f));
            }
            else
            {
                _output.WriteLine($"Successfully handled all {edgeCases.Length} edge cases");
            }
        }

        // Helper methods
        private List<string> GenerateTestQueries(int count)
        {
            var artists = new[] { "Miles Davis", "John Coltrane", "Bill Evans", "Herbie Hancock", "Wayne Shorter" };
            var albums = new[] { "Kind of Blue", "Giant Steps", "Waltz for Debby", "Head Hunters", "Speak No Evil" };
            var genres = new[] { "Jazz", "Bebop", "Fusion", "Modal", "Hard Bop" };
            var years = new[] { "1959", "1960", "1961", "1965", "1973" };

            var queries = new List<string>();
            var random = new Random(42); // Fixed seed for reproducibility

            for (int i = 0; i < count; i++)
            {
                var queryType = random.Next(5);
                string query = queryType switch
                {
                    0 => $"{artists[random.Next(artists.Length)]} {albums[random.Next(albums.Length)]}",
                    1 => albums[random.Next(albums.Length)],
                    2 => artists[random.Next(artists.Length)],
                    3 => $"{genres[random.Next(genres.Length)]} {years[random.Next(years.Length)]}",
                    _ => $"{artists[random.Next(artists.Length)]} {genres[random.Next(genres.Length)]}"
                };
                queries.Add(query);
            }

            return queries;
        }

        private List<string> GenerateRealisticQueryPattern(int count)
        {
            var queries = new List<string>();
            var popularQueries = GenerateTestQueries(20);
            var random = new Random(42);

            for (int i = 0; i < count; i++)
            {
                // 70% chance of repeated popular query (to test cache)
                if (random.NextDouble() < 0.7 && popularQueries.Any())
                {
                    queries.Add(popularQueries[random.Next(popularQueries.Count)]);
                }
                else
                {
                    // 30% chance of unique query
                    queries.Add($"Unique Query {Guid.NewGuid().ToString().Substring(0, 8)}");
                }
            }

            return queries;
        }

        private List<string> GenerateTrainingSet(int count)
        {
            return GenerateTestQueries(count);
        }

        private List<string> GenerateTestSet(int count)
        {
            return GenerateTestQueries(count);
        }

        private async Task<QobuzSearchResult> SimulateUnoptimizedSearch(string query)
        {
            // Simulate unoptimized search that makes multiple API calls
            await _mockApiClient.Object.SearchAlbumsAsync(query, 50, 0);
            await _mockApiClient.Object.SearchAlbumsAsync(query, 50, 50);
            await _mockApiClient.Object.SearchAlbumsAsync(query, 50, 100);
            
            return GenerateMockSearchResult(query);
        }

        private QobuzSearchResult GenerateMockSearchResult(string query)
        {
            return new QobuzSearchResult
            {
                Albums = new QobuzAlbumList
                {
                    Items = new List<QobuzAlbum>
                    {
                        new QobuzAlbum
                        {
                            Id = Guid.NewGuid().ToString(),
                            Title = $"Result for {query}",
                            Artist = new QobuzArtist { Name = "Test Artist" }
                        }
                    },
                    Total = 1
                }
            };
        }

        public void Dispose()
        {
            _mlOptimizer?.Dispose();
        }

        // Helper classes
        private class PerformanceMetrics
        {
            public int ApiCallCount { get; set; }
            public List<long> ResponseTimes { get; } = new List<long>();
            
            public void Reset()
            {
                ApiCallCount = 0;
                ResponseTimes.Clear();
            }
        }

        private class OptimizedSearchResult : QobuzSearchResult
        {
            public bool FromCache { get; set; }
        }

        private enum QueryClassification
        {
            ArtistAlbum,
            AlbumOnly,
            ArtistOnly,
            GenreYear,
            Label,
            Edition,
            TrackSearch
        }
    }
}