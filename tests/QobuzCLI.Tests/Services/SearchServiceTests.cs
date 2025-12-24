using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using QobuzCLI.Models;
using QobuzCLI.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace QobuzCLI.Tests.Services;

/// <summary>
/// Comprehensive tests for SearchService that validate search logic and scoring algorithms.
/// </summary>
public class SearchServiceTests
{
    private readonly SearchService _searchService;
    private readonly Mock<ILogger<SearchService>> _mockLogger;

    public SearchServiceTests()
    {
        _mockLogger = new Mock<ILogger<SearchService>>();
        _searchService = new SearchService(_mockLogger.Object);
    }

    [Theory]
    [InlineData("beatles abbey road", SearchType.Album)]
    [InlineData("The Beatles", SearchType.Artist)]
    [InlineData("come together beatles", SearchType.Track)]
    [InlineData("https://www.qobuz.com/album/123", SearchType.Album)]
    [InlineData("qobuz.com/artist/456", SearchType.Artist)]
    public void DetectSearchType_ShouldCorrectlyIdentifyType(string query, SearchType expectedType)
    {
        // Act
        var result = _searchService.DetectSearchType(query);

        // Assert
        result.Should().Be(expectedType);
    }

    [Fact]
    public void ScoreResults_ShouldPrioritizeExactMatches()
    {
        // Arrange
        var results = new List<SearchResult>
        {
            new() { Title = "Abbey Road", Artist = "The Beatles", Type = "album" },
            new() { Title = "Abbey Road Deluxe", Artist = "The Beatles", Type = "album" },
            new() { Title = "Road to Abbey", Artist = "Various Artists", Type = "album" }
        };
        var query = "abbey road beatles";

        // Act
        var scoredResults = _searchService.ScoreResults(results, query);

        // Assert
        scoredResults.Should().BeInDescendingOrder(r => r.Score);
        scoredResults[0].Title.Should().Be("Abbey Road");
        scoredResults[0].Score.Should().BeGreaterThan(90);
    }

    [Fact]
    public void ScoreResults_ShouldHandleEmptyResults()
    {
        // Arrange
        var results = new List<SearchResult>();
        var query = "test query";

        // Act
        var scoredResults = _searchService.ScoreResults(results, query);

        // Assert
        scoredResults.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", SearchType.Auto)]
    [InlineData(null, SearchType.Auto)]
    [InlineData("just text", SearchType.Auto)]
    public void DetectSearchType_ShouldDefaultToAuto(string? query, SearchType expectedType)
    {
        // Act
        var result = _searchService.DetectSearchType(query!);

        // Assert
        result.Should().Be(expectedType);
    }

    [Fact]
    public void ScoreResults_ShouldBoostHighQualityFormats()
    {
        // Arrange
        var results = new List<SearchResult>
        {
            new() { Title = "Test Album", Artist = "Test Artist", Quality = "MP3 320kbps", Type = "album" },
            new() { Title = "Test Album", Artist = "Test Artist", Quality = "FLAC Hi-Res 24bit/96kHz", Type = "album" }
        };
        var query = "test album";

        // Act
        var scoredResults = _searchService.ScoreResults(results, query);

        // Assert
        var hiResResult = scoredResults.First(r => r.Quality.Contains("Hi-Res"));
        var mp3Result = scoredResults.First(r => r.Quality.Contains("MP3"));
        
        hiResResult.Score.Should().BeGreaterThan(mp3Result.Score, 
            "Hi-Res quality should score higher than MP3");
    }
}

/// <summary>
/// Tests for search result scoring algorithms.
/// </summary>
public class SearchScoringTests
{
    private readonly SearchService _searchService;
    private readonly Mock<ILogger<SearchService>> _mockLogger;

    public SearchScoringTests()
    {
        _mockLogger = new Mock<ILogger<SearchService>>();
        _searchService = new SearchService(_mockLogger.Object);
    }

    [Fact]
    public void ScoreResults_ShouldScoreExactTitleMatchesHighly()
    {
        // Arrange
        var results = new List<SearchResult>
        {
            new() { Title = "Dark Side of the Moon", Artist = "Pink Floyd", Type = "album" }
        };
        var query = "dark side of the moon";

        // Act
        var scored = _searchService.ScoreResults(results, query);

        // Assert
        scored[0].Score.Should().BeGreaterThan(95, "Exact title matches should score very highly");
    }

    [Fact]
    public void ScoreResults_ShouldBoostPopularArtists()
    {
        // Arrange
        var results = new List<SearchResult>
        {
            new() { Title = "Test Album", Artist = "The Beatles", Type = "album" },
            new() { Title = "Test Album", Artist = "Unknown Artist", Type = "album" }
        };
        var query = "test album";

        // Act
        var scored = _searchService.ScoreResults(results, query);

        // Assert
        var beatlesResult = scored.First(r => r.Artist == "The Beatles");
        var unknownResult = scored.First(r => r.Artist == "Unknown Artist");
        
        beatlesResult.Score.Should().BeGreaterThan(unknownResult.Score,
            "Well-known artists should receive score boost");
    }

    [Theory]
    [InlineData("Pink Floyd", 5)]
    [InlineData("The Beatles", 10)]
    [InlineData("Led Zeppelin", 7)]
    [InlineData("Unknown Artist", 0)]
    public void GetArtistPopularityBoost_ShouldReturnExpectedBoosts(string artistName, int expectedBoost)
    {
        // Use reflection to test private method
        var method = typeof(SearchService).GetMethod("GetArtistPopularityBoost", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        // Act
        var boost = (int)method!.Invoke(_searchService, new object[] { artistName })!;

        // Assert
        boost.Should().Be(expectedBoost);
    }
}