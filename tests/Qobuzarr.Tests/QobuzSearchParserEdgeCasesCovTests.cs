using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Newtonsoft.Json;
using NLog;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Common.Http;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Music;
using Qobuzarr.Tests.Builders;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Edge case coverage tests for QobuzParser - covering paths not in existing tests.
    /// Target: src/Indexers/QobuzParser.cs
    ///
    /// Uncovered paths tested:
    /// - Non-streamable albums (line 451-454)
    /// - Album with null/empty ID (line 169-172)
    /// - Album with null title in CreateReleaseInfoForQuality (line 206-211)
    /// - Album with empty artist name (line 220-224)
    /// - FindBestMatchingAlbum with null/empty criteria albums (line 312-313)
    /// - Title similarity edge cases through search context
    /// </summary>
    public class QobuzSearchParserEdgeCasesCovTests
    {
        private readonly QobuzIndexerSettings _settings;
        private readonly Logger _logger;

        public QobuzSearchParserEdgeCasesCovTests()
        {
            _settings = new QobuzIndexerSettings
            {
                IncludeSingles = true,
                IncludeCompilations = true
            };
            _logger = LogManager.GetCurrentClassLogger();
        }

        #region Non-Streamable Album Tests

        /// <summary>
        /// Non-streamable albums should be filtered out.
        /// Source: QobuzParser.cs line 451-454
        /// </summary>
        [Fact]
        public void ParseResponse_WithNonStreamableAlbum_ShouldReturnEmpty()
        {
            // Arrange
            var parser = new QobuzParser(_settings, _logger);
            var nonStreamableAlbum = new QobuzAlbumBuilder()
                .WithId("nonstream123")
                .WithTitle("Non-Streamable Album")
                .WithArtist("Artist", "artist")
                .AsNotStreamable()
                .Build();

            // Act
            var releases = ParseAlbum(parser, nonStreamableAlbum);

            // Assert - Source: QobuzParser.cs line 451-454
            releases.Should().BeEmpty("Non-streamable albums should be filtered");
        }

        /// <summary>
        /// Streamable albums should pass through.
        /// Source: QobuzParser.cs line 451-454
        /// </summary>
        [Fact]
        public void ParseResponse_WithStreamableAlbum_ShouldReturnReleases()
        {
            // Arrange
            var parser = new QobuzParser(_settings, _logger);
            var streamableAlbum = new QobuzAlbumBuilder()
                .WithId("stream123")
                .WithTitle("Streamable Album")
                .WithArtist("Artist", "artist")
                .Build();

            // Act
            var releases = ParseAlbum(parser, streamableAlbum);

            // Assert - Source: QobuzParser.cs line 451
            releases.Should().NotBeEmpty("Streamable albums should not be filtered");
            releases.Should().HaveCount(4, "Hi-res album should create 4 quality releases");
        }

        #endregion

        #region Null/Empty ID Tests

        /// <summary>
        /// Album with null ID should be skipped.
        /// Source: QobuzParser.cs line 169-172
        /// </summary>
        [Fact]
        public void ParseResponse_WithNullAlbumId_ShouldReturnEmpty()
        {
            // Arrange
            var parser = new QobuzParser(_settings, _logger);
            var albumWithNullId = new QobuzAlbumBuilder()
                .WithId(null)
                .WithTitle("Album With Null ID")
                .WithArtist("Artist", "artist")
                .Build();

            // Act
            var releases = ParseAlbum(parser, albumWithNullId);

            // Assert - Source: QobuzParser.cs line 169-172
            releases.Should().BeEmpty("Album with null ID should be skipped");
        }

        /// <summary>
        /// Album with empty ID should be skipped.
        /// Source: QobuzParser.cs line 169-172
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void ParseResponse_WithEmptyAlbumId_ShouldReturnEmpty(string emptyId)
        {
            // Arrange
            var parser = new QobuzParser(_settings, _logger);
            var albumWithEmptyId = new QobuzAlbumBuilder()
                .WithId(emptyId)
                .WithTitle("Album With Empty ID")
                .WithArtist("Artist", "artist")
                .Build();

            // Act
            var releases = ParseAlbum(parser, albumWithEmptyId);

            // Assert - Source: QobuzParser.cs line 169
            releases.Should().BeEmpty("Album with empty ID should be skipped");
        }

        #endregion

        #region Null/Empty Title Tests

        /// <summary>
        /// Album with null title should be skipped in CreateReleaseInfoForQuality.
        /// Source: QobuzParser.cs line 206-211
        /// </summary>
        [Fact]
        public void ParseResponse_WithNullAlbumTitle_ShouldReturnEmpty()
        {
            // Arrange
            var parser = new QobuzParser(_settings, _logger);
            var albumWithNullTitle = new QobuzAlbum
            {
                Id = "nulltitle123",
                Title = null,
                Artist = new QobuzArtist { Name = "Artist", Id = "artist" },
                Streamable = true,
                TracksCount = 10,
                DurationSeconds = 3000
            };

            // Act
            var releases = ParseAlbum(parser, albumWithNullTitle);

            // Assert - Source: QobuzParser.cs line 206-211
            releases.Should().BeEmpty("Album with null title should be skipped");
        }

        /// <summary>
        /// Album with empty title should be skipped in CreateReleaseInfoForQuality.
        /// Source: QobuzParser.cs line 206-211
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void ParseResponse_WithEmptyAlbumTitle_ShouldReturnEmpty(string emptyTitle)
        {
            // Arrange
            var parser = new QobuzParser(_settings, _logger);
            var albumWithEmptyTitle = new QobuzAlbum
            {
                Id = "emptytitle123",
                Title = emptyTitle,
                Artist = new QobuzArtist { Name = "Artist", Id = "artist" },
                Streamable = true,
                TracksCount = 10,
                DurationSeconds = 3000
            };

            // Act
            var releases = ParseAlbum(parser, albumWithEmptyTitle);

            // Assert - Source: QobuzParser.cs line 206-211
            releases.Should().BeEmpty("Album with empty title should be skipped");
        }

        #endregion

        #region Artist Name Fallback Tests

        /// <summary>
        /// Album with null artist name should use "Various Artists" fallback.
        /// Source: QobuzAlbum.cs line 224-227, QobuzParser.cs line 215
        /// </summary>
        [Fact]
        public void ParseResponse_WithNullArtistName_ShouldUseFallback()
        {
            // Arrange
            var parser = new QobuzParser(_settings, _logger);
            var albumWithNullArtist = new QobuzAlbum
            {
                Id = "nullartist123",
                Title = "Album With Null Artist",
                Artist = new QobuzArtist { Name = null, Id = "artist" },
                Streamable = true,
                TracksCount = 10,
                DurationSeconds = 3000
            };

            // Act
            var releases = ParseAlbum(parser, albumWithNullArtist);

            // Assert - Source: QobuzAlbum.cs line 226 (fallback to "Various Artists")
            releases.Should().NotBeEmpty("Null artist name should fallback to Various Artists");
            releases.First().Artist.Should().Be("Various Artists", "GetArtistName returns Various Artists fallback");
        }

        /// <summary>
        /// Album with empty artist name should be filtered (GetArtistName returns empty, not fallback).
        /// Source: QobuzAlbum.cs line 224-227 (?? only checks null), QobuzParser.cs line 220-224
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void ParseResponse_WithEmptyArtistName_ShouldReturnEmpty(string emptyArtist)
        {
            // Arrange
            var parser = new QobuzParser(_settings, _logger);
            var albumWithEmptyArtist = new QobuzAlbum
            {
                Id = "emptyartist123",
                Title = "Album With Empty Artist",
                Artist = new QobuzArtist { Name = emptyArtist, Id = "artist" },
                Streamable = true,
                TracksCount = 10,
                DurationSeconds = 3000
            };

            // Act
            var releases = ParseAlbum(parser, albumWithEmptyArtist);

            // Assert - Source: QobuzParser.cs line 220-224
            // GetArtistName returns "" (not null), so ?? doesn't kick in
            // Parser's IsNullOrWhiteSpace check then filters the album
            releases.Should().BeEmpty("Empty artist name should be filtered by parser");
        }

        #endregion

        #region Search Context Edge Cases

        /// <summary>
        /// FindBestMatchingAlbum should return null when criteria albums list is null.
        /// Source: QobuzParser.cs line 312-313
        /// </summary>
        [Fact]
        public void ParseResponse_WithNullCriteriaAlbums_ShouldUseOriginalTitle()
        {
            // Arrange
            var parser = new QobuzParser(_settings, _logger);

            // Set search context with null albums list
            var criteria = new AlbumSearchCriteria
            {
                Artist = new Artist { Name = "Test Artist" },
                Albums = null
            };
            parser.SetSearchContext(criteria);

            var album = new QobuzAlbumBuilder()
                .WithId("ctxnull123")
                .WithTitle("Original Title")
                .WithArtist("Artist", "artist")
                .Build();

            // Act
            var releases = ParseAlbum(parser, album);

            // Assert - Source: QobuzParser.cs line 312-313
            releases.Should().NotBeEmpty();
            releases[0].Album.Should().Be("Original Title", "Null criteria albums should use original title");
        }

        /// <summary>
        /// FindBestMatchingAlbum should return null when criteria albums list is empty.
        /// Source: QobuzParser.cs line 312-313
        /// </summary>
        [Fact]
        public void ParseResponse_WithEmptyCriteriaAlbums_ShouldUseOriginalTitle()
        {
            // Arrange
            var parser = new QobuzParser(_settings, _logger);

            // Set search context with empty albums list
            var criteria = new AlbumSearchCriteria
            {
                Artist = new Artist { Name = "Test Artist" },
                Albums = new List<Album>()
            };
            parser.SetSearchContext(criteria);

            var album = new QobuzAlbumBuilder()
                .WithId("ctxempty123")
                .WithTitle("Original Title Empty")
                .WithArtist("Artist", "artist")
                .Build();

            // Act
            var releases = ParseAlbum(parser, album);

            // Assert - Source: QobuzParser.cs line 312-313
            releases.Should().NotBeEmpty();
            releases[0].Album.Should().Be("Original Title Empty", "Empty criteria albums should use original title");
        }

        #endregion

        #region Release Info Generation Tests

        /// <summary>
        /// Verify release contains expected metadata from album.
        /// Source: QobuzParser.cs line 240-261
        /// </summary>
        [Fact]
        public void ParseResponse_ShouldGenerateValidReleaseInfo()
        {
            // Arrange
            var parser = new QobuzParser(_settings, _logger);
            var releaseDate = new DateTime(2023, 6, 15);
            var album = new QobuzAlbumBuilder()
                .WithId("releaseinfo123")
                .WithTitle("Release Info Test")
                .WithArtist("Test Artist", "test")
                .WithReleaseDate(releaseDate)
                .Build();

            // Act
            var releases = ParseAlbum(parser, album);

            // Assert - Source: QobuzParser.cs line 240-261
            releases.Should().NotBeEmpty();
            var release = releases.First();
            release.Artist.Should().Be("Test Artist");
            release.Album.Should().Be("Release Info Test");
            release.PublishDate.Should().BeCloseTo(releaseDate, TimeSpan.FromDays(1), "PublishDate should match album release date");
            release.Indexer.Should().Be("Qobuzarr");
            release.DownloadProtocol.Should().Be("QobuzarrDownloadProtocol");
        }

        /// <summary>
        /// Verify different quality levels create separate releases.
        /// Source: QobuzParser.cs line 182-199
        /// </summary>
        [Fact]
        public void ParseResponse_WithHiResAlbum_ShouldCreateMultipleQualityReleases()
        {
            // Arrange
            var parser = new QobuzParser(_settings, _logger);
            var hiResAlbum = new QobuzAlbumBuilder()
                .WithId("hires123")
                .WithTitle("Hi-Res Album")
                .WithArtist("Artist", "artist")
                .AsHiResFlac()
                .Build();

            // Act
            var releases = ParseAlbum(parser, hiResAlbum);

            // Assert - Source: QobuzParser.cs line 182-199
            releases.Should().HaveCount(4, "Hi-Res album should create 4 quality releases");
            releases.Select(r => r.Guid).Should().OnlyHaveUniqueItems("Each quality should have unique GUID");
        }

        /// <summary>
        /// Verify CD quality album creates fewer releases.
        /// Source: QobuzParser.cs line 182-187
        /// </summary>
        [Fact]
        public void ParseResponse_WithCdQualityAlbum_ShouldCreateTwoQualityReleases()
        {
            // Arrange
            var parser = new QobuzParser(_settings, _logger);
            var cdQualityAlbum = new QobuzAlbumBuilder()
                .WithId("cdquality123")
                .WithTitle("CD Quality Album")
                .WithArtist("Artist", "artist")
                .AsCdQualityFlac()
                .Build();

            // Act
            var releases = ParseAlbum(parser, cdQualityAlbum);

            // Assert - Source: QobuzParser.cs line 182-187
            releases.Should().HaveCount(2, "CD quality album should create 2 quality releases (MP3 and FLAC)");
        }

        #endregion

        #region CalculateRelevanceScore Additional Edge Cases

        /// <summary>
        /// CalculateRelevanceScore should handle release with null properties.
        /// Source: QobuzParser.cs line 539-571
        /// </summary>
        [Fact]
        public void CalculateRelevanceScore_WithNullReleaseProperties_ShouldNotThrow()
        {
            // Arrange
            var parser = new QobuzParser(_settings, _logger);
            var release = new ReleaseInfo
            {
                Title = null,
                Artist = null,
                Album = null
            };

            // Act
            var score = parser.CalculateRelevanceScore(release, "test query");

            // Assert - Should not throw, score should be 0
            score.Should().Be(0, "Null properties should result in zero score");
        }

        /// <summary>
        /// CalculateRelevanceScore with all matches should sum all bonuses.
        /// Source: QobuzParser.cs line 550-569
        /// </summary>
        [Fact]
        public void CalculateRelevanceScore_WithAllMatches_ShouldSumAllBonuses()
        {
            // Arrange
            var parser = new QobuzParser(_settings, _logger);
            var recentDate = DateTime.Now.AddMonths(-6);
            var release = new ReleaseInfo
            {
                Title = "Pink Floyd FLAC Album",
                Artist = "Pink Floyd",
                Album = "Pink Floyd Album",
                PublishDate = recentDate
            };

            // Act
            var score = parser.CalculateRelevanceScore(release, "Pink Floyd");

            // Assert - Source: QobuzParser.cs line 550-569
            // Title contains query: +100, Artist match: +50, Album match: +75, FLAC bonus: +10, Recent: +5
            // Total: 240
            score.Should().Be(240, "All matches should sum: 100+50+75+10+5=240");
        }

        #endregion

        #region Helper Methods

        private IndexerResponse CreateIndexerResponse(QobuzParser parser, params QobuzAlbum[] albums)
        {
            var albumSearchResponse = new QobuzAlbumSearchResponse
            {
                Albums = new QobuzSearchResultContainer<QobuzAlbum>
                {
                    Items = albums.ToList()
                }
            };

            var httpResponse = new HttpResponse(
                new HttpRequest("http://test.qobuz.com/api"),
                new HttpHeader(),
                JsonConvert.SerializeObject(albumSearchResponse),
                HttpStatusCode.OK
            );

            return new IndexerResponse(
                new IndexerRequest("http://test.qobuz.com/api", new HttpAccept("application/json")),
                httpResponse
            );
        }

        private List<ReleaseInfo> ParseAlbum(QobuzParser parser, QobuzAlbum album)
        {
            var response = CreateIndexerResponse(parser, album);
            return parser.ParseResponse(response).ToList();
        }

        #endregion
    }
}
