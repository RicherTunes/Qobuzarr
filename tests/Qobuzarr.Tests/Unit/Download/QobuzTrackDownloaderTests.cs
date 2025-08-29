using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NzbDrone.Common.Http;
using Xunit;
using Lidarr.Plugin.Qobuzarr.API;
// DISABLED: QobuzTrackDownloader, SafeMetadataOptimizer and related services have been removed
// using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Abstractions;
// using Lidarr.Plugin.Qobuzarr.Download.Services;
// using Lidarr.Plugin.Qobuzarr.Services;
// using Lidarr.Plugin.Qobuzarr.Services.Metadata;
using Lidarr.Plugin.Qobuzarr.Indexers;
using NLog;
using Qobuzarr.Tests.Helpers;

namespace Qobuzarr.Tests.Unit.Download
{
    /// <summary>
    /// DISABLED: Basic unit tests for QobuzTrackDownloader using the current API
    /// QobuzTrackDownloader has been removed - functionality consolidated into other services
    /// </summary>
    /*
    public class QobuzTrackDownloaderTests : IDisposable
    {
        private readonly Mock<IQobuzApiClient> _mockApiClient;
        private readonly Mock<IHttpClient> _mockHttpClient;
        private readonly Mock<IQobuzLogger> _mockLogger;
        private readonly Mock<IStreamUrlProvider> _mockStreamUrlProvider;
        private readonly Mock<IAudioFileDownloader> _mockAudioFileDownloader;
        private readonly Mock<IMetadataProcessor> _mockMetadataProcessor;
        private readonly Mock<IFilePathGenerator> _mockFilePathGenerator;
        private readonly Mock<IQualityFallbackProvider> _mockQualityFallbackProvider;
        private readonly SafeMetadataOptimizer _metadataOptimizer;
        private readonly QobuzTrackDownloader _downloader;
        private readonly string _testOutputPath;

        public QobuzTrackDownloaderTests()
        {
            _mockApiClient = new Mock<IQobuzApiClient>();
            _mockHttpClient = new Mock<IHttpClient>();
            _mockLogger = new Mock<IQobuzLogger>();
            _mockStreamUrlProvider = new Mock<IStreamUrlProvider>();
            _mockAudioFileDownloader = new Mock<IAudioFileDownloader>();
            _mockMetadataProcessor = new Mock<IMetadataProcessor>();
            _mockFilePathGenerator = new Mock<IFilePathGenerator>();
            _mockQualityFallbackProvider = new Mock<IQualityFallbackProvider>();
            
            // Create SafeMetadataOptimizer with null (tests don't exercise it anyway)
            _metadataOptimizer = null;

            _downloader = new QobuzTrackDownloader(
                _mockStreamUrlProvider.Object,
                _mockAudioFileDownloader.Object, 
                _mockMetadataProcessor.Object,
                _mockFilePathGenerator.Object,
                _mockQualityFallbackProvider.Object,
                _mockLogger.Object,
                _metadataOptimizer);
            
            // Create temp directory for test outputs
            _testOutputPath = Path.Combine(Path.GetTempPath(), $"QobuzTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testOutputPath);
        }

        public void Dispose()
        {
            // Clean up test directory
            if (Directory.Exists(_testOutputPath))
            {
                try
                {
                    Directory.Delete(_testOutputPath, true);
                }
                catch
                {
                    // Ignore cleanup errors in tests
                }
            }
        }

        [Fact]
        public void Constructor_WithValidParameters_ShouldInitialize()
        {
            // Act & Assert
            var downloader = new QobuzTrackDownloader(
                _mockStreamUrlProvider.Object,
                _mockAudioFileDownloader.Object, 
                _mockMetadataProcessor.Object,
                _mockFilePathGenerator.Object,
                _mockQualityFallbackProvider.Object,
                _mockLogger.Object,
                _metadataOptimizer);
            downloader.Should().NotBeNull();
        }

        [Fact]
        public async Task DownloadTrackAsync_WithValidInputs_ShouldCallApiClient()
        {
            // Arrange
            var track = CreateTestTrack();
            var album = CreateTestAlbum();
            var streamUrl = "https://stream.qobuz.com/track/123456";
            var audioData = CreateTestAudioData();
            var progress = new Progress<double>();
            var outputFilePath = Path.Combine(_testOutputPath, "test.flac");

            // Setup stream URL provider
            _mockStreamUrlProvider.Setup(x => x.GetStreamUrlAsync(track.Id, 27))
                .ReturnsAsync(streamUrl);

            // Setup audio file downloader
            _mockAudioFileDownloader.Setup(x => x.DownloadAudioFileAsync(streamUrl, outputFilePath, progress, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Setup metadata processor
            _mockMetadataProcessor.Setup(x => x.ApplyBasicMetadata(outputFilePath, track, album));

            // Setup file path generator
            _mockFilePathGenerator.Setup(x => x.GenerateFileName(track, album, 27))
                .Returns("test.flac");

            // Act
            var result = await _downloader.DownloadTrackAsync(
                track, album, _testOutputPath, 27, progress, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Should().Be(outputFilePath);
            _mockStreamUrlProvider.Verify(x => x.GetStreamUrlAsync(track.Id, 27), Times.Once);
            _mockAudioFileDownloader.Verify(x => x.DownloadAudioFileAsync(streamUrl, outputFilePath, progress, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DownloadTrackAsync_WithCancellation_ShouldRespectCancellationToken()
        {
            // Arrange
            var track = CreateTestTrack();
            var album = CreateTestAlbum();
            var progress = new Progress<double>();
            var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel(); // Pre-cancel the token

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                _downloader.DownloadTrackAsync(track, album, _testOutputPath, 27, progress, cancellationTokenSource.Token));
        }

        #region Helper Methods

        private QobuzTrack CreateTestTrack()
        {
            return new QobuzTrack
            {
                Id = "123456",
                Title = "Test Track",
                TrackNumber = 1,
                DiscNumber = 1,
                DurationSeconds = 180,
                Streamable = true,
                // Properties SampleRate, BitDepth, Album don't exist in current QobuzTrack model
                // SampleRate = 44100,
                // BitDepth = 16,
                // Album = CreateTestAlbum(),
                Performer = new QobuzArtist 
                { 
                    Name = "Test Artist",
                    Id = "artist123"
                }
            };
        }

        private QobuzAlbum CreateTestAlbum()
        {
            return new QobuzAlbum
            {
                Id = "album123",
                Title = "Test Album",
                Artist = new QobuzArtist 
                { 
                    Name = "Test Artist",
                    Id = "artist123"
                },
                Label = new QobuzLabel { Name = "Test Label" },
                // ReleaseDate is read-only computed property
                Genre = new QobuzGenre { Name = "Test Genre" },
                TracksCount = 10,
                DurationSeconds = 3600,
                Streamable = true
            };
        }

        private byte[] CreateTestAudioData()
        {
            // Create minimal FLAC-like data for testing
            var data = new byte[1024];
            // Add FLAC signature
            data[0] = 0x66; // 'f'
            data[1] = 0x4C; // 'L'
            data[2] = 0x61; // 'a'
            data[3] = 0x43; // 'C'
            return data;
        }

        #endregion
    }
    */
}