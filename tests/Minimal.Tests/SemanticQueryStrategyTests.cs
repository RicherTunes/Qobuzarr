using System.Linq;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Minimal.Tests
{
    /// <summary>
    /// Unit tests for SemanticQueryStrategy - intelligent query optimization
    /// Tests the integration of semantic understanding with query generation
    /// </summary>
    public class SemanticQueryStrategyTests
    {
        private readonly SemanticQueryStrategy _strategy;

        public SemanticQueryStrategyTests()
        {
            _strategy = new SemanticQueryStrategy();
        }

        [Fact]
        public void DetermineStrategy_InstrumentalAlbum_ShouldUseMinimalCleaning()
        {
            // Arrange - The core bug case
            var artist = "070 Shake";
            var album = "Modus Vivendi Instrumental";

            // Act
            var result = _strategy.DetermineStrategy(artist, album);

            // Assert
            result.CleaningLevel.Should().Be(CleaningLevel.Minimal, "Albums with 'Instrumental' need minimal cleaning");
            result.PreserveTerms.Should().Contain("Instrumental", "Version descriptor must be preserved");
            result.OptimizationLevel.Should().Be(OptimizationLevel.Conservative, "Conservative optimization for version descriptors");
            result.RequireExactMatch.Should().BeTrue("First query should be exact for version descriptors");
        }

        [Theory]
        [InlineData("Artist", "Album Live")]
        [InlineData("Band", "Songs Acoustic")]
        [InlineData("Name", "Title Unplugged")]
        [InlineData("Group", "Music Sessions")]
        [InlineData("070 Shake", "Modus Vivendi Instrumental")]  // Original bug case
        [InlineData("Artist", "Recordings Demo")]
        [InlineData("Band", "Concert Live")]
        [InlineData("Name", "Versions Extended")]
        public void DetermineStrategy_VersionDescriptors_ShouldUseMinimalCleaning(string artist, string album)
        {
            // Act
            var result = _strategy.DetermineStrategy(artist, album);

            // Assert
            result.CleaningLevel.Should().Be(CleaningLevel.Minimal, $"Albums with version descriptors like '{album}' need minimal cleaning");
            result.OptimizationLevel.Should().Be(OptimizationLevel.Conservative);
        }

        [Theory]
        [InlineData("Taylor Swift", "1989")]
        [InlineData("Adele", "25")]
        [InlineData("Drake", "Views")]
        [InlineData("The Beatles", "Abbey Road")]
        [InlineData("Pink Floyd", "The Dark Side of the Moon")]
        public void DetermineStrategy_RegularAlbums_ShouldUseAggressiveCleaning(string artist, string album)
        {
            // Act
            var result = _strategy.DetermineStrategy(artist, album);

            // Assert
            result.CleaningLevel.Should().Be(CleaningLevel.Aggressive, $"Regular album '{album}' should allow aggressive cleaning");
            result.OptimizationLevel.Should().Be(OptimizationLevel.Maximum, "Regular albums can be optimized aggressively");
        }

        [Theory]
        [InlineData("Artist", "Album (Deluxe Edition)", CleaningLevel.Moderate)]
        [InlineData("Band", "Songs (Remastered)", CleaningLevel.Moderate)]
        [InlineData("Name", "Title - Anniversary Edition", CleaningLevel.Moderate)]
        public void DetermineStrategy_EditionMarkers_ShouldUseModerateCleaning(string artist, string album, CleaningLevel expectedLevel)
        {
            // Act
            var result = _strategy.DetermineStrategy(artist, album);

            // Assert
            result.CleaningLevel.Should().Be(expectedLevel, $"Album with edition markers '{album}' should use moderate cleaning");
        }

        [Theory]
        [InlineData("Artist", "Album (Deluxe Edition)")]
        [InlineData("Band", "Songs (Remastered)")]
        [InlineData("Name", "Title - Special Edition")]
        public void DetermineStrategy_EditionMarkers_ShouldUseModerate(string artist, string album)
        {
            // Act
            var result = _strategy.DetermineStrategy(artist, album);

            // Assert
            result.CleaningLevel.Should().Be(CleaningLevel.Moderate, $"Albums with edition markers like '{album}' should use moderate cleaning");
            result.OptimizationLevel.Should().Be(OptimizationLevel.Balanced);
        }

        [Theory]
        [InlineData("Taylor Swift", "1989")]
        [InlineData("Adele", "25")]
        [InlineData("Drake", "Views")]
        public void DetermineStrategy_SimpleAlbums_ShouldUseAggressive(string artist, string album)
        {
            // Act
            var result = _strategy.DetermineStrategy(artist, album);

            // Assert
            result.CleaningLevel.Should().Be(CleaningLevel.Aggressive, $"Simple albums like '{album}' can use aggressive cleaning");
            result.OptimizationLevel.Should().Be(OptimizationLevel.Maximum);
            result.QueryVariants.Should().Be(1, "Simple albums only need one query");
        }

        [Fact]
        public void BuildQueriesForBugCase_070ShakeModusVivendiInstrumental_ShouldGenerateCorrectQueries()
        {
            // Arrange - The exact bug case
            var artist = "070 Shake";
            var album = "Modus Vivendi Instrumental";

            // Act
            var queries = _strategy.BuildQueriesForBugCase(artist, album);

            // Assert
            queries.Should().NotBeEmpty("Should generate at least one query");
            queries.Should().Contain(q => q.Contains("Instrumental"), "Should preserve the 'Instrumental' term");

            // Should try both with and without artist for instrumental albums
            queries.Should().Contain(q => q.Contains("070 Shake") && q.Contains("Instrumental"), "Should have query with artist");
            queries.Should().Contain(q => !q.Contains("070 Shake") && q.Contains("Instrumental"), "Should have album-only query");
        }

        [Fact]
        public void BuildQueriesForStrategy_MinimalCleaning_ShouldPreserveAllTerms()
        {
            // Arrange
            var artist = "Artist";
            var album = "Album Live Recording";
            var strategy = new QueryStrategy
            {
                CleaningLevel = CleaningLevel.Minimal,
                PreserveTerms = new() { "Live", "Recording" },
                QueryVariants = 2
            };

            // Act
            var queries = _strategy.BuildQueriesForStrategy(artist, album, strategy);

            // Assert
            queries.Should().HaveCount(2);
            queries.Should().OnlyContain(q => q.Contains("Live"), "Should preserve 'Live'");
            queries.Should().OnlyContain(q => q.Contains("Recording"), "Should preserve 'Recording'");
        }

        [Fact]
        public void BuildQueriesForStrategy_AggressiveCleaning_ShouldRemoveEditionMarkers()
        {
            // Arrange
            var artist = "Artist";
            var album = "Album Title (Deluxe Edition)";
            var strategy = new QueryStrategy
            {
                CleaningLevel = CleaningLevel.Aggressive,
                PreserveTerms = new(),
                QueryVariants = 1
            };

            // Act
            var queries = _strategy.BuildQueriesForStrategy(artist, album, strategy);

            // Assert
            queries.Should().HaveCount(1);
            queries.First().Should().NotContain("Deluxe", "Aggressive cleaning should remove edition markers");
            queries.First().Should().NotContain("Edition", "Aggressive cleaning should remove edition markers");
            queries.First().Should().Contain("Artist", "Should preserve artist");
            queries.First().Should().Contain("Album Title", "Should preserve core album title");
        }

        [Fact]
        public void BuildQueriesForStrategy_ModerateCleaning_ShouldBalancePreservationAndCleaning()
        {
            // Arrange
            var artist = "Artist";
            var album = "Important Album (Deluxe Edition)";
            var strategy = new QueryStrategy
            {
                CleaningLevel = CleaningLevel.Moderate,
                PreserveTerms = new() { "Important" },
                QueryVariants = 2
            };

            // Act
            var queries = _strategy.BuildQueriesForStrategy(artist, album, strategy);

            // Assert
            queries.Should().HaveCount(2);
            queries.Should().OnlyContain(q => q.Contains("Important"), "Should preserve flagged terms");
            queries.Should().OnlyContain(q => q.Contains("Album"), "Should preserve core title");
        }

        [Theory]
        [InlineData("Band", "Songs (2023)", 1)] // Year should be removed
        [InlineData("Artist", "Album! Title?", 1)] // Punctuation should be handled
        [InlineData("Name", "Title: Subtitle", 1)] // Colons should be handled
        public void BuildQueriesForStrategy_SpecialCharacters_ShouldBeHandledCorrectly(string artist, string album, int expectedCount)
        {
            // Arrange
            var strategy = new QueryStrategy
            {
                CleaningLevel = CleaningLevel.Moderate,
                QueryVariants = expectedCount
            };

            // Act
            var queries = _strategy.BuildQueriesForStrategy(artist, album, strategy);

            // Assert
            queries.Should().HaveCount(expectedCount);
            queries.Should().OnlyContain(q => !string.IsNullOrWhiteSpace(q), "All queries should be valid");
        }

        [Fact]
        public void DetermineStrategy_ComplexCase_ShouldProvideClearRationale()
        {
            // Arrange
            var artist = "Various Artists";
            var album = "Complex Album (Special Edition) Live";

            // Act
            var result = _strategy.DetermineStrategy(artist, album);

            // Assert
            result.Rationale.Should().NotBeNullOrEmpty("Should provide rationale for strategy decision");
            result.Rationale.Should().Contain("Version descriptor", "Should explain why this strategy was chosen");
        }
    }
}
