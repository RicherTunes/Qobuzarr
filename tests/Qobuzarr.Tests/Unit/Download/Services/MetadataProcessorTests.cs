using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NzbDrone.Common.Http;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Models;
using Qobuzarr.Tests.Fixtures;
using Qobuzarr.Tests.Helpers;

namespace Qobuzarr.Tests.Unit.Download.Services
{
    /// <summary>
    /// Comprehensive tests for MetadataProcessor covering metadata application,
    /// cover art downloading, and error handling scenarios.
    /// </summary>
    public class MetadataProcessorTests : TestFixtureBase
    {
        private readonly MetadataProcessor _processor;
        private readonly Mock<IQobuzLogger> _mockQobuzLogger;
        private readonly Mock<IFilePathGenerator> _mockFilePathGenerator;

        public MetadataProcessorTests()
        {
            _mockQobuzLogger = new Mock<IQobuzLogger>();
            _mockFilePathGenerator = new Mock<IFilePathGenerator>();
            
            _processor = new MetadataProcessor(
                _mockQobuzLogger.Object,
                _mockFilePathGenerator.Object,
                MockHttpClient.Object
            );
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Action act = () => new MetadataProcessor(null, _mockFilePathGenerator.Object, MockHttpClient.Object);
            act.Should().Throw<ArgumentNullException>().WithMessage("*logger*");
        }

        [Fact]
        public void Constructor_WithNullFilePathGenerator_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Action act = () => new MetadataProcessor(_mockQobuzLogger.Object, null, MockHttpClient.Object);
            act.Should().Throw<ArgumentNullException>().WithMessage("*filePathGenerator*");
        }

        [Fact]
        public void Constructor_WithNullHttpClient_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Action act = () => new MetadataProcessor(_mockQobuzLogger.Object, _mockFilePathGenerator.Object, null);
            act.Should().Throw<ArgumentNullException>().WithMessage("*httpClient*");
        }

        #endregion

        #region ApplyBasicMetadata Tests

        [Fact]
        public void ApplyBasicMetadata_WithNullFilePath_ShouldThrowArgumentException()
        {
            // Arrange
            var track = CreateSampleTrack();
            var album = CreateSampleAlbum();

            // Act & Assert
            _processor.Invoking(x => x.ApplyBasicMetadata(null, track, album))
                     .Should().Throw<ArgumentException>().WithMessage("*filePath*");
        }

        [Fact]
        public void ApplyBasicMetadata_WithNullTrack_ShouldThrowArgumentNullException()
        {
            // Arrange
            var filePath = "test.flac";
            var album = CreateSampleAlbum();

            // Act & Assert
            _processor.Invoking(x => x.ApplyBasicMetadata(filePath, null, album))
                     .Should().Throw<ArgumentNullException>().WithMessage("*track*");
        }

        [Fact]
        public void ApplyBasicMetadata_WithNullAlbum_ShouldThrowArgumentNullException()
        {
            // Arrange
            var filePath = "test.flac";
            var track = CreateSampleTrack();

            // Act & Assert
            _processor.Invoking(x => x.ApplyBasicMetadata(filePath, track, null))
                     .Should().Throw<ArgumentNullException>().WithMessage("*album*");
        }

        // Note: Testing actual file metadata application would require creating actual audio files
        // and using TagLib, which is complex for unit tests. Integration tests would be better.

        #endregion

        #region ApplyOptimizedMetadata Tests

        [Fact]
        public void ApplyOptimizedMetadata_WithNullFilePath_ShouldThrowArgumentException()
        {
            // Arrange
            var trackDownload = CreateSampleTrackDownload();

            // Act & Assert
            _processor.Invoking(x => x.ApplyOptimizedMetadata(null, trackDownload))
                     .Should().Throw<ArgumentException>().WithMessage("*filePath*");
        }

        [Fact]
        public void ApplyOptimizedMetadata_WithNullTrackDownload_ShouldThrowArgumentNullException()
        {
            // Arrange
            var filePath = "test.flac";

            // Act & Assert
            _processor.Invoking(x => x.ApplyOptimizedMetadata(filePath, null))
                     .Should().Throw<ArgumentNullException>().WithMessage("*trackDownload*");
        }

        #endregion

        #region CreateMetadataFileAsync Tests

        [Fact]
        public async Task CreateMetadataFileAsync_WithNullFilePath_ShouldThrowArgumentException()
        {
            // Arrange
            var track = CreateSampleTrack();
            var album = CreateSampleAlbum();

            // Act & Assert
            await _processor.Invoking(x => x.CreateMetadataFileAsync(null, track, album, 27))
                           .Should().ThrowAsync<ArgumentException>()
                           .WithMessage("*trackFilePath*");
        }

        [Fact]
        public async Task CreateMetadataFileAsync_WithNullTrack_ShouldThrowArgumentNullException()
        {
            // Arrange
            var filePath = "test.flac";
            var album = CreateSampleAlbum();

            // Act & Assert
            await _processor.Invoking(x => x.CreateMetadataFileAsync(filePath, null, album, 27))
                           .Should().ThrowAsync<ArgumentNullException>()
                           .WithMessage("*track*");
        }

        [Fact]
        public async Task CreateMetadataFileAsync_WithNullAlbum_ShouldThrowArgumentNullException()
        {
            // Arrange
            var filePath = "test.flac";
            var track = CreateSampleTrack();

            // Act & Assert
            await _processor.Invoking(x => x.CreateMetadataFileAsync(filePath, track, null, 27))
                           .Should().ThrowAsync<ArgumentNullException>()
                           .WithMessage("*album*");
        }

        #endregion

        #region CreateOptimizedMetadataFileAsync Tests

        [Fact]
        public async Task CreateOptimizedMetadataFileAsync_WithNullFilePath_ShouldThrowArgumentException()
        {
            // Arrange
            var trackDownload = CreateSampleTrackDownload();

            // Act & Assert
            await _processor.Invoking(x => x.CreateOptimizedMetadataFileAsync(null, trackDownload))
                           .Should().ThrowAsync<ArgumentException>()
                           .WithMessage("*trackFilePath*");
        }

        [Fact]
        public async Task CreateOptimizedMetadataFileAsync_WithNullTrackDownload_ShouldThrowArgumentNullException()
        {
            // Arrange
            var filePath = "test.flac";

            // Act & Assert
            await _processor.Invoking(x => x.CreateOptimizedMetadataFileAsync(filePath, null))
                           .Should().ThrowAsync<ArgumentNullException>()
                           .WithMessage("*trackDownload*");
        }

        // Note: Testing actual file creation would require file system operations
        // Unit tests focus on parameter validation, integration tests handle file operations

        #endregion

        #region DownloadCoverArtAsync Tests

        [Fact]
        public async Task DownloadCoverArtAsync_WithNullAlbumPath_ShouldThrowArgumentException()
        {
            // Arrange
            var album = CreateSampleAlbum();

            // Act & Assert
            await _processor.Invoking(x => x.DownloadCoverArtAsync(null, album))
                           .Should().ThrowAsync<ArgumentException>()
                           .WithMessage("*albumPath*");
        }

        [Fact]
        public async Task DownloadCoverArtAsync_WithNullAlbum_ShouldThrowArgumentNullException()
        {
            // Arrange
            var albumPath = "/path/to/album";

            // Act & Assert
            await _processor.Invoking(x => x.DownloadCoverArtAsync(albumPath, null))
                           .Should().ThrowAsync<ArgumentNullException>()
                           .WithMessage("*album*");
        }

        [Fact]
        public async Task DownloadCoverArtAsync_WithNoCoverArtUrl_ShouldLogAndReturn()
        {
            // Arrange
            var albumPath = "/path/to/album";
            var album = new QobuzAlbum
            {
                Id = "12345678",
                Title = "Test Album",
                Image = null // No cover art
            };

            // Act
            await _processor.DownloadCoverArtAsync(albumPath, album);

            // Assert
            _mockQobuzLogger.Verify(x => x.Debug(It.Is<string>(s => s.Contains("No cover art URL")), 
                                               It.IsAny<object[]>()), Times.Once);
        }

        [Fact]
        public async Task DownloadCoverArtAsync_WithSuccessfulDownload_ShouldDownloadImage()
        {
            // Arrange
            var albumPath = "/path/to/album";
            var album = CreateSampleAlbum();
            album.Image = new QobuzImage
            {
                Large = "https://example.com/cover.jpg"
            };

            var imageData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG header bytes
            var successResponse = HttpTestHelpers.CreateResponse("", HttpStatusCode.OK);
            successResponse.ResponseData = imageData;

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(successResponse);

            // Act
            await _processor.DownloadCoverArtAsync(albumPath, album);

            // Assert
            MockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Once);
        }

        [Fact]
        public async Task DownloadCoverArtAsync_WithHttpError_ShouldLogWarningAndCacheFailedUrl()
        {
            // Arrange
            var albumPath = "/path/to/album";
            var album = CreateSampleAlbum();
            album.Image = new QobuzImage
            {
                Large = "https://example.com/cover.jpg"
            };

            var errorResponse = HttpTestHelpers.CreateErrorResponse(HttpStatusCode.NotFound, "Not Found");
            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(errorResponse);

            // Act
            await _processor.DownloadCoverArtAsync(albumPath, album);

            // Assert
            _mockQobuzLogger.Verify(x => x.Warn(It.Is<string>(s => s.Contains("Failed to download cover art")), 
                                              It.IsAny<object[]>()), Times.Once);

            // Second call with same URL should skip download
            await _processor.DownloadCoverArtAsync(albumPath, album);
            _mockQobuzLogger.Verify(x => x.Debug(It.Is<string>(s => s.Contains("URL previously failed")), 
                                               It.IsAny<object[]>()), Times.Once);
        }

        [Fact]
        public async Task DownloadCoverArtAsync_WithNetworkException_ShouldLogWarningAndCacheFailedUrl()
        {
            // Arrange
            var albumPath = "/path/to/album";
            var album = CreateSampleAlbum();
            album.Image = new QobuzImage
            {
                Large = "https://example.com/cover.jpg"
            };

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ThrowsAsync(new HttpRequestException("Network error"));

            // Act
            await _processor.DownloadCoverArtAsync(albumPath, album);

            // Assert
            _mockQobuzLogger.Verify(x => x.Warn(It.IsAny<Exception>(), 
                                              It.Is<string>(s => s.Contains("Failed to download cover art")), 
                                              It.IsAny<object[]>()), Times.Once);
        }

        [Fact]
        public async Task DownloadCoverArtAsync_WithPreferenceForHigherQuality_ShouldUseLargeImage()
        {
            // Arrange
            var albumPath = "/path/to/album";
            var album = CreateSampleAlbum();
            album.Image = new QobuzImage
            {
                Small = "https://example.com/cover-small.jpg",
                Medium = "https://example.com/cover-medium.jpg",
                Large = "https://example.com/cover-large.jpg"
            };

            var imageData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
            var successResponse = HttpTestHelpers.CreateResponse("", HttpStatusCode.OK);
            successResponse.ResponseData = imageData;

            HttpRequest capturedRequest = null;
            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .Callback<HttpRequest>(req => capturedRequest = req)
                         .ReturnsAsync(successResponse);

            // Act
            await _processor.DownloadCoverArtAsync(albumPath, album);

            // Assert
            capturedRequest.Should().NotBeNull();
            capturedRequest.Url.ToString().Should().Be("https://example.com/cover-large.jpg");
        }

        [Fact]
        public async Task DownloadCoverArtAsync_WithOnlyMediumQualityAvailable_ShouldUseMediumImage()
        {
            // Arrange
            var albumPath = "/path/to/album";
            var album = CreateSampleAlbum();
            album.Image = new QobuzImage
            {
                Small = "https://example.com/cover-small.jpg",
                Medium = "https://example.com/cover-medium.jpg",
                Large = null
            };

            var imageData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
            var successResponse = HttpTestHelpers.CreateResponse("", HttpStatusCode.OK);
            successResponse.ResponseData = imageData;

            HttpRequest capturedRequest = null;
            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .Callback<HttpRequest>(req => capturedRequest = req)
                         .ReturnsAsync(successResponse);

            // Act
            await _processor.DownloadCoverArtAsync(albumPath, album);

            // Assert
            capturedRequest.Should().NotBeNull();
            capturedRequest.Url.ToString().Should().Be("https://example.com/cover-medium.jpg");
        }

        #endregion

        #region GetQualityDescription Tests

        [Fact]
        public void GetQualityDescription_WithHiResTrack_ShouldReturnHiResDescription()
        {
            // Arrange
            var track = CreateSampleTrack();
            track.MaximumBitDepth = 24;
            track.MaximumSampleRate = 192000;

            // Act
            var description = _processor.GetQualityDescription(track);

            // Assert
            description.Should().Be("Hi-Res FLAC 24bit/192kHz");
        }

        [Fact]
        public void GetQualityDescription_WithCDQualityTrack_ShouldReturnFLACDescription()
        {
            // Arrange
            var track = CreateSampleTrack();
            track.MaximumBitDepth = 16;
            track.MaximumSampleRate = 44100;

            // Act
            var description = _processor.GetQualityDescription(track);

            // Assert
            description.Should().Be("FLAC 16bit/44kHz");
        }

        [Fact]
        public void GetQualityDescription_WithLowQualityTrack_ShouldReturnMP3Description()
        {
            // Arrange
            var track = CreateSampleTrack();
            track.MaximumBitDepth = 0;
            track.MaximumSampleRate = 0;

            // Act
            var description = _processor.GetQualityDescription(track);

            // Assert
            description.Should().Be("MP3 320kbps");
        }

        [Theory]
        [InlineData(24, 96000, "Hi-Res FLAC 24bit/96kHz")]
        [InlineData(24, 48000, "FLAC 24bit/48kHz")]
        [InlineData(16, 48000, "FLAC 16bit/48kHz")]
        [InlineData(8, 22050, "MP3 320kbps")]
        public void GetQualityDescription_WithVariousQualities_ShouldReturnCorrectDescriptions(
            int bitDepth, int sampleRate, string expectedDescription)
        {
            // Arrange
            var track = CreateSampleTrack();
            track.MaximumBitDepth = bitDepth;
            track.MaximumSampleRate = sampleRate;

            // Act
            var description = _processor.GetQualityDescription(track);

            // Assert
            description.Should().Be(expectedDescription);
        }

        #endregion

        #region Thread Safety Tests

        [Fact]
        public async Task DownloadCoverArtAsync_WithConcurrentFailedUrls_ShouldHandleThreadSafely()
        {
            // Arrange
            var albumPath = "/path/to/album";
            var album = CreateSampleAlbum();
            album.Image = new QobuzImage
            {
                Large = "https://example.com/cover.jpg"
            };

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ThrowsAsync(new HttpRequestException("Network error"));

            // Act - Multiple concurrent calls
            var tasks = new Task[5];
            for (int i = 0; i < 5; i++)
            {
                tasks[i] = _processor.DownloadCoverArtAsync(albumPath, album);
            }

            await Task.WhenAll(tasks);

            // Assert - Should not throw and should log appropriately
            _mockQobuzLogger.Verify(x => x.Warn(It.IsAny<Exception>(), 
                                              It.Is<string>(s => s.Contains("Failed to download cover art")), 
                                              It.IsAny<object[]>()), Times.AtLeast(1));
        }

        #endregion

        #region Helper Methods

        private QobuzTrack CreateSampleTrack()
        {
            return new QobuzTrack
            {
                Id = "87654321",
                Title = "Test Track",
                TrackNumber = 1,
                DiscNumber = 1,
                MaximumBitDepth = 16,
                MaximumSampleRate = 44100,
                Performer = new QobuzArtist { Name = "Test Artist" },
                Composer = new QobuzArtist { Name = "Test Composer" }
            };
        }

        private QobuzAlbum CreateSampleAlbum()
        {
            return new QobuzAlbum
            {
                Id = "12345678",
                Title = "Test Album",
                ReleaseDate = new DateTime(2023, 1, 1),
                Artist = new QobuzArtist { Name = "Test Album Artist" },
                Label = new QobuzLabel { Name = "Test Label" },
                Genre = new QobuzGenre { Name = "Rock" }
            };
        }

        private TrackDownload CreateSampleTrackDownload()
        {
            return new TrackDownload
            {
                QobuzTrackId = "87654321",
                Title = "Test Track",
                Artist = "Test Artist",
                AlbumArtist = "Test Album Artist",
                Album = "Test Album",
                TrackNumber = 1,
                DiscNumber = 1,
                ReleaseDate = new DateTime(2023, 1, 1),
                Genre = "Rock",
                Composer = "Test Composer",
                Label = "Test Label",
                Quality = "FLAC 16bit/44kHz",
                MetadataSource = "Qobuz",
                Duration = TimeSpan.FromMinutes(3),
                MusicBrainzTrackId = "track-mb-id",
                MusicBrainzAlbumId = "album-mb-id",
                MusicBrainzArtistId = "artist-mb-id"
            };
        }

        #endregion

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}