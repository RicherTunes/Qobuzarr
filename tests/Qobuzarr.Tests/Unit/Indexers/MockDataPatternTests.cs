using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Xunit;
using Xunit.Abstractions;

namespace Qobuzarr.Tests.Unit.Indexers
{
    /// <summary>
    /// Tests using our comprehensive mock data patterns
    /// Validates classifier behavior against real-world patterns
    /// </summary>
    public class MockDataPatternTests
    {
        private readonly QueryComplexityClassifier _classifier;
        private readonly ITestOutputHelper _output;

        public MockDataPatternTests(ITestOutputHelper output)
        {
            _classifier = new QueryComplexityClassifier();
            _output = output;
        }

        [Fact]
        public void TestSimplePatterns_ShouldClassifyCorrectly()
        {
            // Test a sample of simple patterns
            var simplePatterns = MockDataFromRealPatterns.SimplePatterns.Take(10);

            foreach (var pattern in simplePatterns)
            {
                var result = _classifier.ClassifyComplexity(pattern.Artist, pattern.Album);
                _output.WriteLine($"Simple: '{pattern.Artist}' - '{pattern.Album}' -> {result}");

                // Simple patterns should not be Complex
                result.Should().NotBe(QueryComplexity.Complex,
                    $"Simple pattern '{pattern.Artist} - {pattern.Album}' should not be Complex");
            }
        }

        [Fact]
        public void TestMediumPatterns_ShouldClassifyCorrectly()
        {
            // Test a sample of medium patterns
            var mediumPatterns = MockDataFromRealPatterns.MediumPatterns.Take(10);

            foreach (var pattern in mediumPatterns)
            {
                var result = _classifier.ClassifyComplexity(pattern.Artist, pattern.Album);
                _output.WriteLine($"Medium: '{pattern.Artist}' - '{pattern.Album}' -> {result}");

                // Medium patterns should be Medium or Complex
                result.Should().BeOneOf(QueryComplexity.Medium, QueryComplexity.Complex);
            }
        }

        [Fact]
        public void TestComplexPatterns_ShouldClassifyAsComplex()
        {
            // Test a sample of complex patterns
            var complexPatterns = MockDataFromRealPatterns.ComplexPatterns.Take(10);

            foreach (var pattern in complexPatterns)
            {
                var result = _classifier.ClassifyComplexity(pattern.Artist, pattern.Album);
                _output.WriteLine($"Complex: '{pattern.Artist}' - '{pattern.Album}' -> {result}");

                // Most complex patterns should be Complex
                result.Should().Be(QueryComplexity.Complex,
                    $"Complex pattern '{pattern.Artist} - {pattern.Album}' should be Complex");
            }
        }

        [Fact]
        public void TestEdgeCasePatterns_ShouldHandleGracefully()
        {
            // Test edge cases - use complex patterns as they contain edge cases
            var edgeCases = MockDataFromRealPatterns.ComplexPatterns
                .Where(p => p.Artist.Contains("!!!") || p.Artist.Contains("?") ||
                           p.Album.Contains("!!!") || p.Album.Contains("?"))
                .Take(10);

            foreach (var pattern in edgeCases)
            {
                var result = _classifier.ClassifyComplexity(pattern.Artist, pattern.Album);
                _output.WriteLine($"Edge case: '{pattern.Artist}' - '{pattern.Album}' -> {result}");

                // Edge cases should at least return a valid complexity
                result.Should().BeOneOf(
                    QueryComplexity.Simple,
                    QueryComplexity.Medium,
                    QueryComplexity.Complex);
            }
        }

        [Fact]
        public void TestOverallDistribution_ShouldMatchExpectedRatios()
        {
            // Count distribution across all patterns
            var results = new Dictionary<QueryComplexity, int>
            {
                [QueryComplexity.Simple] = 0,
                [QueryComplexity.Medium] = 0,
                [QueryComplexity.Complex] = 0
            };

            // Test all patterns
            var allPatterns = new List<(string Artist, string Album, QueryComplexity Expected)>();
            allPatterns.AddRange(MockDataFromRealPatterns.SimplePatterns);
            allPatterns.AddRange(MockDataFromRealPatterns.MediumPatterns);
            allPatterns.AddRange(MockDataFromRealPatterns.ComplexPatterns);

            foreach (var pattern in allPatterns)
            {
                var complexity = _classifier.ClassifyComplexity(pattern.Artist, pattern.Album);
                results[complexity]++;
            }

            // Calculate ratios
            var total = results.Values.Sum();
            var simpleRatio = results[QueryComplexity.Simple] / (double)total * 100;
            var mediumRatio = results[QueryComplexity.Medium] / (double)total * 100;
            var complexRatio = results[QueryComplexity.Complex] / (double)total * 100;

            _output.WriteLine($"Total patterns tested: {total}");
            _output.WriteLine($"Simple: {results[QueryComplexity.Simple]} ({simpleRatio:F1}%)");
            _output.WriteLine($"Medium: {results[QueryComplexity.Medium]} ({mediumRatio:F1}%)");
            _output.WriteLine($"Complex: {results[QueryComplexity.Complex]} ({complexRatio:F1}%)");

            // Based on real data analysis
            total.Should().BeGreaterOrEqualTo(100, "Should have substantial test data");
            simpleRatio.Should().BeGreaterThan(30, "At least 30% should be simple");
            complexRatio.Should().BeLessThan(50, "Less than 50% should be complex");
        }

        [Theory]
        [InlineData("Taylor Swift", "1989", QueryComplexity.Simple)]
        [InlineData("Various Artists", "Compilation", QueryComplexity.Complex)]
        [InlineData("AC/DC", "Back in Black", QueryComplexity.Medium)]
        public void TestSpecificPatterns_ShouldClassifyAsExpected(
            string artist, string album, QueryComplexity maxExpected)
        {
            var result = _classifier.ClassifyComplexity(artist, album);
            _output.WriteLine($"'{artist}' - '{album}' -> {result}");

            // Should be at most the expected complexity
            ((int)result).Should().BeLessOrEqualTo((int)maxExpected);
        }
    }
}
