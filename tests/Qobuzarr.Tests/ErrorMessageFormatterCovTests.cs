using System;
using System.Collections.Generic;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Exceptions;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Xunit;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Coverage tests for ErrorMessageFormatter (src/Utilities/ErrorMessageFormatter.cs).
    /// Targets all enum branches in GetDetailedReason / GetSuggestion plus the public
    /// formatter overloads. Wave 12 baseline: 0/94 lines covered.
    /// </summary>
    public class ErrorMessageFormatterCovTests
    {
        [Theory]
        [InlineData(TrackUnavailableReason.RegionalRestriction, "Geographic restriction")]
        [InlineData(TrackUnavailableReason.SubscriptionRestriction, "Subscription tier limitation")]
        [InlineData(TrackUnavailableReason.PreviewOnly, "Preview/sample only")]
        [InlineData(TrackUnavailableReason.NoQualityAvailable, "No suitable quality")]
        [InlineData(TrackUnavailableReason.NotStreamable, "Not available for streaming")]
        [InlineData(TrackUnavailableReason.Restricted, "Download restricted")]
        [InlineData(TrackUnavailableReason.ApiError, "Technical error")]
        [InlineData(TrackUnavailableReason.Unknown, "Unknown restriction")]
        public void FormatTrackError_AllReasons_IncludesReasonText(TrackUnavailableReason reason, string expectedFragment)
        {
            var msg = ErrorMessageFormatter.FormatTrackError("Some Track", reason);

            msg.Should().Contain("Track unavailable");
            msg.Should().Contain("'Some Track'");
            msg.Should().Contain(expectedFragment);
        }

        [Fact]
        public void FormatTrackError_WithContext_IncludesContextInReason()
        {
            var msg = ErrorMessageFormatter.FormatTrackError("T", TrackUnavailableReason.RegionalRestriction, "FR");
            msg.Should().Contain("(FR)");
        }

        [Fact]
        public void FormatTrackError_SubscriptionContext_HasCurrentLabel()
        {
            var msg = ErrorMessageFormatter.FormatTrackError("T", TrackUnavailableReason.SubscriptionRestriction, "Free");
            msg.Should().Contain("Current: Free");
        }

        [Fact]
        public void FormatTrackError_NoQualityContext_HasRequestedLabel()
        {
            var msg = ErrorMessageFormatter.FormatTrackError("T", TrackUnavailableReason.NoQualityAvailable, "27");
            msg.Should().Contain("Requested: 27");
        }

        [Fact]
        public void FormatTrackError_UnknownContext_AppendsContextWithDash()
        {
            var msg = ErrorMessageFormatter.FormatTrackError("T", TrackUnavailableReason.Unknown, "details");
            msg.Should().Contain("- details");
        }

        [Fact]
        public void FormatTrackError_RetryableReasons_IncludeSuggestion()
        {
            var msg = ErrorMessageFormatter.FormatTrackError("T", TrackUnavailableReason.ApiError);
            msg.Should().Contain("Suggestion:");
            msg.Should().Contain("Retry");
        }

        [Fact]
        public void FormatTrackError_UnknownReason_OmitsSuggestion()
        {
            var msg = ErrorMessageFormatter.FormatTrackError("T", TrackUnavailableReason.Unknown);
            // Unknown returns null suggestion
            msg.Should().NotContain("Suggestion:");
        }

        [Fact]
        public void FormatAlbumError_AllTracksFailed_SaysCompletelyUnavailable()
        {
            var msg = ErrorMessageFormatter.FormatAlbumError("Album X", "Artist Y", 10, 10);
            msg.Should().Contain("completely unavailable");
            msg.Should().Contain("Artist Y - Album X");
            msg.Should().Contain("All 10 tracks");
        }

        [Fact]
        public void FormatAlbumError_PartialFailure_SaysPartiallyAvailable()
        {
            var msg = ErrorMessageFormatter.FormatAlbumError("Album X", "Artist Y", 3, 10);
            msg.Should().Contain("partially available");
            msg.Should().Contain("3 of 10");
        }

        [Fact]
        public void FormatAlbumError_WithPrimaryReason_IncludesPrimaryIssue()
        {
            var msg = ErrorMessageFormatter.FormatAlbumError("A", "B", 1, 5, "geo restriction");
            msg.Should().Contain("Primary issue: geo restriction");
        }

        [Fact]
        public void FormatAlbumError_NoPrimaryReason_OmitsPrimaryIssue()
        {
            var msg = ErrorMessageFormatter.FormatAlbumError("A", "B", 1, 5);
            msg.Should().NotContain("Primary issue");
        }

        [Fact]
        public void FormatQualityFallback_DifferentQualities_ShowsRequestedLabel()
        {
            var msg = ErrorMessageFormatter.FormatQualityFallback("Track 1", "FLAC", "Hi-Res");
            msg.Should().Contain("'Track 1'");
            msg.Should().Contain("FLAC");
            msg.Should().Contain("(requested Hi-Res)");
        }

        [Fact]
        public void FormatQualityFallback_SameQuality_OmitsRequestedLabel()
        {
            var msg = ErrorMessageFormatter.FormatQualityFallback("Track 1", "FLAC", "FLAC");
            msg.Should().NotContain("requested");
        }

        [Fact]
        public void FormatQualityFallback_WithReason_IncludesReason()
        {
            var msg = ErrorMessageFormatter.FormatQualityFallback("T", "MP3", "FLAC", "format unavailable");
            msg.Should().Contain("Reason: format unavailable");
        }

        [Fact]
        public void FormatNetworkError_NoAttempts_OmitsAttemptInfo()
        {
            var ex = new InvalidOperationException("boom");
            var msg = ErrorMessageFormatter.FormatNetworkError("download", ex);

            msg.Should().Contain("Network error during download");
            msg.Should().Contain("boom");
            msg.Should().NotContain("Attempt:");
        }

        [Fact]
        public void FormatNetworkError_WithAttempts_ShowsAttemptInfo()
        {
            var ex = new InvalidOperationException("network failure");
            var msg = ErrorMessageFormatter.FormatNetworkError("fetch", ex, attemptNumber: 2, maxAttempts: 5);

            msg.Should().Contain("Attempt: 2/5");
            msg.Should().Contain("Retrying in");
        }

        [Fact]
        public void FormatNetworkError_FinalAttempt_OmitsRetryNotice()
        {
            var ex = new InvalidOperationException("boom");
            var msg = ErrorMessageFormatter.FormatNetworkError("op", ex, attemptNumber: 3, maxAttempts: 3);

            msg.Should().Contain("Attempt: 3/3");
            msg.Should().NotContain("Retrying in");
        }

        // ── FormatGroupedFailureReasons (A3: reason-grouped queue messages) ────────────

        [Fact]
        public void FormatGroupedFailureReasons_SingleReason_ReturnsCountAndLabel()
        {
            var trackResults = new List<TrackDownloadResult>
            {
                new TrackDownloadResult { Success = true, TrackId = "1" },
                new TrackDownloadResult
                {
                    Success = false,
                    TrackId = "2",
                    Reason = TrackUnavailableReason.SubscriptionRestriction,
                },
            };

            var exception = new AlbumDownloadException(
                "album1", "Test Album", totalTracks: 2, successfulTracks: 1,
                skippedTracks: 0, failedTracks: 1, trackResults);

            var summary = ErrorMessageFormatter.FormatGroupedFailureReasons(exception);

            summary.Should().Be("1 restricted (subscription tier)");
        }

        [Fact]
        public void FormatGroupedFailureReasons_MultipleReasons_GroupsAndOrdersByCountDescending()
        {
            var trackResults = new List<TrackDownloadResult>
            {
                new TrackDownloadResult { Success = false, TrackId = "1", Reason = TrackUnavailableReason.SubscriptionRestriction },
                new TrackDownloadResult { Success = false, TrackId = "2", Reason = TrackUnavailableReason.SubscriptionRestriction },
                new TrackDownloadResult { Success = false, TrackId = "3", Reason = TrackUnavailableReason.RegionalRestriction },
            };

            var exception = new AlbumDownloadException(
                "album2", "Test Album", totalTracks: 3, successfulTracks: 0,
                skippedTracks: 0, failedTracks: 3, trackResults);

            var summary = ErrorMessageFormatter.FormatGroupedFailureReasons(exception);

            summary.Should().Be("2 restricted (subscription tier), 1 region-locked");
        }

        [Fact]
        public void FormatGroupedFailureReasons_NoClassifiedReasons_ReturnsNullForGracefulFallback()
        {
            // A deficit track with no classified Reason (Reason == null) is symptomatic of an
            // unexpected/unmapped failure. The formatter must degrade gracefully by signalling
            // "nothing to group" (null) rather than fabricating a misleading count, so callers can
            // fall back to the exception's own generic message.
            var trackResults = new List<TrackDownloadResult>
            {
                new TrackDownloadResult { Success = false, TrackId = "1", Reason = null },
            };

            var exception = new AlbumDownloadException(
                "album3", "Test Album", totalTracks: 1, successfulTracks: 0,
                skippedTracks: 0, failedTracks: 1, trackResults);

            var summary = ErrorMessageFormatter.FormatGroupedFailureReasons(exception);

            summary.Should().BeNull();
        }

        [Fact]
        public void FormatGroupedFailureReasons_MixedClassifiedAndUnclassified_AppendsUnspecifiedBucket()
        {
            var trackResults = new List<TrackDownloadResult>
            {
                new TrackDownloadResult { Success = false, TrackId = "1", Reason = TrackUnavailableReason.Restricted },
                new TrackDownloadResult { Success = false, TrackId = "2", Reason = null },
            };

            var exception = new AlbumDownloadException(
                "album4", "Test Album", totalTracks: 2, successfulTracks: 0,
                skippedTracks: 0, failedTracks: 2, trackResults);

            var summary = ErrorMessageFormatter.FormatGroupedFailureReasons(exception);

            summary.Should().Be("1 restricted (rights holder), 1 unspecified");
        }

        [Theory]
        [InlineData(TrackUnavailableReason.RegionalRestriction, "region-locked")]
        [InlineData(TrackUnavailableReason.SubscriptionRestriction, "restricted (subscription tier)")]
        [InlineData(TrackUnavailableReason.PreviewOnly, "preview-only")]
        [InlineData(TrackUnavailableReason.NoQualityAvailable, "no suitable quality")]
        [InlineData(TrackUnavailableReason.NotStreamable, "not available for streaming")]
        [InlineData(TrackUnavailableReason.Restricted, "restricted (rights holder)")]
        [InlineData(TrackUnavailableReason.ApiError, "technical error")]
        [InlineData(TrackUnavailableReason.Unknown, "unknown restriction")]
        public void FormatGroupedFailureReasons_AllReasonKinds_UseExpectedLabel(TrackUnavailableReason reason, string expectedLabel)
        {
            var trackResults = new List<TrackDownloadResult>
            {
                new TrackDownloadResult { Success = false, TrackId = "1", Reason = reason },
            };

            var exception = new AlbumDownloadException(
                "album5", "Test Album", totalTracks: 1, successfulTracks: 0,
                skippedTracks: 0, failedTracks: 1, trackResults);

            var summary = ErrorMessageFormatter.FormatGroupedFailureReasons(exception);

            summary.Should().Be($"1 {expectedLabel}");
        }

        [Fact]
        public void FormatGroupedFailureReasons_NullException_Throws()
        {
            Action act = () => ErrorMessageFormatter.FormatGroupedFailureReasons(null);

            act.Should().Throw<ArgumentNullException>();
        }
    }
}
