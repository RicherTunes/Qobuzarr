using System;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Qobuzarr.Tests.Unit.Indexers
{
    /// <summary>
    /// Edge case tests for QueryComplexityClassifier
    /// Tests null handling, very long strings, unicode, and concurrent access
    /// </summary>
    public class QueryComplexityClassifierEdgeCaseTests
    {
        private readonly QueryComplexityClassifier _classifier;

        public QueryComplexityClassifierEdgeCaseTests()
        {
            _classifier = new QueryComplexityClassifier();
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData("   ", "   ")]
        [InlineData(null, "Album")]
        [InlineData("Artist", null)]
        public void ClassifyComplexity_WithNullOrEmpty_ShouldReturnComplex(string artist, string album)
        {
            // Act
            var result = _classifier.ClassifyComplexity(artist, album);

            // Assert
            result.Should().Be(QueryComplexity.Complex,
                "null or empty inputs should default to Complex to preserve quality");
        }

        [Fact]
        public void ClassifyComplexity_WithVeryLongStrings_ShouldClassifyCorrectly()
        {
            // Arrange
            var longArtist = new string('A', 100);
            var longAlbum = new string('B', 100);

            // Act
            var result = _classifier.ClassifyComplexity(longArtist, longAlbum);

            // Assert
            result.Should().NotBe(QueryComplexity.Simple,
                "Very long strings should not be classified as simple");
        }

        [Theory]
        [InlineData("宇多田ヒカル", "First Love")] // Japanese
        [InlineData("방탄소년단", "MAP OF THE SOUL: 7")] // Korean
        [InlineData("Björk", "Homogenic")] // Icelandic
        [InlineData("Émilie Simon", "Émilie Simon")] // French
        public void ClassifyComplexity_WithUnicodeCharacters_ShouldHandleCorrectly(string artist, string album)
        {
            // Act
            var result = _classifier.ClassifyComplexity(artist, album);

            // Assert
            // Should not throw and should classify based on non-ASCII content
            result.Should().BeOneOf(QueryComplexity.Medium, QueryComplexity.Complex);
        }

        [Fact]
        public async Task ClassifyComplexity_ConcurrentAccess_ShouldBeThreadSafe()
        {
            // Arrange
            var tasks = new Task<QueryComplexity>[100];
            var testData = new[]
            {
                ("Taylor Swift", "1989"),
                ("AC/DC", "Back in Black"),
                ("Various Artists", "Compilation"),
                ("宇多田ヒカル", "First Love"),
                ("!!!", "Louden Up Now")
            };

            // Act
            for (int i = 0; i < tasks.Length; i++)
            {
                var data = testData[i % testData.Length];
                tasks[i] = Task.Run(() => _classifier.ClassifyComplexity(data.Item1, data.Item2));
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            results.Should().NotContainNulls();
            results.Should().HaveCount(100);

            // Verify consistent results for same input
            for (int i = 0; i < results.Length; i++)
            {
                var expectedData = testData[i % testData.Length];
                var expectedResult = _classifier.ClassifyComplexity(expectedData.Item1, expectedData.Item2);
                results[i].Should().Be(expectedResult);
            }
        }

        [Theory]
        [InlineData("Artist\0Name", "Album\0Title")] // Null characters
        [InlineData("Artist\u200B", "Album\u200B")] // Zero-width space
        [InlineData("Artist\t\n\r", "Album\t\n\r")] // Control characters
        public void ClassifyComplexity_WithSpecialCharacters_ShouldNotThrow(string artist, string album)
        {
            // Act
            Action act = () => _classifier.ClassifyComplexity(artist, album);

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void ClassifyComplexity_WithExactThresholdValues_ShouldClassifyCorrectly()
        {
            // Test exact threshold boundaries
            // Simple threshold = 2, Medium threshold = 4

            // Arrange & Act
            var simpleResult = _classifier.ClassifyComplexity("Simple Artist", "Simple Album");
            var mediumResult = _classifier.ClassifyComplexity("AC/DC", "Rock Album"); // Special char = 2 points
            var complexResult = _classifier.ClassifyComplexity("Various Artists feat. Many & More", "Compilation (2023)");

            // Assert
            simpleResult.Should().Be(QueryComplexity.Simple);
            mediumResult.Should().Be(QueryComplexity.Medium);
            complexResult.Should().Be(QueryComplexity.Complex);
        }
    }
}
