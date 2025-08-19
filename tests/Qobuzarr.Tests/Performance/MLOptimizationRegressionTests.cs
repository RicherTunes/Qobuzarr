using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Indexers;
using NLog;
using Xunit;
using Xunit.Abstractions;
using Newtonsoft.Json;

namespace Qobuzarr.Tests.Performance
{
    /// <summary>
    /// Performance regression tests for ML optimization
    /// Validates that ML models meet performance targets for API call reduction
    /// </summary>
    [Collection("Performance")]
    [Trait("Category", "Performance")]
    public class MLOptimizationRegressionTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly CompiledMLQueryOptimizer _optimizer;
        private readonly HybridMLQueryOptimizer _hybridOptimizer;
        private readonly MLPerformanceMetrics _metrics;
        private readonly List<QueryTestCase> _productionQueries;
        private readonly Logger _logger;

        // Performance targets from requirements
        private const double TARGET_API_REDUCTION = 49.0; // 49% minimum API call reduction
        private const double TARGET_ACCURACY = 87.0; // 87% classification accuracy
        private const double TARGET_CACHE_HIT_RATIO = 60.0; // 60% cache hit ratio
        private const int TARGET_PREDICTION_TIME_MS = 10; // 10ms p95 latency
        private const int TARGET_MEMORY_MB = 50; // 50MB maximum memory overhead

        public MLOptimizationRegressionTests(ITestOutputHelper output)
        {
            _output = output;
            _logger = LogManager.GetCurrentClassLogger();
            _metrics = new MLPerformanceMetrics(_logger);
            _optimizer = new CompiledMLQueryOptimizer();
            _hybridOptimizer = new HybridMLQueryOptimizer(_logger);
            _productionQueries = LoadProductionQueries();
        }

        [Fact]
        public async Task MLOptimization_AchievesTargetApiReduction()
        {
            // Arrange
            var totalApiCalls = 0;
            var savedApiCalls = 0;
            var stopwatch = new Stopwatch();

            // Act - Test with production query samples
            foreach (var testCase in _productionQueries)
            {
                stopwatch.Restart();
                
                var optimizedQuery = await _optimizer.OptimizeQueryAsync(testCase.Query);
                
                stopwatch.Stop();
                
                // Simulate API calls based on optimization
                var baselineCalls = testCase.ExpectedApiCalls;
                var optimizedCalls = CalculateOptimizedApiCalls(optimizedQuery, testCase);
                
                totalApiCalls += baselineCalls;
                savedApiCalls += (baselineCalls - optimizedCalls);
                
                _metrics.RecordApiOptimization(baselineCalls - optimizedCalls, baselineCalls);
                _metrics.RecordPrediction(stopwatch.ElapsedMilliseconds, 
                    optimizedCalls < baselineCalls, 
                    CalculateConfidence(optimizedQuery));
            }

            // Assert
            var actualReduction = (savedApiCalls / (double)totalApiCalls) * 100;
            
            actualReduction.Should().BeGreaterOrEqualTo(TARGET_API_REDUCTION,
                $"ML optimization must achieve {TARGET_API_REDUCTION}% API call reduction, but achieved {actualReduction:F1}%");
            
            // Log detailed results
            _output.WriteLine($"API Call Reduction Results:");
            _output.WriteLine($"  Total API calls (baseline): {totalApiCalls}");
            _output.WriteLine($"  API calls saved: {savedApiCalls}");
            _output.WriteLine($"  Reduction percentage: {actualReduction:F1}%");
            _output.WriteLine($"  Target: {TARGET_API_REDUCTION}%");
            _output.WriteLine($"  Status: {(actualReduction >= TARGET_API_REDUCTION ? "PASS ✓" : "FAIL ✗")}");
        }

        [Fact]
        public void MLClassification_MeetsAccuracyTarget()
        {
            // Arrange
            var correctPredictions = 0;
            var totalPredictions = _productionQueries.Count;

            // Act - Test classification accuracy
            foreach (var testCase in _productionQueries)
            {
                var classifier = new QueryComplexityClassifier();
                var complexity = classifier.ClassifyComplexity(testCase.Query);
                
                var strategy = new SmartQueryStrategy();
                var result = strategy.DetermineSearchApproach(testCase.Query, complexity);
                
                // Check if classification was correct
                if (IsClassificationCorrect(result, testCase))
                {
                    correctPredictions++;
                }
            }

            // Assert
            var accuracy = (correctPredictions / (double)totalPredictions) * 100;
            
            accuracy.Should().BeGreaterOrEqualTo(TARGET_ACCURACY,
                $"ML classification must achieve {TARGET_ACCURACY}% accuracy, but achieved {accuracy:F1}%");
            
            _output.WriteLine($"Classification Accuracy Results:");
            _output.WriteLine($"  Total predictions: {totalPredictions}");
            _output.WriteLine($"  Correct predictions: {correctPredictions}");
            _output.WriteLine($"  Accuracy: {accuracy:F1}%");
            _output.WriteLine($"  Target: {TARGET_ACCURACY}%");
            _output.WriteLine($"  Status: {(accuracy >= TARGET_ACCURACY ? "PASS ✓" : "FAIL ✗")}");
        }

        [Fact]
        public async Task PredictionLatency_MeetsPerformanceTarget()
        {
            // Arrange
            var latencies = new List<double>();
            var stopwatch = new Stopwatch();

            // Warm up the model
            for (int i = 0; i < 10; i++)
            {
                await _optimizer.OptimizeQueryAsync("warmup query");
            }

            // Act - Measure prediction latencies
            foreach (var testCase in _productionQueries)
            {
                stopwatch.Restart();
                
                await _optimizer.OptimizeQueryAsync(testCase.Query);
                
                stopwatch.Stop();
                latencies.Add(stopwatch.ElapsedMilliseconds);
            }

            // Calculate p95 latency
            latencies.Sort();
            var p95Index = (int)(latencies.Count * 0.95);
            var p95Latency = latencies[Math.Min(p95Index, latencies.Count - 1)];

            // Assert
            p95Latency.Should().BeLessOrEqualTo(TARGET_PREDICTION_TIME_MS,
                $"P95 prediction latency must be under {TARGET_PREDICTION_TIME_MS}ms, but was {p95Latency:F1}ms");
            
            _output.WriteLine($"Prediction Latency Results:");
            _output.WriteLine($"  Samples: {latencies.Count}");
            _output.WriteLine($"  Min: {latencies.Min():F1}ms");
            _output.WriteLine($"  Max: {latencies.Max():F1}ms");
            _output.WriteLine($"  Average: {latencies.Average():F1}ms");
            _output.WriteLine($"  P50: {latencies[latencies.Count / 2]:F1}ms");
            _output.WriteLine($"  P95: {p95Latency:F1}ms");
            _output.WriteLine($"  Target P95: {TARGET_PREDICTION_TIME_MS}ms");
            _output.WriteLine($"  Status: {(p95Latency <= TARGET_PREDICTION_TIME_MS ? "PASS ✓" : "FAIL ✗")}");
        }

        [Fact]
        public void CacheHitRatio_MeetsEfficiencyTarget()
        {
            // Arrange
            var cache = new QobuzPatternCache();
            var substringCache = new QobuzSubstringCache();
            
            // Act - Simulate production cache usage
            foreach (var testCase in _productionQueries)
            {
                // First pass - populate cache
                cache.TryGetCachedPattern(testCase.Query, out _);
                substringCache.GetMatchingPatterns(testCase.Query);
                
                _metrics.RecordCacheMiss();
            }
            
            // Second pass - should hit cache
            foreach (var testCase in _productionQueries.Take(_productionQueries.Count / 2))
            {
                if (cache.TryGetCachedPattern(testCase.Query, out _))
                {
                    _metrics.RecordCacheHit();
                }
                else
                {
                    _metrics.RecordCacheMiss();
                }
            }
            
            // Third pass - variations should partially hit
            foreach (var testCase in _productionQueries)
            {
                var variation = testCase.Query + " deluxe";
                if (cache.TryGetCachedPattern(variation, out _) || 
                    substringCache.GetMatchingPatterns(variation).Any())
                {
                    _metrics.RecordCacheHit();
                }
                else
                {
                    _metrics.RecordCacheMiss();
                }
            }

            // Assert
            var hitRatio = _metrics.GetCacheHitRatio() * 100;
            
            hitRatio.Should().BeGreaterOrEqualTo(TARGET_CACHE_HIT_RATIO,
                $"Cache hit ratio must be at least {TARGET_CACHE_HIT_RATIO}%, but was {hitRatio:F1}%");
            
            var summary = _metrics.GetPerformanceSummary();
            _output.WriteLine($"Cache Performance Results:");
            _output.WriteLine($"  Cache hits: {summary.CacheHits}");
            _output.WriteLine($"  Cache misses: {summary.CacheMisses}");
            _output.WriteLine($"  Hit ratio: {hitRatio:F1}%");
            _output.WriteLine($"  Target: {TARGET_CACHE_HIT_RATIO}%");
            _output.WriteLine($"  Status: {(hitRatio >= TARGET_CACHE_HIT_RATIO ? "PASS ✓" : "FAIL ✗")}");
        }

        [Fact]
        public void MemoryUsage_StaysWithinBudget()
        {
            // Arrange
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            var initialMemory = GC.GetTotalMemory(false);
            
            // Act - Load all ML components
            var optimizer = new CompiledMLQueryOptimizer();
            var hybridOptimizer = new HybridMLQueryOptimizer(_logger);
            var classifier = new QueryComplexityClassifier();
            var cache = new QobuzPatternCache();
            var substringCache = new QobuzSubstringCache();
            
            // Process queries to populate caches
            foreach (var testCase in _productionQueries)
            {
                optimizer.OptimizeQueryAsync(testCase.Query).Wait();
                classifier.ClassifyComplexity(testCase.Query);
                cache.TryGetCachedPattern(testCase.Query, out _);
                substringCache.GetMatchingPatterns(testCase.Query);
            }
            
            GC.Collect();
            var finalMemory = GC.GetTotalMemory(false);
            
            // Assert
            var memoryUsedBytes = finalMemory - initialMemory;
            var memoryUsedMB = memoryUsedBytes / (1024.0 * 1024.0);
            
            memoryUsedMB.Should().BeLessOrEqualTo(TARGET_MEMORY_MB,
                $"ML components must use less than {TARGET_MEMORY_MB}MB, but used {memoryUsedMB:F1}MB");
            
            _output.WriteLine($"Memory Usage Results:");
            _output.WriteLine($"  Initial memory: {initialMemory / (1024.0 * 1024.0):F1}MB");
            _output.WriteLine($"  Final memory: {finalMemory / (1024.0 * 1024.0):F1}MB");
            _output.WriteLine($"  ML overhead: {memoryUsedMB:F1}MB");
            _output.WriteLine($"  Target: {TARGET_MEMORY_MB}MB");
            _output.WriteLine($"  Status: {(memoryUsedMB <= TARGET_MEMORY_MB ? "PASS ✓" : "FAIL ✗")}");
        }

        [Fact]
        public async Task HybridOptimizer_OutperformsBaseline()
        {
            // Arrange
            var baselineReduction = 0.0;
            var hybridReduction = 0.0;

            // Act - Compare baseline vs hybrid optimizer
            foreach (var testCase in _productionQueries)
            {
                // Baseline optimizer
                var baselineResult = await _optimizer.OptimizeQueryAsync(testCase.Query);
                var baselineSaved = CalculateApiSavings(baselineResult, testCase);
                baselineReduction += baselineSaved;
                
                // Hybrid optimizer with enterprise features
                var hybridResult = await _hybridOptimizer.OptimizeWithEnterpriseSecurityAsync(
                    testCase.Query, 
                    SecurityLevel.Enterprise);
                var hybridSaved = CalculateApiSavings(hybridResult, testCase);
                hybridReduction += hybridSaved;
            }

            // Assert
            hybridReduction.Should().BeGreaterThan(baselineReduction,
                "Hybrid optimizer should outperform baseline optimizer");
            
            var improvement = ((hybridReduction - baselineReduction) / baselineReduction) * 100;
            
            _output.WriteLine($"Hybrid Optimizer Performance:");
            _output.WriteLine($"  Baseline API reduction: {baselineReduction:F0} calls");
            _output.WriteLine($"  Hybrid API reduction: {hybridReduction:F0} calls");
            _output.WriteLine($"  Improvement: {improvement:F1}%");
            _output.WriteLine($"  Status: {(improvement > 0 ? "PASS ✓" : "FAIL ✗")}");
        }

        [Fact]
        public async Task ConcurrentPredictions_MaintainPerformance()
        {
            // Arrange
            var concurrentTasks = 100;
            var tasks = new Task<double>[concurrentTasks];
            var stopwatch = new Stopwatch();

            // Act - Simulate concurrent predictions
            stopwatch.Start();
            
            for (int i = 0; i < concurrentTasks; i++)
            {
                var query = _productionQueries[i % _productionQueries.Count].Query;
                tasks[i] = Task.Run(async () =>
                {
                    var sw = Stopwatch.StartNew();
                    await _optimizer.OptimizeQueryAsync(query);
                    sw.Stop();
                    return sw.ElapsedMilliseconds;
                });
            }
            
            var latencies = await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            var avgLatency = latencies.Average();
            var maxLatency = latencies.Max();
            var totalTime = stopwatch.ElapsedMilliseconds;
            var throughput = (concurrentTasks / (totalTime / 1000.0));
            
            avgLatency.Should().BeLessOrEqualTo(TARGET_PREDICTION_TIME_MS * 2,
                "Average latency should not degrade significantly under load");
            
            throughput.Should().BeGreaterThan(50,
                "Should handle at least 50 predictions per second");
            
            _output.WriteLine($"Concurrent Performance Results:");
            _output.WriteLine($"  Concurrent requests: {concurrentTasks}");
            _output.WriteLine($"  Total time: {totalTime}ms");
            _output.WriteLine($"  Average latency: {avgLatency:F1}ms");
            _output.WriteLine($"  Max latency: {maxLatency:F1}ms");
            _output.WriteLine($"  Throughput: {throughput:F1} req/s");
            _output.WriteLine($"  Status: {(avgLatency <= TARGET_PREDICTION_TIME_MS * 2 ? "PASS ✓" : "FAIL ✗")}");
        }

        [Fact]
        public void PerformanceReport_GeneratesComprehensiveMetrics()
        {
            // Arrange - Run all optimizations to collect metrics
            foreach (var testCase in _productionQueries.Take(10))
            {
                using (_metrics.StartPredictionTiming())
                {
                    var result = _optimizer.OptimizeQueryAsync(testCase.Query).Result;
                    _metrics.RecordPrediction(5.0, true, 0.92);
                }
                
                _metrics.RecordApiOptimization(3, 10);
                _metrics.RecordCacheHit();
            }

            // Act
            var summary = _metrics.GetPerformanceSummary();
            var report = summary.GetFormattedReport();
            var health = summary.GetHealthStatus();

            // Assert
            report.Should().NotBeNullOrEmpty();
            report.Should().Contain("ML Performance Report");
            report.Should().Contain("ACCURACY METRICS");
            report.Should().Contain("API OPTIMIZATION");
            
            health.Should().NotBeNull();
            health.Score.Should().BeGreaterThan(0);
            
            // Output comprehensive report
            _output.WriteLine("=== ML OPTIMIZATION PERFORMANCE REPORT ===");
            _output.WriteLine(report);
            _output.WriteLine($"\nHealth Score: {health.Score}/100");
            _output.WriteLine($"Health Status: {health.Status}");
            
            if (health.Issues.Any())
            {
                _output.WriteLine("\nIdentified Issues:");
                foreach (var issue in health.Issues)
                {
                    _output.WriteLine($"  - {issue}");
                }
            }
        }

        private List<QueryTestCase> LoadProductionQueries()
        {
            // Load real production query patterns for testing
            var queries = new List<QueryTestCase>
            {
                new("Daft Punk Random Access Memories", 5),
                new("Miles Davis Kind of Blue", 4),
                new("The Beatles Abbey Road", 6),
                new("Pink Floyd Dark Side of the Moon", 7),
                new("Michael Jackson Thriller", 5),
                new("Nirvana Nevermind", 4),
                new("Led Zeppelin IV", 3),
                new("Queen Greatest Hits", 8),
                new("Bob Dylan Highway 61 Revisited", 6),
                new("The Rolling Stones Exile on Main St.", 7),
                new("Radiohead OK Computer", 5),
                new("Kendrick Lamar To Pimp a Butterfly", 6),
                new("Arcade Fire Funeral", 4),
                new("David Bowie Heroes", 3),
                new("Prince Purple Rain", 5),
                new("Beyoncé Lemonade", 4),
                new("Taylor Swift 1989", 5),
                new("Kanye West My Beautiful Dark Twisted Fantasy", 8),
                new("Frank Ocean Blonde", 4),
                new("Amy Winehouse Back to Black", 5),
                // Edge cases
                new("Various Artists Now That's What I Call Music 99", 12),
                new("明日", 15), // Unicode
                new("AC/DC", 10), // Special characters
                new("2001年宇宙の旅", 15), // Japanese title
                new("Live at Madison Square Garden NYC December 31 1999", 20),
            };
            
            return queries;
        }

        private int CalculateOptimizedApiCalls(string optimizedQuery, QueryTestCase testCase)
        {
            // Simulate API call calculation based on optimization
            if (optimizedQuery.Contains("[OPTIMIZED]"))
            {
                return Math.Max(1, testCase.ExpectedApiCalls / 2);
            }
            return testCase.ExpectedApiCalls;
        }

        private double CalculateConfidence(string optimizedQuery)
        {
            // Calculate confidence score based on optimization
            return optimizedQuery.Contains("[OPTIMIZED]") ? 0.92 : 0.75;
        }

        private bool IsClassificationCorrect(object result, QueryTestCase testCase)
        {
            // Validate classification against expected complexity
            var expectedComplexity = testCase.ExpectedApiCalls > 5 ? "complex" : "simple";
            return result.ToString()?.ToLower().Contains(expectedComplexity) ?? false;
        }

        private double CalculateApiSavings(string result, QueryTestCase testCase)
        {
            // Calculate API calls saved by optimization
            if (result.Contains("[OPTIMIZED]") || result.Contains("cached"))
            {
                return testCase.ExpectedApiCalls * 0.5;
            }
            return 0;
        }

        public void Dispose()
        {
            _metrics?.Dispose();
        }

        private record QueryTestCase(string Query, int ExpectedApiCalls);
        
        private enum SecurityLevel
        {
            Standard,
            Enterprise
        }
    }
}