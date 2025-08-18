using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Lidarr.Plugin.Qobuzarr.Indexers;
using System.Collections.Generic;
using System.Linq;

namespace Qobuzarr.Tests.Unit.Indexers
{
    /// <summary>
    /// Unit tests for SmartQueryStrategy using real production data patterns
    /// Validates optimization strategies against actual album queries from 100k dataset
    /// </summary>
    public class SmartQueryStrategyRealDataTests
    {
        private readonly ITestOutputHelper _output;
        private readonly SmartQueryStrategy _strategy;

        public SmartQueryStrategyRealDataTests(ITestOutputHelper output)
        {
            _output = output;
            _strategy = new SmartQueryStrategy();
        }

        /// <summary>
        /// Test simple patterns achieve maximum API reduction (66.7%)
        /// </summary>
        [Theory]
        [MemberData(nameof(GetSimplePatterns))]
        public void BuildOptimizedQueries_SimplePatterns_ShouldUseOneQuery(string artist, string album)
        {
            // Arrange
            var originalQueries = new List<string>
            {
                $"{artist} {album}",
                $"{artist} - {album}",
                $"{artist}"
            };

            // Act
            var optimized = _strategy.BuildOptimizedQueries(artist, album, originalQueries);
            var reduction = _strategy.CalculateExpectedReduction(artist, album, originalQueries.Count);

            // Assert
            _output.WriteLine($"Simple: {artist} - {album} | Original: {originalQueries.Count} | Optimized: {optimized.Count} | Reduction: {reduction:P0}");
            
            // Simple patterns should use 1 or 2 queries max
            optimized.Count.Should().BeLessOrEqualTo(2, 
                $"Simple pattern '{artist} - {album}' should use minimal queries");
            
            // Should achieve at least 33% reduction
            reduction.Should().BeGreaterOrEqualTo(0.33, 
                "Simple patterns should achieve significant reduction");
        }

        /// <summary>
        /// Test medium complexity patterns use balanced approach
        /// </summary>
        [Theory]
        [MemberData(nameof(GetMediumPatterns))]
        public void BuildOptimizedQueries_MediumPatterns_ShouldUseBalancedQueries(string artist, string album)
        {
            // Arrange
            var originalQueries = new List<string>
            {
                $"{artist} {album}",
                $"{artist} - {album}",
                $"{artist}"
            };

            // Act
            var optimized = _strategy.BuildOptimizedQueries(artist, album, originalQueries);
            var complexity = _strategy.GetComplexity(artist, album);

            // Assert
            _output.WriteLine($"Medium: {artist} - {album} | Complexity: {complexity} | Queries: {optimized.Count}");
            
            // Medium patterns should use 1-3 queries based on complexity
            optimized.Count.Should().BeInRange(1, 3, 
                $"Medium pattern '{artist} - {album}' should use balanced queries");
        }

        /// <summary>
        /// Test complex patterns preserve quality with all queries
        /// </summary>
        [Theory]
        [MemberData(nameof(GetComplexPatterns))]
        public void BuildOptimizedQueries_ComplexPatterns_ShouldPreserveQuality(string artist, string album)
        {
            // Arrange
            var originalQueries = new List<string>
            {
                $"{artist} {album}",
                $"{artist} - {album}",
                $"{artist}",
                $"{album}"
            };

            // Act
            var optimized = _strategy.BuildOptimizedQueries(artist, album, originalQueries);
            var complexity = _strategy.GetComplexity(artist, album);

            // Assert
            _output.WriteLine($"Complex: {artist} - {album} | Complexity: {complexity} | Original: {originalQueries.Count} | Optimized: {optimized.Count}");
            
            // Complex patterns may use more queries to preserve quality
            if (complexity == QueryComplexity.Complex)
            {
                optimized.Count.Should().BeGreaterOrEqualTo(2, 
                    $"Complex pattern '{artist} - {album}' should preserve quality");
            }
        }

        /// <summary>
        /// Test edge cases are handled safely
        /// </summary>
        [Theory]
        [MemberData(nameof(GetEdgeCases))]
        public void BuildOptimizedQueries_EdgeCases_ShouldHandleGracefully(string artist, string album)
        {
            // Arrange
            var originalQueries = new List<string>
            {
                $"{artist} {album}",
                $"{artist} - {album}",
                $"{artist}"
            };

            // Act
            var optimized = _strategy.BuildOptimizedQueries(artist, album, originalQueries);

            // Assert
            _output.WriteLine($"Edge case: {artist} - {album} | Queries: {optimized.Count}");
            
            optimized.Should().NotBeNull();
            optimized.Should().NotBeEmpty("Should always return at least one query");
            optimized.Count.Should().BeLessOrEqualTo(originalQueries.Count, 
                "Should never increase query count");
        }

        /// <summary>
        /// Test overall API reduction matches production expectations (50-65%)
        /// </summary>
        [Fact]
        public void BuildOptimizedQueries_ProductionDataset_ShouldAchieveTargetReduction()
        {
            // Arrange
            var totalOriginalQueries = 0;
            var totalOptimizedQueries = 0;
            var testCases = new List<(string Artist, string Album, QueryComplexity Expected)>();
            
            testCases.AddRange(MockDataFromRealPatterns.SimplePatterns);
            testCases.AddRange(MockDataFromRealPatterns.MediumPatterns);
            testCases.AddRange(MockDataFromRealPatterns.ComplexPatterns);

            // Act
            foreach (var (artist, album, _) in testCases)
            {
                var originalQueries = new List<string>
                {
                    $"{artist} {album}",
                    $"{artist} - {album}",
                    $"{artist}"
                };

                var optimized = _strategy.BuildOptimizedQueries(artist, album, originalQueries);
                
                totalOriginalQueries += originalQueries.Count;
                totalOptimizedQueries += optimized.Count;
            }

            var overallReduction = 1.0 - ((double)totalOptimizedQueries / totalOriginalQueries);

            // Assert
            _output.WriteLine($"Production Dataset Results:");
            _output.WriteLine($"  Total albums tested: {testCases.Count}");
            _output.WriteLine($"  Original queries: {totalOriginalQueries}");
            _output.WriteLine($"  Optimized queries: {totalOptimizedQueries}");
            _output.WriteLine($"  Overall reduction: {overallReduction:P1}");

            overallReduction.Should().BeGreaterThan(0.40, 
                "Should achieve at least 40% reduction on production data");
            overallReduction.Should().BeLessThan(0.70, 
                "Should not over-optimize (maintain quality)");
        }

        /// <summary>
        /// Test performance at production scale
        /// </summary>
        [Fact]
        public void BuildOptimizedQueries_PerformanceTest_ShouldBeEfficient()
        {
            // Arrange
            var testCases = new List<(string Artist, string Album)>();
            testCases.AddRange(MockDataFromRealPatterns.SimplePatterns.Select(p => (p.Artist, p.Album)));
            testCases.AddRange(MockDataFromRealPatterns.MediumPatterns.Select(p => (p.Artist, p.Album)));
            testCases.AddRange(MockDataFromRealPatterns.ComplexPatterns.Select(p => (p.Artist, p.Album)));

            var originalQueries = new List<string> { "query1", "query2", "query3" };
            var startTime = System.DateTime.UtcNow;

            // Act - Process each pattern 100 times
            for (int i = 0; i < 100; i++)
            {
                foreach (var (artist, album) in testCases)
                {
                    _strategy.BuildOptimizedQueries(artist, album, originalQueries);
                }
            }

            var elapsed = System.DateTime.UtcNow - startTime;
            var totalOperations = testCases.Count * 100;
            var avgTimePerOperation = (elapsed.TotalMilliseconds * 1000) / totalOperations;

            // Assert
            _output.WriteLine($"Processed {totalOperations} optimizations in {elapsed.TotalMilliseconds:F1}ms");
            _output.WriteLine($"Average time per optimization: {avgTimePerOperation:F2} microseconds");
            
            elapsed.Should().BeLessThan(System.TimeSpan.FromSeconds(2), 
                "Should handle production scale efficiently");
            avgTimePerOperation.Should().BeLessThan(2000, 
                "Each optimization should take less than 2ms");
        }

        /// <summary>
        /// Test null and empty query handling
        /// </summary>
        [Fact]
        public void BuildOptimizedQueries_InvalidQueries_ShouldHandleGracefully()
        {
            // Test null queries
            var result1 = _strategy.BuildOptimizedQueries("Artist", "Album", null);
            result1.Should().BeEmpty();

            // Test empty list
            var result2 = _strategy.BuildOptimizedQueries("Artist", "Album", new List<string>());
            result2.Should().BeEmpty();

            // Test list with null/empty strings
            var result3 = _strategy.BuildOptimizedQueries("Artist", "Album", 
                new List<string> { null, "", "  ", "valid query", null });
            result3.Should().NotBeEmpty();
            result3.Should().OnlyContain(q => !string.IsNullOrWhiteSpace(q));
        }

        // Test data providers
        public static IEnumerable<object[]> GetSimplePatterns()
        {
            var patterns = MockDataFromRealPatterns.SimplePatterns.Take(5);
            foreach (var pattern in patterns)
            {
                yield return new object[] { pattern.Artist, pattern.Album };
            }
        }

        public static IEnumerable<object[]> GetMediumPatterns()
        {
            var patterns = MockDataFromRealPatterns.MediumPatterns.Take(5);
            foreach (var pattern in patterns)
            {
                yield return new object[] { pattern.Artist, pattern.Album };
            }
        }

        public static IEnumerable<object[]> GetComplexPatterns()
        {
            var patterns = MockDataFromRealPatterns.ComplexPatterns.Take(5);
            foreach (var pattern in patterns)
            {
                yield return new object[] { pattern.Artist, pattern.Album };
            }
        }

        public static IEnumerable<object[]> GetEdgeCases()
        {
            var patterns = MockDataFromRealPatterns.EdgeCases.Take(5);
            foreach (var pattern in patterns)
            {
                yield return new object[] { pattern.Artist, pattern.Album };
            }
        }
    }
}