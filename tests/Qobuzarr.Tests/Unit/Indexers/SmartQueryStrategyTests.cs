using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Qobuzarr.Tests.Unit.Indexers
{
    /// <summary>
    /// Comprehensive unit tests for SmartQueryStrategy
    /// Tests query optimization logic and reduction calculations
    /// </summary>
    public class SmartQueryStrategyTests
    {
        private readonly ITestOutputHelper _output;
        private readonly SmartQueryStrategy _strategy;

        public SmartQueryStrategyTests(ITestOutputHelper output)
        {
            _output = output;
            _strategy = new SmartQueryStrategy();
        }

        [Fact]
        public void BuildOptimizedQueries_SimpleCase_ShouldReturnSingleQuery()
        {
            // Arrange
            var artist = "Taylor Swift";
            var album = "1989";
            var originalQueries = new List<string> { "query1", "query2", "query3" };

            // Act
            var result = _strategy.BuildOptimizedQueries(artist, album, originalQueries);

            // Assert
            result.Should().HaveCount(1, "Simple cases should optimize to single query");
            result[0].Should().Be("query1", "Should use the first (primary) query");
        }

        [Fact]
        public void BuildOptimizedQueries_WithNullQueries_ShouldReturnEmpty()
        {
            // Act
            var result = _strategy.BuildOptimizedQueries("Artist", "Album", null);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty("Should return empty list for null input");
        }

        [Fact]
        public void BuildOptimizedQueries_WithEmptyQueries_ShouldReturnEmpty()
        {
            // Arrange
            var queries = new List<string>();

            // Act
            var result = _strategy.BuildOptimizedQueries("Artist", "Album", queries);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty("Should return empty list for empty input");
        }

        [Fact]
        public void BuildOptimizedQueries_WithOnlyEmptyStringQueries_ShouldReturnEmpty()
        {
            // Arrange
            var queries = new List<string> { "", " ", "   ", null };
            var queryList = queries?.ToList();

            // Act
            var result = _strategy.BuildOptimizedQueries("Artist", "Album", queryList);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty("Should return empty list for null/empty input");
        }

        [Fact]
        public void BuildOptimizedQueries_MediumCase_ShouldReturnTwoQueries()
        {
            // Arrange
            var artist = "AC/DC";
            var album = "Back in Black";
            var originalQueries = new List<string> { "query1", "query2", "query3" };

            // Act
            var result = _strategy.BuildOptimizedQueries(artist, album, originalQueries);

            // Assert
            result.Should().HaveCount(2, "Medium cases should optimize to two queries");
            result[0].Should().Be("query1", "Should include primary query");
            result[1].Should().Be("query2", "Should include secondary query");
        }

        [Fact]
        public void BuildOptimizedQueries_ComplexCase_ShouldPreserveAllQueries()
        {
            // Arrange
            var artist = "Various Artists";
            var album = "Now That's What I Call Music";
            var originalQueries = new List<string> { "query1", "query2", "query3" };

            // Act
            var result = _strategy.BuildOptimizedQueries(artist, album, originalQueries);

            // Assert
            result.Should().HaveCount(3, "Complex cases should preserve all queries");
            result.Should().BeEquivalentTo(originalQueries, "Should maintain all original queries");
        }

        [Fact]
        public void BuildOptimizedQueries_EmptyQueries_ShouldReturnEmpty()
        {
            // Arrange
            var artist = "Test Artist";
            var album = "Test Album";
            var originalQueries = new List<string>();

            // Act
            var result = _strategy.BuildOptimizedQueries(artist, album, originalQueries);

            // Assert
            result.Should().BeEmpty("Empty input should return empty result");
        }

        [Fact]
        public void BuildOptimizedQueries_NullQueries_ShouldReturnEmpty()
        {
            // Arrange
            var artist = "Test Artist";
            var album = "Test Album";

            // Act
            var result = _strategy.BuildOptimizedQueries(artist, album, null);

            // Assert
            result.Should().BeEmpty("Null input should return empty result");
        }

        [Fact]
        public void BuildOptimizedQueries_SingleQuery_ShouldHandleGracefully()
        {
            // Arrange
            var artist = "Simple Artist";
            var album = "Simple Album";
            var originalQueries = new List<string> { "single_query" };

            // Act
            var result = _strategy.BuildOptimizedQueries(artist, album, originalQueries);

            // Assert
            result.Should().HaveCount(1, "Single query should be preserved");
            result[0].Should().Be("single_query", "Should preserve the single query");
        }

        [Theory]
        [InlineData("Taylor Swift", "1989", 3, 66.67, "Simple case should optimize significantly")]
        [InlineData("AC/DC", "Back in Black", 3, 33.33, "Medium case should optimize moderately")]
        [InlineData("Various Artists", "Compilation", 3, 33.33, "Medium case should optimize moderately")]
        [InlineData("Simple", "Album", 1, 0.0, "Single query should not reduce")]
        [InlineData("Simple", "Album", 2, 50.0, "Two queries should reduce to one")]
        public void CalculateExpectedReduction_VariousCases_ShouldReturnCorrectPercentage(
            string artist, string album, int originalCount, double expectedReduction, string reason)
        {
            // Act
            var result = _strategy.CalculateExpectedReduction(artist, album, originalCount);

            // Assert
            _output.WriteLine($"Testing: {artist} - {album}, Original: {originalCount}, Expected: {expectedReduction:F2}%, Actual: {result:P2}");
            result.Should().BeApproximately(expectedReduction / 100.0, 0.01, reason);
        }

        [Fact]
        public void GetComplexity_VariousCases_ShouldReturnCorrectClassification()
        {
            // Arrange
            var testCases = new[]
            {
                ("Taylor Swift", "1989", QueryComplexity.Simple),
                ("AC/DC", "Back in Black", QueryComplexity.Medium),
                ("Various Artists", "Compilation", QueryComplexity.Medium), // Conservative classifier classifies as Medium
                ("", "Empty Artist", QueryComplexity.Complex)
            };

            // Act & Assert
            foreach (var (artist, album, expected) in testCases)
            {
                var result = _strategy.GetComplexity(artist, album);
                _output.WriteLine($"{artist} - {album}: {result}");
                result.Should().Be(expected, $"Classification for {artist} - {album}");
            }
        }

        [Fact]
        public void BuildOptimizedQueries_LargeQueryList_ShouldOptimizeCorrectly()
        {
            // Arrange - Test with larger query list
            var artist = "Simple Artist";
            var album = "Simple Album";
            var originalQueries = new List<string> 
            { 
                "query1", "query2", "query3", "query4", "query5", "query6", "query7"
            };

            // Act
            var result = _strategy.BuildOptimizedQueries(artist, album, originalQueries);

            // Assert
            result.Should().HaveCount(1, "Simple case should still optimize to single query regardless of input size");
            result[0].Should().Be("query1", "Should use first query");
        }

        [Fact]
        [Trait("Category", "Slow")]
        public void BuildOptimizedQueries_PerformanceTest_ShouldBeEfficient()
        {
            // Arrange
            var testCases = new[]
            {
                ("Artist1", "Album1", new List<string> {"q1", "q2", "q3"}),
                ("Artist2 & Band", "Album2", new List<string> {"q1", "q2", "q3"}),
                ("Various Artists", "Compilation", new List<string> {"q1", "q2", "q3"}),
            };

            var startTime = System.DateTime.UtcNow;

            // Act - Process 1000 times
            for (int i = 0; i < 1000; i++)
            {
                foreach (var (artist, album, queries) in testCases)
                {
                    _strategy.BuildOptimizedQueries(artist, album, queries);
                }
            }

            var elapsed = System.DateTime.UtcNow - startTime;

            // Assert
            _output.WriteLine($"Processed {testCases.Length * 1000} optimizations in {elapsed.TotalMilliseconds:F1}ms");
            elapsed.Should().BeLessThan(System.TimeSpan.FromSeconds(1), "Query optimization should be very fast");
        }

        [Fact]
        [Trait("Category", "Slow")]
        public void BuildOptimizedQueries_ThreadSafety_ShouldHandleConcurrentAccess()
        {
            // Arrange
            var testCases = new[]
            {
                ("Artist1", "Album1"),
                ("Artist2 & Band", "Album2"), 
                ("Various Artists", "Compilation")
            };

            var results = new System.Collections.Concurrent.ConcurrentBag<List<string>>();

            // Act - Process concurrently
            System.Threading.Tasks.Parallel.ForEach(testCases, testCase =>
            {
                for (int i = 0; i < 100; i++)
                {
                    var queries = new List<string> { "q1", "q2", "q3" };
                    var result = _strategy.BuildOptimizedQueries(testCase.Item1, testCase.Item2, queries);
                    results.Add(result);
                }
            });

            // Assert
            var allResults = results.ToList();
            allResults.Should().HaveCount(300, "All concurrent operations should complete");
            allResults.Should().AllSatisfy(r => r.Should().NotBeNull(), "All results should be valid");
        }

        [Fact]
        public void BuildOptimizedQueries_ConsistentResults_ShouldBeDeterministic()
        {
            // Arrange
            var testCases = new[]
            {
                ("Taylor Swift", "1989"),
                ("AC/DC", "Back in Black"),
                ("Various Artists", "Compilation"),
                ("Empty Artist", ""),
            };

            // Act & Assert
            foreach (var (artist, album) in testCases)
            {
                var queries = new List<string> { "q1", "q2", "q3" };
                
                var result1 = _strategy.BuildOptimizedQueries(artist, album, queries);
                var result2 = _strategy.BuildOptimizedQueries(artist, album, queries);
                var result3 = _strategy.BuildOptimizedQueries(artist, album, queries);

                result1.Should().BeEquivalentTo(result2, $"Results should be consistent for {artist} - {album}");
                result2.Should().BeEquivalentTo(result3, $"Results should be consistent across multiple calls for {artist} - {album}");
            }
        }

        [Fact]
        public void BuildOptimizedQueries_MediumCaseWithTwoQueries_ShouldReturnBoth()
        {
            // Arrange - Test medium case with only 2 original queries
            var artist = "AC/DC";
            var album = "Back in Black";
            var originalQueries = new List<string> { "query1", "query2" };

            // Act
            var result = _strategy.BuildOptimizedQueries(artist, album, originalQueries);

            // Assert
            result.Should().HaveCount(2, "Medium case with 2 queries should return both");
            result.Should().BeEquivalentTo(originalQueries, "Should preserve all available queries");
        }

        [Fact]
        public void BuildOptimizedQueries_ComplexCaseWithFewerQueries_ShouldPreserveAll()
        {
            // Arrange - Test complex case with fewer than 3 queries
            var artist = "Various Artists";
            var album = "Compilation";
            var originalQueries = new List<string> { "query1" };

            // Act
            var result = _strategy.BuildOptimizedQueries(artist, album, originalQueries);

            // Assert
            result.Should().HaveCount(1, "Complex case should preserve all available queries");
            result[0].Should().Be("query1", "Should preserve the single available query");
        }
    }
}
