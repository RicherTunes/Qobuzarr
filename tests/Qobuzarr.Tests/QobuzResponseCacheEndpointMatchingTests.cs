using FluentAssertions;
using NLog;
using Xunit;
using Lidarr.Plugin.Qobuzarr.API.Caching;
using Lidarr.Plugin.Qobuzarr.Configuration;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Regression coverage for <see cref="QobuzResponseCache"/> endpoint-matching predicates.
    ///
    /// The original predicates matched leading-slash substrings ("/track/get", "/search/", ...)
    /// that do NOT match the endpoint strings the plugin actually passes to the cache. The cache
    /// receives the raw endpoint from <c>QobuzApiClient.ResolveCachePolicy</c>, which is the same
    /// value handed to the internal request helpers: e.g. "track/get", "playlist/search" (no
    /// leading slash). Net effect on main: NO metadata read and NO search result was ever cached
    /// (no real endpoint contains "/search/" either — they are all "*/search"). Separately, the
    /// "/track/get" pattern is a prefix of the signed, short-lived streaming endpoint
    /// "track/getFileUrl", so the moment a leading slash appeared it would have cached an expiring
    /// stream URL. These tests pin the real endpoint strings (verified against the call sites in
    /// QobuzApiClient / QobuzIndexerAdapter / LidarrAlbumRetriever) to the intended behaviour.
    /// </summary>
    public class QobuzResponseCacheEndpointMatchingTests
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
        // Signed, short-lived streaming URLs must NEVER be cached (prefix collision with "track/get").
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
