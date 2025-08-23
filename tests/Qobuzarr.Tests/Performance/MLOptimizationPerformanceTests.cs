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
using System.Text.Json;

namespace Qobuzarr.Tests.Performance
{
    /// <summary>
    /// Performance tests for ML query optimization
    /// Validates API call reduction targets, query classification accuracy, and performance baselines
    /// </summary>
    [Collection("Performance")]
    [Trait("Category", "Performance")]
    [Trait("Component", "MLOptimization")]
    public class MLOptimizationPerformanceTests
    {
        private readonly ITestOutputHelper _output;
        private readonly CompiledMLQueryOptimizer _optimizer;
        private readonly List<QueryPerformanceMetric> _performanceMetrics = new();
        
        // Target: 49% API call reduction as per requirements
        private const double TARGET_API_REDUCTION_PERCENTAGE = 49.0;
        private const double MINIMUM_API_REDUCTION_PERCENTAGE = 35.0;
        private const int MAX_QUERY_PROCESSING_TIME_MS = 50;

        public MLOptimizationPerformanceTests(ITestOutputHelper output)
        {
            _output = output;
            _optimizer = new CompiledMLQueryOptimizer();
        }

        #region API Call Reduction Tests

        [Fact]
        public void MLOptimizer_AchievesTargetApiReduction()
        {
            // Arrange - Test queries representing real-world usage patterns
            var testQueries = GenerateRealWorldQueries();
            var apiCallsWithoutML = 0;
            var apiCallsWithML = 0;
            
            // Act
            foreach (var query in testQueries)
            {
                var optimizationResult = _optimizer.OptimizeQuery(query);
                
                // Without ML: every query needs API call
                apiCallsWithoutML++;
                
                // With ML: only non-cacheable queries need API calls
                if (!optimizationResult.CanUseCachedResult)
                {
                    apiCallsWithML++;
                }
                
                _performanceMetrics.Add(new QueryPerformanceMetric
                {
                    Query = query,
                    WasOptimized = optimizationResult.CanUseCachedResult,
                    ProcessingTimeMs = optimizationResult.ProcessingTimeMs
                });
            }
            
            // Calculate reduction percentage
            var reductionPercentage = ((double)(apiCallsWithoutML - apiCallsWithML) / apiCallsWithoutML) * 100;
            
            // Assert
            reductionPercentage.Should().BeGreaterThanOrEqualTo(MINIMUM_API_REDUCTION_PERCENTAGE,
                $"ML optimization should achieve at least {MINIMUM_API_REDUCTION_PERCENTAGE}% API call reduction");
            
            reductionPercentage.Should().BeCloseTo(TARGET_API_REDUCTION_PERCENTAGE, 10,
                $"ML optimization should achieve close to target {TARGET_API_REDUCTION_PERCENTAGE}% reduction");
            
            _output.WriteLine($"API Call Reduction: {reductionPercentage:F1}%");
            _output.WriteLine($"Total queries: {testQueries.Count}, API calls needed: {apiCallsWithML}");
            _output.WriteLine($"Target: {TARGET_API_REDUCTION_PERCENTAGE}%, Achieved: {reductionPercentage:F1}%");
        }

        [Theory]
        [InlineData("Miles Davis Kind of Blue", true)]  // Common album
        [InlineData("Beatles Abbey Road", true)]         // Classic album
        [InlineData("Pink Floyd Dark Side", true)]       // Popular search
        [InlineData("obscure_artist_12345", false)]      // Rare query
        [InlineData("新しいアルバム 2024", false)]        // Non-Latin characters
        public void MLOptimizer_ClassifiesQueriesCorrectly(string query, bool expectedCacheable)
        {
            // Act
            var sw = Stopwatch.StartNew();
            var result = _optimizer.OptimizeQuery(query);
            sw.Stop();
            
            // Assert
            result.CanUseCachedResult.Should().Be(expectedCacheable,
                $"Query '{query}' should be classified as {(expectedCacheable ? "cacheable" : "non-cacheable")}");
            
            sw.ElapsedMilliseconds.Should().BeLessThan(MAX_QUERY_PROCESSING_TIME_MS,
                "Query classification should be fast");
            
            _output.WriteLine($"Query: '{query}' - Cacheable: {result.CanUseCachedResult}, Time: {sw.ElapsedMilliseconds}ms");
        }

        #endregion

        #region Query Classification Performance

        [Fact]
        public void MLOptimizer_ProcessingTime_MeetsPerformanceTargets()
        {
            // Arrange
            var queries = GenerateLargeQuerySet(1000);
            var processingTimes = new List<long>();
            
            // Act - Process all queries and measure time
            foreach (var query in queries)
            {
                var sw = Stopwatch.StartNew();
                _optimizer.OptimizeQuery(query);
                sw.Stop();
                processingTimes.Add(sw.ElapsedMilliseconds);
            }
            
            // Calculate statistics
            var avgTime = processingTimes.Average();
            var p50 = GetPercentile(processingTimes, 50);
            var p95 = GetPercentile(processingTimes, 95);
            var p99 = GetPercentile(processingTimes, 99);
            
            // Assert performance targets
            avgTime.Should().BeLessThan(10, "Average processing time should be under 10ms");
            p50.Should().BeLessThan(5, "Median processing time should be under 5ms");
            p95.Should().BeLessThan(25, "P95 processing time should be under 25ms");
            p99.Should().BeLessThan(50, "P99 processing time should be under 50ms");
            
            _output.WriteLine($"Processing Time Statistics (ms):");
            _output.WriteLine($"  Average: {avgTime:F2}");
            _output.WriteLine($"  P50: {p50}");
            _output.WriteLine($"  P95: {p95}");
            _output.WriteLine($"  P99: {p99}");
        }

        [Fact]
        public void MLOptimizer_ConcurrentQueries_HandledEfficiently()
        {
            // Arrange
            var queries = GenerateLargeQuerySet(100);
            var concurrentTasks = new List<Task<OptimizationResult>>();
            
            // Act - Process queries concurrently
            var sw = Stopwatch.StartNew();
            foreach (var query in queries)
            {
                concurrentTasks.Add(Task.Run(() => _optimizer.OptimizeQuery(query)));
            }
            
            var results = Task.WhenAll(concurrentTasks).Result;
            sw.Stop();
            
            // Assert
            results.Should().AllSatisfy(r => r.Should().NotBeNull());
            sw.ElapsedMilliseconds.Should().BeLessThan(1000,
                "100 concurrent queries should process in under 1 second");
            
            var throughput = (queries.Count / (sw.ElapsedMilliseconds / 1000.0));
            throughput.Should().BeGreaterThan(100, "Should process >100 queries/second");
            
            _output.WriteLine($"Concurrent processing: {queries.Count} queries in {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"Throughput: {throughput:F0} queries/second");
        }

        #endregion

        #region Pattern Learning Validation

        [Fact]
        public void MLOptimizer_LearnsFromQueryPatterns()
        {
            // Test that the optimizer improves with pattern recognition
            var artistQueries = new[]
            {
                "Miles Davis",
                "Miles Davis Quintet",
                "Miles Davis & John Coltrane",
                "The Miles Davis Sextet"
            };
            
            var optimizationResults = new List<bool>();
            
            // Process similar queries
            foreach (var query in artistQueries)
            {
                var result = _optimizer.OptimizeQuery(query);
                optimizationResults.Add(result.CanUseCachedResult);
                _output.WriteLine($"Query: '{query}' - Optimized: {result.CanUseCachedResult}");
            }
            
            // Should recognize pattern and optimize most queries
            var optimizedCount = optimizationResults.Count(r => r);
            optimizedCount.Should().BeGreaterThanOrEqualTo(artistQueries.Length - 1,
                "ML should learn patterns and optimize similar queries");
        }

        [Fact]
        public void MLOptimizer_HandlesQueryVariations()
        {
            // Test query normalization and variation handling
            var queryVariations = new[]
            {
                ("Dark Side of the Moon", "dark side of the moon"),
                ("The Beatles", "Beatles"),
                ("AC/DC", "AC DC"),
                ("Beyoncé", "Beyonce"),
                ("Björk", "Bjork")
            };
            
            foreach (var (query1, query2) in queryVariations)
            {
                var result1 = _optimizer.OptimizeQuery(query1);
                var result2 = _optimizer.OptimizeQuery(query2);
                
                // Similar queries should have similar optimization results
                result1.CanUseCachedResult.Should().Be(result2.CanUseCachedResult,
                    $"Variations of '{query1}' should be handled consistently");
                
                _output.WriteLine($"'{query1}' vs '{query2}': Both cacheable={result1.CanUseCachedResult}");
            }
        }

        #endregion

        #region Memory and Resource Usage

        [Fact]
        public void MLOptimizer_MemoryUsage_RemainsStable()
        {
            // Test memory usage doesn't grow excessively with query volume
            var initialMemory = GC.GetTotalMemory(true);
            
            // Process large number of queries
            for (int i = 0; i < 10000; i++)
            {
                _optimizer.OptimizeQuery($"Test Query {i}");
            }
            
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            var finalMemory = GC.GetTotalMemory(true);
            var memoryGrowth = finalMemory - initialMemory;
            
            // Memory growth should be minimal (< 10MB for 10k queries)
            memoryGrowth.Should().BeLessThan(10 * 1024 * 1024,
                "Memory usage should remain stable with high query volume");
            
            _output.WriteLine($"Memory growth after 10k queries: {memoryGrowth / 1024.0:F2} KB");
        }

        #endregion

        #region Regression Detection

        [Fact]
        public void MLOptimizer_PerformanceBaseline_NoRegression()
        {
            // Establish and validate performance baselines
            var baselineMetrics = new Dictionary<string, double>
            {
                ["ApiReduction"] = 49.0,
                ["AvgProcessingTime"] = 5.0,
                ["P95ProcessingTime"] = 25.0,
                ["Throughput"] = 200.0
            };
            
            // Run current performance tests
            var currentMetrics = MeasureCurrentPerformance();
            
            // Compare against baselines
            currentMetrics["ApiReduction"].Should().BeGreaterThanOrEqualTo(baselineMetrics["ApiReduction"] * 0.9,
                "API reduction should not regress more than 10%");
            
            currentMetrics["AvgProcessingTime"].Should().BeLessThanOrEqualTo(baselineMetrics["AvgProcessingTime"] * 1.2,
                "Processing time should not increase more than 20%");
            
            currentMetrics["Throughput"].Should().BeGreaterThanOrEqualTo(baselineMetrics["Throughput"] * 0.8,
                "Throughput should not decrease more than 20%");
            
            _output.WriteLine("Performance Baseline Comparison:");
            foreach (var metric in currentMetrics)
            {
                var baseline = baselineMetrics.GetValueOrDefault(metric.Key, 0);
                var change = ((metric.Value - baseline) / baseline) * 100;
                _output.WriteLine($"  {metric.Key}: Current={metric.Value:F2}, Baseline={baseline:F2}, Change={change:+0.0;-0.0}%");
            }
        }

        #endregion

        #region Helper Methods

        private List<string> GenerateRealWorldQueries()
        {
            // Generate queries based on real usage patterns
            var queries = new List<string>();
            
            // Common artist searches (should be optimized)
            queries.AddRange(new[] {
                "Taylor Swift", "Drake", "The Beatles", "Queen", "Pink Floyd",
                "Led Zeppelin", "Michael Jackson", "Madonna", "U2", "Radiohead"
            });
            
            // Album searches (should be optimized)
            queries.AddRange(new[] {
                "Abbey Road", "Dark Side of the Moon", "Thriller", "Back in Black",
                "Rumours", "Hotel California", "The Wall", "Born to Run"
            });
            
            // Specific track searches (mixed optimization)
            queries.AddRange(new[] {
                "Bohemian Rhapsody", "Stairway to Heaven", "Imagine", "Hey Jude",
                "Smells Like Teen Spirit", "Billie Jean", "Like a Rolling Stone"
            });
            
            // Rare/specific searches (should not be optimized)
            queries.AddRange(new[] {
                "underground indie band 2024", "local artist demo tape",
                "specific remix version 12 inch", "bootleg concert recording 1973"
            });
            
            // Repeat some common queries to simulate real patterns
            queries.AddRange(Enumerable.Repeat("Miles Davis", 5));
            queries.AddRange(Enumerable.Repeat("Beatles", 3));
            
            return queries;
        }

        private List<string> GenerateLargeQuerySet(int count)
        {
            var queries = new List<string>();
            var artists = new[] { "Beatles", "Queen", "Pink Floyd", "Led Zeppelin", "Nirvana" };
            var albums = new[] { "Greatest Hits", "Live Album", "Studio Sessions", "Unplugged", "Remastered" };
            
            var random = new Random(42); // Fixed seed for reproducibility
            
            for (int i = 0; i < count; i++)
            {
                var queryType = random.Next(3);
                var query = queryType switch
                {
                    0 => artists[random.Next(artists.Length)],
                    1 => $"{artists[random.Next(artists.Length)]} {albums[random.Next(albums.Length)]}",
                    _ => $"Random Query {i}"
                };
                queries.Add(query);
            }
            
            return queries;
        }

        private double GetPercentile(List<long> values, int percentile)
        {
            var sortedValues = values.OrderBy(v => v).ToList();
            var index = (int)Math.Ceiling((percentile / 100.0) * sortedValues.Count) - 1;
            return sortedValues[Math.Max(0, Math.Min(index, sortedValues.Count - 1))];
        }

        private Dictionary<string, double> MeasureCurrentPerformance()
        {
            var metrics = new Dictionary<string, double>();
            
            // Measure API reduction
            var queries = GenerateRealWorldQueries();
            var optimized = queries.Count(q => _optimizer.OptimizeQuery(q).CanUseCachedResult);
            metrics["ApiReduction"] = (optimized / (double)queries.Count) * 100;
            
            // Measure processing times
            var times = new List<long>();
            foreach (var query in queries.Take(100))
            {
                var sw = Stopwatch.StartNew();
                _optimizer.OptimizeQuery(query);
                sw.Stop();
                times.Add(sw.ElapsedMilliseconds);
            }
            
            metrics["AvgProcessingTime"] = times.Average();
            metrics["P95ProcessingTime"] = GetPercentile(times, 95);
            
            // Measure throughput
            var sw2 = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++)
            {
                _optimizer.OptimizeQuery($"Query {i}");
            }
            sw2.Stop();
            metrics["Throughput"] = 100.0 / (sw2.ElapsedMilliseconds / 1000.0);
            
            return metrics;
        }

        #endregion

        private class QueryPerformanceMetric
        {
            public string Query { get; set; }
            public bool WasOptimized { get; set; }
            public long ProcessingTimeMs { get; set; }
        }

        private class OptimizationResult
        {
            public bool CanUseCachedResult { get; set; }
            public long ProcessingTimeMs { get; set; }
        }
    }
}