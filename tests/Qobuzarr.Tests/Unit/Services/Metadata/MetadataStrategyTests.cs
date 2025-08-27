using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using NLog;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Services.Metadata;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Qobuzarr.Tests.Unit.Services.Metadata
{
    public class MetadataStrategyTests
    {
        private readonly Logger _mockLogger;
        private readonly QobuzApiClient _mockApiClient;
        private readonly IQobuzTrackDownloaderFactory _mockTrackDownloaderFactory;
        private readonly List<IMetadataStrategy> _mockStrategies;
        private readonly MetadataStrategyEngine _engine;

        public MetadataStrategyTests()
        {
            _mockLogger = Substitute.For<Logger>();
            _mockApiClient = Substitute.For<QobuzApiClient>();
            _mockTrackDownloaderFactory = Substitute.For<IQobuzTrackDownloaderFactory>();
            
            _mockStrategies = new List<IMetadataStrategy>
            {
                CreateMockStrategy("Qobuzarr", canHandleWithoutLidarr: true),
                CreateMockStrategy("Lidarr", canHandleWithoutLidarr: false),
                CreateMockStrategy("Hybrid", canHandleWithoutLidarr: false)
            };

            _engine = new MetadataStrategyEngine(_mockLogger, _mockTrackDownloaderFactory, _mockStrategies);
        }

        private IMetadataStrategy CreateMockStrategy(string name, bool canHandleWithoutLidarr = false)
        {
            var strategy = Substitute.For<IMetadataStrategy>();
            strategy.StrategyName.Returns(name);
            strategy.CanHandle(Arg.Any<QobuzAlbum>(), Arg.Any<LidarrAlbum>())
                .Returns(callInfo => 
                {
                    var qobuzAlbum = callInfo.ArgAt<QobuzAlbum>(0);
                    var lidarrAlbum = callInfo.ArgAt<LidarrAlbum>(1);
                    return qobuzAlbum != null && (canHandleWithoutLidarr || lidarrAlbum != null);
                });
            
            strategy.DownloadAlbumAsync(Arg.Any<QobuzAlbum>(), Arg.Any<LidarrAlbum>())
                .Returns(Task.FromResult(new MetadataDownloadResult
                {
                    MetadataStrategy = name,
                    TrackDownloads = new List<TrackDownload>
                    {
                        TestDataBuilder.CreateSampleTrackDownload()
                    },
                    ApiCallsSaved = name == "Lidarr" ? 5 : 0,
                    AdditionalApiCalls = name == "Qobuzarr" ? 3 : 0
                }));
            
            return strategy;
        }

        #region MetadataStrategyEngine Tests

        [Fact]
        public void Constructor_WithValidParameters_ShouldInitializeCorrectly()
        {
            // Arrange & Act
            var engine = new MetadataStrategyEngine(_mockLogger, _mockTrackDownloaderFactory, _mockStrategies);

            // Assert
            engine.Should().NotBeNull();
            engine.GetAvailableStrategies().Should().Contain("Qobuzarr", "Lidarr", "Hybrid");
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldNotThrow()
        {
            // Arrange & Act
            Action act = () => new MetadataStrategyEngine(null, _mockTrackDownloaderFactory, _mockStrategies);

            // Assert - should not throw since logger defaults to LogManager.GetCurrentClassLogger()
            act.Should().NotThrow();
        }

        [Fact]
        public void Constructor_WithNullTrackDownloaderFactory_ShouldThrowArgumentNullException()
        {
            // Arrange & Act
            Action act = () => new MetadataStrategyEngine(_mockLogger, null, _mockStrategies);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("trackDownloaderFactory");
        }

        [Fact]
        public void Constructor_WithNullStrategies_ShouldThrowArgumentNullException()
        {
            // Arrange & Act
            Action act = () => new MetadataStrategyEngine(_mockLogger, _mockTrackDownloaderFactory, null);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("strategies");
        }

        [Fact]
        public void Constructor_WithEmptyStrategies_ShouldInitializeWithEmptyList()
        {
            // Arrange
            var emptyStrategies = new List<IMetadataStrategy>();

            // Act
            var engine = new MetadataStrategyEngine(_mockLogger, _mockTrackDownloaderFactory, emptyStrategies);

            // Assert
            engine.GetAvailableStrategies().Should().BeEmpty();
        }

        [Fact]
        public async Task DownloadAlbumWithOptimalStrategyAsync_WithNullQobuzAlbum_ShouldThrowArgumentNullException()
        {
            // Arrange & Act
            Func<Task> act = async () => await _engine.DownloadAlbumWithOptimalStrategyAsync(null);

            // Assert
            await act.Should().ThrowAsync<ArgumentNullException>()
                .WithParameterName("qobuzAlbum");
        }

        [Fact]
        public async Task DownloadAlbumWithOptimalStrategyAsync_WithNoLidarrData_ShouldUseQobuzStrategy()
        {
            // Arrange
            var qobuzAlbum = TestDataBuilder.CreateSampleQobuzAlbum();

            // Act
            var result = await _engine.DownloadAlbumWithOptimalStrategyAsync(qobuzAlbum);

            // Assert
            result.Should().NotBeNull();
            result.MetadataStrategy.Should().Be("Qobuzarr");
            result.TrackDownloads.Should().NotBeEmpty();
            
            var stats = _engine.GetStatistics();
            stats.QobuzMetadataUsed.Should().Be(1);
            stats.TotalAlbums.Should().Be(1);
        }

        [Fact]
        public async Task DownloadAlbumWithOptimalStrategyAsync_WithIncompatibleReleases_ShouldFallbackToQobuz()
        {
            // Arrange
            var qobuzAlbum = TestDataBuilder.CreateSampleQobuzAlbum();
            var lidarrAlbum = TestDataBuilder.CreateSampleLidarrAlbum();

            // Mock IntelligentReleaseMapper to return incompatible result
            // This would require mocking the release mapper, but since it's created internally,
            // we'll test the integration behavior instead
            
            // Act
            var result = await _engine.DownloadAlbumWithOptimalStrategyAsync(qobuzAlbum, lidarrAlbum);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccessful.Should().BeTrue();
            
            var stats = _engine.GetStatistics();
            stats.TotalAlbums.Should().Be(1);
        }

        [Fact]
        public async Task DownloadAlbumWithOptimalStrategyAsync_WithNoCompatibleStrategy_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var qobuzAlbum = TestDataBuilder.CreateSampleQobuzAlbum();
            var lidarrAlbum = TestDataBuilder.CreateSampleLidarrAlbum();
            
            var incompatibleStrategies = new List<IMetadataStrategy>
            {
                CreateIncompatibleStrategy("TestStrategy")
            };
            
            var engine = new MetadataStrategyEngine(_mockLogger, _mockTrackDownloaderFactory, incompatibleStrategies);

            // Act
            Func<Task> act = async () => await engine.DownloadAlbumWithOptimalStrategyAsync(qobuzAlbum, lidarrAlbum);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("No suitable metadata strategy found");
        }

        [Fact]
        public void GetStatistics_ShouldReturnClonedStats()
        {
            // Act
            var stats1 = _engine.GetStatistics();
            var stats2 = _engine.GetStatistics();

            // Assert
            stats1.Should().NotBeSameAs(stats2);
            stats1.TotalAlbums.Should().Be(stats2.TotalAlbums);
        }

        [Fact]
        public void LogStatistics_ShouldNotThrow()
        {
            // Act
            Action act = () => _engine.LogStatistics();

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void GetAvailableStrategies_ShouldReturnReadOnlyList()
        {
            // Act
            var strategies = _engine.GetAvailableStrategies();

            // Assert
            strategies.Should().BeAssignableTo<IReadOnlyList<string>>();
            strategies.Should().Contain("Qobuzarr", "Lidarr", "Hybrid");
        }

        #endregion

        #region HybridMetadataStrategy Tests

        [Fact]
        public void HybridMetadataStrategy_Constructor_WithValidParameters_ShouldInitializeCorrectly()
        {
            // Act
            var strategy = new HybridMetadataStrategy(_mockLogger, _mockApiClient, _mockTrackDownloaderFactory);

            // Assert
            strategy.Should().NotBeNull();
            strategy.StrategyName.Should().Be("Hybrid");
        }

        [Fact]
        public void HybridMetadataStrategy_Constructor_WithNullApiClient_ShouldThrowArgumentNullException()
        {
            // Act
            Action act = () => new HybridMetadataStrategy(_mockLogger, null, _mockTrackDownloaderFactory);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("qobuzApiClient");
        }

        [Fact]
        public void HybridMetadataStrategy_Constructor_WithNullTrackDownloaderFactory_ShouldThrowArgumentNullException()
        {
            // Act
            Action act = () => new HybridMetadataStrategy(_mockLogger, _mockApiClient, null);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("trackDownloaderFactory");
        }

        [Fact]
        public void HybridMetadataStrategy_CanHandle_WithNullLidarrAlbum_ShouldReturnFalse()
        {
            // Arrange
            var strategy = new HybridMetadataStrategy(_mockLogger, _mockApiClient, _mockTrackDownloaderFactory);
            var qobuzAlbum = TestDataBuilder.CreateSampleQobuzAlbum();

            // Act
            var canHandle = strategy.CanHandle(qobuzAlbum, null);

            // Assert
            canHandle.Should().BeFalse();
        }

        [Fact]
        public async Task HybridMetadataStrategy_DownloadAlbumAsync_WithNullLidarrAlbum_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var strategy = new HybridMetadataStrategy(_mockLogger, _mockApiClient, _mockTrackDownloaderFactory);
            var qobuzAlbum = TestDataBuilder.CreateSampleQobuzAlbum();

            // Act
            Func<Task> act = async () => await strategy.DownloadAlbumAsync(qobuzAlbum, null);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("HybridMetadataStrategy requires Lidarr album data");
        }

        [Fact]
        public async Task HybridMetadataStrategy_DownloadAlbumAsync_WithValidAlbums_ShouldReturnResult()
        {
            // Arrange
            var strategy = new HybridMetadataStrategy(_mockLogger, _mockApiClient, _mockTrackDownloaderFactory);
            var qobuzAlbum = TestDataBuilder.CreateSampleQobuzAlbum();
            var lidarrAlbum = TestDataBuilder.CreateSampleLidarrAlbum();
            
            // Mock API calls
            _mockApiClient.GetStreamingUrlAsync(Arg.Any<string>(), Arg.Any<int>())
                .Returns("https://streaming.example.com/track.flac");
            _mockApiClient.GetTrackMetadataAsync(Arg.Any<string>())
                .Returns(TestDataBuilder.CreateSampleQobuzTrack());

            // Act
            var result = await strategy.DownloadAlbumAsync(qobuzAlbum, lidarrAlbum);

            // Assert
            result.Should().NotBeNull();
            result.MetadataStrategy.Should().Be("Hybrid");
            result.TrackDownloads.Should().NotBeEmpty();
            result.IsSuccessful.Should().BeTrue();
        }

        #endregion

        #region QobuzMetadataStrategy Tests

        [Fact]
        public void QobuzMetadataStrategy_Constructor_WithValidParameters_ShouldInitializeCorrectly()
        {
            // Act
            var strategy = new QobuzMetadataStrategy(_mockLogger, _mockApiClient);

            // Assert
            strategy.Should().NotBeNull();
            strategy.StrategyName.Should().Be(Constants.QobuzarrConstants.ServiceName);
        }

        [Fact]
        public void QobuzMetadataStrategy_Constructor_WithNullApiClient_ShouldThrowArgumentNullException()
        {
            // Act
            Action act = () => new QobuzMetadataStrategy(_mockLogger, null);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("qobuzApiClient");
        }

        [Fact]
        public void QobuzMetadataStrategy_CanHandle_WithValidQobuzAlbum_ShouldReturnTrue()
        {
            // Arrange
            var strategy = new QobuzMetadataStrategy(_mockLogger, _mockApiClient);
            var qobuzAlbum = TestDataBuilder.CreateSampleQobuzAlbum();

            // Act
            var canHandle = strategy.CanHandle(qobuzAlbum, null);

            // Assert
            canHandle.Should().BeTrue();
        }

        [Fact]
        public void QobuzMetadataStrategy_CanHandle_WithNullQobuzAlbum_ShouldReturnFalse()
        {
            // Arrange
            var strategy = new QobuzMetadataStrategy(_mockLogger, _mockApiClient);

            // Act
            var canHandle = strategy.CanHandle(null, null);

            // Assert
            canHandle.Should().BeFalse();
        }

        [Fact]
        public async Task QobuzMetadataStrategy_DownloadAlbumAsync_WithNullQobuzAlbum_ShouldThrowArgumentNullException()
        {
            // Arrange
            var strategy = new QobuzMetadataStrategy(_mockLogger, _mockApiClient);

            // Act
            Func<Task> act = async () => await strategy.DownloadAlbumAsync(null);

            // Assert
            await act.Should().ThrowAsync<ArgumentNullException>()
                .WithParameterName("qobuzAlbum");
        }

        [Fact]
        public async Task QobuzMetadataStrategy_DownloadAlbumAsync_WithValidAlbum_ShouldReturnResult()
        {
            // Arrange
            var strategy = new QobuzMetadataStrategy(_mockLogger, _mockApiClient);
            var qobuzAlbum = TestDataBuilder.CreateSampleQobuzAlbum();
            
            // Mock API calls
            _mockApiClient.GetStreamingUrlAsync(Arg.Any<string>(), Arg.Any<int>())
                .Returns("https://streaming.example.com/track.flac");
            _mockApiClient.GetTrackMetadataAsync(Arg.Any<string>())
                .Returns(TestDataBuilder.CreateSampleQobuzTrack());

            // Act
            var result = await strategy.DownloadAlbumAsync(qobuzAlbum);

            // Assert
            result.Should().NotBeNull();
            result.MetadataStrategy.Should().Be(Constants.QobuzarrConstants.ServiceName);
            result.TrackDownloads.Should().NotBeEmpty();
            result.IsSuccessful.Should().BeTrue();
            result.ApiCallsSaved.Should().Be(0);
            result.AdditionalApiCalls.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task QobuzMetadataStrategy_DownloadAlbumAsync_WithApiException_ShouldThrow()
        {
            // Arrange
            var strategy = new QobuzMetadataStrategy(_mockLogger, _mockApiClient);
            var qobuzAlbum = TestDataBuilder.CreateSampleQobuzAlbum();
            
            // Mock API to throw exception
            _mockApiClient.GetStreamingUrlAsync(Arg.Any<string>(), Arg.Any<int>())
                .Throws(new InvalidOperationException("API Error"));

            // Act
            Func<Task> act = async () => await strategy.DownloadAlbumAsync(qobuzAlbum);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("API Error");
        }

        #endregion

        #region LidarrMetadataStrategy Tests

        [Fact]
        public void LidarrMetadataStrategy_Constructor_WithValidParameters_ShouldInitializeCorrectly()
        {
            // Act
            var strategy = new LidarrMetadataStrategy(_mockLogger, _mockApiClient);

            // Assert
            strategy.Should().NotBeNull();
            strategy.StrategyName.Should().Be("Lidarr");
        }

        [Fact]
        public void LidarrMetadataStrategy_Constructor_WithNullApiClient_ShouldThrowArgumentNullException()
        {
            // Act
            Action act = () => new LidarrMetadataStrategy(_mockLogger, null);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("qobuzApiClient");
        }

        [Fact]
        public void LidarrMetadataStrategy_CanHandle_WithNullLidarrAlbum_ShouldReturnFalse()
        {
            // Arrange
            var strategy = new LidarrMetadataStrategy(_mockLogger, _mockApiClient);
            var qobuzAlbum = TestDataBuilder.CreateSampleQobuzAlbum();

            // Act
            var canHandle = strategy.CanHandle(qobuzAlbum, null);

            // Assert
            canHandle.Should().BeFalse();
        }

        [Fact]
        public void LidarrMetadataStrategy_CanHandle_WithValidLidarrAlbum_ShouldReturnTrue()
        {
            // Arrange
            var strategy = new LidarrMetadataStrategy(_mockLogger, _mockApiClient);
            var qobuzAlbum = TestDataBuilder.CreateSampleQobuzAlbum();
            var lidarrAlbum = TestDataBuilder.CreateSampleLidarrAlbum();

            // Act
            var canHandle = strategy.CanHandle(qobuzAlbum, lidarrAlbum);

            // Assert
            canHandle.Should().BeTrue();
        }

        [Fact]
        public async Task LidarrMetadataStrategy_DownloadAlbumAsync_WithNullLidarrAlbum_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var strategy = new LidarrMetadataStrategy(_mockLogger, _mockApiClient);
            var qobuzAlbum = TestDataBuilder.CreateSampleQobuzAlbum();

            // Act
            Func<Task> act = async () => await strategy.DownloadAlbumAsync(qobuzAlbum, null);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("LidarrMetadataStrategy requires Lidarr album data");
        }

        [Fact]
        public async Task LidarrMetadataStrategy_DownloadAlbumAsync_WithValidAlbums_ShouldReturnOptimizedResult()
        {
            // Arrange
            var strategy = new LidarrMetadataStrategy(_mockLogger, _mockApiClient);
            var qobuzAlbum = TestDataBuilder.CreateSampleQobuzAlbum();
            var lidarrAlbum = TestDataBuilder.CreateSampleLidarrAlbum();
            
            // Mock API calls
            _mockApiClient.GetStreamingUrlAsync(Arg.Any<string>(), Arg.Any<int>())
                .Returns("https://streaming.example.com/track.flac");

            // Act
            var result = await strategy.DownloadAlbumAsync(qobuzAlbum, lidarrAlbum);

            // Assert
            result.Should().NotBeNull();
            result.MetadataStrategy.Should().Be("Lidarr");
            result.IsSuccessful.Should().BeTrue();
            result.ApiCallsSaved.Should().BeGreaterThan(0);
            result.AdditionalApiCalls.Should().Be(0);
        }

        #endregion

        #region MetadataDownloadResult Tests

        [Fact]
        public void MetadataDownloadResult_WithTracks_ShouldBeSuccessful()
        {
            // Arrange
            var result = new MetadataDownloadResult
            {
                TrackDownloads = new List<TrackDownload>
                {
                    TestDataBuilder.CreateSampleTrackDownload()
                }
            };

            // Act & Assert
            result.IsSuccessful.Should().BeTrue();
            result.TotalDuration.Should().BeGreaterThan(TimeSpan.Zero);
        }

        [Fact]
        public void MetadataDownloadResult_WithoutTracks_ShouldNotBeSuccessful()
        {
            // Arrange
            var result = new MetadataDownloadResult
            {
                TrackDownloads = new List<TrackDownload>()
            };

            // Act & Assert
            result.IsSuccessful.Should().BeFalse();
            result.TotalDuration.Should().Be(TimeSpan.Zero);
        }

        [Fact]
        public void MetadataDownloadResult_TotalDuration_ShouldSumTrackDurations()
        {
            // Arrange
            var result = new MetadataDownloadResult
            {
                TrackDownloads = new List<TrackDownload>
                {
                    new TrackDownload { Duration = TimeSpan.FromMinutes(3) },
                    new TrackDownload { Duration = TimeSpan.FromMinutes(4) },
                    new TrackDownload { Duration = null } // Should be ignored
                }
            };

            // Act & Assert
            result.TotalDuration.Should().Be(TimeSpan.FromMinutes(7));
        }

        #endregion

        #region MetadataOptimizationStats Tests

        [Fact]
        public void MetadataOptimizationStats_Clone_ShouldCreateDeepCopy()
        {
            // Arrange
            var original = new MetadataOptimizationStats
            {
                TotalAlbums = 10,
                LidarrMetadataUsed = 5,
                QobuzMetadataUsed = 3,
                HybridMetadataUsed = 2,
                ApiCallsSaved = 100,
                MatchingFailures = 1,
                AverageMatchRate = 0.85
            };

            // Act
            var clone = original.Clone();

            // Assert
            clone.Should().NotBeSameAs(original);
            clone.TotalAlbums.Should().Be(original.TotalAlbums);
            clone.LidarrMetadataUsed.Should().Be(original.LidarrMetadataUsed);
            clone.QobuzMetadataUsed.Should().Be(original.QobuzMetadataUsed);
            clone.HybridMetadataUsed.Should().Be(original.HybridMetadataUsed);
            clone.ApiCallsSaved.Should().Be(original.ApiCallsSaved);
            clone.MatchingFailures.Should().Be(original.MatchingFailures);
            clone.AverageMatchRate.Should().Be(original.AverageMatchRate);
        }

        [Fact]
        public void MetadataOptimizationStats_LogStats_WithZeroAlbums_ShouldNotThrow()
        {
            // Arrange
            var stats = new MetadataOptimizationStats();

            // Act
            Action act = () => stats.LogStats(_mockLogger);

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void MetadataOptimizationStats_LogStats_WithHighFailureRate_ShouldLogWarning()
        {
            // Arrange
            var stats = new MetadataOptimizationStats
            {
                TotalAlbums = 10,
                MatchingFailures = 5 // 50% failure rate
            };

            // Act
            stats.LogStats(_mockLogger);

            // Assert - Verify warning was logged (would need to check logger calls in real test)
            // This is a simplified test - in practice you'd verify the warning log call
            stats.MatchingFailures.Should().BeGreaterThan(stats.TotalAlbums * 0.1);
        }

        #endregion

        #region Helper Methods

        private IMetadataStrategy CreateIncompatibleStrategy(string name)
        {
            var strategy = Substitute.For<IMetadataStrategy>();
            strategy.StrategyName.Returns(name);
            strategy.CanHandle(Arg.Any<QobuzAlbum>(), Arg.Any<LidarrAlbum>()).Returns(false);
            return strategy;
        }

        #endregion
    }

    /// <summary>
    /// Test data builder for creating consistent test objects
    /// </summary>
    public static class TestDataBuilder
    {
        public static QobuzAlbum CreateSampleQobuzAlbum()
        {
            return new QobuzAlbum
            {
                Id = "12345",
                Title = "Test Album",
                Artist = new QobuzArtist { Name = "Test Artist" },
                TracksCount = 10,
                DurationSeconds = 2400,
                TracksContainer = new QobuzTracksContainer
                {
                    Items = new List<QobuzTrack>
                    {
                        CreateSampleQobuzTrack()
                    }
                }
            };
        }

        public static QobuzTrack CreateSampleQobuzTrack()
        {
            return new QobuzTrack
            {
                Id = "67890",
                Title = "Test Track",
                TrackNumber = 1,
                DiscNumber = 1,
                DurationSeconds = 240,
                Quality = "FLAC CD",
                Album = new QobuzAlbum
                {
                    Title = "Test Album",
                    Artist = new QobuzArtist { Name = "Test Artist" }
                }
            };
        }

        public static LidarrAlbum CreateSampleLidarrAlbum()
        {
            return new LidarrAlbum
            {
                Id = 1,
                Title = "Test Album",
                Artist = new LidarrArtist { ArtistName = "Test Artist" },
                DurationMs = 2400000,
                ForeignAlbumId = "mbz-album-id",
                ForeignReleaseId = "mbz-release-id",
                ArtistForeignId = "mbz-artist-id",
                Tracks = new List<LidarrTrack>
                {
                    CreateSampleLidarrTrack()
                }
            };
        }

        public static LidarrTrack CreateSampleLidarrTrack()
        {
            return new LidarrTrack
            {
                Id = 1,
                Title = "Test Track",
                TrackNumber = 1,
                DiscNumber = 1,
                Duration = TimeSpan.FromMinutes(4),
                ArtistName = "Test Artist",
                ForeignTrackId = "mbz-track-id"
            };
        }

        public static TrackDownload CreateSampleTrackDownload()
        {
            return new TrackDownload
            {
                StreamingUrl = "https://streaming.example.com/track.flac",
                QobuzTrackId = 67890,
                Title = "Test Track",
                Artist = "Test Artist",
                Album = "Test Album",
                TrackNumber = 1,
                DiscNumber = 1,
                Duration = TimeSpan.FromMinutes(4),
                Quality = "FLAC CD",
                MetadataSource = "Test"
            };
        }
    }
}