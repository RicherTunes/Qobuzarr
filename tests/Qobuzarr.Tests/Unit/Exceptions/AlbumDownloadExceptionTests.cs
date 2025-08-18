using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Exceptions;
using Xunit;

namespace Qobuzarr.Tests.Unit.Exceptions
{
    public class AlbumDownloadExceptionTests
    {
        [Fact]
        public void Constructor_WithNoSuccessfulTracks_ShouldCreateCorrectMessage()
        {
            // Arrange
            var trackResults = new List<TrackDownloadResult>
            {
                new TrackDownloadResult { Success = false, TrackId = "1", Reason = TrackUnavailableReason.PreviewOnly },
                new TrackDownloadResult { Success = false, TrackId = "2", Reason = TrackUnavailableReason.PreviewOnly },
                new TrackDownloadResult { Success = false, TrackId = "3", Reason = TrackUnavailableReason.ApiError }
            };

            // Act
            var exception = new AlbumDownloadException(
                "album123",
                "Test Album",
                totalTracks: 3,
                successfulTracks: 0,
                skippedTracks: 2,
                failedTracks: 1,
                trackResults);

            // Assert
            exception.Message.Should().Contain("Failed to download any tracks");
            exception.Message.Should().Contain("2 tracks were preview-only");
            exception.Message.Should().Contain("1 tracks failed");
            exception.AlbumId.Should().Be("album123");
            exception.AlbumTitle.Should().Be("Test Album");
            exception.IsPartialSuccess.Should().BeFalse();
        }

        [Fact]
        public void Constructor_WithPartialSuccess_ShouldCreateCorrectMessage()
        {
            // Arrange
            var trackResults = new List<TrackDownloadResult>
            {
                new TrackDownloadResult { Success = true, TrackId = "1" },
                new TrackDownloadResult { Success = true, TrackId = "2" },
                new TrackDownloadResult { Success = false, TrackId = "3", Reason = TrackUnavailableReason.PreviewOnly },
                new TrackDownloadResult { Success = false, TrackId = "4", Reason = TrackUnavailableReason.RegionalRestriction }
            };

            // Act
            var exception = new AlbumDownloadException(
                "album123",
                "Test Album",
                totalTracks: 4,
                successfulTracks: 2,
                skippedTracks: 1,
                failedTracks: 1,
                trackResults);

            // Assert
            exception.Message.Should().Contain("partially downloaded");
            exception.Message.Should().Contain("2/4 tracks successful");
            exception.Message.Should().Contain("1 preview-only");
            exception.Message.Should().Contain("1 failed");
            exception.IsPartialSuccess.Should().BeTrue();
            exception.SuccessPercentage.Should().Be(50.0);
        }

        [Fact]
        public void GetIssuesSummary_ShouldGroupByReason()
        {
            // Arrange
            var trackResults = new List<TrackDownloadResult>
            {
                new TrackDownloadResult { Success = true, TrackId = "1" },
                new TrackDownloadResult { Success = false, TrackId = "2", Reason = TrackUnavailableReason.PreviewOnly },
                new TrackDownloadResult { Success = false, TrackId = "3", Reason = TrackUnavailableReason.PreviewOnly },
                new TrackDownloadResult { Success = false, TrackId = "4", Reason = TrackUnavailableReason.RegionalRestriction },
                new TrackDownloadResult { Success = false, TrackId = "5", Reason = TrackUnavailableReason.ApiError }
            };

            var exception = new AlbumDownloadException(
                "album123", "Test Album", 5, 1, 2, 2, trackResults);

            // Act
            var summary = exception.GetIssuesSummary();

            // Assert
            summary.Should().HaveCount(3);
            summary[TrackUnavailableReason.PreviewOnly].Should().HaveCount(2);
            summary[TrackUnavailableReason.RegionalRestriction].Should().HaveCount(1);
            summary[TrackUnavailableReason.ApiError].Should().HaveCount(1);
        }

        [Fact]
        public void ShouldTreatAsFailure_WithHighThreshold_ShouldReturnTrue()
        {
            // Arrange
            var exception = new AlbumDownloadException(
                "album123", "Test Album",
                totalTracks: 3,
                successfulTracks: 1,
                skippedTracks: 1, // One preview track
                failedTracks: 1,
                trackResults: null);

            // Act - 33% success rate with preview counting as failure
            var shouldFail = exception.ShouldTreatAsFailure(0.8, treatPreviewAsFailure: true);

            // Assert
            shouldFail.Should().BeTrue();
        }

        [Fact]
        public void ShouldTreatAsFailure_WithLowThreshold_ShouldReturnFalse()
        {
            // Arrange
            var exception = new AlbumDownloadException(
                "album123", "Test Album",
                totalTracks: 3,
                successfulTracks: 1,
                skippedTracks: 1, // One preview track
                failedTracks: 1,
                trackResults: null);

            // Act - 50% effective success rate when not counting preview
            var shouldFail = exception.ShouldTreatAsFailure(0.5, treatPreviewAsFailure: false);

            // Assert
            shouldFail.Should().BeFalse();
        }

        [Fact]
        public void ShouldTreatAsFailure_WithNoDownloadableTracks_ShouldReturnTrue()
        {
            // Arrange
            var exception = new AlbumDownloadException(
                "album123", "Test Album",
                totalTracks: 3,
                successfulTracks: 0,
                skippedTracks: 3, // All preview
                failedTracks: 0,
                trackResults: null);

            // Act
            var shouldFail = exception.ShouldTreatAsFailure(0.5, treatPreviewAsFailure: false);

            // Assert
            shouldFail.Should().BeTrue();
        }

        [Fact]
        public void Properties_ShouldBeSetCorrectly()
        {
            // Arrange
            var trackResults = new List<TrackDownloadResult>
            {
                new TrackDownloadResult { Success = true, TrackId = "1" }
            };

            // Act
            var exception = new AlbumDownloadException(
                "album123",
                "Test Album",
                totalTracks: 10,
                successfulTracks: 7,
                skippedTracks: 2,
                failedTracks: 1,
                trackResults);

            // Assert
            exception.AlbumId.Should().Be("album123");
            exception.AlbumTitle.Should().Be("Test Album");
            exception.TotalTracks.Should().Be(10);
            exception.SuccessfulTracks.Should().Be(7);
            exception.SkippedTracks.Should().Be(2);
            exception.FailedTracks.Should().Be(1);
            exception.TrackResults.Should().HaveCount(1);
            exception.SuccessPercentage.Should().Be(70.0);
        }

        [Fact]
        public void Constructor_WithNullTrackResults_ShouldHandleGracefully()
        {
            // Act
            var exception = new AlbumDownloadException(
                "album123",
                "Test Album",
                totalTracks: 1,
                successfulTracks: 0,
                skippedTracks: 0,
                failedTracks: 1,
                trackResults: null);

            // Assert
            exception.TrackResults.Should().NotBeNull();
            exception.TrackResults.Should().BeEmpty();
        }
    }
}