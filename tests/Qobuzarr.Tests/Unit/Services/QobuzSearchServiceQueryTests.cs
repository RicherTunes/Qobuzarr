using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.Qobuzarr.Services;
using Moq;
using Xunit;

namespace Qobuzarr.Tests.Unit.Services;

[Trait("Category", "Unit")]
public class QobuzSearchServiceQueryTests
{
    private readonly QobuzSearchService _service;

    public QobuzSearchServiceQueryTests()
    {
        _service = new QobuzSearchService(
            new Mock<Lidarr.Plugin.Qobuzarr.Abstractions.IQobuzHttpClient>().Object,
            new Mock<Lidarr.Plugin.Qobuzarr.Abstractions.IQobuzLogger>().Object,
            new Mock<Lidarr.Plugin.Qobuzarr.Authentication.IQobuzAuthenticationService>().Object);
    }

    #region CleanSearchQuery

    [Fact]
    public void CleanSearchQuery_Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, _service.CleanSearchQuery(null!));
    }

    [Fact]
    public void CleanSearchQuery_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, _service.CleanSearchQuery(""));
    }

    [Fact]
    public void CleanSearchQuery_PlainTitle_ReturnsUnchanged()
    {
        Assert.Equal("Abbey Road", _service.CleanSearchQuery("Abbey Road"));
    }

    [Fact]
    public void CleanSearchQuery_RemovesLiveVenue()
    {
        var result = _service.CleanSearchQuery("Kind of Blue (Live at Montreux)");
        Assert.DoesNotContain("Montreux", result);
        Assert.Contains("Kind of Blue", result);
    }

    [Fact]
    public void CleanSearchQuery_RemovesLiveAtSuffix()
    {
        var result = _service.CleanSearchQuery("So What - Live at Newport Jazz Festival");
        Assert.DoesNotContain("Newport", result);
        Assert.Contains("So What", result);
    }

    [Fact]
    public void CleanSearchQuery_RemovesRecordedDate()
    {
        var result = _service.CleanSearchQuery("Blue Train (Recorded January 1957)");
        Assert.DoesNotContain("1957", result);
        Assert.Contains("Blue Train", result);
    }

    [Fact]
    public void CleanSearchQuery_RemovesYearInParens()
    {
        var result = _service.CleanSearchQuery("A Love Supreme (1964)");
        Assert.DoesNotContain("1964", result);
        Assert.Contains("A Love Supreme", result);
    }

    [Fact]
    public void CleanSearchQuery_RemovesLiveVersion()
    {
        var result = _service.CleanSearchQuery("Take Five (Live Version)");
        Assert.DoesNotContain("Live Version", result);
        Assert.Contains("Take Five", result);
    }

    [Fact]
    public void CleanSearchQuery_RemovesRadioEdit()
    {
        var result = _service.CleanSearchQuery("Bohemian Rhapsody (Radio Edit)");
        Assert.DoesNotContain("Radio Edit", result);
        Assert.Contains("Bohemian Rhapsody", result);
    }

    [Fact]
    public void CleanSearchQuery_RemovesDemo()
    {
        var result = _service.CleanSearchQuery("Yesterday (Demo)");
        Assert.DoesNotContain("Demo", result);
        Assert.Contains("Yesterday", result);
    }

    [Fact]
    public void CleanSearchQuery_CollapsesWhitespace()
    {
        var result = _service.CleanSearchQuery("  Multiple   Spaces  ");
        Assert.DoesNotContain("  ", result);
    }

    #endregion

    #region GenerateSearchVariations

    [Fact]
    public void GenerateSearchVariations_PlainQuery_ReturnsSingleVariation()
    {
        var result = _service.GenerateSearchVariations("Abbey Road");
        Assert.Single(result);
        Assert.Equal("Abbey Road", result[0]);
    }

    [Fact]
    public void GenerateSearchVariations_WithLiveVenue_ReturnsMultiple()
    {
        var result = _service.GenerateSearchVariations("Kind of Blue (Live at Montreux)");
        Assert.True(result.Count >= 2, $"Expected >=2 variations, got {result.Count}: [{string.Join(", ", result)}]");
        Assert.Equal("Kind of Blue (Live at Montreux)", result[0]);
    }

    [Fact]
    public void GenerateSearchVariations_NullQuery_ReturnsEmpty()
    {
        var result = _service.GenerateSearchVariations(null!);
        Assert.Empty(result);
    }

    [Fact]
    public void GenerateSearchVariations_NoDuplicates()
    {
        var result = _service.GenerateSearchVariations("Simple Title");
        Assert.Equal(result.Distinct().Count(), result.Count);
    }

    [Fact]
    public void GenerateSearchVariations_WithFeaturing_SimplifiesProgressively()
    {
        var result = _service.GenerateSearchVariations("Song Title feat. Artist B");
        Assert.True(result.Count >= 2);
        Assert.Contains(result, v => !v.Contains("feat"));
    }

    [Fact]
    public void GenerateSearchVariations_WithRemix_SimplifiesProgressively()
    {
        var result = _service.GenerateSearchVariations("Track Name (Club Remix)");
        Assert.True(result.Count >= 2);
        Assert.Contains(result, v => !v.Contains("Remix"));
    }

    #endregion
}
