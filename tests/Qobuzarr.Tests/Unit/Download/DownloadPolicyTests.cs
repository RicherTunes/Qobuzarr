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
        // An album is only "successful" when EVERY track ends up on disk. A track that
        // failed (genuine error) OR was sample/preview-skipped means the album is incomplete,
        // and Lidarr's NoMissingOrUnmatchedTracksSpecification permanently rejects incomplete
        // albums (live-confirmed: 29/30 FLACs downloaded -> 0 imported, no retry). So any
        // incomplete album must report failure so Lidarr can blocklist + try another source
        // (parity with TidalLidarrDownloadClient: failedTracks > 0 => Failed).
        [InlineData(10, 8, 0, false, false, true)]  // 2 tracks failed -> incomplete -> FAIL (was wrongly true: 80% >= threshold)
        [InlineData(10, 7, 0, false, false, false)] // 3 tracks failed -> incomplete -> FAIL
        [InlineData(10, 5, 5, false, false, true)]  // 5 downloaded + 5 sample-only -> 5 tracks missing -> incomplete -> FAIL (was wrongly true)
        [InlineData(10, 5, 5, false, true, false)]  // 5 downloaded + 5 sample-only -> incomplete -> FAIL
        [InlineData(10, 10, 0, true, false, true)]  // 100% on disk -> complete -> SUCCESS
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
        public void IsAlbumDownloadSuccessful_IncompleteAlbumIsNeverSuccessful_RegardlessOfThreshold()
        {
            // Arrange — even a permissive 50% threshold must NOT mark a partial album successful.
            // A partial album is unimportable by Lidarr (Has missing tracks => Permanent reject),
            // so the threshold can only ever gate a COMPLETE album, never rescue an incomplete one.
            var policy = new DownloadPolicy
            {
                MinimumSuccessRate = 0.5, // 50% threshold
                TreatPreviewAsFailure = false
            };

            // Act & Assert
            policy.IsAlbumDownloadSuccessful(10, 5, 0).Should().BeFalse(); // 5/10 on disk -> incomplete -> FAIL (was wrongly true)
            policy.IsAlbumDownloadSuccessful(10, 4, 0).Should().BeFalse(); // 4/10 on disk -> incomplete -> FAIL
            policy.IsAlbumDownloadSuccessful(10, 6, 0).Should().BeFalse(); // 6/10 on disk -> incomplete -> FAIL (was wrongly true)
            policy.IsAlbumDownloadSuccessful(10, 10, 0).Should().BeTrue(); // 10/10 on disk -> complete -> SUCCESS
        }

        [Fact]
        public void IsAlbumDownloadSuccessful_PartialAlbum_29Of30_ReportsFailure()
        {
            // Live-confirmed bug (root cause of "29/30 FLACs downloaded, 0 imported"):
            // a single unfetchable track (sample stream / removed / geo-locked) used to leave
            // the album marked successful (96.7% >= 80% threshold). Lidarr then hits
            // NoMissingOrUnmatchedTracksSpecification -> [Permanent] Has missing tracks and
            // permanently rejects the release with no retry/fallback. Reporting failure instead
            // makes Lidarr blocklist + re-search across indexers (parity with Tidalarr).
            var policy = new DownloadPolicy(); // default 80% threshold

            policy.IsAlbumDownloadSuccessful(totalTracks: 30, successfulTracks: 29, skippedTracks: 0)
                .Should().BeFalse("an incomplete album is unimportable by Lidarr and must be reported failed so it can fall back to another source");
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
            // All 10 tracks are sample/preview-only => 0 importable files => the album is
            // entirely missing from Lidarr's perspective. Both policies must report failure;
            // a "lenient" threshold cannot rescue an album that has nothing on disk.
            lenientPolicy.IsAlbumDownloadSuccessful(10, 0, 10).Should().BeFalse(); // 0/10 on disk -> incomplete -> FAIL (was wrongly true)
            strictPolicy.IsAlbumDownloadSuccessful(10, 0, 10).Should().BeFalse(); // 0/10 on disk -> incomplete -> FAIL
        }
    }
}
