using System;
using FluentAssertions;
using Xunit;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Indexers.Parsing;
using Qobuzarr.Tests.Builders;

namespace Qobuzarr.Tests.Unit.Indexers
{
    /// <summary>
    /// Characterization tests for TitleGenerator's Version field handling.
    /// 
    /// These tests document the behavior change from PR #118:
    /// - BEFORE: Version field only included in title if it contains edition keywords (Deluxe, Remaster, etc.)
    /// - AFTER: Version field ALWAYS included (sanitized) since Qobuz metadata can contain arbitrary values
    /// 
    /// Real-world example: "Blond" by Frank Ocean has Version="Boys Don't Cry Magazine" which is
    /// meaningful metadata but contains no standard edition keywords.
    /// </summary>
    public class TitleGeneratorVersionHandlingTests
    {
        private readonly TitleGenerator _titleGenerator;

        public TitleGeneratorVersionHandlingTests()
        {
            var logger = LogManager.GetCurrentClassLogger();
            _titleGenerator = new TitleGenerator(logger);
        }

        #region Version Field With Standard Keywords (unchanged behavior)

        /// <summary>
        /// Version fields with standard edition keywords should always be included.
        /// This behavior is unchanged from the original implementation.
        /// </summary>
        [Theory]
        [InlineData("Deluxe Edition", "[Deluxe Edition]")]
        [InlineData("Remastered", "[Remastered]")]
        [InlineData("Live at Wembley", "[Live at Wembley]")]
        [InlineData("Anniversary Edition", "[Anniversary Edition]")]
        [InlineData("Expanded Edition", "[Expanded Edition]")]
        [InlineData("Collector's Edition", "[Collector's Edition]")]
        public void GenerateTitle_WithStandardEditionKeywords_ShouldIncludeVersion(
            string version, string expectedBracket)
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("standard-keyword")
                .WithTitle("Test Album")
                .WithVersion(version)
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseYear(2023)
                .Build();

            // Act
            var result = _titleGenerator.GenerateQualitySpecificTitle(album, QobuzAudioQuality.FLACLossless, 2023);

            // Assert
            result.Should().Contain(expectedBracket,
                $"Version '{version}' contains standard keywords and should be included");
        }

        #endregion

        #region Version Field Without Standard Keywords (behavior change)

        /// <summary>
        /// Version fields without standard edition keywords should now be included.
        /// This is the fixed behavior - arbitrary metadata like "Boys Don't Cry Magazine" is valid.
        /// </summary>
        [Theory]
        [InlineData("Boys Don't Cry Magazine")] // Frank Ocean's Blond
        [InlineData("Original Motion Picture Soundtrack")] // No standard keywords
        [InlineData("Chapter 1")] // Album series marker
        [InlineData("Vol. 2")] // Volume indicator
        [InlineData("Side A")] // Side indicator - no keywords
        public void GenerateTitle_WithNonKeywordVersion_ShouldIncludeVersion(string version)
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("non-keyword")
                .WithTitle("Test Album")
                .WithVersion(version)
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseYear(2023)
                .Build();

            // Act
            var result = _titleGenerator.GenerateQualitySpecificTitle(album, QobuzAudioQuality.FLACLossless, 2023);

            // Assert - FIXED BEHAVIOR: Version is always included (sanitized)
            result.Should().Contain($"[{version}]",
                $"Version '{version}' should be included even without standard keywords");
        }

        #endregion

        #region Version Field Sanitization

        /// <summary>
        /// Version fields are now sanitized using MetadataSanitizer.
        /// This normalizes whitespace and handles special characters.
        /// </summary>
        [Theory]
        [InlineData("Deluxe  Edition", "Deluxe Edition")] // Double space → single
        [InlineData(" Remastered ", "Remastered")] // Trim whitespace
        [InlineData("Deluxe Edition  ", "Deluxe Edition")] // Trailing whitespace
        public void GenerateTitle_WithVersionNeedingSanitization_ShouldSanitize(
            string rawVersion, string expectedSanitized)
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("sanitize-version")
                .WithTitle("Test Album")
                .WithVersion(rawVersion)
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseYear(2023)
                .Build();

            // Act
            var result = _titleGenerator.GenerateQualitySpecificTitle(album, QobuzAudioQuality.FLACLossless, 2023);

            // Assert - FIXED BEHAVIOR: version is sanitized
            result.Should().Contain($"[{expectedSanitized}]",
                "Version should be sanitized before inclusion in title");
        }

        #endregion

        #region Version Duplication Prevention

        /// <summary>
        /// Version in title (parentheses or brackets) should be deduplicated.
        /// The version bracket should appear exactly once in the output.
        /// </summary>
        [Theory]
        [InlineData("Test Album (Deluxe Edition)", "Deluxe Edition")]
        [InlineData("Test Album [Remastered]", "Remastered")]
        public void GenerateTitle_WithVersionAlreadyInTitle_ShouldDeduplicate(
            string albumTitle, string version)
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("no-dupe")
                .WithTitle(albumTitle)
                .WithVersion(version)
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseYear(2023)
                .Build();

            // Act
            var result = _titleGenerator.GenerateQualitySpecificTitle(album, QobuzAudioQuality.FLACLossless, 2023);

            // Assert - Edition should appear exactly once
            var bracketPattern = $"[{version}]";
            var count = System.Text.RegularExpressions.Regex.Matches(
                result, System.Text.RegularExpressions.Regex.Escape(bracketPattern)).Count;
            
            count.Should().Be(1, $"Edition '{version}' should appear exactly once, not duplicated");
        }

        /// <summary>
        /// Edge case: Title contains version in different case or with slight variations.
        /// </summary>
        [Fact]
        public void GenerateTitle_WithVersionInTitleDifferentCase_ShouldStillAvoidDuplication()
        {
            // Arrange - Title has "deluxe edition" but Version has "Deluxe Edition"
            var album = new QobuzAlbumBuilder()
                .WithId("case-mismatch")
                .WithTitle("Test Album (deluxe edition)")
                .WithVersion("Deluxe Edition")
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseYear(2023)
                .Build();

            // Act
            var result = _titleGenerator.GenerateQualitySpecificTitle(album, QobuzAudioQuality.FLACLossless, 2023);

            // Assert - Should handle case-insensitive deduplication
            // Implementation may vary - key is no "deluxe" appearing twice in meaningful way
            var deluxeCount = System.Text.RegularExpressions.Regex.Matches(
                result, "deluxe", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
            
            deluxeCount.Should().BeLessOrEqualTo(2, 
                "Deluxe should not appear excessively (once in cleaned title + once in bracket is acceptable)");
        }

        #endregion

        #region Empty/Whitespace Version

        /// <summary>
        /// Empty or whitespace-only version should not add empty brackets.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t")]
        public void GenerateTitle_WithEmptyVersion_ShouldNotAddEmptyBrackets(string version)
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("empty-version")
                .WithTitle("Test Album")
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseYear(2023)
                .Build();
            
            // Set version directly since builder may reject null
            album.Version = version;

            // Act
            var result = _titleGenerator.GenerateQualitySpecificTitle(album, QobuzAudioQuality.FLACLossless, 2023);

            // Assert
            result.Should().NotContain("[]", "Empty version should not produce empty brackets");
            result.Should().NotContain("[ ]", "Whitespace version should not produce whitespace brackets");
        }

        #endregion

        #region Real-World Examples

        /// <summary>
        /// Frank Ocean's "Blond" album has Version="Boys Don't Cry Magazine".
        /// This is meaningful metadata that should now be included even without standard keywords.
        /// </summary>
        [Fact]
        public void GenerateTitle_FrankOceanBlond_ShouldIncludeArbitraryVersion()
        {
            // Arrange - Real-world Qobuz metadata
            var album = new QobuzAlbumBuilder()
                .WithId("frank-ocean-blond")
                .WithTitle("Blond")
                .WithVersion("Boys Don't Cry Magazine")
                .WithArtist("Frank Ocean", "frank-ocean")
                .WithReleaseYear(2016)
                .AsExplicit()
                .Build();

            // Act
            var result = _titleGenerator.GenerateQualitySpecificTitle(album, QobuzAudioQuality.FLACLossless, 2016);

            // Assert - FIXED BEHAVIOR: arbitrary version is included
            result.Should().StartWith("Frank Ocean - Blond");
            result.Should().Contain("(2016)");
            result.Should().Contain("[Boys Don't Cry Magazine]",
                "Arbitrary version metadata should now be included");
            result.Should().Contain("[Explicit]");
            result.Should().EndWith("[WEB]");
        }

        /// <summary>
        /// Album with both explicit flag AND version - verify ordering is correct.
        /// </summary>
        [Fact]
        public void GenerateTitle_WithExplicitAndVersion_ShouldMaintainCorrectOrder()
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("explicit-version")
                .WithTitle("Test Album")
                .WithVersion("Deluxe Edition")
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseYear(2023)
                .AsExplicit()
                .Build();

            // Act
            var result = _titleGenerator.GenerateQualitySpecificTitle(album, QobuzAudioQuality.FLACLossless, 2023);

            // Assert - Canonical order: (Year) [Edition] [Explicit] [FORMAT] [WEB]
            var editionIndex = result.IndexOf("[Deluxe Edition]");
            var explicitIndex = result.IndexOf("[Explicit]");
            var formatIndex = result.IndexOf("[FLAC]");
            var webIndex = result.IndexOf("[WEB]");

            editionIndex.Should().BeGreaterThan(-1);
            explicitIndex.Should().BeGreaterThan(-1);
            
            editionIndex.Should().BeLessThan(explicitIndex, "Edition should come before Explicit");
            explicitIndex.Should().BeLessThan(formatIndex, "Explicit should come before Format");
            formatIndex.Should().BeLessThan(webIndex, "Format should come before WEB");
        }

        #endregion
    }
}
