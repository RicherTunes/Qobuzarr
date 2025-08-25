using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using NLog;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Services;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Comprehensive ML performance benchmark suite
    /// Validates the 49.83% API reduction claim and identifies optimization opportunities
    /// </summary>
    public class MLPerformanceBenchmarkTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly CompiledMLQueryOptimizer _optimizer;
        private readonly MLPerformanceMetrics _metrics;
        private readonly List<BenchmarkResult> _results = new();
        private readonly Logger _logger;
        
        // Target performance metrics
        private const double TARGET_API_REDUCTION = 49.83;
        private const double TARGET_CACHE_HIT_RATE = 25.0;
        private const double TARGET_PREDICTION_ACCURACY = 87.3;
        private const double TARGET_PREDICTION_LATENCY_MS = 10.0;
        private const double TARGET_MEMORY_USAGE_MB = 10.0;
        
        public MLPerformanceBenchmarkTests(ITestOutputHelper output)
        {
            _output = output;
            _logger = LogManager.GetCurrentClassLogger();
            _metrics = new MLPerformanceMetrics(_logger);
            _optimizer = new CompiledMLQueryOptimizer(_logger);
        }
        
        [Fact]
        public async Task Benchmark_APICallReduction_MeetsTarget()
        {
            // Arrange
            var testQueries = GenerateRealWorldQueries(10000);
            var baseline = new NoOptimizationStrategy();
            var optimized = new MLOptimizedStrategy(_optimizer);
            
            // Act
            var baselineResults = await RunStrategy(baseline, testQueries, "Baseline");
            var optimizedResults = await RunStrategy(optimized, testQueries, "ML-Optimized");
            
            // Calculate reduction
            var reduction = CalculateAPIReduction(baselineResults, optimizedResults);
            
            // Assert
            _output.WriteLine($"API Call Reduction: {reduction:F2}% (Target: {TARGET_API_REDUCTION}%)");
            _output.WriteLine($"Baseline API Calls: {baselineResults.TotalAPICalls}");
            _output.WriteLine($"Optimized API Calls: {optimizedResults.TotalAPICalls}");
            _output.WriteLine($"Calls Saved: {baselineResults.TotalAPICalls - optimizedResults.TotalAPICalls}");
            
            Assert.True(reduction >= TARGET_API_REDUCTION * 0.95, // Allow 5% tolerance
                $"API reduction {reduction:F2}% below target {TARGET_API_REDUCTION}%");
            
            // Record for reporting
            _results.Add(new BenchmarkResult
            {
                TestName = "API Call Reduction",
                Target = TARGET_API_REDUCTION,
                Actual = reduction,
                Passed = reduction >= TARGET_API_REDUCTION * 0.95,
                Details = new Dictionary<string, object>
                {
                    ["BaselineCalls"] = baselineResults.TotalAPICalls,
                    ["OptimizedCalls"] = optimizedResults.TotalAPICalls,
                    ["QueryDistribution"] = optimizedResults.ComplexityDistribution
                }
            });
        }
        
        [Fact]
        public async Task Benchmark_PredictionLatency_MeetsTarget()
        {
            // Arrange
            var testQueries = GenerateRealWorldQueries(1000);
            var latencies = new List<double>();
            
            // Warm up
            foreach (var query in testQueries.Take(100))
            {
                _optimizer.PredictComplexity(query.Artist, query.Album);
            }
            
            // Act - Measure latency
            foreach (var query in testQueries)
            {
                var sw = Stopwatch.StartNew();
                var complexity = _optimizer.PredictComplexity(query.Artist, query.Album);
                sw.Stop();
                
                latencies.Add(sw.Elapsed.TotalMilliseconds);
                
                // Simulate result recording
                _optimizer.RecordResult(query.Artist, query.Album, complexity, true);
            }
            
            // Calculate statistics
            var avgLatency = latencies.Average();
            var p95Latency = CalculatePercentile(latencies, 0.95);
            var p99Latency = CalculatePercentile(latencies, 0.99);
            
            // Assert
            _output.WriteLine($"Average Latency: {avgLatency:F2}ms (Target: <{TARGET_PREDICTION_LATENCY_MS}ms)");
            _output.WriteLine($"P95 Latency: {p95Latency:F2}ms");
            _output.WriteLine($"P99 Latency: {p99Latency:F2}ms");
            
            Assert.True(avgLatency <= TARGET_PREDICTION_LATENCY_MS,
                $"Average latency {avgLatency:F2}ms exceeds target {TARGET_PREDICTION_LATENCY_MS}ms");
            
            _results.Add(new BenchmarkResult
            {
                TestName = "Prediction Latency",
                Target = TARGET_PREDICTION_LATENCY_MS,
                Actual = avgLatency,
                Passed = avgLatency <= TARGET_PREDICTION_LATENCY_MS,
                Details = new Dictionary<string, object>
                {
                    ["P50"] = CalculatePercentile(latencies, 0.50),
                    ["P95"] = p95Latency,
                    ["P99"] = p99Latency,
                    ["Min"] = latencies.Min(),
                    ["Max"] = latencies.Max()
                }
            });
        }
        
        [Fact]
        public async Task Benchmark_PredictionAccuracy_MeetsTarget()
        {
            // Arrange
            var validationSet = GenerateLabeledQueries(1000);
            int correct = 0;
            int total = 0;
            
            // Act
            foreach (var (query, expectedComplexity) in validationSet)
            {
                var predicted = _optimizer.PredictComplexity(query.Artist, query.Album);
                
                if (predicted == expectedComplexity)
                    correct++;
                
                total++;
                
                // Record for learning
                _optimizer.RecordResult(query.Artist, query.Album, expectedComplexity, true);
            }
            
            var accuracy = (double)correct / total * 100;
            
            // Assert
            _output.WriteLine($"Prediction Accuracy: {accuracy:F1}% (Target: {TARGET_PREDICTION_ACCURACY}%)");
            _output.WriteLine($"Correct: {correct}/{total}");
            
            // Get confusion matrix
            var stats = _optimizer.GetStatistics();
            _output.WriteLine($"Distribution: Simple={stats.PatternDistribution[QueryComplexity.Simple]}, " +
                            $"Medium={stats.PatternDistribution[QueryComplexity.Medium]}, " +
                            $"Complex={stats.PatternDistribution[QueryComplexity.Complex]}");
            
            Assert.True(accuracy >= TARGET_PREDICTION_ACCURACY * 0.95,
                $"Accuracy {accuracy:F1}% below target {TARGET_PREDICTION_ACCURACY}%");
            
            _results.Add(new BenchmarkResult
            {
                TestName = "Prediction Accuracy",
                Target = TARGET_PREDICTION_ACCURACY,
                Actual = accuracy,
                Passed = accuracy >= TARGET_PREDICTION_ACCURACY * 0.95,
                Details = stats.HybridStatistics
            });
        }
        
        [Fact]
        public async Task Benchmark_MemoryEfficiency_MeetsTarget()
        {
            // Arrange
            var initialMemory = GC.GetTotalMemory(true) / 1024.0 / 1024.0; // MB
            var testQueries = GenerateRealWorldQueries(5000);
            
            // Act - Process queries and measure memory
            var memorySnapshots = new List<double>();
            
            for (int i = 0; i < testQueries.Count; i++)
            {
                var query = testQueries[i];
                _optimizer.PredictComplexity(query.Artist, query.Album);
                
                if (i % 100 == 0)
                {
                    var currentMemory = GC.GetTotalMemory(false) / 1024.0 / 1024.0;
                    var memoryUsed = currentMemory - initialMemory;
                    memorySnapshots.Add(memoryUsed);
                }
            }
            
            var peakMemory = memorySnapshots.Max();
            var avgMemory = memorySnapshots.Average();
            
            // Force cleanup and measure
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            var finalMemory = GC.GetTotalMemory(false) / 1024.0 / 1024.0;
            var memoryLeak = finalMemory - initialMemory;
            
            // Assert
            _output.WriteLine($"Peak Memory Usage: {peakMemory:F2}MB (Target: <{TARGET_MEMORY_USAGE_MB}MB)");
            _output.WriteLine($"Average Memory: {avgMemory:F2}MB");
            _output.WriteLine($"Memory Leak: {memoryLeak:F2}MB");
            
            Assert.True(peakMemory <= TARGET_MEMORY_USAGE_MB * 1.5, // Allow 50% buffer
                $"Peak memory {peakMemory:F2}MB exceeds target {TARGET_MEMORY_USAGE_MB}MB");
            
            Assert.True(memoryLeak <= 1.0,
                $"Memory leak detected: {memoryLeak:F2}MB retained after cleanup");
            
            _results.Add(new BenchmarkResult
            {
                TestName = "Memory Efficiency",
                Target = TARGET_MEMORY_USAGE_MB,
                Actual = peakMemory,
                Passed = peakMemory <= TARGET_MEMORY_USAGE_MB * 1.5,
                Details = new Dictionary<string, object>
                {
                    ["PeakMemoryMB"] = peakMemory,
                    ["AverageMemoryMB"] = avgMemory,
                    ["MemoryLeakMB"] = memoryLeak,
                    ["QueriesProcessed"] = testQueries.Count
                }
            });
        }
        
        [Fact]
        public async Task Benchmark_ConcurrentPerformance_ThreadSafe()
        {
            // Arrange
            var testQueries = GenerateRealWorldQueries(10000);
            var tasks = new List<Task<double>>();
            var concurrency = 10;
            
            // Act - Run concurrent predictions
            var sw = Stopwatch.StartNew();
            
            for (int i = 0; i < concurrency; i++)
            {
                int threadId = i;
                tasks.Add(Task.Run(() => ProcessQueriesConcurrently(testQueries, threadId)));
            }
            
            var latencies = await Task.WhenAll(tasks);
            sw.Stop();
            
            var totalThroughput = testQueries.Count * concurrency / sw.Elapsed.TotalSeconds;
            var avgLatency = latencies.Average();
            
            // Assert
            _output.WriteLine($"Concurrent Throughput: {totalThroughput:F0} queries/sec");
            _output.WriteLine($"Average Thread Latency: {avgLatency:F2}ms");
            _output.WriteLine($"Total Time: {sw.Elapsed.TotalSeconds:F2}s");
            
            Assert.True(avgLatency <= TARGET_PREDICTION_LATENCY_MS * 2,
                $"Concurrent latency {avgLatency:F2}ms too high");
            
            _results.Add(new BenchmarkResult
            {
                TestName = "Concurrent Performance",
                Target = 1000, // queries/sec
                Actual = totalThroughput,
                Passed = totalThroughput >= 1000,
                Details = new Dictionary<string, object>
                {
                    ["Concurrency"] = concurrency,
                    ["TotalQueries"] = testQueries.Count * concurrency,
                    ["ThroughputPerSec"] = totalThroughput,
                    ["AvgThreadLatency"] = avgLatency
                }
            });
        }
        
        [Fact]
        public async Task Benchmark_CacheFusion_ImprovedHitRate()
        {
            // Arrange
            var patternCache = new QobuzPatternCache(_logger);
            var substringCache = new QobuzSubstringCache(_logger);
            var fusionStrategy = new EnhancedCacheFusionStrategy(
                patternCache, substringCache, _metrics, _logger);
            
            var testQueries = GenerateRealWorldQueries(1000);
            int hits = 0;
            int misses = 0;
            
            // Act - First pass to populate cache
            foreach (var query in testQueries)
            {
                var complexity = _optimizer.PredictComplexity(query.Artist, query.Album);
                var result = await fusionStrategy.TryGetAsync(query.Artist, query.Album, complexity);
                
                if (!result.Found)
                {
                    // Simulate storing after API call
                    var fakeResults = GenerateFakeResults(query);
                    await fusionStrategy.StoreAsync(query.Artist, query.Album, fakeResults, complexity);
                }
            }
            
            // Second pass to measure hit rate
            foreach (var query in testQueries)
            {
                var complexity = _optimizer.PredictComplexity(query.Artist, query.Album);
                var result = await fusionStrategy.TryGetAsync(query.Artist, query.Album, complexity);
                
                if (result.Found)
                    hits++;
                else
                    misses++;
            }
            
            var hitRate = (double)hits / (hits + misses) * 100;
            var stats = fusionStrategy.GetStatistics();
            
            // Assert
            _output.WriteLine($"Cache Hit Rate: {hitRate:F1}% (Target: >{TARGET_CACHE_HIT_RATE}%)");
            _output.WriteLine($"Layer 1 (Pattern): {stats.Layer1HitRate * 100:F1}%");
            _output.WriteLine($"Layer 2 (Substring): {stats.Layer2HitRate * 100:F1}%");
            _output.WriteLine($"Layer 3 (Semantic): {stats.Layer3HitRate * 100:F1}%");
            _output.WriteLine($"Layer 4 (Temporal): {stats.Layer4HitRate * 100:F1}%");
            _output.WriteLine($"Layer 5 (Collaborative): {stats.Layer5HitRate * 100:F1}%");
            
            Assert.True(hitRate >= TARGET_CACHE_HIT_RATE,
                $"Cache hit rate {hitRate:F1}% below target {TARGET_CACHE_HIT_RATE}%");
            
            _results.Add(new BenchmarkResult
            {
                TestName = "Cache Fusion Hit Rate",
                Target = TARGET_CACHE_HIT_RATE,
                Actual = hitRate,
                Passed = hitRate >= TARGET_CACHE_HIT_RATE,
                Details = new Dictionary<string, object>
                {
                    ["OverallHitRate"] = stats.OverallHitRate,
                    ["Layer1HitRate"] = stats.Layer1HitRate,
                    ["Layer2HitRate"] = stats.Layer2HitRate,
                    ["Layer3HitRate"] = stats.Layer3HitRate,
                    ["Layer4HitRate"] = stats.Layer4HitRate,
                    ["Layer5HitRate"] = stats.Layer5HitRate
                }
            });
            
            fusionStrategy.Dispose();
        }
        
        [Fact]
        public void GenerateBenchmarkReport()
        {
            // Generate comprehensive benchmark report
            _output.WriteLine("\n========== ML PERFORMANCE BENCHMARK REPORT ==========\n");
            
            var passedTests = _results.Count(r => r.Passed);
            var totalTests = _results.Count;
            var overallSuccess = (double)passedTests / totalTests * 100;
            
            _output.WriteLine($"Overall Success Rate: {overallSuccess:F1}% ({passedTests}/{totalTests} tests passed)\n");
            
            foreach (var result in _results)
            {
                var status = result.Passed ? "✓ PASS" : "✗ FAIL";
                var performance = result.Actual / result.Target * 100;
                
                _output.WriteLine($"{status} - {result.TestName}");
                _output.WriteLine($"  Target: {result.Target:F2}");
                _output.WriteLine($"  Actual: {result.Actual:F2}");
                _output.WriteLine($"  Performance: {performance:F1}% of target");
                
                if (result.Details != null && result.Details.Any())
                {
                    _output.WriteLine("  Details:");
                    foreach (var detail in result.Details)
                    {
                        _output.WriteLine($"    {detail.Key}: {detail.Value}");
                    }
                }
                
                _output.WriteLine();
            }
            
            // Optimization recommendations
            _output.WriteLine("========== OPTIMIZATION RECOMMENDATIONS ==========\n");
            
            if (_results.Any(r => !r.Passed))
            {
                _output.WriteLine("Failed benchmarks require immediate attention:");
                foreach (var failed in _results.Where(r => !r.Passed))
                {
                    var deficit = (failed.Target - failed.Actual) / failed.Target * 100;
                    _output.WriteLine($"- {failed.TestName}: {deficit:F1}% below target");
                }
            }
            else
            {
                _output.WriteLine("All benchmarks passed! Consider:");
                _output.WriteLine("- Increasing targets for next iteration");
                _output.WriteLine("- Adding more complex test scenarios");
                _output.WriteLine("- Testing with production data");
            }
        }
        
        #region Helper Methods
        
        private List<TestQuery> GenerateRealWorldQueries(int count)
        {
            var queries = new List<TestQuery>();
            var random = new Random(42); // Deterministic for reproducibility
            
            var artists = new[] { "Miles Davis", "The Beatles", "Pink Floyd", "Led Zeppelin", 
                                  "Nirvana", "Radiohead", "Queen", "David Bowie", 
                                  "Bob Dylan", "The Rolling Stones" };
            
            var albumPatterns = new[] { "{0}", "{0} (Deluxe Edition)", "{0} [Remastered]", 
                                       "{0} - Live", "{0} Vol. 1", "Greatest Hits", 
                                       "The Best of {0}", "{0} (Anniversary Edition)" };
            
            for (int i = 0; i < count; i++)
            {
                var artist = artists[random.Next(artists.Length)];
                var pattern = albumPatterns[random.Next(albumPatterns.Length)];
                var album = string.Format(pattern, $"Album {i % 100}");
                
                queries.Add(new TestQuery { Artist = artist, Album = album });
            }
            
            return queries;
        }
        
        private List<(TestQuery query, QueryComplexity expected)> GenerateLabeledQueries(int count)
        {
            var labeled = new List<(TestQuery, QueryComplexity)>();
            
            // Simple queries
            for (int i = 0; i < count / 3; i++)
            {
                labeled.Add((
                    new TestQuery { Artist = "Queen", Album = $"Album {i}" },
                    QueryComplexity.Simple
                ));
            }
            
            // Medium queries
            for (int i = 0; i < count / 3; i++)
            {
                labeled.Add((
                    new TestQuery { Artist = "Various Artists", Album = $"Compilation {i}" },
                    QueryComplexity.Medium
                ));
            }
            
            // Complex queries
            for (int i = 0; i < count / 3; i++)
            {
                labeled.Add((
                    new TestQuery { Artist = "London Symphony Orchestra", Album = $"Symphony No. {i} in D Minor, Op. {i*10}" },
                    QueryComplexity.Complex
                ));
            }
            
            return labeled;
        }
        
        private async Task<StrategyResult> RunStrategy(ISearchStrategy strategy, List<TestQuery> queries, string name)
        {
            var result = new StrategyResult { StrategyName = name };
            var complexityCount = new Dictionary<QueryComplexity, int>();
            
            foreach (var query in queries)
            {
                var apiCalls = await strategy.ExecuteSearch(query.Artist, query.Album);
                result.TotalAPICalls += apiCalls;
                
                var complexity = _optimizer.PredictComplexity(query.Artist, query.Album);
                complexityCount[complexity] = complexityCount.GetValueOrDefault(complexity) + 1;
            }
            
            result.ComplexityDistribution = complexityCount;
            return result;
        }
        
        private double CalculateAPIReduction(StrategyResult baseline, StrategyResult optimized)
        {
            var saved = baseline.TotalAPICalls - optimized.TotalAPICalls;
            return (double)saved / baseline.TotalAPICalls * 100;
        }
        
        private double CalculatePercentile(List<double> values, double percentile)
        {
            var sorted = values.OrderBy(v => v).ToList();
            var index = (int)Math.Ceiling(sorted.Count * percentile) - 1;
            return sorted[Math.Max(0, Math.Min(index, sorted.Count - 1))];
        }
        
        private async Task<double> ProcessQueriesConcurrently(List<TestQuery> queries, int threadId)
        {
            var latencies = new List<double>();
            
            foreach (var query in queries)
            {
                var sw = Stopwatch.StartNew();
                _optimizer.PredictComplexity(query.Artist, query.Album);
                sw.Stop();
                
                latencies.Add(sw.Elapsed.TotalMilliseconds);
                
                // Simulate some work
                await Task.Delay(1);
            }
            
            return latencies.Average();
        }
        
        private List<QobuzSearchResult> GenerateFakeResults(TestQuery query)
        {
            return new List<QobuzSearchResult>
            {
                new QobuzSearchResult
                {
                    Artist = query.Artist,
                    Album = query.Album,
                    ReleaseDate = DateTime.UtcNow.AddDays(-30),
                    Genre = "Rock"
                }
            };
        }
        
        #endregion
        
        #region Test Infrastructure
        
        private class TestQuery
        {
            public string Artist { get; set; }
            public string Album { get; set; }
        }
        
        private class BenchmarkResult
        {
            public string TestName { get; set; }
            public double Target { get; set; }
            public double Actual { get; set; }
            public bool Passed { get; set; }
            public Dictionary<string, object> Details { get; set; }
        }
        
        private class StrategyResult
        {
            public string StrategyName { get; set; }
            public int TotalAPICalls { get; set; }
            public Dictionary<QueryComplexity, int> ComplexityDistribution { get; set; }
        }
        
        private interface ISearchStrategy
        {
            Task<int> ExecuteSearch(string artist, string album);
        }
        
        private class NoOptimizationStrategy : ISearchStrategy
        {
            public Task<int> ExecuteSearch(string artist, string album)
            {
                // Always uses 3 API calls (exact, fuzzy, partial)
                return Task.FromResult(3);
            }
        }
        
        private class MLOptimizedStrategy : ISearchStrategy
        {
            private readonly CompiledMLQueryOptimizer _optimizer;
            
            public MLOptimizedStrategy(CompiledMLQueryOptimizer optimizer)
            {
                _optimizer = optimizer;
            }
            
            public Task<int> ExecuteSearch(string artist, string album)
            {
                var complexity = _optimizer.PredictComplexity(artist, album);
                
                return Task.FromResult(complexity switch
                {
                    QueryComplexity.Simple => 1,  // 66.7% reduction
                    QueryComplexity.Medium => 2,  // 33.3% reduction
                    QueryComplexity.Complex => 3, // 0% reduction
                    _ => 3
                });
            }
        }
        
        private class QobuzSearchResult
        {
            public string Artist { get; set; }
            public string Album { get; set; }
            public DateTime ReleaseDate { get; set; }
            public string Genre { get; set; }
        }
        
        #endregion
        
        public void Dispose()
        {
            _optimizer?.Dispose();
            _metrics?.Dispose();
        }
    }
}