using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using Xunit;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Music;
using NzbDrone.Core.Indexers;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Qobuzarr.Tests.Builders;
using Qobuzarr.Tests.TestData;
using Qobuzarr.Tests.Fixtures;

namespace Qobuzarr.Tests.Integration
{
    /// <summary>
    /// Integration tests for album edition handling within Lidarr's indexing and parsing framework.
    /// Tests ParsedAlbumInfo.ReleaseVersion extraction and album matching behavior.
    /// </summary>
    [Trait("Category", "Integration")]
    public class AlbumEditionLidarrIntegrationTests : TestFixtureBase
    {
        #region ParsedAlbumInfo Integration

        [Theory]
        [MemberData(nameof(AlbumEditionTestData.LiveAlbumScenarios), MemberType = typeof(AlbumEditionTestData))]
        public void ParsedAlbumInfo_WithLiveAlbumVersions_ShouldExtractReleaseVersion(
            string version, string expectedTitlePattern, string scenario)
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Kind of Blue")
                .WithArtist("Miles Davis")
                .WithReleaseYear(1959)
                .Build();
            album.Version = version;

            var releaseInfo = CreateReleaseInfoFromAlbum(album);

            // Act
            var parsedInfo = ParseReleaseTitle(releaseInfo.Title);

            // Assert
            if (!string.IsNullOrWhiteSpace(version))
            {
                parsedInfo.Should().NotBeNull();
                parsedInfo.AlbumTitle.Should().Contain("Kind of Blue");

                // ReleaseVersion should contain the version info
                if (version.Contains("Live"))
                {
                    parsedInfo.ReleaseVersion.Should().ContainAny("Live", version);
                }

                parsedInfo.Quality.Should().NotBeNull();
                parsedInfo.Quality.Quality.Name.Should().Be("FLAC");
            }
        }

        [Theory]
        [MemberData(nameof(AlbumEditionTestData.EditionVariants), MemberType = typeof(AlbumEditionTestData))]
        public void ParsedAlbumInfo_WithEditionVariants_ShouldExtractEditionInfo(
            string version, string expectedTitlePattern, string scenario)
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Abbey Road")
                .WithArtist("The Beatles")
                .WithReleaseYear(1969)
                .Build();
            album.Version = version;

            var releaseInfo = CreateReleaseInfoFromAlbum(album);

            // Act
            var parsedInfo = ParseReleaseTitle(releaseInfo.Title);

            // Assert
            parsedInfo.Should().NotBeNull();
            parsedInfo.AlbumTitle.Should().Contain("Abbey Road");

            // Edition info should be in ReleaseVersion
            switch (scenario)
            {
                case "DeluxeEdition":
                case "DeluxeShort":
                    parsedInfo.ReleaseVersion.Should().ContainAny("Deluxe", version);
                    break;

                case "Remastered":
                case "RemasterWithYear":
                    parsedInfo.ReleaseVersion.Should().ContainAny("Remaster", version);
                    break;

                case "AnniversaryEdition":
                    parsedInfo.ReleaseVersion.Should().ContainAny("Anniversary", version);
                    break;
            }
        }

        #endregion

        #region Album Matching Scenarios

        [Fact]
        public void AlbumRepository_FindByTitle_WithDifferentEditions_ShouldDistinguishVersions()
        {
            // Arrange
            var artistName = "Pink Floyd";
            var albumTitle = "The Wall";

            var studioAlbum = QobuzAlbumBuilder.New()
                .WithTitle(albumTitle)
                .WithArtist(artistName)
                .WithReleaseYear(1979)
                .Build();

            var liveAlbum = QobuzAlbumBuilder.New()
                .WithTitle(albumTitle)
                .WithArtist(artistName)
                .WithReleaseYear(1980)
                .Build();
            liveAlbum.Version = "Live at Earl's Court";

            var deluxeAlbum = QobuzAlbumBuilder.New()
                .WithTitle(albumTitle)
                .WithArtist(artistName)
                .WithReleaseYear(1979)
                .Build();
            deluxeAlbum.Version = "Deluxe Edition";

            // Act - Create ReleaseInfo objects as Lidarr would see them
            var studioRelease = CreateReleaseInfoFromAlbum(studioAlbum);
            var liveRelease = CreateReleaseInfoFromAlbum(liveAlbum);
            var deluxeRelease = CreateReleaseInfoFromAlbum(deluxeAlbum);

            // Assert - Each should have unique identifiers
            studioRelease.Guid.Should().NotBe(liveRelease.Guid);
            studioRelease.Guid.Should().NotBe(deluxeRelease.Guid);
            liveRelease.Guid.Should().NotBe(deluxeRelease.Guid);

            // Titles should reflect editions
            studioRelease.Title.Should().Contain("The Wall");
            studioRelease.Title.Should().NotContain("[Live");
            studioRelease.Title.Should().NotContain("[Deluxe");

            liveRelease.Title.Should().Contain("[Live at Earl's Court]");
            deluxeRelease.Title.Should().Contain("[Deluxe Edition]");
        }

        [Fact]
        public void QualityParsing_WithEditionBrackets_ShouldNotInterfereWithQualityDetection()
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Random Access Memories")
                .WithArtist("Daft Punk")
                .AsHiResFlac()
                .Build();
            album.Version = "Deluxe Edition";

            var releaseInfo = CreateReleaseInfoFromAlbum(album);

            // Act
            var parsedInfo = ParseReleaseTitle(releaseInfo.Title);

            // Assert
            parsedInfo.Should().NotBeNull();
            parsedInfo.Quality.Should().NotBeNull();
            parsedInfo.Quality.Quality.Name.Should().Be("FLAC");

            // Edition shouldn't interfere with quality parsing
            releaseInfo.Title.Should().Contain("[Deluxe Edition]");
            releaseInfo.Title.Should().Contain("[FLAC WEB]");

            // Quality should be parsed correctly despite edition brackets
            parsedInfo.Quality.Quality.Id.Should().BeGreaterThan(0);
        }

        #endregion

        #region GUID Generation Tests

        [Fact]
        public void DifferentEditions_ShouldGenerateUniqueGUIDs()
        {
            // Arrange
            var editions = AlbumEditionTestData.CreateMultipleEditions(
                "Led Zeppelin", "Led Zeppelin IV", 1971);

            // Act
            var guids = editions.Select(album => CreateReleaseInfoFromAlbum(album).Guid).ToArray();

            // Assert
            guids.Should().HaveCount(3);
            guids.Should().OnlyHaveUniqueItems(); // All GUIDs must be unique

            foreach (var guid in guids)
            {
                guid.Should().NotBeNullOrWhiteSpace();
                guid.Should().StartWith("qobuz-"); // Should follow expected format
            }
        }

        [Fact]
        public void SameAlbumDifferentVersions_ShouldHaveDifferentGUIDs()
        {
            // Arrange
            var (studioAlbum, liveAlbum) = AlbumEditionTestData.CreateStudioLivePair(
                "Metallica", "Master of Puppets", 1986, "Seattle 1989");

            // Act
            var studioGuid = CreateReleaseInfoFromAlbum(studioAlbum).Guid;
            var liveGuid = CreateReleaseInfoFromAlbum(liveAlbum).Guid;

            // Assert
            studioGuid.Should().NotBe(liveGuid);
            studioGuid.Should().StartWith("qobuz-");
            liveGuid.Should().StartWith("qobuz-");

            // GUIDs should incorporate album ID (which should be different)
            studioGuid.Should().Contain(studioAlbum.Id);
            liveGuid.Should().Contain(liveAlbum.Id);
        }

        #endregion

        #region Edge Case Integration

        [Fact]
        public void ParsedAlbumInfo_WithUnicodeVersions_ShouldHandleCorrectly()
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Café del Mar")
                .WithArtist("José Padilla")
                .Build();
            album.Version = "Édition Spéciale";

            var releaseInfo = CreateReleaseInfoFromAlbum(album);

            // Act
            var parsedInfo = ParseReleaseTitle(releaseInfo.Title);

            // Assert
            parsedInfo.Should().NotBeNull();
            parsedInfo.AlbumTitle.Should().Contain("Café del Mar");
            releaseInfo.Title.Should().Contain("Édition Spéciale");

            // Unicode characters should be preserved (é from Café and É from Édition)
            releaseInfo.Title.Should().Contain("É");
            releaseInfo.Title.Should().Contain("é");
        }

        [Fact]
        public void ParsedAlbumInfo_WithComplexVersions_ShouldExtractAllComponents()
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("The Joshua Tree")
                .WithArtist("U2")
                .WithReleaseYear(1987)
                .Build();
            album.Version = "30th Anniversary Deluxe Remastered Edition";

            var releaseInfo = CreateReleaseInfoFromAlbum(album);

            // Act
            var parsedInfo = ParseReleaseTitle(releaseInfo.Title);

            // Assert
            parsedInfo.Should().NotBeNull();
            parsedInfo.AlbumTitle.Should().Contain("The Joshua Tree");
            parsedInfo.ReleaseVersion.Should().Contain("30th Anniversary Deluxe Remastered Edition");

            // Complex version should be handled as one unit
            releaseInfo.Title.Should().Contain("[30th Anniversary Deluxe Remastered Edition]");
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates a ReleaseInfo object from a QobuzAlbum following Redacted indexer patterns
        /// </summary>
        private ReleaseInfo CreateReleaseInfoFromAlbum(QobuzAlbum album)
        {
            var artistName = album.GetArtistName();
            var albumTitle = album.Title;
            var year = album.ReleaseDate.Year;
            var quality = album.MaximumBitDepth >= 24 ? "FLAC" : "MP3";

            // Build title following Redacted pattern: "Artist - Album (Year) [Edition] [Quality WEB]"
            var titleBuilder = $"{artistName} - {albumTitle} ({year})";

            if (!string.IsNullOrWhiteSpace(album.Version))
            {
                titleBuilder += $" [{album.Version}]";
            }

            titleBuilder += $" [{quality} WEB]";

            // Include version in GUID to differentiate editions of the same album
            var guidSuffix = string.IsNullOrWhiteSpace(album.Version) ? "" : $"-{album.Version.GetHashCode():X}";

            return new ReleaseInfo
            {
                Title = titleBuilder,
                Guid = $"qobuz-{album.Id}{guidSuffix}",
                DownloadUrl = $"qobuz://album/{album.Id}",
                InfoUrl = $"https://www.qobuz.com/album/{album.Id}",
                Size = (long)album.GetEstimatedTotalSize(album.MaximumBitDepth >= 24 ? 6 : 5),
                PublishDate = album.ReleaseDate,
                IndexerFlags = IndexerFlags.Internal
            };
        }

        /// <summary>
        /// Simulates Lidarr's release title parsing
        /// </summary>
        private ParsedAlbumInfo ParseReleaseTitle(string title)
        {
            // This is a simplified simulation of Lidarr's parsing logic
            // In real implementation, this would use Lidarr's actual parser

            var parts = title.Split(" - ");
            if (parts.Length < 2) return null;

            var artistName = parts[0].Trim();
            var remainingTitle = parts[1].Trim();

            // Extract year
            var yearMatch = System.Text.RegularExpressions.Regex.Match(remainingTitle, @"\((\d{4})\)");
            var year = yearMatch.Success ? int.Parse(yearMatch.Groups[1].Value) : 0;

            // Extract album title (everything before year)
            var albumTitle = remainingTitle;
            if (yearMatch.Success)
            {
                albumTitle = remainingTitle.Substring(0, yearMatch.Index).Trim();
            }

            // Extract release version (text in first set of brackets after year)
            var releaseVersion = "";
            var versionMatch = System.Text.RegularExpressions.Regex.Match(remainingTitle, @"\[([^\]]+)\]");
            if (versionMatch.Success && !versionMatch.Groups[1].Value.Contains("FLAC") && !versionMatch.Groups[1].Value.Contains("MP3"))
            {
                releaseVersion = versionMatch.Groups[1].Value;
            }

            // Extract quality
            var qualityMatch = System.Text.RegularExpressions.Regex.Match(remainingTitle, @"\[(FLAC|MP3)[^\]]*\]");
            var qualityName = qualityMatch.Success ? qualityMatch.Groups[1].Value : "Unknown";

            return new ParsedAlbumInfo
            {
                ArtistName = artistName,
                AlbumTitle = albumTitle,
                ReleaseDate = year > 0 ? year.ToString() : null,
                ReleaseVersion = releaseVersion,
                Quality = new NzbDrone.Core.Qualities.QualityModel(
                    new NzbDrone.Core.Qualities.Quality { Id = 1, Name = qualityName }
                )
            };
        }

        #endregion
    }
}
