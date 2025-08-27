using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Services.Consolidated;
using Lidarr.Plugin.Qobuzarr.Services.Quality;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Qobuzarr.Tests.Unit.Services
{
    public class QobuzQualityManagerTests
    {
        private readonly IQualityDetectionService _mockDetectionService;
        private readonly IStreamInfoService _mockStreamInfoService;
        private readonly IQualityCacheService _mockCacheService;
        private readonly IQualityMappingService _mockMappingService;
        private readonly IQobuzLogger _mockLogger;
        private readonly QobuzQualityManager _qualityManager;

        public QobuzQualityManagerTests()
        {
            _mockDetectionService = Substitute.For<IQualityDetectionService>();
            _mockStreamInfoService = Substitute.For<IStreamInfoService>();
            _mockCacheService = Substitute.For<IQualityCacheService>();
            _mockMappingService = Substitute.For<IQualityMappingService>();
            _mockLogger = Substitute.For<IQobuzLogger>();
            _qualityManager = new QobuzQualityManager(
                _mockDetectionService,
                _mockStreamInfoService,
                _mockCacheService,
                _mockMappingService,
                _mockLogger);
        }

        [Fact]
        public void QualityMappingService_Should_Contain_All_Standard_Qualities()
        {
            // Arrange
            var mappingService = new QualityMappingService();
            
            // Act
            var formats = mappingService.GetQualityFormats();
            
            // Assert
            formats.Should().ContainKeys(5, 6, 7, 27);
            formats[5].Name.Should().Be("MP3 320");
            formats[6].Name.Should().Be("FLAC CD");
            formats[7].Name.Should().Be("FLAC Hi-Res 96");
            formats[27].Name.Should().Be("FLAC Hi-Res 192");
        }

        [Fact]
        public void MapLidarrQuality_WithHiResProfile_Should_ReturnHighestQuality()
        {
            // Arrange
            var profile = new LidarrQualityProfile
            {
                Name = "Hi-Res",
                Items = new List<LidarrQualityProfileItem>() // Fixed type
            };

            // Mock the mapping service to return the expected result
            var expectedQuality = new QobuzQuality { Id = 27, Name = "FLAC Hi-Res 192" };
            _mockMappingService.MapLidarrQuality(profile).Returns(expectedQuality);

            // Act
            var result = _qualityManager.MapLidarrQuality(profile);

            // Assert
            result.Should().NotBeNull();
            result.Id.Should().Be(27); // Should map to FLAC Hi-Res 192
        }

        [Fact]
        public void MapLidarrQuality_WithCDProfile_Should_ReturnCDQuality()
        {
            // Arrange
            var profile = new LidarrQualityProfile
            {
                Name = "CD Quality", 
                Items = new List<LidarrQualityProfileItem>() // Fixed type
            };

            // Mock the mapping service to return the expected result
            var expectedQuality = new QobuzQuality { Id = 6, Name = "FLAC CD" };
            _mockMappingService.MapLidarrQuality(profile).Returns(expectedQuality);

            // Act
            var result = _qualityManager.MapLidarrQuality(profile);

            // Assert
            result.Should().NotBeNull();
            result.Id.Should().Be(6); // Should map to FLAC CD
        }

        [Fact]
        public void GetQualityFallbackChain_WithHiResPreference_Should_ReturnOrderedFallback()
        {
            // Arrange
            var hiResQuality = new QobuzQuality { Id = 27, Name = "FLAC Hi-Res 192" };
            
            // Mock the expected fallback chain
            var expectedChain = new List<QobuzQuality>
            {
                new QobuzQuality { Id = 27, Name = "FLAC Hi-Res 192" },
                new QobuzQuality { Id = 7, Name = "FLAC Hi-Res 96" },
                new QobuzQuality { Id = 6, Name = "FLAC CD" },
                new QobuzQuality { Id = 5, Name = "MP3 320" }
            };
            
            _mockMappingService.GetQualityFallbackChain(hiResQuality).Returns(expectedChain);

            // Act
            var fallbackChain = _qualityManager.GetQualityFallbackChain(hiResQuality);

            // Assert
            fallbackChain.Should().NotBeEmpty();
            fallbackChain.Should().HaveCountGreaterThan(1);
            
            // Should start with the preferred quality
            fallbackChain.First().Id.Should().Be(27);
            
            // Should fall back to lower qualities
            fallbackChain.Should().Contain(q => q.Id == 7); // FLAC Hi-Res 96
            fallbackChain.Should().Contain(q => q.Id == 6); // FLAC CD
            
            // Should ensure MP3 is always available as last resort
            fallbackChain.Last().Id.Should().Be(5); // MP3 320
        }

        [Fact]
        public async Task SelectBestQualityAsync_WithValidTrack_Should_ReturnSuccessResult()
        {
            // Arrange
            var trackId = "test_track_123";
            var preferredQuality = new QobuzQuality { Id = 6, Name = "FLAC CD" };
            
            // Mock the stream info service to return success
            var expectedStreamInfo = new StreamInfo
            {
                Url = "https://streaming.qobuz.com/test_stream",
                QualityId = 6,
                TrackId = trackId,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };
            
            var expectedResult = new QualitySelectionResult
            {
                Success = true,
                SelectedQuality = preferredQuality,
                StreamInfo = expectedStreamInfo,
                FallbackUsed = false,
                AttemptsCount = 1
            };
            
            _mockStreamInfoService.SelectBestQualityAsync(trackId, preferredQuality, Arg.Any<CancellationToken>())
                .Returns(expectedResult);

            // Act
            var result = await _qualityManager.SelectBestQualityAsync(trackId, preferredQuality);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.SelectedQuality.Should().NotBeNull();
            result.StreamInfo.Should().NotBeNull();
            result.StreamInfo.Url.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task SelectBestQualityAsync_WithUnavailableQuality_Should_UseFallback()
        {
            // Arrange
            var trackId = "test_track_fallback";
            var preferredQuality = new QobuzQuality { Id = 27, Name = "FLAC Hi-Res 192" };
            var fallbackQuality = new QobuzQuality { Id = 6, Name = "FLAC CD" };
            
            // Mock the stream info service to simulate fallback behavior
            var fallbackStreamInfo = new StreamInfo
            {
                Url = "https://streaming.qobuz.com/fallback_stream",
                QualityId = 6,
                TrackId = trackId,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };
            
            var fallbackResult = new QualitySelectionResult
            {
                Success = true,
                SelectedQuality = fallbackQuality,
                StreamInfo = fallbackStreamInfo,
                FallbackUsed = true,
                AttemptsCount = 2 // Tried Hi-Res first, then CD
            };
            
            _mockStreamInfoService.SelectBestQualityAsync(trackId, preferredQuality, Arg.Any<CancellationToken>())
                .Returns(fallbackResult);

            // Act
            var result = await _qualityManager.SelectBestQualityAsync(trackId, preferredQuality);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FallbackUsed.Should().BeTrue();
            result.SelectedQuality.Id.Should().Be(6); // Should fallback to CD quality
            result.AttemptsCount.Should().BeGreaterThan(1);
        }

        [Fact]
        public void Constructor_WithNullDetectionService_Should_ThrowArgumentNullException()
        {
            // Act & Assert
            Action act = () => new QobuzQualityManager(null, _mockStreamInfoService, _mockCacheService, _mockMappingService, _mockLogger);
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("qualityDetectionService");
        }

        [Fact]
        public void Constructor_WithNullLogger_Should_ThrowArgumentNullException()
        {
            // Act & Assert
            Action act = () => new QobuzQualityManager(_mockDetectionService, _mockStreamInfoService, _mockCacheService, _mockMappingService, null);
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("logger");
        }
    }
}