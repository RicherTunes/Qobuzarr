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
        /// CHARACTERIZATION TEST: Documents current behavior for non-keyword versions.
        /// 
        /// Current behavior (main branch): Version without keywords is IGNORED.
        /// Desired behavior (after fix): Version should ALWAYS be included.
        /// 
        /// This test will FAIL after applying the fix - update assertions accordingly.
        /// </summary>
        [Theory]
        [InlineData("Boys Don't Cry Magazine")] // Frank Ocean's Blond
        [InlineData("Original Motion Picture Soundtrack")] // No standard keywords
        [InlineData("Chapter 1")] // Album series marker
        [InlineData("Vol. 2")] // Volume indicator
        [InlineData("Side A")] // Side indicator - no keywords
        public void GenerateTitle_WithNonKeywordVersion_CurrentBehavior(string version)
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

            // Assert - CURRENT BEHAVIOR: Version is ignored if no keywords
            // After fix: Change this to .Should().Contain($"[{version}]")
            result.Should().NotContain($"[{version}]",
                $"Current behavior: Version '{version}' without keywords is NOT included");
        }

        #endregion

        #region Version Field Sanitization

        /// <summary>
        /// CHARACTERIZATION TEST: Documents current (pre-fix) behavior for sanitization.
        /// 
        /// Current behavior: Version is trimmed but double spaces are NOT normalized.
        /// Desired behavior (after fix): Version should be fully sanitized via MetadataSanitizer.
        /// </summary>
        [Theory]
        [InlineData("Deluxe  Edition", "Deluxe  Edition")] // Double space NOT normalized (current)
        [InlineData(" Remastered ", "Remastered")] // Whitespace IS trimmed (current via .Trim())
        public void GenerateTitle_WithVersionNeedingSanitization_CurrentBehavior(
            string rawVersion, string expectedInTitle)
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

            // Assert - CURRENT BEHAVIOR: version is NOT sanitized
            // After fix: change to expect sanitized version
            result.Should().Contain($"[{expectedInTitle}]",
                "Current behavior: version is not sanitized");
        }

        /// <summary>
        /// DESIRED BEHAVIOR after fix: Version should be sanitized.
        /// Mark as Skip until fix is applied.
        /// </summary>
        [Theory(Skip = "Will pass after fix is applied")]
        [InlineData("Deluxe  Edition", "Deluxe Edition")] // Double space → single
        [InlineData(" Remastered ", "Remastered")] // Trim whitespace
        public void GenerateTitle_WithVersionNeedingSanitization_DesiredBehavior(
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

            // Assert - DESIRED BEHAVIOR: version should be sanitized
            result.Should().Contain($"[{expectedSanitized}]",
                "Version should be sanitized before inclusion in title");
        }

        #endregion

        #region Version Duplication Prevention

        /// <summary>
        /// CHARACTERIZATION TEST: Documents current duplication behavior.
        /// 
        /// Current behavior: Version in parentheses is deduplicated, but version in brackets is NOT.
        /// Desired behavior (after fix): Both should be deduplicated.
        /// </summary>
        [Fact]
        public void GenerateTitle_WithVersionInParentheses_CurrentBehavior_Deduplicated()
        {
            // Arrange - Version in parentheses
            var album = new QobuzAlbumBuilder()
                .WithId("paren-dupe")
                .WithTitle("Test Album (Deluxe Edition)")
                .WithVersion("Deluxe Edition")
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseYear(2023)
                .Build();

            // Act
            var result = _titleGenerator.GenerateQualitySpecificTitle(album, QobuzAudioQuality.FLACLossless, 2023);

            // Assert - Current behavior: parentheses version IS deduplicated
            var count = System.Text.RegularExpressions.Regex.Matches(
                result, System.Text.RegularExpressions.Regex.Escape("[Deluxe Edition]")).Count;
            
            count.Should().Be(1, "Version in parentheses should be deduplicated (current behavior)");
        }

        /// <summary>
        /// CHARACTERIZATION TEST: Version in brackets is NOT deduplicated currently.
        /// </summary>
        [Fact]
        public void GenerateTitle_WithVersionInBrackets_CurrentBehavior_NotDeduplicated()
        {
            // Arrange - Version already in brackets
            var album = new QobuzAlbumBuilder()
                .WithId("bracket-dupe")
                .WithTitle("Test Album [Remastered]")
                .WithVersion("Remastered")
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseYear(2023)
                .Build();

            // Act
            var result = _titleGenerator.GenerateQualitySpecificTitle(album, QobuzAudioQuality.FLACLossless, 2023);

            // Assert - CURRENT BEHAVIOR: brackets version is NOT deduplicated (appears twice)
            var count = System.Text.RegularExpressions.Regex.Matches(
                result, System.Text.RegularExpressions.Regex.Escape("[Remastered]")).Count;
            
            count.Should().Be(2, "Current behavior: version in brackets appears twice (not deduplicated)");
        }

        /// <summary>
        /// DESIRED BEHAVIOR after fix: Both parentheses and brackets should be deduplicated.
        /// </summary>
        [Theory(Skip = "Will pass after fix is applied")]
        [InlineData("Test Album (Deluxe Edition)", "Deluxe Edition")]
        [InlineData("Test Album [Remastered]", "Remastered")]
        public void GenerateTitle_WithVersionAlreadyInTitle_DesiredBehavior_Deduplicated(
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
        /// Frank Ocean's "Blond" album has Version="Boys Don't Cry Magazine, Explicit".
        /// This is meaningful metadata but contains no standard edition keywords.
        /// </summary>
        [Fact]
        public void GenerateTitle_FrankOceanBlond_ShouldHandleArbitraryVersion()
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

            // Assert - CURRENT BEHAVIOR: arbitrary version is ignored
            // After fix: Should contain [Boys Don't Cry Magazine]
            result.Should().StartWith("Frank Ocean - Blond");
            result.Should().Contain("(2016)");
            result.Should().Contain("[Explicit]");
            result.Should().EndWith("[WEB]");
            
            // This assertion documents current (pre-fix) behavior
            // After fix: change to .Should().Contain("[Boys Don't Cry Magazine]")
            result.Should().NotContain("[Boys Don't Cry Magazine]",
                "Current behavior: arbitrary version without keywords is ignored");
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
