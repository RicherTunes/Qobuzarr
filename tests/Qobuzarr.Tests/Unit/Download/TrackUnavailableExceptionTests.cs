using System;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Download;
using Xunit;

namespace Qobuzarr.Tests.Unit.Download
{
    public class TrackUnavailableExceptionTests
    {
        [Fact]
        public void Constructor_ShouldSetPropertiesCorrectly()
        {
            // Arrange
            const string trackId = "123456";
            const string message = "Track is geo-restricted";
            const TrackUnavailableReason reason = TrackUnavailableReason.RegionalRestriction;

            // Act
            var exception = new TrackUnavailableException(trackId, message, reason);

            // Assert
            exception.TrackId.Should().Be(trackId);
            exception.Reason.Should().Be(reason);
            exception.Message.Should().Contain(trackId);
            exception.Message.Should().Contain(message);
        }

        [Theory]
        [InlineData(TrackUnavailableReason.RegionalRestriction, "This track is not available in your region")]
        [InlineData(TrackUnavailableReason.SubscriptionRestriction, "This track requires a higher subscription tier")]
        [InlineData(TrackUnavailableReason.PreviewOnly, "Only a preview/sample of this track is available")]
        [InlineData(TrackUnavailableReason.NoQualityAvailable, "No suitable audio quality found for this track")]
        [InlineData(TrackUnavailableReason.NotStreamable, "This track is not available for streaming")]
        [InlineData(TrackUnavailableReason.Restricted, "This track has download restrictions")]
        [InlineData(TrackUnavailableReason.ApiError, "Technical error occurred while accessing this track")]
        [InlineData(TrackUnavailableReason.Unknown, "Track is unavailable for an unknown reason")]
        public void GetUserFriendlyMessage_ShouldReturnCorrectMessage(TrackUnavailableReason reason, string expectedMessage)
        {
            // Arrange
            var exception = new TrackUnavailableException("123", "test", reason);

            // Act
            var message = exception.GetUserFriendlyMessage();

            // Assert
            message.Should().Be(expectedMessage);
        }

        [Theory]
        [InlineData(TrackUnavailableReason.ApiError, true)]
        [InlineData(TrackUnavailableReason.RegionalRestriction, false)]
        [InlineData(TrackUnavailableReason.SubscriptionRestriction, false)]
        [InlineData(TrackUnavailableReason.PreviewOnly, false)]
        public void IsTemporary_ShouldReturnCorrectValue(TrackUnavailableReason reason, bool expectedIsTemporary)
        {
            // Arrange
            var exception = new TrackUnavailableException("123", "test", reason);

            // Act
            var isTemporary = exception.IsTemporary();

            // Assert
            isTemporary.Should().Be(expectedIsTemporary);
        }

        [Theory]
        [InlineData(TrackUnavailableReason.SubscriptionRestriction, true)]
        [InlineData(TrackUnavailableReason.RegionalRestriction, false)]
        [InlineData(TrackUnavailableReason.ApiError, false)]
        [InlineData(TrackUnavailableReason.PreviewOnly, false)]
        public void IsUserResolvable_ShouldReturnCorrectValue(TrackUnavailableReason reason, bool expectedIsResolvable)
        {
            // Arrange
            var exception = new TrackUnavailableException("123", "test", reason);

            // Act
            var isResolvable = exception.IsUserResolvable();

            // Assert
            isResolvable.Should().Be(expectedIsResolvable);
        }

        [Fact]
        public void Constructor_WithInnerException_ShouldSetInnerException()
        {
            // Arrange
            var innerException = new InvalidOperationException("Inner test");
            const string trackId = "123456";
            const string message = "API error";
            const TrackUnavailableReason reason = TrackUnavailableReason.ApiError;

            // Act
            var exception = new TrackUnavailableException(trackId, message, reason, innerException);

            // Assert
            exception.InnerException.Should().Be(innerException);
            exception.TrackId.Should().Be(trackId);
            exception.Reason.Should().Be(reason);
        }
    }
}
