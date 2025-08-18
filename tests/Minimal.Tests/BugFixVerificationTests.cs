using System.Linq;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Minimal.Tests
{
    /// <summary>
    /// Comprehensive test suite to verify the specific bug fix for "070 Shake - Modus Vivendi Instrumental"
    /// These tests ensure our semantic-aware query intelligence correctly handles version descriptors
    /// </summary>
    public class BugFixVerificationTests
    {
        private readonly SemanticQueryStrategy _semanticStrategy;
        private readonly AlbumComponentClassifier _classifier;

        public BugFixVerificationTests()
        {
            _semanticStrategy = new SemanticQueryStrategy();
            _classifier = new AlbumComponentClassifier();
        }

        [Fact]
        public void BugFix_070ShakeModusVivendiInstrumental_ShouldUseMinimalCleaning()
        {
            // Arrange - The exact reported bug case
            var artist = "070 Shake";
            var album = "Modus Vivendi Instrumental";

            // Act - Semantic analysis
            var strategy = _semanticStrategy.DetermineStrategy(artist, album);

            // Assert - Should preserve "Instrumental"
            strategy.CleaningLevel.Should().Be(CleaningLevel.Minimal, "Albums with 'Instrumental' should use minimal cleaning to preserve version descriptor");
            strategy.PreserveTerms.Should().Contain("Instrumental", "The word 'Instrumental' must be preserved as it's a version descriptor");
            strategy.OptimizationLevel.Should().Be(OptimizationLevel.Conservative, "Version descriptors require conservative optimization");
            strategy.RequireExactMatch.Should().BeTrue("First query should be exact for albums with version descriptors");
        }

        [Fact]
        public void BugFix_070ShakeModusVivendiInstrumental_ShouldGenerateCorrectQueries()
        {
            // Arrange
            var artist = "070 Shake";
            var album = "Modus Vivendi Instrumental";

            // Act - Generate actual search queries
            var queries = _semanticStrategy.BuildQueriesForBugCase(artist, album);

            // Assert - Queries should preserve "Instrumental" 
            queries.Should().NotBeEmpty("Should generate at least one query");
            queries.Should().OnlyContain(q => q.Contains("Instrumental"), "All queries MUST contain 'Instrumental' to find the album");
            
            // Should try multiple query formats for better match probability
            queries.Should().Contain(q => q.Contains("070 Shake") && q.Contains("Modus Vivendi Instrumental"), 
                "Should have query with full artist and album");
            queries.Should().Contain(q => !q.Contains("070 Shake") && q.Contains("Modus Vivendi Instrumental"), 
                "Should have album-only query for version descriptors");
        }

        [Theory]
        [InlineData("Artist", "Album Instrumental")]
        [InlineData("Band", "Songs Live")]
        [InlineData("Name", "Title Acoustic")]
        [InlineData("Group", "Music Unplugged")]
        [InlineData("Solo", "Tracks Demo")]
        public void BugFix_AllVersionDescriptors_ShouldBePreserved(string artist, string album)
        {
            // Act
            var strategy = _semanticStrategy.DetermineStrategy(artist, album);
            var queries = _semanticStrategy.BuildQueriesForStrategy(artist, album, strategy);

            // Assert
            strategy.CleaningLevel.Should().Be(CleaningLevel.Minimal, $"Version descriptor albums like '{album}' should use minimal cleaning");
            queries.Should().OnlyContain(q => ContainsVersionDescriptor(q, album), 
                $"All queries for '{album}' must preserve the version descriptor");
        }

        [Fact]
        public void BugFix_RegularAlbumsWithoutVersionDescriptors_ShouldNotBeAffected()
        {
            // Arrange - Regular albums that should NOT be affected by the bug fix
            var testCases = new[]
            {
                ("Taylor Swift", "1989"),
                ("Adele", "25"),
                ("Drake", "Views"),
                ("The Beatles", "Abbey Road")
            };

            foreach (var (artist, album) in testCases)
            {
                // Act
                var strategy = _semanticStrategy.DetermineStrategy(artist, album);

                // Assert - Should still use aggressive cleaning for simple albums
                strategy.CleaningLevel.Should().Be(CleaningLevel.Aggressive, 
                    $"Simple albums like '{album}' should still use aggressive cleaning");
                strategy.QueryVariants.Should().Be(1, "Simple albums only need one optimized query");
            }
        }

        [Fact]
        public void BugFix_AlbumComponentClassifier_CorrectlyIdentifiesInstrumental()
        {
            // Arrange
            var albumTitle = "Modus Vivendi Instrumental";

            // Act
            var components = _classifier.ClassifyComponents(albumTitle);
            var preservedTerms = _classifier.GetPreservedTerms(albumTitle);
            var cleaningLevel = _classifier.RecommendCleaningLevel(albumTitle);

            // Assert
            components.Should().ContainKey("Instrumental");
            components["Instrumental"].Should().Be(AlbumComponentType.VersionDescriptor, 
                "Instrumental should be classified as a version descriptor");
            
            preservedTerms.Should().Contain("Instrumental", "Instrumental should be in preserved terms");
            cleaningLevel.Should().Be(CleaningLevel.Minimal, "Albums with version descriptors should use minimal cleaning");
        }

        [Fact]
        public void BugFix_IntegrationTest_FullWorkflow()
        {
            // Arrange - Simulate the complete workflow from user search to query generation
            var artist = "070 Shake";
            var album = "Modus Vivendi Instrumental";

            // Act 1: Classify the album components
            var components = _classifier.ClassifyComponents(album);
            var hasVersionDescriptor = components.Values.Any(c => c == AlbumComponentType.VersionDescriptor);

            // Act 2: Determine semantic strategy
            var strategy = _semanticStrategy.DetermineStrategy(artist, album);

            // Act 3: Generate queries
            var queries = _semanticStrategy.BuildQueriesForStrategy(artist, album, strategy);

            // Assert - Complete workflow verification
            hasVersionDescriptor.Should().BeTrue("Album should be detected to have version descriptor");
            
            strategy.CleaningLevel.Should().Be(CleaningLevel.Minimal);
            strategy.PreserveTerms.Should().Contain("Instrumental");
            
            queries.Should().NotBeEmpty();
            queries.Should().OnlyContain(q => q.Contains("Instrumental"), 
                "Final queries must preserve 'Instrumental' to fix the bug");

            // Verify that we would find "Modus Vivendi (Instrumental Selections)" 
            // by ensuring our queries contain the key terms
            queries.Should().OnlyContain(q => q.Contains("Modus") && q.Contains("Vivendi") && q.Contains("Instrumental"),
                "Queries should contain all core terms to match 'Modus Vivendi (Instrumental Selections)'");
        }

        [Fact]
        public void BugFix_EditionVsVersionDescriptor_ShouldBeTreatedDifferently()
        {
            // Arrange - Compare version descriptor vs edition marker
            var versionDescriptorAlbum = "Album Instrumental";
            var editionMarkerAlbum = "Album (Deluxe Edition)";

            // Act
            var versionStrategy = _semanticStrategy.DetermineStrategy("Artist", versionDescriptorAlbum);
            var editionStrategy = _semanticStrategy.DetermineStrategy("Artist", editionMarkerAlbum);

            // Assert - They should be handled very differently
            versionStrategy.CleaningLevel.Should().Be(CleaningLevel.Minimal, 
                "Version descriptors require minimal cleaning");
            editionStrategy.CleaningLevel.Should().Be(CleaningLevel.Moderate, 
                "Edition markers can use moderate cleaning");

            versionStrategy.PreserveTerms.Should().Contain("Instrumental", 
                "Version descriptors must be preserved");
            editionStrategy.PreserveTerms.Should().NotContain("Deluxe", 
                "Edition markers can be safely removed for broader search");
        }

        private bool ContainsVersionDescriptor(string query, string originalAlbum)
        {
            var versionDescriptors = new[] { "Instrumental", "Live", "Acoustic", "Unplugged", "Demo" };
            return versionDescriptors.Any(descriptor => 
                originalAlbum.Contains(descriptor, System.StringComparison.OrdinalIgnoreCase) &&
                query.Contains(descriptor, System.StringComparison.OrdinalIgnoreCase));
        }
    }
}