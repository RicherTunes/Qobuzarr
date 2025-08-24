using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Services.Consolidated;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;

namespace Qobuzarr.Tests.Unit.Services
{
    public class QobuzQualityManagerTests
    {
        private readonly IQobuzApiClient _mockApiClient;
        private readonly IQobuzLogger _mockLogger;
        private readonly QobuzQualityManager _qualityManager;

        public QobuzQualityManagerTests()
        {
            _mockApiClient = Substitute.For<IQobuzApiClient>();
            _mockLogger = Substitute.For<IQobuzLogger>();
            _qualityManager = new QobuzQualityManager(_mockApiClient, _mockLogger);
        }

        [Fact]
        public void QobuzQualityFormats_Should_Contain_All_Standard_Qualities()
        {
            // Assert
            QobuzQualityManager.QobuzQualityFormats.Should().ContainKeys(5, 6, 7, 27);
            QobuzQualityManager.QobuzQualityFormats[5].Name.Should().Be("MP3 320");
            QobuzQualityManager.QobuzQualityFormats[6].Name.Should().Be("FLAC CD");
            QobuzQualityManager.QobuzQualityFormats[7].Name.Should().Be("FLAC Hi-Res 96");
            QobuzQualityManager.QobuzQualityFormats[27].Name.Should().Be("FLAC Hi-Res 192");
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
            
            // Mock successful API response
            var mockStreamResponse = new Dictionary<string, object>
            {
                ["url"] = "https://streaming.qobuz.com/test_stream",
                ["format_id"] = 6
            };
            
            _mockApiClient.GetAsync<Dictionary<string, object>>(
                "/track/getFileUrl", 
                Arg.Any<Dictionary<string, string>>())
                .Returns(mockStreamResponse);

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
            
            // Mock API to fail for Hi-Res but succeed for CD quality
            _mockApiClient.When(x => x.GetAsync<Dictionary<string, object>>(
                "/track/getFileUrl", 
                Arg.Is<Dictionary<string, string>>(d => d.ContainsValue("27"))))
                .Do(x => throw new Exception("Quality not available"));
                
            _mockApiClient.GetAsync<Dictionary<string, object>>(
                "/track/getFileUrl", 
                Arg.Is<Dictionary<string, string>>(d => d.ContainsValue("6")))
                .Returns(new Dictionary<string, object>
                {
                    ["url"] = "https://streaming.qobuz.com/fallback_stream",
                    ["format_id"] = 6
                });

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
        public void Constructor_WithNullApiClient_Should_ThrowArgumentNullException()
        {
            // Act & Assert
            Action act = () => new QobuzQualityManager(null, _mockLogger);
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("apiClient");
        }

        [Fact]
        public void Constructor_WithNullLogger_Should_ThrowArgumentNullException()
        {
            // Act & Assert
            Action act = () => new QobuzQualityManager(_mockApiClient, null);
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("logger");
        }
    }
}