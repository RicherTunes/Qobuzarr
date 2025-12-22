using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;
using NLog;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Models;
using Qobuzarr.Tests.Builders;
using Lidarr.Plugin.Qobuzarr.Constants;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Common.Http;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Indexers.Parsing;

namespace Qobuzarr.Tests.Unit.Indexers
{
    /// <summary>
    /// Tests for QobuzParser - focusing on ReleaseInfo creation, album filtering, and size calculation.
    /// 
    /// NOTE: Title generation logic is tested in TitleGeneratorTests.cs which tests TitleGenerator directly.
    /// These tests focus on QobuzParser's integration with TitleGenerator and other internal methods.
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

        #region QobuzParser Integration - Verifies wiring to TitleGenerator

        /// <summary>
        /// Verify QobuzParser correctly delegates title generation to TitleGenerator.
        /// This is a wiring test - detailed title format tests are in TitleGeneratorTests.
        /// </summary>
        [Fact]
        public void ConvertAlbumToReleases_ShouldGenerateTitlesWithExpectedQualityMarkers()
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
            var method = typeof(QobuzParser).GetMethod("ConvertAlbumToReleases", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var releases = (IEnumerable<ReleaseInfo>)method.Invoke(_parser, new object[] { album, "test query" });
            var releaseList = releases.ToList();

            // Assert - Verify titles contain quality markers (wiring is correct)
            releaseList.Should().NotBeEmpty("QobuzParser should generate releases");
            releaseList.Should().Contain(r => r.Title.Contains("[WEB]"), "All releases should have [WEB] marker");
            releaseList.Should().Contain(r => r.Title.Contains("FLAC") || r.Title.Contains("MP3"), 
                "Releases should have format markers");
        }

        #endregion

        #region ReleaseInfo Creation Tests

        /// <summary>
        /// Test that ReleaseInfo objects are created correctly for each quality
        /// </summary>
        [Theory]
        [InlineData(QobuzAudioQuality.MP3320)]
        [InlineData(QobuzAudioQuality.FLACLossless)]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit96kHz)]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit192Khz)]
        public void CreateReleaseInfoForQuality_WithValidAlbum_ShouldCreateValidReleaseInfo(
            QobuzAudioQuality quality)
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("release123")
                .WithTitle("Release Test Album")
                .WithArtist("Release Artist", "release-artist")
                .WithReleaseDate(new DateTime(2023, 6, 15))
                .WithTracks(12, 250) // 12 tracks, ~4.2 minutes each = 50 minutes total
                .Build();

            // Act - Using reflection to test private method
            var method = typeof(QobuzParser).GetMethod("CreateReleaseInfoForQuality", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var result = (ReleaseInfo)method.Invoke(_parser, new object[] { album, quality, "test query" });

            // Assert
            result.Should().NotBeNull();
            result.Guid.Should().StartWith("qobuz-release123-");
            result.Guid.Should().Contain(((int)quality).ToString(), "GUID should include quality ID");
            result.Artist.Should().Be("Release Artist");
            result.Album.Should().Be("Release Test Album");
            result.PublishDate.Date.Should().Be(new DateTime(2023, 6, 15).Date, "Date should match regardless of timezone");
            result.Indexer.Should().Be("Qobuzarr");
            // Support both legacy enum-based protocol and new string-based plugin protocol
            object dpObj = result.DownloadProtocol;
            if (dpObj is string s)
            {
                s.Should().Be(nameof(QobuzarrDownloadProtocol));
            }
            else
            {
                // Enum path: we expect Unknown in legacy environments
                dpObj.ToString().Should().Be("Unknown");
            }
            result.Size.Should().BeGreaterThan(0, "Size should be calculated based on quality");
            
            // Critical: Title should contain quality markers for Lidarr detection
            result.Title.Should().NotBeNullOrEmpty();
            result.Title.Should().Contain("[WEB]");
        }

        /// <summary>
        /// Test that albums with missing critical data are rejected
        /// </summary>
        [Theory]
        [InlineData("", "Valid Album", "Empty artist should be rejected")]
        [InlineData("Valid Artist", "", "Empty album title should be rejected")]
        [InlineData("  ", "Valid Album", "Whitespace-only artist should be rejected")]
        [InlineData("Valid Artist", "   ", "Whitespace-only album should be rejected")]
        public void CreateReleaseInfoForQuality_WithInvalidData_ShouldReturnNull(
            string artistName, string albumTitle, string reason)
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("invalid123")
                .WithTitle(albumTitle)
                .WithArtist(artistName, "test-slug")
                .Build();

            // Act
            var method = typeof(QobuzParser).GetMethod("CreateReleaseInfoForQuality", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var result = (ReleaseInfo)method.Invoke(_parser, new object[] { album, QobuzAudioQuality.FLACLossless, "test" });

            // Assert
            result.Should().BeNull(reason);
        }

        #endregion

        #region Album Filtering Tests

        /// <summary>
        /// Test filtering of non-streamable albums
        /// </summary>
        [Fact]
        public void ConvertAlbumToReleases_WithNonStreamableAlbum_ShouldReturnEmptyList()
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("nonstream123")
                .WithTitle("Non-Streamable Album")
                .WithArtist("Test Artist", "test")
                .AsNotStreamable()
                .Build();

            // Act
            var method = typeof(QobuzParser).GetMethod("ConvertAlbumToReleases", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var result = (IEnumerable<ReleaseInfo>)method.Invoke(_parser, new object[] { album, "test query" });

            // Assert
            result.Should().BeEmpty("Non-streamable albums should be filtered out");
        }

        /// <summary>
        /// Test that valid streamable albums generate multiple quality releases
        /// </summary>
        [Fact]
        public void ConvertAlbumToReleases_WithValidStreamableAlbum_ShouldGenerateMultipleQualityReleases()
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("multi123")
                .WithTitle("Multi Quality Album")
                .WithArtist("Test Artist", "test")
                .AsHiResFlac()
                .Build();

            // Act
            var method = typeof(QobuzParser).GetMethod("ConvertAlbumToReleases", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var result = (IEnumerable<ReleaseInfo>)method.Invoke(_parser, new object[] { album, "test query" });

            // Assert
            var releases = result.ToList();
            releases.Should().HaveCountGreaterThan(1, "Should generate releases for multiple qualities");
            
            // Should have at least MP3 and FLAC releases
            releases.Should().Contain(r => r.Title.Contains("MP3"), "Should include MP3 release");
            releases.Should().Contain(r => r.Title.Contains("FLAC"), "Should include FLAC release");
            
            // All releases should have unique GUIDs
            var guids = releases.Select(r => r.Guid).ToList();
            guids.Should().OnlyHaveUniqueItems("Each quality release should have unique GUID");
        }

        #endregion

        #region Size Calculation Tests

        [Theory]
        [InlineData(QobuzAudioQuality.MP3320, 320000)]
        [InlineData(QobuzAudioQuality.FLACLossless, 1411200)]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit96kHz, 4608000)]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit192Khz, 9216000)]
        public void CalculateSizeForQuality_WithDifferentQualities_ShouldCalculateCorrectSize(
            QobuzAudioQuality quality, int expectedBitrate)
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("size123")
                .WithTitle("Size Test")
                .WithArtist("Test", "test")
                .WithTracks(15, 240) // 15 tracks, 4 minutes each = 60 minutes total
                .Build();

            // Act
            var method = typeof(QobuzParser).GetMethod("CalculateSizeForQuality", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var result = (long)method.Invoke(_parser, new object[] { album, quality });

            // Assert
            var expectedSize = 3600 * (expectedBitrate / 8.0); // 1 hour in bytes
            result.Should().Be((long)expectedSize, $"Size calculation for {quality} should match expected formula");
            result.Should().BeGreaterThan(0, "Calculated size should be positive");
        }

        #endregion

        #region Critical Integration Test

        /// <summary>
        /// End-to-end integration test that verifies the complete quality detection chain
        /// This test simulates the exact flow that happens when Lidarr processes our releases
        /// </summary>
        [Fact]
        public void QobuzParser_EndToEndQualityDetection_ShouldProduceLidarrCompatibleReleases()
        {
            // Arrange
            var testAlbum = new QobuzAlbumBuilder()
                .WithId("integration123")
                .WithTitle("Integration Test Album")
                .WithArtist("Integration Artist", "integration-artist")
                .WithReleaseDate(new DateTime(2023, 12, 25))
                .WithTracks(10, 270) // 10 tracks, ~4.5 minutes each = 45 minutes total
                .AsHiResFlac()
                .Build();

            // Act
            var method = typeof(QobuzParser).GetMethod("ConvertAlbumToReleases", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var releases = (IEnumerable<ReleaseInfo>)method.Invoke(_parser, new object[] { testAlbum, "test search" });

            // Assert
            var releaseList = releases.ToList();
            releaseList.Should().NotBeEmpty("Should generate at least one release");

            foreach (var release in releaseList)
            {
                // Critical validations for Lidarr compatibility
                release.Should().NotBeNull();
                release.Guid.Should().NotBeNullOrEmpty("GUID is required for Lidarr");
                release.Title.Should().NotBeNullOrEmpty("Title is used for quality detection");
                release.Artist.Should().Be("Integration Artist");
                release.Album.Should().Be("Integration Test Album");
                release.DownloadUrl.Should().StartWith("qobuz://");
                release.Size.Should().BeGreaterThan(0, "Size must be calculated");
                release.Indexer.Should().Be("Qobuzarr");
                release.PublishDate.Date.Should().Be(new DateTime(2023, 12, 25).Date, "Date should match regardless of timezone");

                // The most critical test: Title should contain quality markers
                release.Title.Should().MatchRegex(@"\[(MP3 320kbps|FLAC|FLAC 24bit \d+kHz)\]",
                    "Title must contain quality markers that Lidarr can parse");
                release.Title.Should().Contain("[WEB]", "Should indicate WEB release");
            }

            // Verify we have multiple quality variants
            var titles = releaseList.Select(r => r.Title).ToList();
            titles.Should().Contain(t => t.Contains("MP3"), "Should include MP3 variant");
            titles.Should().Contain(t => t.Contains("FLAC"), "Should include FLAC variant");
        }

        #endregion

        #region Unique Album ID Preservation Tests

        /// <summary>
        /// Test that each album generates releases with unique GUIDs based on album ID.
        /// This is a critical integration test for QobuzParser's release generation.
        /// </summary>
        [Fact]
        public void ConvertAlbumToReleases_MultipleAlbums_ShouldPreserveUniqueAlbumIds()
        {
            // Arrange: Multiple albums with different IDs
            var albums = new[]
            {
                new QobuzAlbumBuilder().WithId("unique1").WithTitle("Album One").WithArtist("Artist", "artist").Build(),
                new QobuzAlbumBuilder().WithId("unique2").WithTitle("Album Two").WithArtist("Artist", "artist").Build(),
                new QobuzAlbumBuilder().WithId("unique3").WithTitle("Album Three").WithArtist("Artist", "artist").Build()
            };

            // Act: Create releases for each album
            var releases = new List<ReleaseInfo>();
            var method = typeof(QobuzParser).GetMethod("ConvertAlbumToReleases", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            foreach (var album in albums)
            {
                var albumReleases = (IEnumerable<ReleaseInfo>)method.Invoke(_parser, new object[] { album, "test" });
                releases.AddRange(albumReleases);
            }

            // Assert: Each album should have unique releases based on its own ID
            var guids = releases.Select(r => r.Guid).ToList();
            guids.Should().OnlyHaveUniqueItems("Each album should generate unique release GUIDs");
            
            // Verify GUIDs contain original album IDs
            releases.Should().Contain(r => r.Guid.Contains("unique1"), "Should have releases for album unique1");
            releases.Should().Contain(r => r.Guid.Contains("unique2"), "Should have releases for album unique2");
            releases.Should().Contain(r => r.Guid.Contains("unique3"), "Should have releases for album unique3");
        }

        #endregion
    }
}
