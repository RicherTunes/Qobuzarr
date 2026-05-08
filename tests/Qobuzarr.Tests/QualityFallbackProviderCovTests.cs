using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using NzbDrone.Common.Http;
using Xunit;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Coverage tests for QualityFallbackProvider (src/Download/Services/QualityFallbackProvider.cs).
    /// Wave 12 baseline: 0/29 lines covered.
    /// </summary>
    public class QualityFallbackProviderCovTests
    {
        private readonly QualityFallbackProvider _provider = new();

        [Theory]
        [InlineData(27, new[] { 7, 6, 5 })]
        [InlineData(7, new[] { 6, 5 })]
        [InlineData(6, new[] { 5 })]
        public void GetFallbackQualities_KnownPreferredQuality_ReturnsExpectedSequence(int preferred, int[] expected)
        {
            var result = _provider.GetFallbackQualities(preferred);
            result.Should().Equal(expected);
        }

        [Fact]
        public void GetFallbackQualities_Mp3Preferred_ReturnsEmpty()
        {
            _provider.GetFallbackQualities(5).Should().BeEmpty();
        }

        [Fact]
        public void GetFallbackQualities_UnknownQuality_ReturnsDefaultSequence()
        {
            _provider.GetFallbackQualities(999).Should().Equal(new[] { 7, 6, 5 });
        }

        [Theory]
        [InlineData("This is a preview only", TrackUnavailableReason.PreviewOnly)]
        [InlineData("Sample track", TrackUnavailableReason.PreviewOnly)]
        [InlineData("Geo block detected", TrackUnavailableReason.RegionalRestriction)]
        [InlineData("Region restriction", TrackUnavailableReason.RegionalRestriction)]
        [InlineData("Country block", TrackUnavailableReason.RegionalRestriction)]
        [InlineData("Subscription required", TrackUnavailableReason.SubscriptionRestriction)]
        [InlineData("Higher tier needed", TrackUnavailableReason.SubscriptionRestriction)]
        [InlineData("Format X is not available here", TrackUnavailableReason.NoQualityAvailable)]
        [InlineData("Some random error", TrackUnavailableReason.Unknown)]
        public void DetermineUnavailableReason_RecognizesKeywords(string msg, TrackUnavailableReason expected)
        {
            _provider.DetermineUnavailableReason(msg).Should().Be(expected);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void DetermineUnavailableReason_NullOrEmpty_ReturnsNoQualityAvailable(string msg)
        {
            _provider.DetermineUnavailableReason(msg).Should().Be(TrackUnavailableReason.NoQualityAvailable);
        }

        [Fact]
        public void IsRetryableException_HttpRequestException_IsRetryable()
        {
            _provider.IsRetryableException(new HttpRequestException("boom")).Should().BeTrue();
        }

        [Fact]
        public void IsRetryableException_TimeoutException_IsRetryable()
        {
            _provider.IsRetryableException(new TimeoutException()).Should().BeTrue();
        }

        [Fact]
        public void IsRetryableException_TaskCanceledException_NotRetryable()
        {
            _provider.IsRetryableException(new TaskCanceledException()).Should().BeFalse();
        }

        [Fact]
        public void IsRetryableException_OperationCanceledException_NotRetryable()
        {
            _provider.IsRetryableException(new OperationCanceledException()).Should().BeFalse();
        }

        [Fact]
        public void IsRetryableException_ArbitraryException_NotRetryable()
        {
            _provider.IsRetryableException(new InvalidOperationException()).Should().BeFalse();
        }
    }
}
