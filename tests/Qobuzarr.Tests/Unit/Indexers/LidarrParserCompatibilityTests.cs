using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;
using NSubstitute;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Models;
using Qobuzarr.Tests.Builders;
using Qobuzarr.Tests.TestData;
using Qobuzarr.Tests.Fixtures;
using NLog;

namespace Qobuzarr.Tests.Unit.Indexers
{
    /// <summary>
    /// Critical tests for Lidarr parser-compatible album title generation.
    /// Ensures titles are generated in a stable bracket format and do not
    /// collide with Lidarr's legacy hyphen parser regex.
    /// </summary>
    public class LidarrParserCompatibilityTests : TestFixtureBase
    {
        private readonly QobuzParser _parser;
        private readonly QobuzIndexerSettings _settings;
        private readonly Logger _logger;

        public LidarrParserCompatibilityTests()
        {
            _settings = new QobuzIndexerSettings
            {
                IncludeSingles = true,
                IncludeCompilations = true
            };
            _logger = Substitute.For<Logger>();
            _parser = new QobuzParser(_settings, _logger);
        }

        #region Bracket Format Generation for Edition Albums

        [Theory]
        [InlineData("Deluxe Edition", "Spoon-They Want My Soul-Deluxe Edition-WEB-2014")]
        [InlineData("Deluxe More Soul Edition", "Spoon-They Want My Soul-Deluxe More Soul Edition-WEB-2014")]
        [InlineData("Live at Brixton", "Radiohead-OK Computer-Live at Brixton-WEB-1997")]
        [InlineData("Remastered", "Led Zeppelin-IV-Remastered-WEB-1971")]
        [InlineData("Anniversary Edition", "Pink Floyd-The Wall-Anniversary Edition-WEB-1979")]
        [InlineData("Expanded Edition", "Nirvana-Nevermind-Expanded Edition-WEB-1991")]
        [InlineData("Special Edition", "Michael Jackson-Thriller-Special Edition-WEB-1982")]
        public void GenerateTitle_WithEditionKeywords_ShouldIncludeEditionBracket(string version, string expectedPattern)
        {
            // Arrange
            var album = CreateEditionAlbum(version);
            var releases = ConvertAlbumToReleases(album);

            // Act
            var flacRelease = releases.FirstOrDefault(r => r.Title.Contains("FLAC"));
            
            // Assert
            flacRelease.Should().NotBeNull();
            var actualTitle = flacRelease.Title;

            actualTitle.Should().Contain($"[{version}]");
            actualTitle.Should().MatchRegex(@"^.+ - .+ \(\d{4}\) \[[^\]]+\] \[[^\]]+\] \[WEB\]$",
                "Edition albums should use bracket format: Artist - Album (Year) [Edition] [Quality] [WEB]");
        }

        [Theory]
        [MemberData(nameof(AlbumEditionTestData.EditionVariants), MemberType = typeof(AlbumEditionTestData))]
        public void GenerateTitle_WithAllEditionVariants_ShouldIncludeEditionBracket(string version, string expectedPattern, string scenario)
        {
            // Arrange
            var album = CreateEditionAlbum(version);
            var releases = ConvertAlbumToReleases(album);

            // Act
            var flacRelease = releases.FirstOrDefault(r => r.Title.Contains("FLAC"));
            
            // Assert
            flacRelease.Should().NotBeNull();
            flacRelease.Title.Should().Contain($"[{version}]");
            flacRelease.Title.Should().MatchRegex(@"^.+ - .+ \(\d{4}\) \[[^\]]+\] \[[^\]]+\] \[WEB\]$");
        }

        #endregion

        #region Standard Album Space Format (Backwards Compatibility)

        [Fact]
        public void GenerateTitle_WithStandardAlbum_ShouldUseSpaceFormat()
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Random Access Memories")
                .WithArtist("Daft Punk")
                .WithReleaseYear(2013)
                .AsCdQualityFlac()
                .Build();

            var releases = ConvertAlbumToReleases(album);

            // Act
            var flacRelease = releases.FirstOrDefault(r => r.Title.Contains("FLAC"));
            
            // Assert
            flacRelease.Should().NotBeNull();
            flacRelease.Title.Should().Be("Daft Punk - Random Access Memories (2013) [FLAC] [WEB]");
            
            // Verify space format structure
            flacRelease.Title.Should().MatchRegex(@"^.+ - .+ \(\d{4}\) \[.+\] \[WEB\]$",
                "Standard albums should use space format: Artist - Album (Year) [Quality] [WEB]");
        }

        [Fact]
        public void GenerateTitle_WithStandardAlbumNoVersion_ShouldNotTriggerHyphenFormat()
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("The Dark Side of the Moon")
                .WithArtist("Pink Floyd")
                .WithReleaseYear(1973)
                .AsCdQualityFlac()
                .Build();
            album.Version = null; // Explicitly no version

            var releases = ConvertAlbumToReleases(album);

            // Act
            var flacRelease = releases.FirstOrDefault(r => r.Title.Contains("FLAC"));
            
            // Assert
            flacRelease.Should().NotBeNull();
            flacRelease.Title.Should().Be("Pink Floyd - The Dark Side of the Moon (1973) [FLAC] [WEB]");
            flacRelease.Title.Should().NotMatch(@"^[^-]+-[^-]+-[^-]+-WEB-\d{4}$",
                "Albums without editions should not use hyphen format");
        }

        #endregion

        #region Edition Keyword Detection

        /// <summary>
        /// Version field is now always included (when non-empty), not gated by keywords.
        /// These tests verify that version values are properly sanitized and included.
        /// Note: The sanitizer may transform special characters (e.g., "&amp;" → "_").
        /// </summary>
        [Theory]
        [InlineData("deluxe", "deluxe")]
        [InlineData("Deluxe Edition", "Deluxe Edition")]
        [InlineData("live at", "live at")]
        [InlineData("Live in London", "Live in London")]
        [InlineData("remaster", "remaster")]
        [InlineData("2020 Remaster", "2020 Remaster")]
        [InlineData("anniversary", "anniversary")]
        [InlineData("25th Anniversary Edition", "25th Anniversary Edition")]
        [InlineData("expanded", "expanded")]
        [InlineData("Expanded & Remastered", "Expanded _ Remastered")] // Sanitizer converts & to _
        [InlineData("special", "special")]
        [InlineData("Special Edition", "Special Edition")]
        [InlineData("standard", "standard")] // Now included even without keywords
        [InlineData("regular edition", "regular edition")] // Now included
        public void VersionField_ShouldBeIncludedInTitle(string version, string expectedSanitized)
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Test Album")
                .WithArtist("Test Artist")
                .WithReleaseYear(2020)
                .AsCdQualityFlac()
                .Build();
            album.Version = version;

            var releases = ConvertAlbumToReleases(album);
            var flacRelease = releases.FirstOrDefault(r => r.Title.Contains("FLAC"));

            // Act & Assert - version should be included (sanitized)
            flacRelease.Title.Should().Contain($"[{expectedSanitized}]",
                $"Version '{version}' should be included in title as '[{expectedSanitized}]'");
        }

        /// <summary>
        /// Empty or null version should not produce empty brackets.
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void VersionField_WhenEmpty_ShouldNotProduceBrackets(string version)
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Test Album")
                .WithArtist("Test Artist")
                .WithReleaseYear(2020)
                .AsCdQualityFlac()
                .Build();
            album.Version = version;

            var releases = ConvertAlbumToReleases(album);
            var flacRelease = releases.FirstOrDefault(r => r.Title.Contains("FLAC"));

            // Act & Assert - no empty brackets
            flacRelease.Title.Should().NotContain("[]");
            flacRelease.Title.Should().MatchRegex(@"^.+ - .+ \(\d{4}\) \[[^\]]+\] \[WEB\]$",
                "Empty version should not add extra brackets");
        }

        #endregion

        #region Lidarr Parser Regex Simulation

        [Fact]
        public void EditionBracketFormat_ShouldNotMatchLidarrParserHyphenRegex()
        {
            // Arrange - Simulate Lidarr's Parser.cs line 73 regex
            var lidarrParserRegex = @"^(?<artist>[a-z0-9,\(\)\.\&''_]+)-(?<album>[a-z0-9,\(\)\.\&''_]+)-(?<version>[a-z0-9,\(\)\.\&''_\s]+)-(?<source>WEB|CD|Vinyl)-(?<year>\d{4})$";

            var album = CreateEditionAlbum("Deluxe More Soul Edition");
            var releases = ConvertAlbumToReleases(album);
            var flacRelease = releases.FirstOrDefault(r => r.Title.Contains("FLAC"));

            // Act
            var title = flacRelease.Title.Replace(" ", "").ToLowerInvariant();
            var match = System.Text.RegularExpressions.Regex.Match(title, lidarrParserRegex,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Assert
            match.Success.Should().BeFalse("Bracket format should not match Lidarr's legacy hyphen parser regex");
        }

        [Fact]
        public void SpaceFormat_ShouldNotMatchLidarrHyphenRegex()
        {
            // Arrange - Standard albums should NOT match hyphen regex
            var lidarrHyphenRegex = @"^(?<artist>[^-]+)-(?<album>[^-]+)-(?<version>[^-]+)-(?<source>WEB|CD)-(?<year>\d{4})$";
            
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Standard Album")
                .WithArtist("Standard Artist")
                .WithReleaseYear(2020)
                .AsCdQualityFlac()
                .Build();

            var releases = ConvertAlbumToReleases(album);
            var flacRelease = releases.FirstOrDefault(r => r.Title.Contains("FLAC"));

            // Act
            var match = System.Text.RegularExpressions.Regex.Match(flacRelease.Title, lidarrHyphenRegex);

            // Assert
            match.Success.Should().BeFalse("Standard format should NOT match hyphen regex, allowing Lidarr to use standard parsing");
        }

        #endregion

        #region Search Context Integration

        [Fact]
        public void GenerateTitle_WithLidarrSearchContext_ShouldUseExactLidarrTitle()
        {
            // Arrange
            var qobuzAlbum = CreateEditionAlbum("Deluxe Edition");
            var lidarrAlbums = new List<Album>
            {
                new Album 
                { 
                    Id = 123,
                    Title = "They Want My Soul (Deluxe Edition)",
                    ReleaseDate = new DateTime(2014, 8, 5)
                }
            };

            var searchCriteria = new AlbumSearchCriteria
            {
                Albums = lidarrAlbums,
                Artist = new Artist { Name = "Spoon" }
            };

            // Set search context
            _parser.SetSearchContext(searchCriteria);

            var releases = ConvertAlbumToReleases(qobuzAlbum);

            // Act
            var flacRelease = releases.FirstOrDefault(r => r.Title.Contains("FLAC"));

            // Assert
            flacRelease.Should().NotBeNull();
            // Should use exact Lidarr title when context available and matching
            flacRelease.Album.Should().Be("They Want My Soul (Deluxe Edition)");
        }

        #endregion

        #region Quality Format Variations

        [Theory]
        [InlineData(QobuzAudioQuality.MP3320, "MP3 320kbps")]
        [InlineData(QobuzAudioQuality.FLACLossless, "FLAC")]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit96kHz, "FLAC 24bit 96kHz")]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit192Khz, "FLAC 24bit 192kHz")]
        public void GenerateTitle_WithDifferentQualities_ShouldFormatCorrectly(QobuzAudioQuality quality, string expectedQualityString)
        {
            // Arrange
            var album = CreateEditionAlbum("Deluxe Edition", quality);
            var releases = ConvertAlbumToReleases(album);

            // Act
            var qualityRelease = releases.FirstOrDefault(r => r.Title.Contains($"[{expectedQualityString}]"));

            // Assert
            qualityRelease.Should().NotBeNull($"Should have release for quality {quality}");
            qualityRelease.Title.Should().Contain("[Deluxe Edition]");
            qualityRelease.Title.Should().Contain($"[{expectedQualityString}]");
            qualityRelease.Title.Should().MatchRegex(@"^.+ - .+ \(\d{4}\) \[[^\]]+\] \[[^\]]+\] \[WEB\]$");
        }

        #endregion

        #region Edge Cases and Error Handling

        [Fact]
        public void GenerateTitle_WithEmptyVersion_ShouldNotIncludeEditionBracket()
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Test Album")
                .WithArtist("Test Artist") 
                .WithReleaseYear(2020)
                .AsCdQualityFlac()
                .Build();
            album.Version = ""; // Empty version

            var releases = ConvertAlbumToReleases(album);

            // Act
            var flacRelease = releases.FirstOrDefault(r => r.Title.Contains("FLAC"));

            // Assert
            flacRelease.Should().NotBeNull();
            flacRelease.Title.Should().MatchRegex(@"^.+ - .+ \(\d{4}\) \[[^\]]+\] \[WEB\]$",
                "Empty version should use standard format");
        }

        [Fact]
        public void GenerateTitle_WithSpecialCharactersInVersion_ShouldHandleGracefully()
        {
            // Arrange
            var album = CreateEditionAlbum("Live @ Madison Square Garden [Deluxe]");
            var releases = ConvertAlbumToReleases(album);

            // Act
            var flacRelease = releases.FirstOrDefault(r => r.Title.Contains("FLAC"));

            // Assert
            flacRelease.Should().NotBeNull();
            flacRelease.Title.Should().Contain("Live");
            flacRelease.Title.Should().Contain("Madison Square Garden");  
            flacRelease.Title.Should().Contain("Deluxe");
            // Should handle special characters without breaking format   
            flacRelease.Title.Should().MatchRegex(@"^.+ - .+ \(\d{4}\).+\[WEB\]$");
        }

        [Fact]
        public void GenerateTitle_WithUnicodeCharacters_ShouldPreserveUnicode()
        {
            // Arrange
            var album = CreateEditionAlbum("Édition Spéciale");
            var releases = ConvertAlbumToReleases(album);

            // Act
            var flacRelease = releases.FirstOrDefault(r => r.Title.Contains("FLAC"));

            // Assert
            flacRelease.Should().NotBeNull();
            flacRelease.Title.Should().Contain("Édition");
            flacRelease.Title.Should().Contain("Spéciale");
        }

        #endregion

        #region Helper Methods

        private QobuzAlbum CreateEditionAlbum(string version, QobuzAudioQuality? quality = null)
        {
            var builder = QobuzAlbumBuilder.New()
                .WithTitle("They Want My Soul")
                .WithArtist("Spoon")
                .WithReleaseYear(2014)
                .AsCdQualityFlac();

            builder = quality switch
            {
                QobuzAudioQuality.MP3320 => builder.AsMp3Only(),
                QobuzAudioQuality.FLACHiRes24Bit96kHz => builder.WithQuality(24, 96000),
                QobuzAudioQuality.FLACHiRes24Bit192Khz => builder.WithQuality(24, 192000),
                _ => builder
            };

            var album = builder.Build();
            album.Version = version;
            return album;
        }

        private List<NzbDrone.Core.Parser.Model.ReleaseInfo> ConvertAlbumToReleases(QobuzAlbum album)
        {
            // Create a mock IndexerResponse to simulate parser behavior
            var albumSearchResponse = new QobuzAlbumSearchResponse
            {
                Albums = new QobuzSearchResultContainer<QobuzAlbum> { Items = new List<QobuzAlbum> { album } }
            };

            var mockResponse = new NzbDrone.Common.Http.HttpResponse(
                new NzbDrone.Common.Http.HttpRequest("http://test.com"),
                new NzbDrone.Common.Http.HttpHeader(),
                Newtonsoft.Json.JsonConvert.SerializeObject(albumSearchResponse),
                System.Net.HttpStatusCode.OK
            );

            var indexerResponse = new IndexerResponse(
                new IndexerRequest("http://test.com", new NzbDrone.Common.Http.HttpAccept("application/json")),
                mockResponse
            );

            // Use the parser to convert album to releases
            var releases = _parser.ParseResponse(indexerResponse);
            return releases.ToList();
        }

        #endregion
    }
}
