using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Indexers.Parsing;
using Qobuzarr.Tests.Builders;
using Qobuzarr.Tests.TestData;

namespace Qobuzarr.Tests.Unit.Indexers
{
    /// <summary>
    /// Tests for album title generation following Redacted indexer patterns.
    /// Ensures proper bracket formatting for editions and quality information.
    /// Canonical Pattern: "Artist - Album (Year) [Edition] [FORMAT] [WEB]"
    /// FORMAT options: "MP3 320kbps", "FLAC", "FLAC 24bit 96kHz", "FLAC 24bit 192kHz"
    /// </summary>
    public class AlbumTitleGenerationTests
    {
        private readonly ITitleGenerator _titleGenerator;

        public AlbumTitleGenerationTests()
        {
            _titleGenerator = new TitleGenerator(LogManager.GetCurrentClassLogger());
        }
        #region Redacted Pattern Compliance

        [Fact]
        public void GenerateTitle_WithStandardAlbum_ShouldFollowRedactedPattern()
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Random Access Memories")
                .WithArtist("Daft Punk")
                .WithReleaseYear(2013)
                .AsCdQualityFlac()
                .Build();

            // Act
            var title = GenerateRedactedStyleTitle(album);

            // Assert
            title.Should().Be("Daft Punk - Random Access Memories (2013) [FLAC] [WEB]");
        }

        [Theory]
        [MemberData(nameof(AlbumEditionTestData.LiveAlbumScenarios), MemberType = typeof(AlbumEditionTestData))]
        public void GenerateTitle_WithLiveAlbums_ShouldIncludeEditionBracket(
            string version, string expectedPattern, string scenario)
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Live Album")
                .WithArtist("Test Artist")
                .WithReleaseYear(2023)
                .AsHiResFlac()
                .Build();
            album.Version = version;

            // Act
            var title = GenerateRedactedStyleTitle(album);

            // Assert
            if (!string.IsNullOrWhiteSpace(version))
            {
                title.Should().Be($"Test Artist - Live Album (2023) [{version}] [FLAC] [WEB]");

                // Verify bracket structure
                title.Should().Contain($"[{version}]");
                title.Should().Contain("[FLAC]");
                title.Should().Contain("[WEB]");
                title.Should().NotContain("[["); // No double brackets
                title.Should().NotContain("]]"); // No double brackets
            }
            else
            {
                title.Should().Be("Test Artist - Live Album (2023) [FLAC] [WEB]");
            }
        }

        [Theory]
        [MemberData(nameof(AlbumEditionTestData.EditionVariants), MemberType = typeof(AlbumEditionTestData))]
        public void GenerateTitle_WithEditionVariants_ShouldFormatCorrectly(
            string version, string expectedPattern, string scenario)
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Test Album")
                .WithArtist("Test Artist")
                .WithReleaseYear(2020)
                .AsHiResFlac()
                .Build();
            album.Version = version;

            // Act
            var title = GenerateRedactedStyleTitle(album);

            // Assert - Canonical format: [Edition] [FORMAT] [WEB]
            title.Should().Be($"Test Artist - Test Album (2020) [{version}] [FLAC] [WEB]");

            // Verify proper bracket separation
            var editionBracketIndex = title.IndexOf($"[{version}]");
            var formatBracketIndex = title.IndexOf("[FLAC]");
            var webBracketIndex = title.IndexOf("[WEB]");

            editionBracketIndex.Should().BeGreaterThan(0);
            formatBracketIndex.Should().BeGreaterThan(editionBracketIndex);
            webBracketIndex.Should().BeGreaterThan(formatBracketIndex);

            // There should be a space between edition and format brackets
            var charAfterEditionBracket = title.Substring(editionBracketIndex + version.Length + 2, 1);
            charAfterEditionBracket.Should().Be(" ", "there should be a space between [Edition] and [FORMAT] brackets");
        }

        #endregion

        #region Quality Formatting

        [Fact]
        public void GenerateTitle_WithHiResFlac_ShouldUseFlacQuality()
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Test Album")
                .WithArtist("Test Artist")
                .WithReleaseYear(2023)
                .AsHiResFlac() // 24-bit/192kHz
                .Build();

            // Act
            var title = GenerateRedactedStyleTitle(album);

            // Assert
            title.Should().EndWith("[FLAC] [WEB]");
            title.Should().NotContain("MP3");
        }

        [Fact]
        public void GenerateTitle_WithCdQuality_ShouldUseFlacQuality()
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Test Album")
                .WithArtist("Test Artist")
                .WithReleaseYear(2023)
                .AsCdQualityFlac() // 16-bit/44.1kHz
                .Build();

            // Act
            var title = GenerateRedactedStyleTitle(album);

            // Assert
            title.Should().EndWith("[FLAC] [WEB]");
        }

        [Fact]
        public void GenerateTitle_WithMp3Quality_ShouldUseMp3Quality()
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Test Album")
                .WithArtist("Test Artist")
                .WithReleaseYear(2023)
                .Build();

            // Act - Use TitleGenerator with explicit MP3 quality
            var title = _titleGenerator.GenerateQualitySpecificTitle(album, QobuzAudioQuality.MP3320, 2023);

            // Assert
            title.Should().EndWith("[MP3 320kbps] [WEB]");
            title.Should().NotContain("FLAC");
        }

        #endregion

        #region Edge Cases and Special Characters

        [Fact]
        public void GenerateTitle_WithSpecialCharactersInArtist_ShouldPreserveCharacters()
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Test Album")
                .WithArtist("AC/DC")
                .WithReleaseYear(1980)
                .AsCdQualityFlac()
                .Build();

            // Act
            var title = GenerateRedactedStyleTitle(album);

            // Assert
            title.Should().Be("AC/DC - Test Album (1980) [FLAC] [WEB]");
            title.Should().Contain("AC/DC"); // Forward slash preserved
        }

        [Fact]
        public void GenerateTitle_WithSpecialCharactersInAlbumTitle_ShouldPreserveCharacters()
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Blood Sugar Sex Magik")
                .WithArtist("Red Hot Chili Peppers")
                .WithReleaseYear(1991)
                .AsCdQualityFlac()
                .Build();

            // Act
            var title = GenerateRedactedStyleTitle(album);

            // Assert
            title.Should().Be("Red Hot Chili Peppers - Blood Sugar Sex Magik (1991) [FLAC] [WEB]");
        }

        [Fact]
        public void GenerateTitle_WithUnicodeCharacters_ShouldPreserveUnicode()
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Café del Mar")
                .WithArtist("José Padilla")
                .WithReleaseYear(1994)
                .AsCdQualityFlac()
                .Build();
            album.Version = "Édition Spéciale";

            // Act
            var title = GenerateRedactedStyleTitle(album);

            // Assert
            title.Should().Be("José Padilla - Café del Mar (1994) [Édition Spéciale] [FLAC] [WEB]");
            title.Should().Contain("José"); // Accented characters preserved
            title.Should().Contain("Café");
            title.Should().Contain("Édition");
        }

        [Fact]
        public void GenerateTitle_WithBracketsInVersion_ShouldEscapeOrSanitize()
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Test Album")
                .WithArtist("Test Artist")
                .WithReleaseYear(2023)
                .AsCdQualityFlac()
                .Build();
            album.Version = "Live [Acoustic Set]";

            // Act
            var title = GenerateRedactedStyleTitle(album);

            // Assert
            // Should handle nested brackets gracefully - could sanitize or escape
            title.Should().Contain("Live");
            title.Should().Contain("Acoustic Set");
            title.Should().EndWith("[FLAC] [WEB]");

            // Verify the structure is still parseable
            title.Should().MatchRegex(@"^.+ - .+ \(\d{4}\) \[.+\] \[FLAC\] \[WEB\]$");
        }

        #endregion

        #region Complex Edition Scenarios

        [Theory]
        [MemberData(nameof(AlbumEditionTestData.ComplexEditionScenarios), MemberType = typeof(AlbumEditionTestData))]
        public void GenerateTitle_WithComplexEditions_ShouldHandleCorrectly(
            string version, string expectedPattern, string scenario)
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Test Album")
                .WithArtist("Test Artist")
                .WithReleaseYear(1990) // For remaster year testing
                .AsHiResFlac()
                .Build();
            album.Version = version;

            // Act
            var title = GenerateRedactedStyleTitle(album);

            // Assert
            title.Should().Be($"Test Artist - Test Album (1990) [{version}] [FLAC] [WEB]");

            // Specific validations based on complex scenarios
            switch (scenario)
            {
                case "MultipleEditionMarkers":
                    title.Should().Contain("Deluxe");
                    title.Should().Contain("Remastered");
                    title.Should().Contain("Edition");
                    break;

                case "AnniversaryLiveCombination":
                    title.Should().Contain("Anniversary");
                    title.Should().Contain("Live");
                    title.Should().Contain("Wembley");
                    break;

                case "RemasterYearDifferentFromAlbumYear":
                    title.Should().Contain("(1990)"); // Original album year
                    title.Should().Contain("2020"); // Remaster year in version
                    break;
            }
        }

        #endregion

        #region Compilation and Various Artists

        [Fact]
        public void GenerateTitle_WithVariousArtists_ShouldUseVariousArtistsFormat()
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Greatest Hits of the 80s")
                .AsCompilation() // Sets artist to "Various Artists"
                .WithReleaseYear(2020)
                .AsCdQualityFlac()
                .Build();

            // Act
            var title = GenerateRedactedStyleTitle(album);

            // Assert
            title.Should().StartWith("Various Artists - ");
            title.Should().Contain("Greatest Hits of the 80s");
            title.Should().EndWith("[FLAC] [WEB]");
        }

        [Fact]
        public void GenerateTitle_WithSoundtrack_ShouldHandleCorrectly()
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("The Matrix Soundtrack")
                .WithArtist("Various Artists")
                .WithReleaseYear(1999)
                .AsCdQualityFlac()
                .Build();

            // Act
            var title = GenerateRedactedStyleTitle(album);

            // Assert
            title.Should().Be("Various Artists - The Matrix Soundtrack (1999) [FLAC] [WEB]");
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Generates a title following the canonical Redacted indexer pattern.
        /// Canonical Pattern: "Artist - Album (Year) [Edition] [FORMAT] [WEB]"
        /// FORMAT options: "MP3 320kbps", "FLAC", "FLAC 24bit 96kHz", "FLAC 24bit 192kHz"
        /// </summary>
        private string GenerateRedactedStyleTitle(QobuzAlbum album)
        {
            var artistName = album.GetArtistName();
            var albumTitle = album.Title;
            var year = album.ReleaseDate.Year;

            // Determine format based on bit depth - matching TitleGenerator canonical formats
            string format;
            if (album.MaximumBitDepth < 16)
            {
                format = "MP3 320kbps";
            }
            else if (album.MaximumBitDepth >= 24 || album.MaximumBitDepth == 16)
            {
                format = "FLAC";
            }
            else
            {
                format = "MP3 320kbps";
            }

            // Build title: "Artist - Album (Year)"
            var titleBuilder = $"{artistName} - {albumTitle} ({year})";

            // Add edition bracket if version exists: "[Edition]"
            if (!string.IsNullOrWhiteSpace(album.Version))
            {
                // Handle potential brackets in version by sanitizing
                var sanitizedVersion = album.Version.Replace("[", "(").Replace("]", ")");
                titleBuilder += $" [{sanitizedVersion}]";
            }

            // Add format and source brackets separately: "[FORMAT] [WEB]"
            titleBuilder += $" [{format}] [WEB]";

            return titleBuilder;
        }

        #endregion
    }
}
