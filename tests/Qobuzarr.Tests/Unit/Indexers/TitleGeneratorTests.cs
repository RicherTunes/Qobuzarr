using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Indexers.Parsing;
using Qobuzarr.Tests.Builders;

namespace Qobuzarr.Tests.Unit.Indexers
{
    /// <summary>
    /// Tests for TitleGenerator - the public API for title generation.
    /// These tests verify that release titles match Lidarr's quality detection regex patterns.
    /// 
    /// Refactored from QobuzParserTests to use TitleGenerator directly instead of reflection.
    /// </summary>
    public class TitleGeneratorTests
    {
        private readonly TitleGenerator _titleGenerator;

        public TitleGeneratorTests()
        {
            var logger = LogManager.GetCurrentClassLogger();
            _titleGenerator = new TitleGenerator(logger);
        }

        #region Title Generation - Critical for Lidarr Quality Detection

        /// <summary>
        /// Test that verifies our release titles match Lidarr's regex patterns exactly.
        /// This is THE most critical test - if these fail, quality detection won't work.
        /// </summary>
        [Theory]
        [InlineData(QobuzAudioQuality.MP3320, "Test Artist - Test Album (2023) [MP3 320kbps] [WEB]")]
        [InlineData(QobuzAudioQuality.FLACLossless, "Test Artist - Test Album (2023) [FLAC] [WEB]")]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit96kHz, "Test Artist - Test Album (2023) [FLAC 24bit 96kHz] [WEB]")]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit192Khz, "Test Artist - Test Album (2023) [FLAC 24bit 192kHz] [WEB]")]
        public void GenerateQualitySpecificTitle_WithDifferentQualities_ShouldMatchLidarrRegexPatterns(
            QobuzAudioQuality quality, string expectedTitle)
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("test123")
                .WithTitle("Test Album")
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseDate(new DateTime(2023, 1, 1))
                .WithTracks(10, 270)
                .Build();

            // Act
            var result = _titleGenerator.GenerateQualitySpecificTitle(album, quality, 2023);

            // Assert
            result.Should().Be(expectedTitle,
                $"Title for {quality} must exactly match expected format for Lidarr quality detection");
        }

        /// <summary>
        /// Test explicit content handling in titles.
        /// </summary>
        [Fact]
        public void GenerateQualitySpecificTitle_WithExplicitContent_ShouldIncludeExplicitTag()
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("explicit123")
                .WithTitle("Explicit Album")
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseDate(new DateTime(2023, 1, 1))
                .AsExplicit()
                .Build();

            // Act
            var result = _titleGenerator.GenerateQualitySpecificTitle(album, QobuzAudioQuality.MP3320, 2023);

            // Assert
            result.Should().Contain("[Explicit]", "Explicit content should be marked in the title");
            result.Should().Be("Test Artist - Explicit Album (2023) [Explicit] [MP3 320kbps] [WEB]");
        }

        /// <summary>
        /// Test handling of albums without release year.
        /// </summary>
        [Fact]
        public void GenerateQualitySpecificTitle_WithoutReleaseYear_ShouldNotIncludeYearInTitle()
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("noyear123")
                .WithTitle("No Year Album")
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseDate(new DateTime(1800, 1, 1))
                .Build();

            // Act
            var result = _titleGenerator.GenerateQualitySpecificTitle(album, QobuzAudioQuality.FLACLossless, 0);

            // Assert
            result.Should().NotContain("(1800)", "Very old years should be excluded");
            result.Should().Be("Test Artist - No Year Album [FLAC] [WEB]");
        }

        #endregion

        #region Lidarr Quality Pattern Verification

        /// <summary>
        /// Verify that generated titles contain quality markers Lidarr expects.
        /// </summary>
        [Theory]
        [InlineData(QobuzAudioQuality.MP3320, "320kbps", "Lidarr should detect 320kbps bitrate")]
        [InlineData(QobuzAudioQuality.FLACLossless, "FLAC", "Lidarr should detect FLAC codec")]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit96kHz, "24bit", "Lidarr should detect 24bit sample size")]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit192Khz, "24bit", "Lidarr should detect 24bit sample size")]
        public void GeneratedTitles_ShouldContainLidarrQualityMarkers(
            QobuzAudioQuality quality, string expectedMarker, string reason)
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("quality123")
                .WithTitle("Quality Test Album")
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseDate(new DateTime(2023, 1, 1))
                .Build();

            // Act
            var result = _titleGenerator.GenerateQualitySpecificTitle(album, quality, 2023);

            // Assert
            result.Should().Contain(expectedMarker, reason);
        }

        /// <summary>
        /// Test that all quality markers are present in generated titles.
        /// </summary>
        [Fact]
        public void GeneratedTitles_AllQualities_ShouldMatchLidarrExpectedPatterns()
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("patterns123")
                .WithTitle("Pattern Test")
                .WithArtist("Artist", "artist")
                .WithReleaseDate(new DateTime(2023, 1, 1))
                .Build();

            var qualityExpectations = new Dictionary<QobuzAudioQuality, string[]>
            {
                { QobuzAudioQuality.MP3320, new[] { "MP3", "320kbps" } },
                { QobuzAudioQuality.FLACLossless, new[] { "FLAC" } },
                { QobuzAudioQuality.FLACHiRes24Bit96kHz, new[] { "FLAC", "24bit", "96kHz" } },
                { QobuzAudioQuality.FLACHiRes24Bit192Khz, new[] { "FLAC", "24bit", "192kHz" } }
            };

            // Act & Assert
            foreach (var (quality, expectedMarkers) in qualityExpectations)
            {
                var result = _titleGenerator.GenerateQualitySpecificTitle(album, quality, 2023);

                foreach (var marker in expectedMarkers)
                {
                    result.Should().Contain(marker,
                        $"Title for {quality} should contain '{marker}' for Lidarr quality detection");
                }
            }
        }

        #endregion

        #region Title Format Consistency

        /// <summary>
        /// Verify all titles follow the canonical format.
        /// </summary>
        [Theory]
        [InlineData(QobuzAudioQuality.MP3320)]
        [InlineData(QobuzAudioQuality.FLACLossless)]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit96kHz)]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit192Khz)]
        public void GenerateQualitySpecificTitle_AllQualities_ShouldFollowCanonicalFormat(QobuzAudioQuality quality)
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("format123")
                .WithTitle("Format Test Album")
                .WithArtist("Format Artist", "format-artist")
                .WithReleaseDate(new DateTime(2023, 6, 15))
                .Build();

            // Act
            var result = _titleGenerator.GenerateQualitySpecificTitle(album, quality, 2023);

            // Assert - Canonical format: "Artist - Album (Year) [FORMAT] [WEB]"
            result.Should().StartWith("Format Artist - Format Test Album");
            result.Should().Contain("(2023)");
            result.Should().EndWith("[WEB]");
            result.Should().MatchRegex(@"\[(MP3 320kbps|FLAC|FLAC 24bit \d+kHz)\]");
        }

        /// <summary>
        /// Verify unique titles are generated for different albums.
        /// This prevents regression where context-aware logic applied same title to all albums.
        /// </summary>
        [Fact]
        public void GenerateQualitySpecificTitle_DifferentAlbums_ShouldGenerateUniqueTitles()
        {
            // Arrange
            var album1 = new QobuzAlbumBuilder()
                .WithId("album1")
                .WithTitle("First Album Title")
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseDate(new DateTime(2023, 1, 1))
                .Build();

            var album2 = new QobuzAlbumBuilder()
                .WithId("album2")
                .WithTitle("Second Album Title")
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseDate(new DateTime(2023, 2, 1))
                .Build();

            var album3 = new QobuzAlbumBuilder()
                .WithId("album3")
                .WithTitle("Third Album Title")
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseDate(new DateTime(2023, 3, 1))
                .Build();

            // Act
            var title1 = _titleGenerator.GenerateQualitySpecificTitle(album1, QobuzAudioQuality.FLACLossless, 2023);
            var title2 = _titleGenerator.GenerateQualitySpecificTitle(album2, QobuzAudioQuality.FLACLossless, 2023);
            var title3 = _titleGenerator.GenerateQualitySpecificTitle(album3, QobuzAudioQuality.FLACLossless, 2023);

            // Assert
            title1.Should().Contain("First Album Title");
            title2.Should().Contain("Second Album Title");
            title3.Should().Contain("Third Album Title");

            title1.Should().NotBe(title2);
            title2.Should().NotBe(title3);
            title1.Should().NotBe(title3);
        }

        #endregion

        #region Edition/Version Handling

        /// <summary>
        /// Test edition extraction from Version field.
        /// </summary>
        [Theory]
        [InlineData("Deluxe Edition", "[Deluxe Edition]")]
        [InlineData("Remastered 2023", "[Remastered 2023]")]
        [InlineData("25th Anniversary Edition", "[25th Anniversary Edition]")]
        public void GenerateQualitySpecificTitle_WithVersionField_ShouldIncludeEdition(
            string version, string expectedEditionBracket)
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("edition123")
                .WithTitle("Edition Test Album")
                .WithVersion(version)
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseDate(new DateTime(2023, 1, 1))
                .Build();

            // Act
            var result = _titleGenerator.GenerateQualitySpecificTitle(album, QobuzAudioQuality.FLACLossless, 2023);

            // Assert
            result.Should().Contain(expectedEditionBracket);
        }

        #endregion

        #region ContainsEditionKeywords Tests

        [Theory]
        [InlineData("Deluxe Edition", true)]
        [InlineData("Remastered", true)]
        [InlineData("Live at Venue", true)]
        [InlineData("Anniversary Edition", true)]
        [InlineData("Rock", false)]
        [InlineData("2023", false)]
        [InlineData("", false)]
        public void ContainsEditionKeywords_ShouldCorrectlyIdentifyEditions(string text, bool expected)
        {
            // Act
            var result = _titleGenerator.ContainsEditionKeywords(text);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("DELUXE EDITION", true)]
        [InlineData("remastered", true)]
        [InlineData("LIVE AT VENUE", true)]
        public void ContainsEditionKeywords_ShouldBeCaseInsensitive(string text, bool expected)
        {
            // Act
            var result = _titleGenerator.ContainsEditionKeywords(text);

            // Assert
            result.Should().Be(expected);
        }

        #endregion

        #region IsLiveAlbum Tests

        [Theory]
        [InlineData("Album Live", true)]
        [InlineData("Album (Live)", true)]
        [InlineData("Live at Wembley", true)]
        [InlineData("MTV Unplugged", true)]
        [InlineData("Studio Album", false)]
        [InlineData("Greatest Hits", false)]
        public void IsLiveAlbum_ShouldCorrectlyIdentifyLiveAlbums(string title, bool expected)
        {
            // Act
            var result = _titleGenerator.IsLiveAlbum(title);

            // Assert
            result.Should().Be(expected);
        }

        #endregion

        #region Canonical Format Contract Tests

        /// <summary>
        /// Verifies the canonical bracket ordering contract:
        /// Artist - Album (Year) [Edition] [Explicit] [LIVE] [FORMAT] [WEB]
        /// 
        /// This test is resilient to formatting tweaks - it only checks ORDER, not exact strings.
        /// </summary>
        [Fact]
        public void GenerateQualitySpecificTitle_ShouldFollowCanonicalBracketOrder()
        {
            // Arrange - album with ALL optional components
            var album = new QobuzAlbumBuilder()
                .WithId("order123")
                .WithTitle("Live at Wembley") // Contains "Live" for [LIVE] marker
                .WithVersion("Deluxe Edition")
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseDate(new DateTime(2023, 1, 1))
                .AsExplicit()
                .Build();

            // Act
            var result = _titleGenerator.GenerateQualitySpecificTitle(album, QobuzAudioQuality.FLACLossless, 2023);

            // Assert - Check ORDER of components using index positions
            var yearIndex = result.IndexOf("(2023)");
            var editionIndex = result.IndexOf("[Deluxe Edition]");
            var explicitIndex = result.IndexOf("[Explicit]");
            var liveIndex = result.IndexOf("[LIVE]");
            var formatIndex = result.IndexOf("[FLAC]");
            var webIndex = result.IndexOf("[WEB]");

            // All components should be present
            yearIndex.Should().BeGreaterThan(-1, "Year should be present");
            editionIndex.Should().BeGreaterThan(-1, "Edition should be present");
            explicitIndex.Should().BeGreaterThan(-1, "Explicit should be present");
            liveIndex.Should().BeGreaterThan(-1, "LIVE should be present");
            formatIndex.Should().BeGreaterThan(-1, "Format should be present");
            webIndex.Should().BeGreaterThan(-1, "WEB should be present");

            // Verify ORDER: Year < Edition < Explicit < LIVE < Format < WEB
            yearIndex.Should().BeLessThan(editionIndex, "Year should come before Edition");
            editionIndex.Should().BeLessThan(explicitIndex, "Edition should come before Explicit");
            explicitIndex.Should().BeLessThan(liveIndex, "Explicit should come before LIVE");
            liveIndex.Should().BeLessThan(formatIndex, "LIVE should come before Format");
            formatIndex.Should().BeLessThan(webIndex, "Format should come before WEB");
        }

        /// <summary>
        /// Tests that [WEB] is ALWAYS the last bracket, regardless of other components.
        /// </summary>
        [Theory]
        [InlineData(false, false, null)] // No explicit, no live, no edition
        [InlineData(true, false, null)]  // Explicit only
        [InlineData(false, true, null)]  // Live only (via title)
        [InlineData(true, true, null)]   // Both explicit and live
        [InlineData(false, false, "Deluxe Edition")] // Edition only
        [InlineData(true, true, "Remastered")] // All components
        public void GenerateQualitySpecificTitle_WebMarkerShouldAlwaysBeLastBracket(
            bool isExplicit, bool isLive, string version)
        {
            // Arrange
            var builder = new QobuzAlbumBuilder()
                .WithId("web-last-test")
                .WithTitle(isLive ? "Live at Venue" : "Studio Album")
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseDate(new DateTime(2023, 1, 1));

            if (isExplicit) builder.AsExplicit();
            if (!string.IsNullOrEmpty(version)) builder.WithVersion(version);

            var album = builder.Build();

            // Act
            var result = _titleGenerator.GenerateQualitySpecificTitle(album, QobuzAudioQuality.FLACLossless, 2023);

            // Assert
            result.Should().EndWith("[WEB]", "WEB marker must always be the last bracket");
        }

        #endregion

        #region Edge Cases and Interactions

        /// <summary>
        /// Test album with Explicit + Live + Edition - all components together.
        /// </summary>
        [Fact]
        public void GenerateQualitySpecificTitle_WithAllOptionalComponents_ShouldIncludeAll()
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("all-components")
                .WithTitle("Unplugged") // Triggers [LIVE]
                .WithVersion("25th Anniversary Edition")
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseDate(new DateTime(2023, 1, 1))
                .AsExplicit()
                .Build();

            // Act
            var result = _titleGenerator.GenerateQualitySpecificTitle(album, QobuzAudioQuality.FLACHiRes24Bit96kHz, 2023);

            // Assert - All components present
            result.Should().Contain("[25th Anniversary Edition]");
            result.Should().Contain("[Explicit]");
            result.Should().Contain("[LIVE]");
            result.Should().Contain("[FLAC 24bit 96kHz]");
            result.Should().Contain("[WEB]");
            result.Should().Contain("(2023)");
        }

        /// <summary>
        /// Test year=0 explicitly (different from very old year).
        /// </summary>
        [Fact]
        public void GenerateQualitySpecificTitle_WithYearZero_ShouldOmitYear()
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("year-zero")
                .WithTitle("Unknown Year Album")
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseDate(new DateTime(2023, 1, 1)) // Has a date, but we pass year=0
                .Build();

            // Act
            var result = _titleGenerator.GenerateQualitySpecificTitle(album, QobuzAudioQuality.FLACLossless, 0);

            // Assert
            result.Should().NotMatchRegex(@"\(\d{4}\)", "Year should not appear when year=0");
            result.Should().Be("Test Artist - Unknown Year Album [FLAC] [WEB]");
        }

        /// <summary>
        /// Test that edition in title doesn't duplicate with Version field.
        /// E.g., "Album (Deluxe Edition)" with Version="Deluxe Edition" should not show twice.
        /// </summary>
        [Fact]
        public void GenerateQualitySpecificTitle_WithEditionInBothTitleAndVersion_ShouldNotDuplicate()
        {
            // Arrange - Edition appears in both title and Version field
            var album = new QobuzAlbumBuilder()
                .WithId("dupe-edition")
                .WithTitle("Album (Deluxe Edition)")
                .WithVersion("Deluxe Edition")
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseDate(new DateTime(2023, 1, 1))
                .Build();

            // Act
            var result = _titleGenerator.GenerateQualitySpecificTitle(album, QobuzAudioQuality.FLACLossless, 2023);

            // Assert - Should only have one [Deluxe Edition] bracket
            var count = System.Text.RegularExpressions.Regex.Matches(result, @"\[Deluxe Edition\]").Count;
            count.Should().Be(1, "Edition should not appear twice");
        }

        /// <summary>
        /// Test nested brackets in album title are handled gracefully.
        /// The implementation extracts bracketed content as edition info, which is valid behavior.
        /// </summary>
        [Fact]
        public void GenerateQualitySpecificTitle_WithNestedBracketsInTitle_ShouldNotBreakFormat()
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("nested-brackets")
                .WithTitle("Album [Special Mix]") // Pre-existing brackets in title
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseDate(new DateTime(2023, 1, 1))
                .Build();

            // Act
            var result = _titleGenerator.GenerateQualitySpecificTitle(album, QobuzAudioQuality.FLACLossless, 2023);

            // Assert - Should still end with [WEB] and have proper format
            result.Should().EndWith("[WEB]");
            result.Should().Contain("[FLAC]");
            // The bracketed content may be extracted as edition - that's OK
            // Key contract: title components are preserved and format is valid
            result.Should().Contain("Album");
            result.Should().Contain("[Special Mix]"); // Content preserved (as edition bracket)
        }

        #endregion
    }
}
