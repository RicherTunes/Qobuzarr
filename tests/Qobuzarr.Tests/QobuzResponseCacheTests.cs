using FluentAssertions;
using NLog;
using Xunit;
using Lidarr.Plugin.Qobuzarr.API.Caching;
using Lidarr.Plugin.Qobuzarr.Configuration;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Regression coverage for <see cref="QobuzResponseCache"/> caching predicates.
    ///
    /// The original predicates matched leading-slash substrings (e.g. "/track/get",
    /// "/search/") that did not match the endpoint strings the plugin actually passes
    /// ("track/get", "album/search", ...). The net effect was: metadata reads issued by
    /// QobuzApiClient were never cached, search results were never cached (no endpoint
    /// contains "/search/"), and short-lived signed streaming URLs WERE cached via the
    /// "/track/getFileUrl".Contains("/track/get") prefix collision. These tests pin the
    /// real endpoint strings (verified against the call sites) to the intended behaviour.
    /// </summary>
    public class QobuzResponseCacheTests
    {
        private static QobuzResponseCache CreateCache() => new(LogManager.GetCurrentClassLogger());

        [Theory]
        // Metadata reads — must be cached regardless of a leading slash.
        [InlineData("album/get")]
        [InlineData("/album/get")]
        [InlineData("track/get")]
        [InlineData("artist/get")]
        [InlineData("playlist/get")]
        [InlineData("label/get")]
        // Search endpoints — the real shapes the plugin uses (none contain "/search/").
        [InlineData("album/search")]
        [InlineData("/album/search")]
        [InlineData("/catalog/search")]
        [InlineData("playlist/search")]
        [InlineData("label/search")]
        public void ShouldCache_returns_true_for_metadata_and_search(string endpoint)
        {
            CreateCache().ShouldCache(endpoint).Should().BeTrue($"'{endpoint}' is cacheable metadata/search");
        }

        [Theory]
        // Signed, short-lived streaming URLs must NOT be cached (prefix collision with "track/get").
        [InlineData("track/getFileUrl")]
        [InlineData("/track/getFileUrl")]
        // Auth / unrelated endpoints must not be cached.
        [InlineData("user/login")]
        [InlineData("favorite/getUserFavorites")]
        public void ShouldCache_returns_false_for_streaming_and_other(string endpoint)
        {
            CreateCache().ShouldCache(endpoint).Should().BeFalse($"'{endpoint}' must not be cached");
        }

        [Theory]
        [InlineData("album/search", 5)]      // ShortDuration
        [InlineData("/catalog/search", 5)]
        [InlineData("album/get", 60)]        // MediumDuration
        [InlineData("track/get", 60)]
        [InlineData("playlist/get", 60)]
        [InlineData("artist/get", 1440)]     // LongDuration (24h)
        [InlineData("label/get", 1440)]
        public void GetCacheDuration_maps_real_endpoints_to_intended_ttl(string endpoint, int expectedMinutes)
        {
            CreateCache().GetCacheDuration(endpoint).TotalMinutes.Should().Be(expectedMinutes);
        }
    }
}
