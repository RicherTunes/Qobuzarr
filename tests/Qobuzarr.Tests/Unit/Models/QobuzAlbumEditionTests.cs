using System;
using System.Linq;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Models;
using Qobuzarr.Tests.Builders;
using Qobuzarr.Tests.TestData;

namespace Qobuzarr.Tests.Unit.Models
{
    /// <summary>
    /// Comprehensive tests for album edition differentiation and Version field handling.
    /// Tests the critical album edition matching solution for Qobuzarr.
    /// </summary>
    public class QobuzAlbumEditionTests
    {
        #region Version Field Handling

        [Theory]
        [MemberData(nameof(AlbumEditionTestData.LiveAlbumScenarios), MemberType = typeof(AlbumEditionTestData))]
        public void GetFullTitle_WithLiveAlbumVersions_ShouldIncludeVersionInParentheses(
            string version, string expectedTitlePattern, string scenario)
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Test Album")
                .WithArtist("Test Artist")
                .Build();
            album.Version = version;

            // Act
            var fullTitle = album.GetFullTitle();

            // Assert
            if (version.IsNotNullOrWhiteSpace())
            {
                fullTitle.Should().Contain($"({version})");
                fullTitle.Should().Be($"Test Album ({version})");
            }
            else
            {
                fullTitle.Should().Be("Test Album");
            }
        }

        [Theory]
        [MemberData(nameof(AlbumEditionTestData.EditionVariants), MemberType = typeof(AlbumEditionTestData))]
        public void GetFullTitle_WithEditionVariants_ShouldFormatCorrectly(
            string version, string expectedTitlePattern, string scenario)
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Test Album")
                .WithArtist("Test Artist")
                .Build();
            album.Version = version;

            // Act
            var fullTitle = album.GetFullTitle();

            // Assert
            fullTitle.Should().Be($"Test Album ({version})");
            fullTitle.Should().NotContain("(("); // No double parentheses
            fullTitle.Should().NotContain("))"); // No double parentheses
        }

        [Theory]
        [MemberData(nameof(AlbumEditionTestData.VersionFieldEdgeCases), MemberType = typeof(AlbumEditionTestData))]
        public void GetFullTitle_WithVersionEdgeCases_ShouldHandleGracefully(
            string version, string expectedTitlePattern, string scenario)
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Test Album")
                .WithArtist("Test Artist")
                .Build();
            album.Version = version;

            // Act
            var fullTitle = album.GetFullTitle();

            // Assert
            if (string.IsNullOrWhiteSpace(version))
            {
                fullTitle.Should().Be("Test Album");
                fullTitle.Should().NotContain("()"); // No empty parentheses
            }
            else
            {
                fullTitle.Should().Contain($"({version})");
            }
        }

        [Fact]
        public void GetFullTitle_WhenVersionAlreadyInTitle_ShouldNotDuplicate()
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Test Album Live")
                .WithArtist("Test Artist")
                .Build();
            album.Version = "Live";

            // Act
            var fullTitle = album.GetFullTitle();

            // Assert
            fullTitle.Should().Be("Test Album Live");
            fullTitle.Should().NotContain("(Live)"); // Should not duplicate
        }

        [Fact]
        public void GetFullTitle_WhenVersionPartiallyInTitle_ShouldAddFullVersion()
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Test Album Live")
                .WithArtist("Test Artist")
                .Build();
            album.Version = "Live at Wembley Stadium";

            // Act
            var fullTitle = album.GetFullTitle();

            // Assert
            fullTitle.Should().Be("Test Album Live (Live at Wembley Stadium)");
            // Should add full version even though "Live" is in title
        }

        #endregion

        #region Album Edition Differentiation

        [Fact]
        public void StudioVsLiveAlbums_ShouldHaveDifferentFullTitles()
        {
            // Arrange
            var (studioAlbum, liveAlbum) = AlbumEditionTestData.CreateStudioLivePair(
                "Pink Floyd", "The Wall", 1979, "Earl's Court");

            // Act
            var studioTitle = studioAlbum.GetFullTitle();
            var liveTitle = liveAlbum.GetFullTitle();

            // Assert
            studioTitle.Should().Be("The Wall");
            liveTitle.Should().Be("The Wall (Live at Earl's Court)");
            studioTitle.Should().NotBe(liveTitle);
        }

        [Fact]
        public void MultipleEditions_ShouldHaveDistinctFullTitles()
        {
            // Arrange
            var editions = AlbumEditionTestData.CreateMultipleEditions(
                "The Beatles", "Abbey Road", 1969);

            // Act
            var titles = editions.Select(album => album.GetFullTitle()).ToArray();

            // Assert
            titles.Should().HaveCount(3);
            titles.Should().OnlyHaveUniqueItems(); // All titles should be different
            
            titles[0].Should().Be("Abbey Road"); // Standard edition
            titles[1].Should().Be("Abbey Road (Deluxe Edition)");
            titles[2].Should().Be("Abbey Road (1979 Remaster)");
        }

        [Fact]
        public void CreateLiveAlbum_WithVenueAndDate_ShouldGenerateCorrectMetadata()
        {
            // Arrange
            var concertDate = new DateTime(2023, 7, 15);
            var venue = "Madison Square Garden";

            // Act
            var liveAlbum = AlbumEditionTestData.CreateLiveAlbum(venue, concertDate);

            // Assert
            liveAlbum.Title.Should().Be($"Live at {venue}");
            liveAlbum.ReleaseDate.Date.Should().Be(concertDate.Date);
            liveAlbum.Id.Should().Be("live_album_madison_square_garden");
        }

        #endregion

        #region Complex Edition Scenarios

        [Theory]
        [MemberData(nameof(AlbumEditionTestData.ComplexEditionScenarios), MemberType = typeof(AlbumEditionTestData))]
        public void GetFullTitle_WithComplexEditionScenarios_ShouldHandleCorrectly(
            string version, string expectedTitlePattern, string scenario)
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Test Album")
                .WithArtist("Test Artist")
                .WithReleaseYear(1990) // For testing remaster year scenarios
                .Build();
            album.Version = version;

            // Act
            var fullTitle = album.GetFullTitle();

            // Assert
            fullTitle.Should().Be($"Test Album ({version})");
            fullTitle.Should().NotBeNullOrWhiteSpace();
            
            // Specific validations based on scenario
            switch (scenario)
            {
                case "MultipleEditionMarkers":
                    fullTitle.Should().Contain("Deluxe");
                    fullTitle.Should().Contain("Remastered");
                    fullTitle.Should().Contain("Edition");
                    break;
                    
                case "LiveDeluxeCombination":
                    fullTitle.Should().Contain("Live");
                    fullTitle.Should().Contain("Deluxe");
                    break;
                    
                case "RemasterYearDifferentFromAlbumYear":
                    fullTitle.Should().Contain("2020"); // Remaster year
                    fullTitle.Should().Contain("Remaster");
                    break;
            }
        }

        #endregion

        #region Edge Case Validation

        [Fact]
        public void GetFullTitle_WithNullTitle_ShouldReturnDefaultWithVersion()
        {
            // Arrange
            var album = new QobuzAlbum
            {
                Title = null,
                Version = "Live Edition"
            };

            // Act
            var fullTitle = album.GetFullTitle();

            // Assert
            fullTitle.Should().Be("Unknown Album (Live Edition)");
        }

        [Fact]
        public void GetFullTitle_WithEmptyTitleAndVersion_ShouldReturnDefault()
        {
            // Arrange
            var album = new QobuzAlbum
            {
                Title = "",
                Version = ""
            };

            // Act
            var fullTitle = album.GetFullTitle();

            // Assert
            fullTitle.Should().Be("Unknown Album");
        }

        [Fact]
        public void GetFullTitle_WithUnicodeCharactersInVersion_ShouldPreserveUnicode()
        {
            // Arrange
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Test Album")
                .Build();
            album.Version = "Édition Spéciale Français";

            // Act
            var fullTitle = album.GetFullTitle();

            // Assert
            fullTitle.Should().Be("Test Album (Édition Spéciale Français)");
            fullTitle.Should().Contain("É");
            fullTitle.Should().Contain("ç");
        }

        [Fact]
        public void GetFullTitle_WithVeryLongVersion_ShouldIncludeFullVersion()
        {
            // Arrange
            var longVersion = "25th Anniversary Deluxe Remastered Edition with Bonus Tracks and Rarities";
            var album = QobuzAlbumBuilder.New()
                .WithTitle("Test Album")
                .Build();
            album.Version = longVersion;

            // Act
            var fullTitle = album.GetFullTitle();

            // Assert
            fullTitle.Should().Be($"Test Album ({longVersion})");
            fullTitle.Length.Should().BeGreaterThan(50); // Should include the full long version
        }

        #endregion

        #region Equality and Comparison

        [Fact]
        public void AlbumsWithDifferentVersions_ShouldNotBeConsideredEqual()
        {
            // Arrange
            var studioAlbum = QobuzAlbumBuilder.New()
                .WithId("album123")
                .WithTitle("Test Album")
                .WithArtist("Test Artist")
                .Build();

            var liveAlbum = QobuzAlbumBuilder.New()
                .WithId("album124") // Different ID
                .WithTitle("Test Album") // Same title
                .WithArtist("Test Artist") // Same artist
                .Build();
            liveAlbum.Version = "Live";

            // Act & Assert
            studioAlbum.Id.Should().NotBe(liveAlbum.Id);
            studioAlbum.GetFullTitle().Should().NotBe(liveAlbum.GetFullTitle());
            studioAlbum.Version.Should().NotBe(liveAlbum.Version);
        }

        [Fact]
        public void AlbumsWithSameVersions_ShouldHaveConsistentBehavior()
        {
            // Arrange
            var album1 = QobuzAlbumBuilder.New()
                .WithTitle("Test Album")
                .WithArtist("Test Artist")
                .Build();
            album1.Version = "Deluxe Edition";

            var album2 = QobuzAlbumBuilder.New()
                .WithTitle("Test Album")
                .WithArtist("Test Artist")
                .Build();
            album2.Version = "Deluxe Edition";

            // Act
            var title1 = album1.GetFullTitle();
            var title2 = album2.GetFullTitle();

            // Assert
            title1.Should().Be(title2);
            album1.Version.Should().Be(album2.Version);
        }

        #endregion
    }
}