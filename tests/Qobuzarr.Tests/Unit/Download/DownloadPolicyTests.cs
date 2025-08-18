using System;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Download;
using Xunit;

namespace Qobuzarr.Tests.Unit.Download
{
    public class DownloadPolicyTests
    {
        [Fact]
        public void DefaultPolicy_ShouldHaveReasonableDefaults()
        {
            // Arrange & Act
            var policy = new DownloadPolicy();

            // Assert
            policy.MinimumSuccessRate.Should().Be(0.8);
            policy.TreatPreviewAsFailure.Should().BeFalse();
            policy.FailOnNoTracksAvailable.Should().BeTrue();
            policy.MaxConcurrentTrackDownloads.Should().Be(3);
            policy.ContinueOnTrackFailure.Should().BeTrue();
            policy.EnableQualityFallback.Should().BeTrue();
            policy.SkipPreviewTracks.Should().BeTrue();
        }

        [Fact]
        public void CreateStrict_ShouldReturnStrictPolicy()
        {
            // Act
            var policy = DownloadPolicy.CreateStrict();

            // Assert
            policy.MinimumSuccessRate.Should().Be(1.0);
            policy.TreatPreviewAsFailure.Should().BeTrue();
            policy.FailOnNoTracksAvailable.Should().BeTrue();
            policy.ContinueOnTrackFailure.Should().BeFalse();
            policy.SkipPreviewTracks.Should().BeFalse();
            policy.RegionalRestrictionAction.Should().Be(RegionalRestrictionAction.Fail);
        }

        [Fact]
        public void CreateLenient_ShouldReturnLenientPolicy()
        {
            // Act
            var policy = DownloadPolicy.CreateLenient();

            // Assert
            policy.MinimumSuccessRate.Should().Be(0.5);
            policy.TreatPreviewAsFailure.Should().BeFalse();
            policy.FailOnNoTracksAvailable.Should().BeFalse();
            policy.ContinueOnTrackFailure.Should().BeTrue();
            policy.SkipPreviewTracks.Should().BeTrue();
            policy.RegionalRestrictionAction.Should().Be(RegionalRestrictionAction.Skip);
        }

        [Theory]
        [InlineData(-0.1)]
        [InlineData(1.1)]
        [InlineData(2.0)]
        public void Validate_ShouldThrowForInvalidSuccessRate(double invalidRate)
        {
            // Arrange
            var policy = new DownloadPolicy { MinimumSuccessRate = invalidRate };

            // Act & Assert
            policy.Invoking(p => p.Validate())
                .Should().Throw<ArgumentOutOfRangeException>()
                .WithParameterName("MinimumSuccessRate");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(11)]
        [InlineData(100)]
        public void Validate_ShouldThrowForInvalidConcurrency(int invalidConcurrency)
        {
            // Arrange
            var policy = new DownloadPolicy { MaxConcurrentTrackDownloads = invalidConcurrency };

            // Act & Assert
            policy.Invoking(p => p.Validate())
                .Should().Throw<ArgumentOutOfRangeException>()
                .WithParameterName("MaxConcurrentTrackDownloads");
        }

        [Theory]
        [InlineData(10, 8, 0, true, false, true)]  // 80% success, no preview
        [InlineData(10, 7, 0, false, false, false)] // 70% success, below 80% threshold
        [InlineData(10, 5, 5, true, false, true)]  // 50% success but all others are preview
        [InlineData(10, 5, 5, false, true, false)] // 50% success, previews count as failure
        [InlineData(10, 10, 0, true, false, true)] // 100% success
        public void IsAlbumDownloadSuccessful_ShouldReturnCorrectResult(
            int totalTracks,
            int successfulTracks,
            int skippedTracks,
            bool expectedSuccess,
            bool treatPreviewAsFailure,
            bool failOnNoTracks)
        {
            // Arrange
            var policy = new DownloadPolicy
            {
                MinimumSuccessRate = 0.8,
                TreatPreviewAsFailure = treatPreviewAsFailure,
                FailOnNoTracksAvailable = failOnNoTracks
            };

            // Act
            var result = policy.IsAlbumDownloadSuccessful(totalTracks, successfulTracks, skippedTracks);

            // Assert
            result.Should().Be(expectedSuccess);
        }

        [Fact]
        public void IsAlbumDownloadSuccessful_WithCustomThreshold_ShouldUseThreshold()
        {
            // Arrange
            var policy = new DownloadPolicy
            {
                MinimumSuccessRate = 0.5, // 50% threshold
                TreatPreviewAsFailure = false
            };

            // Act & Assert
            policy.IsAlbumDownloadSuccessful(10, 5, 0).Should().BeTrue(); // Exactly 50%
            policy.IsAlbumDownloadSuccessful(10, 4, 0).Should().BeFalse(); // Below 50%
            policy.IsAlbumDownloadSuccessful(10, 6, 0).Should().BeTrue(); // Above 50%
        }

        [Fact]
        public void IsAlbumDownloadSuccessful_NoTracks_WithFailOnEmpty_ShouldReturnFalse()
        {
            // Arrange
            var policy = new DownloadPolicy
            {
                MinimumSuccessRate = 0.8,
                TreatPreviewAsFailure = true,
                FailOnNoTracksAvailable = true
            };

            // Act
            var result = policy.IsAlbumDownloadSuccessful(0, 0, 0);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void IsAlbumDownloadSuccessful_NoTracks_WithoutFailOnEmpty_ShouldReturnTrue()
        {
            // Arrange
            var policy = new DownloadPolicy
            {
                MinimumSuccessRate = 0.8,
                TreatPreviewAsFailure = false,
                FailOnNoTracksAvailable = false
            };

            // Act
            var result = policy.IsAlbumDownloadSuccessful(0, 0, 0);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void IsAlbumDownloadSuccessful_AllTracksArePreview_ShouldHandleCorrectly()
        {
            // Arrange
            var lenientPolicy = new DownloadPolicy
            {
                MinimumSuccessRate = 0.8,
                TreatPreviewAsFailure = false,
                FailOnNoTracksAvailable = false
            };

            var strictPolicy = new DownloadPolicy
            {
                MinimumSuccessRate = 0.8,
                TreatPreviewAsFailure = true,
                FailOnNoTracksAvailable = true
            };

            // Act & Assert
            // All tracks are preview, none downloaded
            lenientPolicy.IsAlbumDownloadSuccessful(10, 0, 10).Should().BeTrue(); // Previews don't count against success
            strictPolicy.IsAlbumDownloadSuccessful(10, 0, 10).Should().BeFalse(); // Previews count as failures
        }
    }
}