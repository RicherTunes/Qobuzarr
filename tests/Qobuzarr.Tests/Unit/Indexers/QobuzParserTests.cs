using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Xunit;
using NLog;
using Newtonsoft.Json;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Models;
using Qobuzarr.Tests.Builders;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Indexers;
using NzbDrone.Common.Http;
using Lidarr.Plugin.Qobuzarr.Download;

namespace Qobuzarr.Tests.Unit.Indexers
{
    /// <summary>
    /// Tests for QobuzParser - focusing on ParseResponse behavior including:
    /// - Album filtering (non-streamable, singles, compilations)
    /// - Multi-quality release generation
    /// - ReleaseInfo field population
    /// 
    /// All tests use the public ParseResponse API instead of reflection.
    /// Title generation details are tested in TitleGeneratorTests.cs.
    /// Size calculation details are tested in QualitySizeCalculatorTests.cs.
    /// </summary>
    public class QobuzParserTests
    {
        private readonly QobuzParser _parser;
        private readonly QobuzIndexerSettings _settings;

        public QobuzParserTests()
        {
            _settings = new QobuzIndexerSettings
            {
                IncludeSingles = true,
                IncludeCompilations = true
            };

            var logger = LogManager.GetCurrentClassLogger();
            _parser = new QobuzParser(_settings, logger);
        }

        #region Helper Methods

        /// <summary>
        /// Creates an IndexerResponse containing a serialized album search response.
        /// This allows testing ParseResponse with controlled album data.
        /// </summary>
        private IndexerResponse CreateIndexerResponse(params QobuzAlbum[] albums)
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

        /// <summary>
        /// Parses a single album through the public ParseResponse API.
        /// </summary>
        private List<ReleaseInfo> ParseAlbum(QobuzAlbum album)
        {
            var response = CreateIndexerResponse(album);
            return _parser.ParseResponse(response).ToList();
        }

        /// <summary>
        /// Parses multiple albums through the public ParseResponse API.
        /// </summary>
        private List<ReleaseInfo> ParseAlbums(params QobuzAlbum[] albums)
        {
            var response = CreateIndexerResponse(albums);
            return _parser.ParseResponse(response).ToList();
        }

        #endregion

        #region Album Filtering Tests

        /// <summary>
        /// Non-streamable albums should be filtered out and return no releases.
        /// </summary>
        [Fact]
        public void ParseResponse_WithNonStreamableAlbum_ShouldReturnEmpty()
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("nonstream123")
                .WithTitle("Non-Streamable Album")
                .WithArtist("Test Artist", "test")
                .AsNotStreamable()
                .Build();

            // Act
            var releases = ParseAlbum(album);

            // Assert
            releases.Should().BeEmpty("Non-streamable albums should be filtered out");
        }

        /// <summary>
        /// Albums with empty artist name should be rejected.
        /// </summary>
        [Theory]
        [InlineData("", "Valid Album")]
        [InlineData("  ", "Valid Album")]
        public void ParseResponse_WithEmptyArtist_ShouldReturnEmpty(string artistName, string albumTitle)
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("invalid123")
                .WithTitle(albumTitle)
                .WithArtist(artistName, "test-slug")
                .Build();

            // Act
            var releases = ParseAlbum(album);

            // Assert
            releases.Should().BeEmpty("Albums with empty artist should be rejected");
        }

        /// <summary>
        /// Albums with empty title should be rejected.
        /// </summary>
        [Theory]
        [InlineData("Valid Artist", "")]
        [InlineData("Valid Artist", "   ")]
        public void ParseResponse_WithEmptyTitle_ShouldReturnEmpty(string artistName, string albumTitle)
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("invalid456")
                .WithTitle(albumTitle)
                .WithArtist(artistName, "test-slug")
                .Build();

            // Act
            var releases = ParseAlbum(album);

            // Assert
            releases.Should().BeEmpty("Albums with empty title should be rejected");
        }

        #endregion

        #region Multi-Quality Release Generation Tests

        /// <summary>
        /// Valid streamable albums should generate multiple quality releases (MP3, FLAC, Hi-Res).
        /// </summary>
        [Fact]
        public void ParseResponse_WithHiResAlbum_ShouldGenerateMultipleQualityReleases()
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("hires123")
                .WithTitle("Hi-Res Album")
                .WithArtist("Test Artist", "test")
                .AsHiResFlac()
                .Build();

            // Act
            var releases = ParseAlbum(album);

            // Assert
            releases.Should().HaveCountGreaterThan(1, "Hi-Res albums should generate multiple quality releases");

            // Verify different quality formats are present
            releases.Should().Contain(r => r.Title.Contains("MP3"), "Should include MP3 release");
            releases.Should().Contain(r => r.Title.Contains("FLAC"), "Should include FLAC release");

            // Verify unique GUIDs per quality
            var guids = releases.Select(r => r.Guid).ToList();
            guids.Should().OnlyHaveUniqueItems("Each quality release should have unique GUID");

            // Verify GUID format includes quality ID
            releases.Should().Contain(r => r.Guid.StartsWith("qobuz-hires123-"),
                "GUIDs should follow format qobuz-{albumId}-{qualityId}");
        }

        /// <summary>
        /// CD quality albums should generate at least MP3 and FLAC releases.
        /// </summary>
        [Fact]
        public void ParseResponse_WithCdQualityAlbum_ShouldGenerateMp3AndFlac()
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("cd123")
                .WithTitle("CD Quality Album")
                .WithArtist("Test Artist", "test")
                .AsCdQualityFlac()
                .Build();

            // Act
            var releases = ParseAlbum(album);

            // Assert
            releases.Should().HaveCountGreaterOrEqualTo(2, "Should have at least MP3 and FLAC");
            releases.Should().Contain(r => r.Title.Contains("MP3"));
            releases.Should().Contain(r => r.Title.Contains("FLAC"));
        }

        #endregion

        #region ReleaseInfo Field Validation Tests

        /// <summary>
        /// Verify all required ReleaseInfo fields are populated correctly.
        /// </summary>
        [Fact]
        public void ParseResponse_WithValidAlbum_ShouldPopulateAllRequiredFields()
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("fields123")
                .WithTitle("Field Test Album")
                .WithArtist("Field Artist", "field-artist")
                .WithReleaseDate(new DateTime(2023, 6, 15))
                .WithTracks(12, 250) // 12 tracks, ~4.2 minutes each = 50 minutes total
                .AsCdQualityFlac()
                .Build();

            // Act
            var releases = ParseAlbum(album);

            // Assert - Pick a specific quality release by GUID suffix
            var flacRelease = releases.FirstOrDefault(r =>
                r.Guid.EndsWith($"-{(int)QobuzAudioQuality.FLACLossless}"));

            flacRelease.Should().NotBeNull("Should have a FLAC lossless release");
            flacRelease.Guid.Should().Be($"qobuz-fields123-{(int)QobuzAudioQuality.FLACLossless}");
            flacRelease.Artist.Should().Be("Field Artist");
            flacRelease.Album.Should().Be("Field Test Album");
            flacRelease.PublishDate.Date.Should().Be(new DateTime(2023, 6, 15).Date);
            flacRelease.Indexer.Should().Be("Qobuzarr");
            flacRelease.DownloadUrl.Should().StartWith("qobuz://album/fields123/");
            flacRelease.Size.Should().BeGreaterThan(0, "Size should be calculated");

            // Title should contain quality markers
            flacRelease.Title.Should().Contain("[WEB]");
            flacRelease.Title.Should().Contain("FLAC");
        }

        /// <summary>
        /// Verify download protocol is set correctly.
        /// </summary>
        [Fact]
        public void ParseResponse_WithValidAlbum_ShouldSetDownloadProtocol()
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("proto123")
                .WithTitle("Protocol Test")
                .WithArtist("Test Artist", "test")
                .Build();

            // Act
            var releases = ParseAlbum(album);

            // Assert
            releases.Should().NotBeEmpty();
            var release = releases.First();

            // Support both legacy enum-based protocol and new string-based plugin protocol
            object dpObj = release.DownloadProtocol;
            if (dpObj is string s)
            {
                s.Should().Be(nameof(QobuzarrDownloadProtocol));
            }
            else
            {
                // Enum path: we expect Unknown in legacy environments
                dpObj.ToString().Should().Be("Unknown");
            }
        }

        #endregion

        #region Unique Album ID Preservation Tests

        /// <summary>
        /// Multiple albums should each generate releases with their own unique GUIDs.
        /// </summary>
        [Fact]
        public void ParseResponse_WithMultipleAlbums_ShouldPreserveUniqueAlbumIds()
        {
            // Arrange
            var albums = new[]
            {
                new QobuzAlbumBuilder().WithId("unique1").WithTitle("Album One").WithArtist("Artist", "artist").Build(),
                new QobuzAlbumBuilder().WithId("unique2").WithTitle("Album Two").WithArtist("Artist", "artist").Build(),
                new QobuzAlbumBuilder().WithId("unique3").WithTitle("Album Three").WithArtist("Artist", "artist").Build()
            };

            // Act
            var releases = ParseAlbums(albums);

            // Assert
            var guids = releases.Select(r => r.Guid).ToList();
            guids.Should().OnlyHaveUniqueItems("Each album+quality should have unique GUID");

            // Verify GUIDs contain original album IDs
            releases.Should().Contain(r => r.Guid.Contains("unique1"));
            releases.Should().Contain(r => r.Guid.Contains("unique2"));
            releases.Should().Contain(r => r.Guid.Contains("unique3"));
        }

        #endregion

        #region End-to-End Integration Tests

        /// <summary>
        /// End-to-end test verifying the complete quality detection chain produces Lidarr-compatible releases.
        /// </summary>
        [Fact]
        public void ParseResponse_EndToEnd_ShouldProduceLidarrCompatibleReleases()
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("e2e123")
                .WithTitle("End-to-End Test Album")
                .WithArtist("E2E Artist", "e2e-artist")
                .WithReleaseDate(new DateTime(2023, 12, 25))
                .WithTracks(10, 270) // 10 tracks, ~4.5 minutes each = 45 minutes total
                .AsHiResFlac()
                .Build();

            // Act
            var releases = ParseAlbum(album);

            // Assert
            releases.Should().NotBeEmpty("Should generate at least one release");

            foreach (var release in releases)
            {
                // Critical validations for Lidarr compatibility
                release.Guid.Should().NotBeNullOrEmpty("GUID is required for Lidarr");
                release.Title.Should().NotBeNullOrEmpty("Title is used for quality detection");
                release.Artist.Should().Be("E2E Artist");
                release.Album.Should().Be("End-to-End Test Album");
                release.DownloadUrl.Should().StartWith("qobuz://");
                release.Size.Should().BeGreaterThan(0, "Size must be calculated");
                release.Indexer.Should().Be("Qobuzarr");
                release.PublishDate.Date.Should().Be(new DateTime(2023, 12, 25).Date);

                // The most critical test: Title should contain quality markers
                release.Title.Should().MatchRegex(@"\[(MP3 320kbps|FLAC|FLAC 24bit \d+kHz)\]",
                    "Title must contain quality markers that Lidarr can parse");
                release.Title.Should().Contain("[WEB]", "Should indicate WEB release");
            }

            // Verify we have multiple quality variants
            releases.Should().Contain(r => r.Title.Contains("MP3"), "Should include MP3 variant");
            releases.Should().Contain(r => r.Title.Contains("FLAC"), "Should include FLAC variant");
        }

        /// <summary>
        /// Verify title generation includes expected quality markers for wiring to TitleGenerator.
        /// </summary>
        [Fact]
        public void ParseResponse_ShouldGenerateTitlesWithExpectedQualityMarkers()
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("wiring123")
                .WithTitle("Wiring Test Album")
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseDate(new DateTime(2023, 1, 1))
                .AsHiResFlac()
                .Build();

            // Act
            var releases = ParseAlbum(album);

            // Assert - Verify titles contain quality markers (wiring is correct)
            releases.Should().NotBeEmpty("QobuzParser should generate releases");
            releases.Should().Contain(r => r.Title.Contains("[WEB]"), "All releases should have [WEB] marker");
            releases.Should().Contain(r => r.Title.Contains("FLAC") || r.Title.Contains("MP3"),
                "Releases should have format markers");
        }

        #endregion
    }
}
