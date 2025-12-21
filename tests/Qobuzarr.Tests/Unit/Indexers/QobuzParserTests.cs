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
    /// Critical tests for QobuzParser quality detection and title generation
    /// These tests verify that releases will be properly recognized by Lidarr's quality detection
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

        #region Title Generation Tests - Critical for Lidarr Quality Detection

        /// <summary>
        /// Test that verifies our release titles match Lidarr's regex patterns exactly
        /// This is THE most critical test - if these fail, quality detection won't work
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
                .WithTracks(10, 270) // 10 tracks, ~4.5 minutes each = 45 minutes total
                .Build();

            // Act - Using reflection to test private method
            var method = typeof(QobuzParser).GetMethod("GenerateQualitySpecificTitle", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var result = (string)method.Invoke(_parser, new object[] { album, quality, 2023 });

            // Assert
            result.Should().Be(expectedTitle, 
                $"Title for {quality} must exactly match expected format for Lidarr quality detection");
        }

        /// <summary>
        /// Test explicit content handling in titles
        /// </summary>
        [Fact]
        public void GenerateQualitySpecificTitle_WithExplicitContent_ShouldIncludeExplicitTag()
        {
            // Arrange
            var logger = LogManager.GetCurrentClassLogger();
            var titleGenerator = new TitleGenerator(logger);
            var album = new QobuzAlbumBuilder()
                .WithId("explicit123")
                .WithTitle("Explicit Album")
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseDate(new DateTime(2023, 1, 1))
                .AsExplicit() // Mark as explicit content
                .Build();

            // Act
            var result = titleGenerator.GenerateQualitySpecificTitle(album, QobuzAudioQuality.MP3320, 2023);

            // Assert
            result.Should().Contain("[Explicit]", "Explicit content should be marked in the title");
            result.Should().Be("Test Artist - Explicit Album (2023) [Explicit] [MP3 320kbps] [WEB]");
        }

        /// <summary>
        /// Test handling of albums without release year
        /// </summary>
        [Fact]
        public void GenerateQualitySpecificTitle_WithoutReleaseYear_ShouldNotIncludeYearInTitle()
        {
            // Arrange
            var album = new QobuzAlbumBuilder()
                .WithId("noyear123")
                .WithTitle("No Year Album")
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseDate(new DateTime(1800, 1, 1)) // Very old date
                .Build();

            // Act
            var method = typeof(QobuzParser).GetMethod("GenerateQualitySpecificTitle", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var result = (string)method.Invoke(_parser, new object[] { album, QobuzAudioQuality.FLACLossless, 0 });

            // Assert
            result.Should().NotContain("(1800)", "Very old years should be excluded");
            result.Should().Be("Test Artist - No Year Album [FLAC] [WEB]");
        }

        #endregion

        #region Lidarr Quality Pattern Verification Tests

        /// <summary>
        /// Critical test: Verify that our generated titles will be parsed correctly by Lidarr
        /// This simulates Lidarr's quality detection logic
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
            var method = typeof(QobuzParser).GetMethod("GenerateQualitySpecificTitle", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var result = (string)method.Invoke(_parser, new object[] { album, quality, 2023 });

            // Assert
            result.Should().Contain(expectedMarker, reason);
        }

        /// <summary>
        /// Test that verifies all quality markers are present in generated titles
        /// This ensures Lidarr's regex patterns will match
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

            var method = typeof(QobuzParser).GetMethod("GenerateQualitySpecificTitle", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Act & Assert
            foreach (var (quality, expectedMarkers) in qualityExpectations)
            {
                var result = (string)method.Invoke(_parser, new object[] { album, quality, 2023 });

                foreach (var marker in expectedMarkers)
                {
                    result.Should().Contain(marker, 
                        $"Title for {quality} should contain '{marker}' for Lidarr quality detection");
                }
            }
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

        #region Context-Aware Title Generation Tests (Regression Prevention)

        /// <summary>
        /// CRITICAL REGRESSION TEST: Ensures context-aware logic doesn't apply same title to all albums
        /// This test would have caught the duplicate title bug where all search results showed identical titles
        /// </summary>
        [Fact]
        public void ContextAware_WithMultipleAlbums_ShouldGenerateUniqueTitle()
        {
            // Arrange: Create multiple DIFFERENT Qobuz albums
            var album1 = new QobuzAlbumBuilder()
                .WithId("album1")
                .WithTitle("First Album Title")
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseDate(new DateTime(2023, 1, 1))
                .Build();

            var album2 = new QobuzAlbumBuilder()
                .WithId("album2")
                .WithTitle("Second Album Title") // DIFFERENT title
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseDate(new DateTime(2023, 2, 1))
                .Build();

            var album3 = new QobuzAlbumBuilder()
                .WithId("album3")
                .WithTitle("Third Album Title") // DIFFERENT title
                .WithArtist("Test Artist", "test-artist")
                .WithReleaseDate(new DateTime(2023, 3, 1))
                .Build();

            // Mock search criteria with ONE specific album (simulates typical Lidarr search)
            var mockAlbum = new NzbDrone.Core.Music.Album
            {
                Id = 1,
                Title = "I Had the Blues but I Shook Them Loose (live at Brixton)",
                ReleaseDate = new DateTime(2020, 1, 1)
            };
            
            // Set search criteria context (this triggers the bug scenario)
            _parser.SetSearchContext(new AlbumSearchCriteria
            {
                Albums = new List<NzbDrone.Core.Music.Album> { mockAlbum }
            });

            // Act: Process each album individually
            var method = typeof(QobuzParser).GetMethod("GenerateQualitySpecificTitle", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var title1 = (string)method.Invoke(_parser, new object[] { album1, QobuzAudioQuality.FLACLossless, 2023 });
            var title2 = (string)method.Invoke(_parser, new object[] { album2, QobuzAudioQuality.FLACLossless, 2023 });
            var title3 = (string)method.Invoke(_parser, new object[] { album3, QobuzAudioQuality.FLACLossless, 2023 });

            // Assert: Each album should get ITS OWN title, not the context title
            title1.Should().Contain("First Album Title", "Album 1 should keep its own title");
            title2.Should().Contain("Second Album Title", "Album 2 should keep its own title");
            title3.Should().Contain("Third Album Title", "Album 3 should keep its own title");

            // Critical: Titles should NOT all be the same (this is the bug)
            title1.Should().NotBe(title2, "Different albums should have different titles");
            title2.Should().NotBe(title3, "Different albums should have different titles");
            title1.Should().NotBe(title3, "Different albums should have different titles");

            // Should NOT all contain the context album title
            var contextTitle = "I Had the Blues but I Shook Them Loose (live at Brixton)";
            var allTitlesHaveContextTitle = new[] { title1, title2, title3 }.All(t => t.Contains(contextTitle));
            allTitlesHaveContextTitle.Should().BeFalse("Context title should not be applied to every album");
        }

        /// <summary>
        /// Test that context-aware logic only applies when there's an actual match
        /// </summary>
        [Fact]
        public void ContextAware_WithNoMatchingAlbum_ShouldUseOriginalTitle()
        {
            // Arrange: Create album that won't match context
            var unmatchedAlbum = new QobuzAlbumBuilder()
                .WithId("unmatched")
                .WithTitle("Completely Different Album")
                .WithArtist("Different Artist", "different")
                .WithReleaseDate(new DateTime(2019, 1, 1))
                .Build();

            // Set context for a completely different album
            var mockAlbum = new NzbDrone.Core.Music.Album
            {
                Id = 1,
                Title = "Specific Context Album",
                ReleaseDate = new DateTime(2020, 1, 1)
            };
            
            _parser.SetSearchContext(new AlbumSearchCriteria
            {
                Albums = new List<NzbDrone.Core.Music.Album> { mockAlbum }
            });

            // Act
            var method = typeof(QobuzParser).GetMethod("GenerateQualitySpecificTitle", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var result = (string)method.Invoke(_parser, new object[] { unmatchedAlbum, QobuzAudioQuality.FLACLossless, 2019 });

            // Assert: Should use original album title, not context title
            result.Should().Contain("Completely Different Album", "Should use original album title");
            result.Should().NotContain("Specific Context Album", "Should not use context title for unmatched album");
        }

        /// <summary>
        /// Test album ID uniqueness preservation in context-aware mode
        /// </summary>
        [Fact]
        public void ContextAware_WithMultipleAlbums_ShouldPreserveAlbumIdUniqueness()
        {
            // Arrange: Multiple albums with different IDs
            var albums = new[]
            {
                new QobuzAlbumBuilder().WithId("unique1").WithTitle("Album One").WithArtist("Artist", "artist").Build(),
                new QobuzAlbumBuilder().WithId("unique2").WithTitle("Album Two").WithArtist("Artist", "artist").Build(),
                new QobuzAlbumBuilder().WithId("unique3").WithTitle("Album Three").WithArtist("Artist", "artist").Build()
            };

            // Set context
            _parser.SetSearchContext(new AlbumSearchCriteria
            {
                Albums = new List<NzbDrone.Core.Music.Album> 
                { 
                    new NzbDrone.Core.Music.Album { Id = 1, Title = "Context Album", ReleaseDate = new DateTime(2020, 1, 1) }
                }
            });

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
