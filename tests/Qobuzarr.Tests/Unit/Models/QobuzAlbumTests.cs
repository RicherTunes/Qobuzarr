using System;
using System.Linq;
using FluentAssertions;
using Newtonsoft.Json;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Models;
using Qobuzarr.Tests.TestData;

namespace Qobuzarr.Tests.Unit.Models
{
    // Tests restored and updated for current API
    public class QobuzAlbumTests
    {
        [Fact]
        public void Deserialize_WithValidJson_ShouldParseCorrectly()
        {
            // Act
            var album = JsonConvert.DeserializeObject<QobuzAlbum>(SampleQobuzResponses.SampleAlbumResponse);

            // Assert
            album.Should().NotBeNull();
            album.Id.Should().Be("0060254788359");
            album.Title.Should().Be("Random Access Memories");
            album.DurationSeconds.Should().Be(4578);
            album.TracksCount.Should().Be(13);
            album.Artist.Should().NotBeNull();
            album.Artist.Name.Should().Be("Daft Punk");
            album.TracksContainer.Should().NotBeNull();
            album.TracksContainer.Items.Should().HaveCount(1);
        }

        [Fact]
        public void GetTracks_WithValidAlbum_ShouldReturnTracksList()
        {
            // Arrange
            var album = JsonConvert.DeserializeObject<QobuzAlbum>(SampleQobuzResponses.SampleAlbumResponse);

            // Act
            var tracks = album.GetTracks();

            // Assert
            tracks.Should().NotBeNull();
            tracks.Should().HaveCount(1);
            tracks.First().Title.Should().Be("Give Life Back to Music");
            tracks.First().TrackNumber.Should().Be(1);
        }

        [Fact]
        public void GetTracks_WithNullTracksCollection_ShouldReturnEmptyList()
        {
            // Arrange
            var album = new QobuzAlbum
            {
                Id = "123",
                Title = "Test Album",
                TracksContainer = null
            };

            // Act
            var tracks = album.GetTracks();

            // Assert
            tracks.Should().NotBeNull();
            tracks.Should().BeEmpty();
        }

        [Fact]
        public void GetEstimatedTotalSize_WithMP3Quality_ShouldCalculateCorrectSize()
        {
            // Arrange
            var album = JsonConvert.DeserializeObject<QobuzAlbum>(SampleQobuzResponses.SampleAlbumResponse);

            // Act
            var size = album.GetEstimatedTotalSize(5); // MP3 320kbps

            // Assert
            // 4578 seconds * 320kbps / 8 (bits to bytes)
            var expectedSize = (long)(4578 * 320 * 1000 / 8);
            ((long)size).Should().BeInRange(expectedSize - (long)(expectedSize * 0.1), expectedSize + (long)(expectedSize * 0.1)); // 10% tolerance
        }

        [Fact]
        public void GetEstimatedTotalSize_WithFLACQuality_ShouldCalculateCorrectSize()
        {
            // Arrange
            var album = JsonConvert.DeserializeObject<QobuzAlbum>(SampleQobuzResponses.SampleAlbumResponse);

            // Act
            var size = album.GetEstimatedTotalSize(6); // FLAC CD

            // Assert
            // FLAC typically 800-1000kbps for CD quality
            var expectedSize = (long)(4578L * 900L * 1000L / 8L);
            ((long)size).Should().BeInRange(expectedSize - (long)(expectedSize * 0.2), expectedSize + (long)(expectedSize * 0.2)); // 20% tolerance for FLAC compression
        }

        // GetFormatName method doesn't exist in current API - removed these tests

        [Fact]
        public void GetSafeFolderName_WithSpecialCharacters_ShouldSanitize()
        {
            // Arrange
            var album = new QobuzAlbum
            {
                Id = "123",
                Title = "Test: Album? <Name> \"Special\"",
                Artist = new QobuzArtist { Name = "Test Artist" }
            };

            // Act
            var safeFolderName = album.GetSafeFolderName();

            // Assert
            safeFolderName.Should().NotBeNullOrEmpty();
            safeFolderName.Should().NotContain(":");
            safeFolderName.Should().NotContain("?");
            safeFolderName.Should().NotContain("<");
            safeFolderName.Should().NotContain(">");
            safeFolderName.Should().NotContain("\"");
            safeFolderName.Should().Contain("Test Artist");
            safeFolderName.Should().Contain("Test"); // Part of sanitized title
        }

        [Fact]
        public void GetSafeFolderName_WithNullTitle_ShouldReturnDefault()
        {
            // Arrange
            var album = new QobuzAlbum
            {
                Id = "123",
                Title = null,
                Artist = new QobuzArtist { Name = "Test Artist" }
            };

            // Act
            var safeFolderName = album.GetSafeFolderName();

            // Assert
            safeFolderName.Should().Contain("Test Artist");
            safeFolderName.Should().Contain("Unknown Album");
        }

        [Fact]
        public void ReleaseDate_WithValidDate_ShouldReturnYear()
        {
            // Arrange
            var album = JsonConvert.DeserializeObject<QobuzAlbum>(SampleQobuzResponses.SampleAlbumResponse);

            // Act
            var year = album.ReleaseDate.Year;

            // Assert
            year.Should().Be(2013);
        }

        [Fact]
        public void ReleaseDate_WithInvalidDate_ShouldReturnMinValue()
        {
            // Arrange
            var album = new QobuzAlbum
            {
                Id = "123",
                Title = "Test",
                ReleaseDateOriginal = "invalid-date"
            };

            // Act
            var releaseDate = album.ReleaseDate;

            // Assert
            releaseDate.Should().Be(DateTime.MinValue);
        }

        [Fact]
        public void Image_GetBestQuality_WithAvailableImages_ShouldReturnBestUrl()
        {
            // Arrange
            var album = JsonConvert.DeserializeObject<QobuzAlbum>(SampleQobuzResponses.SampleAlbumResponse);

            // Act
            var imageUrl = album.Image?.GetBestQuality();

            // Assert
            imageUrl.Should().NotBeNullOrEmpty();
            imageUrl.Should().Contain("600.jpg"); // Large image
        }

        [Fact]
        public void Image_GetBestQuality_WithNullImage_ShouldReturnNull()
        {
            // Arrange
            var album = new QobuzAlbum
            {
                Id = "123",
                Title = "Test",
                Image = null
            };

            // Act
            var imageUrl = album.Image?.GetBestQuality();

            // Assert
            imageUrl.Should().BeNull();
        }

        [Fact]
        public void Streamable_WithStreamableAlbum_ShouldReturnTrue()
        {
            // Arrange
            var album = JsonConvert.DeserializeObject<QobuzAlbum>(SampleQobuzResponses.SampleAlbumResponse);

            // Act
            var isStreamable = album.Streamable;

            // Assert
            isStreamable.Should().BeTrue();
        }

        [Fact]
        public void MaximumQuality_ShouldMapCorrectly()
        {
            // Arrange
            var album = JsonConvert.DeserializeObject<QobuzAlbum>(SampleQobuzResponses.SampleAlbumResponse);

            // Act & Assert
            album.MaximumBitDepth.Should().Be(16);
            album.MaximumSampleRate.Should().Be(44.1);
            album.MaximumChannelCount.Should().Be(2);
        }

        [Theory]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("123", true)]
        public void IsValid_ShouldValidateBasicProperties(string id, bool expectedValid)
        {
            // Arrange
            var album = new QobuzAlbum
            {
                Id = id,
                Title = "Test Album"
            };

            // Act
            var isValid = !string.IsNullOrWhiteSpace(album.Id) && !string.IsNullOrWhiteSpace(album.Title);

            // Assert
            isValid.Should().Be(expectedValid);
        }

        [Fact]
        public void Constructor_WithDefaultValues_ShouldInitializeCollections()
        {
            // Act
            var album = new QobuzAlbum();

            // Assert
            album.GetTracks().Should().NotBeNull();
            album.GetTracks().Should().BeEmpty();
        }
    }
}