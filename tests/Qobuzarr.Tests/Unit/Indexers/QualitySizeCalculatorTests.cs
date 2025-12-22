using System;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Indexers.Parsing;
using Lidarr.Plugin.Qobuzarr.Models;
using Qobuzarr.Tests.Builders;

namespace Qobuzarr.Tests.Unit.Indexers
{
    /// <summary>
    /// Tests for QualitySizeCalculator - the public API for calculating release sizes.
    /// Replaces reflection-based tests from QobuzParserTests.
    /// </summary>
    public class QualitySizeCalculatorTests
    {
        #region Size Calculation by Quality

        [Theory]
        [InlineData(QobuzAudioQuality.MP3320, 320000)]
        [InlineData(QobuzAudioQuality.FLACLossless, 1411200)]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit96kHz, 4608000)]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit192Khz, 9216000)]
        public void CalculateSize_WithDifferentQualities_ShouldCalculateCorrectSize(
            QobuzAudioQuality quality, int expectedBitrate)
        {
            // Arrange - 1 hour of audio
            var durationSeconds = 3600.0;

            // Act
            var result = QualitySizeCalculator.CalculateSize(durationSeconds, quality);

            // Assert
            var expectedSize = (long)(durationSeconds * (expectedBitrate / 8.0));
            result.Should().Be(expectedSize, $"Size calculation for {quality} should match expected formula");
            result.Should().BeGreaterThan(0, "Calculated size should be positive");
        }

        [Fact]
        public void CalculateSize_WithZeroDuration_ShouldReturnMinimumSize()
        {
            // Act
            var result = QualitySizeCalculator.CalculateSize(0, QobuzAudioQuality.FLACLossless);

            // Assert
            result.Should().Be(QualitySizeCalculator.MinimumSizeBytes);
        }

        [Fact]
        public void CalculateSize_WithNegativeDuration_ShouldReturnMinimumSize()
        {
            // Act
            var result = QualitySizeCalculator.CalculateSize(-100, QobuzAudioQuality.FLACLossless);

            // Assert
            result.Should().Be(QualitySizeCalculator.MinimumSizeBytes);
        }

        [Fact]
        public void CalculateSize_WithVeryShortDuration_ShouldReturnMinimumSize()
        {
            // Arrange - 1 second audio would be less than 1MB
            var durationSeconds = 1.0;

            // Act
            var result = QualitySizeCalculator.CalculateSize(durationSeconds, QobuzAudioQuality.MP3320);

            // Assert - should be clamped to minimum
            result.Should().Be(QualitySizeCalculator.MinimumSizeBytes);
        }

        #endregion

        #region Album-based Size Calculation

        [Fact]
        public void CalculateSize_WithValidAlbum_ShouldCalculateFromDuration()
        {
            // Arrange - 15 tracks, 4 minutes each = 60 minutes = 3600 seconds
            var album = new QobuzAlbumBuilder()
                .WithId("size123")
                .WithTitle("Size Test")
                .WithArtist("Test", "test")
                .WithTracks(15, 240) // 15 tracks, 240 seconds each
                .Build();

            // Act
            var result = QualitySizeCalculator.CalculateSize(album, QobuzAudioQuality.FLACLossless);

            // Assert
            var expectedDuration = 3600; // 15 * 240
            var expectedSize = (long)(expectedDuration * (1411200 / 8.0));
            result.Should().Be(expectedSize);
        }

        [Fact]
        public void CalculateSize_WithNullAlbum_ShouldReturnMinimumSize()
        {
            // Act
            var result = QualitySizeCalculator.CalculateSize(null, QobuzAudioQuality.FLACLossless);

            // Assert
            result.Should().Be(QualitySizeCalculator.MinimumSizeBytes);
        }

        #endregion

        #region Reliable Duration Calculation

        [Fact]
        public void CalculateReliableDuration_WithAlbumDuration_ShouldUseThat()
        {
            // Arrange - album with explicit duration
            var album = new QobuzAlbumBuilder()
                .WithId("dur1")
                .WithTitle("Test")
                .WithArtist("Test", "test")
                .WithDuration(TimeSpan.FromMinutes(45))
                .Build();

            // Act
            var result = QualitySizeCalculator.CalculateReliableDuration(album);

            // Assert
            result.Should().Be(45 * 60); // 45 minutes in seconds
        }

        [Fact]
        public void CalculateReliableDuration_WithNullAlbum_ShouldReturnMinimum()
        {
            // Act
            var result = QualitySizeCalculator.CalculateReliableDuration(null);

            // Assert
            result.Should().Be(30); // Minimum fallback
        }

        [Fact]
        public void CalculateReliableDuration_WithNoData_ShouldEstimateFromTrackCount()
        {
            // Arrange - album with track count but no duration
            var album = new QobuzAlbumBuilder()
                .WithId("est1")
                .WithTitle("Test")
                .WithArtist("Test", "test")
                .WithTracksCount(10)
                .Build();

            // Act
            var result = QualitySizeCalculator.CalculateReliableDuration(album);

            // Assert - should estimate based on track count (10 tracks * 3.5 min average)
            result.Should().BeGreaterThan(0);
            result.Should().BeGreaterOrEqualTo(30); // Minimum
        }

        #endregion

        #region Quality Comparison Tests

        [Fact]
        public void CalculateSize_HiResShouldBeLargerThanLossless()
        {
            // Arrange
            var durationSeconds = 3600.0;

            // Act
            var losslessSize = QualitySizeCalculator.CalculateSize(durationSeconds, QobuzAudioQuality.FLACLossless);
            var hiRes96Size = QualitySizeCalculator.CalculateSize(durationSeconds, QobuzAudioQuality.FLACHiRes24Bit96kHz);
            var hiRes192Size = QualitySizeCalculator.CalculateSize(durationSeconds, QobuzAudioQuality.FLACHiRes24Bit192Khz);

            // Assert
            hiRes96Size.Should().BeGreaterThan(losslessSize);
            hiRes192Size.Should().BeGreaterThan(hiRes96Size);
        }

        [Fact]
        public void CalculateSize_Mp3ShouldBeSmallerThanFlac()
        {
            // Arrange
            var durationSeconds = 3600.0;

            // Act
            var mp3Size = QualitySizeCalculator.CalculateSize(durationSeconds, QobuzAudioQuality.MP3320);
            var flacSize = QualitySizeCalculator.CalculateSize(durationSeconds, QobuzAudioQuality.FLACLossless);

            // Assert
            mp3Size.Should().BeLessThan(flacSize);
        }

        #endregion
    }
}
