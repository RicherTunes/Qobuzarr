using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;
using NSubstitute;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Music;
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
    /// Tests the dual-format approach: hyphen format for editions, space format for standard albums.
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

        #region Hyphen Format Generation for Edition Albums

        [Theory]
        [InlineData("Deluxe Edition", "Spoon-They Want My Soul-Deluxe Edition-WEB-2014")]
        [InlineData("Deluxe More Soul Edition", "Spoon-They Want My Soul-Deluxe More Soul Edition-WEB-2014")]
        [InlineData("Live at Brixton", "Radiohead-OK Computer-Live at Brixton-WEB-1997")]
        [InlineData("Remastered", "Led Zeppelin-IV-Remastered-WEB-1971")]
        [InlineData("Anniversary Edition", "Pink Floyd-The Wall-Anniversary Edition-WEB-1979")]
        [InlineData("Expanded Edition", "Nirvana-Nevermind-Expanded Edition-WEB-1991")]
        [InlineData("Special Edition", "Michael Jackson-Thriller-Special Edition-WEB-1982")]
        public void GenerateTitle_WithEditionKeywords_ShouldUseHyphenFormat(string version, string expectedPattern)
        {
            // Arrange
            var album = CreateEditionAlbum(version);
            var releases = ConvertAlbumToReleases(album);

            // Act
            var flacRelease = releases.FirstOrDefault(r => r.Title.Contains("FLAC"));
            
            // Assert
            flacRelease.Should().NotBeNull();
            var actualTitle = flacRelease.Title;
            
            // Verify hyphen format structure
            actualTitle.Should().MatchRegex(@"^[^-]+-[^-]+-[^-]+-WEB-\d{4}$", 
                "Edition albums should use hyphen format: Artist-Album-Version-Source-Year");
            
            // Verify contains all expected components
            actualTitle.Should().Contain(album.GetArtistName());
            actualTitle.Should().Contain(album.Title);
            actualTitle.Should().Contain(version);
            actualTitle.Should().EndWith($"-WEB-{album.ReleaseDate.Year}");
        }

        [Theory]
        [MemberData(nameof(AlbumEditionTestData.EditionVariants), MemberType = typeof(AlbumEditionTestData))]
        public void GenerateTitle_WithAllEditionVariants_ShouldUseHyphenFormat(string version, string expectedPattern, string scenario)
        {
            // Arrange
            var album = CreateEditionAlbum(version);
            var releases = ConvertAlbumToReleases(album);

            // Act
            var flacRelease = releases.FirstOrDefault(r => r.Title.Contains("FLAC"));
            
            // Assert
            flacRelease.Should().NotBeNull();
            flacRelease.Title.Should().MatchRegex(@"^[^-]+-[^-]+-[^-]+-WEB-\d{4}$");
            flacRelease.Title.Should().Contain(version);
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

        [Theory]
        [InlineData("deluxe", true)]
        [InlineData("Deluxe Edition", true)]
        [InlineData("live at", true)]
        [InlineData("Live in London", true)]
        [InlineData("remaster", true)]
        [InlineData("2020 Remaster", true)]
        [InlineData("anniversary", true)]
        [InlineData("25th Anniversary Edition", true)]
        [InlineData("expanded", true)]
        [InlineData("Expanded & Remastered", true)]
        [InlineData("special", true)]
        [InlineData("Special Edition", true)]
        [InlineData("standard", false)]
        [InlineData("regular edition", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void ContainsEditionKeywords_ShouldDetectEditionAlbums(string version, bool expectedIsEdition)
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

            // Act & Assert
            if (expectedIsEdition)
            {
                flacRelease.Title.Should().MatchRegex(@"^[^-]+-[^-]+-[^-]+-WEB-\d{4}$",
                    $"Version '{version}' should trigger hyphen format");
            }
            else
            {
                flacRelease.Title.Should().MatchRegex(@"^.+ - .+ \(\d{4}\) \[.+\] \[WEB\]$",
                    $"Version '{version}' should use standard space format");
            }
        }

        #endregion

        #region Lidarr Parser Regex Simulation

        [Fact]
        public void HyphenFormat_ShouldMatchLidarrParserRegex()
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
            match.Success.Should().BeTrue("Hyphen format should match Lidarr's parser regex");
            match.Groups["artist"].Value.Should().NotBeNullOrEmpty();
            match.Groups["album"].Value.Should().NotBeNullOrEmpty(); 
            match.Groups["version"].Value.Should().NotBeNullOrEmpty();
            match.Groups["source"].Value.Should().Be("web");
            match.Groups["year"].Value.Should().MatchRegex(@"\d{4}");
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

            var searchCriteria = Substitute.For<AlbumSearchCriteria>();
            searchCriteria.Albums.Returns(lidarrAlbums);
            searchCriteria.Artist.Returns(new Artist { Name = "Spoon" });

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
            var album = CreateEditionAlbum("Deluxe Edition");
            var releases = ConvertAlbumToReleases(album);

            // Act
            var qualityRelease = releases.FirstOrDefault(r => r.Title.Contains(expectedQualityString.Split(' ')[0]));

            // Assert
            qualityRelease.Should().NotBeNull($"Should have release for quality {quality}");
            
            if (expectedQualityString.Contains("FLAC") && album.Version == "Deluxe Edition")
            {
                // Edition albums should use hyphen format
                qualityRelease.Title.Should().MatchRegex(@"^[^-]+-[^-]+-[^-]+-WEB-\d{4}$");
            }
            else
            {
                // MP3 or standard should use space format
                qualityRelease.Title.Should().Contain(expectedQualityString);
            }
        }

        #endregion

        #region Edge Cases and Error Handling

        [Fact]
        public void GenerateTitle_WithEmptyVersion_ShouldNotUseHyphenFormat()
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
            flacRelease.Title.Should().MatchRegex(@"^.+ - .+ \(\d{4}\) \[.+\] \[WEB\]$",
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
            flacRelease.Title.Should().MatchRegex(@"^[^-]+-[^-]+-.+-WEB-\d{4}$");
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

        private QobuzAlbum CreateEditionAlbum(string version)
        {
            var album = QobuzAlbumBuilder.New()
                .WithTitle("They Want My Soul")
                .WithArtist("Spoon")
                .WithReleaseYear(2014)
                .AsCdQualityFlac()
                .Build();
            album.Version = version;
            return album;
        }

        private List<NzbDrone.Core.Parser.Model.ReleaseInfo> ConvertAlbumToReleases(QobuzAlbum album)
        {
            // Create a mock IndexerResponse to simulate parser behavior
            var albumSearchResponse = new QobuzAlbumSearchResponse
            {
                Albums = new QobuzAlbumsList { Items = new List<QobuzAlbum> { album } }
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