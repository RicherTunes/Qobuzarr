using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Minimal.Tests
{
    /// <summary>
    /// Performance benchmark tests for the semantic query system.
    /// These tests ensure the system performs adequately under production load conditions.
    /// CRITICAL: These tests prevent performance regressions in production.
    /// </summary>
    public class PerformanceBenchmarkTests
    {
        private readonly AlbumComponentClassifier _classifier;
        private readonly SemanticQueryStrategy _strategy;

        public PerformanceBenchmarkTests()
        {
            _classifier = new AlbumComponentClassifier();
            _strategy = new SemanticQueryStrategy();
        }

        [Theory]
        [InlineData(100)]    // Light load
        [InlineData(1000)]   // Medium load
        [InlineData(10000)]  // Heavy load
        public void Performance_SemanticClassification_ScalesLinearlyWithLoad(int operationCount)
        {
            // Arrange
            var albumTitles = new[]
            {
                "Simple Album",
                "Album Live Instrumental",
                "Complex MTV Unplugged En Vivo Acoustic Sessions",
                "Ultra Complex Hi-Fi 24-Bit Audiophile Mono Collection"
            };

            var stopwatch = new Stopwatch();

            // Act - Measure classification performance
            stopwatch.Start();
            
            for (int i = 0; i < operationCount; i++)
            {
                var album = albumTitles[i % albumTitles.Length];
                _classifier.ClassifyComponents(album);
            }
            
            stopwatch.Stop();

            // Assert - Performance should scale reasonably
            var operationsPerSecond = operationCount / stopwatch.Elapsed.TotalSeconds;
            var averageTimePerOperation = stopwatch.Elapsed.TotalMilliseconds / operationCount;

            operationsPerSecond.Should().BeGreaterThan(1000, 
                $"Should process at least 1000 classifications per second, got {operationsPerSecond:F0}/sec");
            
            averageTimePerOperation.Should().BeLessThan(1.0, 
                $"Average classification time should be under 1ms, got {averageTimePerOperation:F2}ms");
        }

        [Theory]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(5000)]
        public void Performance_QueryStrategyGeneration_EfficientAtScale(int operationCount)
        {
            // Arrange
            var testCases = new[]
            {
                ("Artist1", "Album Live"),
                ("Artist2", "Songs Instrumental Acoustic"),
                ("Artist3", "Music En Vivo Hi-Fi 24-Bit"),
                ("Artist4", "Collection (Deluxe Edition)")
            };

            var stopwatch = new Stopwatch();

            // Act - Measure strategy generation performance
            stopwatch.Start();
            
            for (int i = 0; i < operationCount; i++)
            {
                var (artist, album) = testCases[i % testCases.Length];
                _strategy.DetermineStrategy(artist, album);
            }
            
            stopwatch.Stop();

            // Assert - Strategy generation should be fast
            var operationsPerSecond = operationCount / stopwatch.Elapsed.TotalSeconds;
            var averageTimePerOperation = stopwatch.Elapsed.TotalMilliseconds / operationCount;

            operationsPerSecond.Should().BeGreaterThan(500, 
                $"Should process at least 500 strategies per second, got {operationsPerSecond:F0}/sec");
            
            averageTimePerOperation.Should().BeLessThan(2.0, 
                $"Average strategy time should be under 2ms, got {averageTimePerOperation:F2}ms");
        }

        [Fact]
        public void Performance_CompleteWorkflow_EndToEndBenchmark()
        {
            // Arrange - Complete workflow from classification to query generation
            var albumTitle = "The Complete MTV Unplugged Live Acoustic Sessions En Vivo Hi-Fi 24-Bit (Deluxe Edition)";
            var artist = "Test Artist";
            var iterationCount = 1000;

            var stopwatch = new Stopwatch();
            var workflows = new List<(Dictionary<string, AlbumComponentType>, QueryStrategy, List<string>)>();

            // Act - Measure complete workflow performance
            stopwatch.Start();

            for (int i = 0; i < iterationCount; i++)
            {
                // Complete semantic workflow
                var components = _classifier.ClassifyComponents(albumTitle);
                var strategy = _strategy.DetermineStrategy(artist, albumTitle);
                var queries = _strategy.BuildQueriesForStrategy(artist, albumTitle, strategy);
                
                workflows.Add((components, strategy, queries));
            }

            stopwatch.Stop();

            // Assert - End-to-end performance
            var workflowsPerSecond = iterationCount / stopwatch.Elapsed.TotalSeconds;
            var averageTimePerWorkflow = stopwatch.Elapsed.TotalMilliseconds / iterationCount;

            workflowsPerSecond.Should().BeGreaterThan(200, 
                $"Should complete at least 200 workflows per second, got {workflowsPerSecond:F0}/sec");
            
            averageTimePerWorkflow.Should().BeLessThan(5.0, 
                $"Average workflow time should be under 5ms, got {averageTimePerWorkflow:F2}ms");

            // Verify all workflows produced valid results
            workflows.Should().OnlyContain(w => w.Item1.Count > 0, "All workflows should produce components");
            workflows.Should().OnlyContain(w => w.Item2 != null, "All workflows should produce strategies");
            workflows.Should().OnlyContain(w => w.Item3.Count > 0, "All workflows should produce queries");
        }

        [Fact]
        public void Performance_MemoryAllocation_EfficientObjectCreation()
        {
            // Arrange - Track object allocations during semantic processing
            var albums = new[]
            {
                "Album",
                "Album Live",
                "Album Live Instrumental",
                "Album Live Instrumental Acoustic Sessions"
            };

            var initialMemory = GC.GetTotalMemory(true);
            var objectsCreated = new List<object>();

            // Act - Process albums while tracking allocations
            for (int i = 0; i < 1000; i++)
            {
                foreach (var album in albums)
                {
                    var components = _classifier.ClassifyComponents(album);
                    var strategy = _strategy.DetermineStrategy("Artist", album);
                    var queries = _strategy.BuildQueriesForStrategy("Artist", album, strategy);
                    
                    // Keep references to track allocation patterns
                    objectsCreated.AddRange(new object[] { components, strategy, queries });
                }
            }

            // Force collection to measure baseline allocation
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            var finalMemory = GC.GetTotalMemory(false);
            var memoryIncrease = finalMemory - initialMemory;
            var allocationsPerOperation = memoryIncrease / (double)(1000 * albums.Length);

            // Assert - Memory efficiency
            allocationsPerOperation.Should().BeLessThan(5000, // 5KB per operation
                $"Memory allocation per operation should be efficient: {allocationsPerOperation:F0} bytes/op");

            objectsCreated.Should().HaveCount(1000 * albums.Length * 3, "Should have created expected object count");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(8)]
        public void Performance_ParallelProcessing_ScalabilityTest(int degreeOfParallelism)
        {
            // Arrange
            var albumTitles = Enumerable.Range(0, 1000)
                .Select(i => $"Album {i} Live Instrumental Acoustic")
                .ToArray();

            var stopwatch = new Stopwatch();

            // Act - Process in parallel
            stopwatch.Start();

            var results = albumTitles.AsParallel()
                .WithDegreeOfParallelism(degreeOfParallelism)
                .Select(album => new
                {
                    Album = album,
                    Components = _classifier.ClassifyComponents(album),
                    Strategy = _strategy.DetermineStrategy("Artist", album)
                })
                .ToList();

            stopwatch.Stop();

            // Assert - Parallel processing should be beneficial
            var operationsPerSecond = 1000 / stopwatch.Elapsed.TotalSeconds;
            
            operationsPerSecond.Should().BeGreaterThan(100 * degreeOfParallelism, 
                $"Parallel processing with {degreeOfParallelism} threads should achieve at least {100 * degreeOfParallelism} ops/sec, got {operationsPerSecond:F0}");

            results.Should().HaveCount(1000, "All parallel operations should complete");
            results.Should().OnlyContain(r => r.Components.Count > 0, "All operations should produce valid results");
        }

        [Fact]
        public void Performance_ComplexAlbumProcessing_WorstCaseScenario()
        {
            // Arrange - Worst-case complex album with maximum version descriptors
            var worstCaseAlbum = "The Complete Live MTV Unplugged Acoustic Instrumental Demo Sessions " +
                               "En Vivo Ao Vivo En Direct Concert Festival Tour Residency Studio " +
                               "Hi-Fi Lo-Fi Audiophile 24-Bit Mono Stereo Binaural Quadraphonic " +
                               "Orchestra Symphony Quartet B-Sides Rarities Unreleased " +
                               "(Deluxe Expanded Remastered Anniversary Special Limited Edition)";

            var stopwatch = new Stopwatch();
            var operationCount = 100;

            // Act - Process worst-case scenario
            stopwatch.Start();

            for (int i = 0; i < operationCount; i++)
            {
                var components = _classifier.ClassifyComponents(worstCaseAlbum);
                var strategy = _strategy.DetermineStrategy("Artist", worstCaseAlbum);
                var queries = _strategy.BuildQueriesForStrategy("Artist", worstCaseAlbum, strategy);
            }

            stopwatch.Stop();

            // Assert - Even worst-case should perform acceptably
            var averageTimeMs = stopwatch.Elapsed.TotalMilliseconds / operationCount;
            
            averageTimeMs.Should().BeLessThan(50, 
                $"Worst-case complex album should process under 50ms, got {averageTimeMs:F2}ms");

            // Verify correctness even in worst case
            var finalComponents = _classifier.ClassifyComponents(worstCaseAlbum);
            var finalStrategy = _strategy.DetermineStrategy("Artist", worstCaseAlbum);

            finalComponents.Should().NotBeEmpty("Complex album should be classified");
            finalStrategy.CleaningLevel.Should().Be(CleaningLevel.Minimal, 
                "Album with many version descriptors should use minimal cleaning");
        }
    }
}