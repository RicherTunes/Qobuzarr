using System;
using Lidarr.Plugin.Qobuzarr.Indexers;
using NLog;
using Xunit;

namespace Qobuzarr.Tests.Unit.Indexers;

[Trait("Category", "Unit")]
public class QobuzSubstringCacheTests
{
    private readonly QobuzSubstringCache _cache = new(LogManager.GetCurrentClassLogger());

    #region StoreResult + FindCachedResults

    [Fact]
    public void StoreResult_ThenFind_ReturnsExactMatch()
    {
        _cache.StoreResult("The Beatles", "Abbey Road", new { Id = "123" });

        var result = _cache.FindCachedResults("The Beatles", "Abbey Road");

        Assert.NotNull(result);
        Assert.True(result.Confidence >= 0.9);
    }

    [Fact]
    public void FindCachedResults_NoMatch_ReturnsNull()
    {
        var result = _cache.FindCachedResults("Nonexistent Artist", "Nonexistent Album");
        Assert.Null(result);
    }

    [Fact]
    public void FindCachedResults_NullArtist_ReturnsNull()
    {
        var result = _cache.FindCachedResults(null!, "Album");
        Assert.Null(result);
    }

    [Fact]
    public void FindCachedResults_EmptyArtist_ReturnsNull()
    {
        var result = _cache.FindCachedResults("", "Album");
        Assert.Null(result);
    }

    [Fact]
    public void StoreResult_MultipleAlbums_SameArtist_AllRetrievable()
    {
        _cache.StoreResult("Pink Floyd", "The Wall", new { Id = "1" });
        _cache.StoreResult("Pink Floyd", "Dark Side of the Moon", new { Id = "2" });

        var wall = _cache.FindCachedResults("Pink Floyd", "The Wall");
        var darkSide = _cache.FindCachedResults("Pink Floyd", "Dark Side of the Moon");

        Assert.NotNull(wall);
        Assert.NotNull(darkSide);
    }

    #endregion

    #region StoreArtistDiscography + FindCachedArtistDiscography

    [Fact]
    public void StoreArtistDiscography_EmptyData_DoesNotThrow()
    {
        _cache.StoreArtistDiscography("Miles Davis", new { Albums = 50 });
        // Anonymous objects don't match Qobuz API response — returns early with no-op
    }

    [Fact]
    public void StoreArtistDiscography_NullArtist_DoesNotThrow()
    {
        _cache.StoreArtistDiscography(null!, new { });
    }

    [Fact]
    public void FindCachedArtistDiscography_NoMatch_ReturnsNull()
    {
        var result = _cache.FindCachedArtistDiscography("Unknown Artist");
        Assert.Null(result);
    }

    #endregion

    #region Clear

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        _cache.StoreResult("Artist", "Album", new { });
        _cache.StoreArtistDiscography("Artist", new { });

        _cache.Clear();

        Assert.Null(_cache.FindCachedResults("Artist", "Album"));
        Assert.Null(_cache.FindCachedArtistDiscography("Artist"));
    }

    #endregion

    #region GetDetailedStatistics

    [Fact]
    public void GetDetailedStatistics_EmptyCache_ReturnsZeros()
    {
        var stats = _cache.GetDetailedStatistics();
        Assert.Equal(0, stats.TotalEntries);
        Assert.Equal(0, stats.TotalHits);
    }

    [Fact]
    public void GetDetailedStatistics_AfterStoreAndFind_TracksHits()
    {
        _cache.StoreResult("Artist", "Album", new { });
        _cache.FindCachedResults("Artist", "Album");

        var stats = _cache.GetDetailedStatistics();
        Assert.True(stats.TotalHits >= 1);
    }

    #endregion

    #region Cache size limits

    [Fact]
    public void Constructor_InvalidCacheSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new QobuzSubstringCache(maxCacheSize: 0));
    }

    [Fact]
    public void Constructor_InvalidSimilarityThreshold_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new QobuzSubstringCache(similarityThreshold: 1.5));
    }

    #endregion

    #region Case sensitivity

    [Fact]
    public void FindCachedResults_CaseInsensitive_FindsMatch()
    {
        _cache.StoreResult("the beatles", "abbey road", new { Id = "123" });

        var result = _cache.FindCachedResults("The Beatles", "Abbey Road");

        Assert.NotNull(result);
    }

    #endregion
}
