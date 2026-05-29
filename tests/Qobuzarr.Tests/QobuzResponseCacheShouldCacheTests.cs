using Lidarr.Plugin.Qobuzarr.API.Caching;
using NLog;
using Xunit;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Regression for the May-2026 defect hunt (#10): endpoint cache decisions used loose
    /// substring matching with leading slashes. Real endpoints arrive WITHOUT a leading slash,
    /// so the patterns matched nothing (metadata caching silently disabled); and the "track/get"
    /// pattern was a prefix of the signed streaming endpoint "track/getFileUrl", which must never
    /// be cached. Matching is now canonicalized and excludes streaming URLs first.
    /// </summary>
    public class QobuzResponseCacheShouldCacheTests
    {
        private static QobuzResponseCache Cache() => new QobuzResponseCache(LogManager.GetCurrentClassLogger());

        [Theory]
        // Signed, time-limited streaming URL — must NEVER be cached (shares the "track/get" prefix).
        [InlineData("track/getFileUrl", false)]
        [InlineData("/track/getFileUrl", false)]
        // Metadata + search — must be cached, with or without a leading slash.
        [InlineData("track/get", true)]
        [InlineData("/track/get", true)]
        [InlineData("album/get", true)]
        [InlineData("artist/get", true)]
        [InlineData("playlist/get", true)]
        [InlineData("label/get", true)]
        [InlineData("catalog/search", true)]
        [InlineData("label/search", true)]
        // Auth must not be cached.
        [InlineData("user/login", false)]
        public void ShouldCache_RespectsEndpointFamily(string endpoint, bool expected)
        {
            Assert.Equal(expected, Cache().ShouldCache(endpoint));
        }
    }
}
