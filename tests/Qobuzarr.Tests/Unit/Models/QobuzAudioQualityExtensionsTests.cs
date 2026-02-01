using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Qobuzarr.Tests.Unit.Models
{
    /// <summary>
    /// Tests for QobuzAudioQuality extension methods
    /// Critical for ensuring Lidarr quality detection works correctly
    /// </summary>
    public class QobuzAudioQualityExtensionsTests
    {
        #region GetFormatDescription Tests

        [Theory]
        [InlineData(QobuzAudioQuality.MP3320, "MP3 320kbps")]
        [InlineData(QobuzAudioQuality.FLACLossless, "FLAC Lossless")]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit96kHz, "FLAC 24bit 96kHz")]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit192Khz, "FLAC 24bit 192kHz")]
        public void GetFormatDescription_WithValidQuality_ShouldReturnCorrectDescription(
            QobuzAudioQuality quality, string expected)
        {
            // Act
            var result = quality.GetFormatDescription();

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void GetFormatDescription_WithInvalidQuality_ShouldReturnUnknown()
        {
            // Arrange
            var invalidQuality = (QobuzAudioQuality)999;

            // Act
            var result = invalidQuality.GetFormatDescription();

            // Assert
            result.Should().Be("Unknown");
        }

        #endregion

        #region GetCodec Tests - Critical for Lidarr Quality Detection

        [Theory]
        [InlineData(QobuzAudioQuality.MP3320, "MP3 320")]
        [InlineData(QobuzAudioQuality.FLACLossless, "FLAC")]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit96kHz, "FLAC24bit")]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit192Khz, "FLAC24bit")]
        public void GetCodec_WithValidQuality_ShouldReturnLidarrCompatibleCodec(
            QobuzAudioQuality quality, string expected)
        {
            // Act
            var result = quality.GetCodec();

            // Assert
            result.Should().Be(expected, "Codec strings must match Lidarr's CodecRegex patterns");
        }

        [Fact]
        public void GetCodec_WithInvalidQuality_ShouldReturnUnknown()
        {
            // Arrange
            var invalidQuality = (QobuzAudioQuality)999;

            // Act
            var result = invalidQuality.GetCodec();

            // Assert
            result.Should().Be("Unknown");
        }

        /// <summary>
        /// Verify codec strings match Lidarr's regex patterns for quality detection
        /// Note: Lidarr's CodecRegex is case-insensitive
        /// </summary>
        [Fact]
        public void GetCodec_AllResults_ShouldMatchLidarrRegexPatterns()
        {
            // Arrange - These are Lidarr's actual regex components for codec detection
            // Note: The actual regex is case-insensitive (RegexOptions.IgnoreCase)
            var mp3Pattern = @"(?i)MP3|MPEG Version \d(.5)? Audio, Layer 3";
            var flacPattern = @"(?i)(web)?flac(?:24(?:[-._ ]?bit)?)?|TR24";

            // Act & Assert for each quality
            QobuzAudioQuality.MP3320.GetCodec().Should().MatchRegex(mp3Pattern,
                "MP3 320 must match Lidarr's MP3 codec pattern");

            QobuzAudioQuality.FLACLossless.GetCodec().Should().MatchRegex(flacPattern,
                "FLAC must match Lidarr's FLAC codec pattern");

            QobuzAudioQuality.FLACHiRes24Bit96kHz.GetCodec().Should().MatchRegex(flacPattern,
                "FLAC24bit must match Lidarr's FLAC codec pattern");

            QobuzAudioQuality.FLACHiRes24Bit192Khz.GetCodec().Should().MatchRegex(flacPattern,
                "FLAC24bit must match Lidarr's FLAC codec pattern");
        }

        #endregion

        #region GetContainer Tests

        [Theory]
        [InlineData(QobuzAudioQuality.MP3320, "MP3 320kbps")]
        [InlineData(QobuzAudioQuality.FLACLossless, "FLAC")]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit96kHz, "FLAC 24-96")]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit192Khz, "FLAC 24-192")]
        public void GetContainer_WithValidQuality_ShouldReturnCorrectContainer(
            QobuzAudioQuality quality, string expected)
        {
            // Act
            var result = quality.GetContainer();

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void GetContainer_WithInvalidQuality_ShouldReturnUnknown()
        {
            // Arrange
            var invalidQuality = (QobuzAudioQuality)999;

            // Act
            var result = invalidQuality.GetContainer();

            // Assert
            result.Should().Be("Unknown");
        }

        #endregion

        #region GetEstimatedBitrate Tests

        [Theory]
        [InlineData(QobuzAudioQuality.MP3320, 320000)]
        [InlineData(QobuzAudioQuality.FLACLossless, 1411200)]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit96kHz, 4608000)]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit192Khz, 9216000)]
        public void GetEstimatedBitrate_WithValidQuality_ShouldReturnCorrectBitrate(
            QobuzAudioQuality quality, int expected)
        {
            // Act
            var result = quality.GetEstimatedBitrate();

            // Assert
            result.Should().Be(expected);
        }

        [Fact]
        public void GetEstimatedBitrate_WithInvalidQuality_ShouldReturnDefaultBitrate()
        {
            // Arrange
            var invalidQuality = (QobuzAudioQuality)999;

            // Act
            var result = invalidQuality.GetEstimatedBitrate();

            // Assert
            result.Should().Be(320000, "Should return MP3 320kbps as default");
        }

        #endregion

        #region Integration Tests for Quality Detection

        /// <summary>
        /// Integration test to verify the complete quality detection chain works
        /// </summary>
        [Theory]
        [InlineData(QobuzAudioQuality.MP3320, "MP3 320", "MP3 320kbps")]
        [InlineData(QobuzAudioQuality.FLACLossless, "FLAC", "FLAC")]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit96kHz, "FLAC24bit", "FLAC 24-96")]
        [InlineData(QobuzAudioQuality.FLACHiRes24Bit192Khz, "FLAC24bit", "FLAC 24-192")]
        public void QualityExtensions_AllMethods_ShouldReturnConsistentResults(
            QobuzAudioQuality quality, string expectedCodec, string expectedContainer)
        {
            // Act
            var codec = quality.GetCodec();
            var container = quality.GetContainer();
            var description = quality.GetFormatDescription();
            var bitrate = quality.GetEstimatedBitrate();

            // Assert
            codec.Should().Be(expectedCodec);
            container.Should().Be(expectedContainer);
            description.Should().NotBeNullOrEmpty();
            bitrate.Should().BeGreaterThan(0);
        }

        /// <summary>
        /// Test that ensures all enum values are handled by extension methods
        /// Prevents runtime exceptions from unhandled enum cases
        /// </summary>
        [Fact]
        public void QualityExtensions_WithAllEnumValues_ShouldNotThrowExceptions()
        {
            // Arrange
            var allQualities = new[]
            {
                QobuzAudioQuality.MP3320,
                QobuzAudioQuality.FLACLossless,
                QobuzAudioQuality.FLACHiRes24Bit96kHz,
                QobuzAudioQuality.FLACHiRes24Bit192Khz
            };

            // Act & Assert
            foreach (var quality in allQualities)
            {
                // These should not throw exceptions
                var codec = quality.GetCodec();
                var container = quality.GetContainer();
                var description = quality.GetFormatDescription();
                var bitrate = quality.GetEstimatedBitrate();

                codec.Should().NotBeNullOrEmpty($"Codec for {quality} should not be null or empty");
                container.Should().NotBeNullOrEmpty($"Container for {quality} should not be null or empty");
                description.Should().NotBeNullOrEmpty($"Description for {quality} should not be null or empty");
                bitrate.Should().BeGreaterThan(0, $"Bitrate for {quality} should be positive");
            }
        }

        #endregion
    }
}
