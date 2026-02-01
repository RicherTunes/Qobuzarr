using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Xunit;

namespace Qobuzarr.Tests.Unit.Utilities
{
    public class PreviewDetectionUtilityTests
    {
        [Theory]
        [InlineData("https://stream.qobuz.com/track_preview_123456.mp3", true)]
        [InlineData("https://stream.qobuz.com/track_sample_123456.mp3", true)]
        [InlineData("https://stream.qobuz.com/preview/track123.flac", true)]
        [InlineData("https://stream.qobuz.com/sample/track123.flac", true)]
        [InlineData("https://stream.qobuz.com/track.mp3?preview=true", true)]
        [InlineData("https://stream.qobuz.com/track.mp3?sample=1", true)]
        [InlineData("https://stream.qobuz.com/track_demo_version.mp3", true)]
        [InlineData("https://stream.qobuz.com/track_30sec_version.mp3", true)]
        [InlineData("https://stream.qobuz.com/track_30s_version.mp3", true)]
        [InlineData("https://stream.qobuz.com/track.mp3?duration=30", true)]
        [InlineData("https://stream.qobuz.com/clip_track123.mp3", true)]
        [InlineData("https://stream.qobuz.com/track_clip_123.mp3", true)]
        [InlineData("https://stream.qobuz.com/track_short_version.mp3", true)]
        [InlineData("https://stream.qobuz.com/track_excerpt_123.mp3", true)]
        [InlineData("https://stream.qobuz.com/track_teaser_123.mp3", true)]
        [InlineData("https://stream.qobuz.com/track_snippet_123.mp3", true)]
        [InlineData("https://stream.qobuz.com/track123456.mp3", false)]
        [InlineData("https://stream.qobuz.com/full_track.flac", false)]
        [InlineData("https://stream.qobuz.com/track.mp3?quality=27", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsPreviewOrSampleUrl_ShouldDetectCorrectly(string url, bool expectedResult)
        {
            // Act
            var result = PreviewDetectionUtility.IsPreviewOrSampleUrl(url);

            // Assert
            result.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData(30, true)]
        [InlineData(60, true)]
        [InlineData(90, true)]
        [InlineData(29, false)]
        [InlineData(31, false)]
        [InlineData(120, false)]
        [InlineData(180, false)]
        [InlineData(0, false)]
        [InlineData(-1, false)]
        public void IsPreviewDuration_ShouldDetectCorrectly(int durationSeconds, bool expectedResult)
        {
            // Act
            var result = PreviewDetectionUtility.IsPreviewDuration(durationSeconds);

            // Assert
            result.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData("https://example.com/preview.mp3", null, null, true)]
        [InlineData("https://example.com/track.mp3", 30, null, true)]
        [InlineData(null, null, "This is a preview version", true)]
        [InlineData(null, null, "Sample track only", true)]
        [InlineData(null, null, "30 second excerpt available", true)]
        [InlineData(null, null, "Short clip", true)]
        [InlineData("https://example.com/track.mp3", 180, "Full track", false)]
        [InlineData("https://example.com/track.mp3", null, null, false)]
        [InlineData(null, 180, null, false)]
        [InlineData(null, null, "Full version available", false)]
        public void IsLikelyPreview_ShouldCombineIndicators(string url, int? duration, string restrictionMessage, bool expectedResult)
        {
            // Act
            var result = PreviewDetectionUtility.IsLikelyPreview(url, duration, restrictionMessage);

            // Assert
            result.Should().Be(expectedResult);
        }

        [Fact]
        public void GetPreviewMessage_ShouldReturnFormattedMessage()
        {
            // Arrange
            const string trackTitle = "Test Track";

            // Act
            var message = PreviewDetectionUtility.GetPreviewMessage(trackTitle);

            // Assert
            message.Should().Contain(trackTitle);
            message.Should().Contain("preview/sample");
            message.Should().Contain("Full version requires");
        }

        [Fact]
        public void IsPreviewOrSampleUrl_ShouldBeCaseInsensitive()
        {
            // Arrange
            var urls = new[]
            {
                "https://example.com/PREVIEW_track.mp3",
                "https://example.com/Preview_track.mp3",
                "https://example.com/pReViEw_track.mp3"
            };

            // Act & Assert
            foreach (var url in urls)
            {
                PreviewDetectionUtility.IsPreviewOrSampleUrl(url).Should().BeTrue();
            }
        }

        [Fact]
        public void IsLikelyPreview_WithMultipleIndicators_ShouldReturnTrue()
        {
            // Arrange
            var url = "https://example.com/preview_track.mp3";
            var duration = 30;
            var message = "This is a preview version";

            // Act
            var result = PreviewDetectionUtility.IsLikelyPreview(url, duration, message);

            // Assert
            result.Should().BeTrue();
        }
    }
}
