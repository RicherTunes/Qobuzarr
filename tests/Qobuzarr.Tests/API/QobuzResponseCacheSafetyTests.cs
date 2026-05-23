using System.Collections.Generic;

using Lidarr.Plugin.Qobuzarr.API.Caching;

using NLog;

using Xunit;

namespace Qobuzarr.Tests.API;

/// <summary>
/// Audit of QobuzResponseCache safety invariants:
///   - Sensitive parameters MUST NOT influence the cache key (otherwise a
///     re-login generates a new key and the cache balloons; also a leak risk
///     if keys are inspected).
///   - Cache keys MUST be deterministic for the same non-sensitive params so
///     repeated calls hit instead of refetching.
///   - Cache keys MUST differ across different non-sensitive params so two
///     queries don't share a cached response.
///   - ShouldCache MUST exclude auth/login endpoints — caching login responses
///     would defeat token rotation.
/// </summary>
public sealed class QobuzResponseCacheSafetyTests
{
    private static QobuzResponseCache NewCache() =>
        new QobuzResponseCache(LogManager.GetCurrentClassLogger());

    [Fact]
    public void CacheKey_IsStable_AcrossDifferentSessionTokens()
    {
        var cache = NewCache();
        var paramsA = new Dictionary<string, string>
        {
            ["query"] = "Miles Davis",
            ["limit"] = "20",
            ["auth_token"] = "session-A-fresh",
        };
        var paramsB = new Dictionary<string, string>
        {
            ["query"] = "Miles Davis",
            ["limit"] = "20",
            ["auth_token"] = "session-B-rotated",
        };

        var keyA = cache.GenerateCacheKey("/album/search", paramsA);
        var keyB = cache.GenerateCacheKey("/album/search", paramsB);

        Assert.Equal(keyA, keyB);
    }

    [Fact]
    public void CacheKey_Differs_ForDifferentQueries()
    {
        var cache = NewCache();
        var a = cache.GenerateCacheKey("/album/search", new Dictionary<string, string> { ["query"] = "A" });
        var b = cache.GenerateCacheKey("/album/search", new Dictionary<string, string> { ["query"] = "B" });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void CacheKey_Differs_ForDifferentEndpoints()
    {
        var cache = NewCache();
        var a = cache.GenerateCacheKey("/album/search", new Dictionary<string, string> { ["query"] = "X" });
        var b = cache.GenerateCacheKey("/track/search", new Dictionary<string, string> { ["query"] = "X" });
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData("auth_token")]
    [InlineData("user_auth_token")]
    [InlineData("session_id")]
    [InlineData("app_secret")]
    [InlineData("password")]
    [InlineData("request_sig")]
    public void CacheKey_IgnoresSensitiveParameter(string sensitiveKey)
    {
        var cache = NewCache();
        var withSecret = new Dictionary<string, string>
        {
            ["query"] = "test",
            [sensitiveKey] = "secret-value-12345",
        };
        var withoutSecret = new Dictionary<string, string>
        {
            ["query"] = "test",
        };

        Assert.Equal(
            cache.GenerateCacheKey("/album/search", withoutSecret),
            cache.GenerateCacheKey("/album/search", withSecret));
    }

    [Fact]
    public void ShouldCache_ExcludesAuthEndpoints()
    {
        var cache = NewCache();
        Assert.False(cache.ShouldCache("/user/login"));
        Assert.False(cache.ShouldCache("/user/logout"));
        Assert.False(cache.ShouldCache("/track/getFileUrl"));
    }

    [Fact]
    public void ShouldCache_IncludesSearchAndMetadataEndpoints()
    {
        var cache = NewCache();
        Assert.True(cache.ShouldCache("/album/search"));
        Assert.True(cache.ShouldCache("/album/get"));
        Assert.True(cache.ShouldCache("/artist/get"));
        Assert.True(cache.ShouldCache("/track/get"));
    }
}
