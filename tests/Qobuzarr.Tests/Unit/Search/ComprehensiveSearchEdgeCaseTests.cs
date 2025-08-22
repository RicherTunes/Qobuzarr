using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using NLog;
using FluentAssertions;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Music;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Qobuzarr.Tests.TestData;

namespace Qobuzarr.Tests.Unit.Search
{
    /// <summary>
    /// Comprehensive edge case tests for search functionality.
    /// Tests boundary conditions, error scenarios, and unusual inputs.
    /// </summary>
    public class ComprehensiveSearchEdgeCaseTests
    {
        private readonly Mock<Logger> _mockLogger;
        private readonly QobuzIndexerSettings _settings;
        private readonly QobuzRequestGenerator _requestGenerator;
        private readonly QobuzParser _parser;

        public ComprehensiveSearchEdgeCaseTests()
        {
            _mockLogger = new Mock<Logger>();
            _settings = new QobuzIndexerSettings
            {
                ApiRateLimit = 60,
                EnableQueryIntelligence = true,
                EnableMLPredictions = false
            };
            
            _requestGenerator = new QobuzRequestGenerator(_settings, _mockLogger.Object, 
                () => new QobuzSession { AppId = "test", AuthToken = "test" });
            _parser = new QobuzParser(_settings, _mockLogger.Object);
        }

        #region Null and Empty Input Tests

        [Fact]
        public void GetSearchRequests_NullSearchCriteria_ShouldHandleGracefully()
        {
            // Arrange
            AlbumSearchCriteria nullCriteria = null;

            // Act & Assert
            var exception = Record.Exception(() => _requestGenerator.GetSearchRequests(nullCriteria));
            exception.Should().BeNull("should handle null criteria gracefully");
        }

        [Fact]
        public void GetSearchRequests_EmptyArtistAndAlbum_ShouldReturnEmptyChain()
        {
            // Arrange
            var criteria = new AlbumSearchCriteria
            {
                Artist = new Artist { Name = "" },
                AlbumTitle = ""
            };

            // Act
            var result = _requestGenerator.GetSearchRequests(criteria);

            // Assert
            result.Should().NotBeNull();
            result.GetAllTiers().Should().BeEmpty("empty search criteria should produce no requests");
        }

        [Theory]
        [InlineData(null, "Album")]
        [InlineData("Artist", null)]
        [InlineData("", "Album")]
        [InlineData("Artist", "")]
        [InlineData("   ", "   ")]
        public void GetSearchRequests_PartiallyEmptyInput_ShouldGenerateValidQueries(string artist, string album)
        {
            // Arrange
            var criteria = new AlbumSearchCriteria
            {
                Artist = new Artist { Name = artist },
                AlbumTitle = album
            };

            // Act
            var result = _requestGenerator.GetSearchRequests(criteria);

            // Assert
            result.Should().NotBeNull();
            var tiers = result.GetAllTiers().ToList();
            if (!string.IsNullOrWhiteSpace(artist) || !string.IsNullOrWhiteSpace(album))
            {
                tiers.Should().NotBeEmpty("non-empty input should generate queries");
            }
        }

        #endregion

        #region Special Character Handling Tests

        [Theory]
        [MemberData(nameof(GetSpecialCharacterTestCases))]
        public void GetSearchRequests_SpecialCharacters_ShouldEscapeProperly(string artist, string album, string description)
        {
            // Arrange
            var criteria = new AlbumSearchCriteria
            {
                Artist = new Artist { Name = artist },
                AlbumTitle = album
            };

            // Act
            var result = _requestGenerator.GetSearchRequests(criteria);

            // Assert
            result.Should().NotBeNull();
            var requests = result.GetAllTiers().SelectMany(t => t).ToList();
            requests.Should().NotBeEmpty($"should generate requests for {description}");
            
            // Verify URL encoding
            foreach (var request in requests)
            {
                var url = request.Url.ToString();
                url.Should().NotContain("&amp;&amp;", "double encoding should not occur");
                url.Should().NotContain("<", "angle brackets should be encoded");
                url.Should().NotContain(">", "angle brackets should be encoded");
                
                // Check for SQL injection patterns
                url.Should().NotContain("--", "SQL comment should be sanitized");
                url.Should().NotContain("/*", "SQL comment should be sanitized");
                url.Should().NotContain("*/", "SQL comment should be sanitized");
            }
        }

        public static IEnumerable<object[]> GetSpecialCharacterTestCases()
        {
            yield return new object[] { "AC/DC", "Back in Black", "slash in artist" };
            yield return new object[] { "Guns N' Roses", "Appetite", "apostrophe" };
            yield return new object[] { "!!!","Louden Up Now", "exclamation marks" };
            yield return new object[] { "?uestlove", "Mo' Meta Blues", "question mark" };
            yield return new object[] { "Earth, Wind & Fire", "September", "ampersand" };
            yield return new object[] { "+44", "When Your Heart", "plus sign" };
            yield return new object[] { ".38 Special", "Hold On", "period at start" };
            yield return new object[] { "\"Weird Al\"", "Mandatory Fun", "quotes" };
            yield return new object[] { "<script>", "alert('xss')", "XSS attempt" };
            yield return new object[] { "'; DROP TABLE albums; --", "SQL Injection", "SQL injection" };
            yield return new object[] { "../../../etc/passwd", "Path Traversal", "path traversal" };
        }

        #endregion

        #region Unicode and International Character Tests

        [Theory]
        [MemberData(nameof(GetUnicodeTestCases))]
        public void GetSearchRequests_UnicodeCharacters_ShouldHandleCorrectly(string artist, string album, string language)
        {
            // Arrange
            var criteria = new AlbumSearchCriteria
            {
                Artist = new Artist { Name = artist },
                AlbumTitle = album
            };

            // Act
            var result = _requestGenerator.GetSearchRequests(criteria);

            // Assert
            result.Should().NotBeNull();
            var requests = result.GetAllTiers().SelectMany(t => t).ToList();
            requests.Should().NotBeEmpty($"should generate requests for {language}");
            
            // Verify Unicode preservation in URLs
            foreach (var request in requests)
            {
                var url = request.Url.ToString();
                // URL should contain percent-encoded Unicode or be properly formed
                url.Should().Match(u => u.Contains("%") || u.All(c => c < 128),
                    "Unicode should be percent-encoded or ASCII only");
            }
        }

        public static IEnumerable<object[]> GetUnicodeTestCases()
        {
            yield return new object[] { "宇多田ヒカル", "First Love", "Japanese" };
            yield return new object[] { "방탄소년단", "MAP OF THE SOUL", "Korean" };
            yield return new object[] { "Björk", "Homogenic", "Icelandic" };
            yield return new object[] { "Émilie Simon", "Émilie Simon", "French" };
            yield return new object[] { "Машина времени", "Поворот", "Cyrillic" };
            yield return new object[] { "فيروز", "معرفتي فيك", "Arabic" };
            yield return new object[] { "🎵🎸🎹", "🎶🎵", "Emoji" };
            yield return new object[] { "Arti​st", "Alb​um", "Zero-width space" };
        }

        #endregion

        #region Extreme Length Tests

        [Fact]
        public void GetSearchRequests_ExtremelyLongNames_ShouldTruncateAppropriately()
        {
            // Arrange
            var longArtist = string.Join(" ", Enumerable.Repeat("VeryLongArtistName", 50));
            var longAlbum = string.Join(" ", Enumerable.Repeat("VeryLongAlbumTitle", 50));
            
            var criteria = new AlbumSearchCriteria
            {
                Artist = new Artist { Name = longArtist },
                AlbumTitle = longAlbum
            };

            // Act
            var result = _requestGenerator.GetSearchRequests(criteria);

            // Assert
            result.Should().NotBeNull();
            var requests = result.GetAllTiers().SelectMany(t => t).ToList();
            requests.Should().NotBeEmpty();
            
            // Verify URL length limits
            foreach (var request in requests)
            {
                var url = request.Url.ToString();
                url.Length.Should().BeLessThan(2048, "URLs should not exceed common browser limits");
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(255)]
        [InlineData(256)]
        [InlineData(1000)]
        public void GetSearchRequests_VariousLengths_ShouldHandleAllLengths(int length)
        {
            // Arrange
            var artist = new string('A', length);
            var album = new string('B', length);
            
            var criteria = new AlbumSearchCriteria
            {
                Artist = new Artist { Name = artist },
                AlbumTitle = album
            };

            // Act
            var exception = Record.Exception(() => _requestGenerator.GetSearchRequests(criteria));

            // Assert
            exception.Should().BeNull($"should handle {length} character inputs");
        }

        #endregion

        #region Query Intelligence Edge Cases

        [Fact]
        public void SmartQueryStrategy_HighComplexityQuery_ShouldNotOptimize()
        {
            // Arrange
            var smartStrategy = new SmartQueryStrategy(_mockLogger.Object, null, false);
            var complexQueries = new List<Dictionary<string, string>>
            {
                new() { ["query"] = "The Beatles (Remastered) [Deluxe Edition] {2023}" },
                new() { ["query"] = "Various Artists - Compilation Vol. 1" },
                new() { ["query"] = "Artist feat. Guest & Friends (Live at Venue)" }
            };

            // Act
            var optimized = smartStrategy.OptimizeQueries(complexQueries, "Complex Artist", "Complex Album");

            // Assert
            optimized.Count.Should().Be(complexQueries.Count, 
                "complex queries should not be reduced");
        }

        [Fact]
        public void SmartQueryStrategy_SimpleQuery_ShouldOptimizeAggressively()
        {
            // Arrange
            var smartStrategy = new SmartQueryStrategy(_mockLogger.Object, null, false);
            var simpleQueries = new List<Dictionary<string, string>>
            {
                new() { ["query"] = "Beatles Abbey Road" },
                new() { ["query"] = "Beatles - Abbey Road" },
                new() { ["query"] = "\"Beatles\" Abbey Road" }
            };

            // Act
            var optimized = smartStrategy.OptimizeQueries(simpleQueries, "Beatles", "Abbey Road");

            // Assert
            optimized.Count.Should().BeLessThan(simpleQueries.Count, 
                "simple queries should be optimized");
        }

        #endregion

        #region Parsing Edge Cases

        [Fact]
        public void ParseResponse_MalformedJson_ShouldReturnEmptyList()
        {
            // Arrange
            var malformedJson = "{ invalid json: true, missing quotes }";
            var response = CreateMockResponse(malformedJson);

            // Act
            var result = _parser.ParseResponse(response);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty("malformed JSON should not crash parser");
        }

        [Fact]
        public void ParseResponse_PartiallyValidJson_ShouldParseWhatItCan()
        {
            // Arrange
            var partialJson = @"{
                ""albums"": {
                    ""items"": [
                        { ""id"": ""123"", ""title"": ""Valid Album"" },
                        null,
                        { ""id"": null, ""title"": ""Missing ID"" },
                        { ""id"": ""456"" }
                    ]
                }
            }";
            var response = CreateMockResponse(partialJson);

            // Act
            var result = _parser.ParseResponse(response);

            // Assert
            result.Should().NotBeNull();
            result.Count.Should().BeGreaterThan(0, "should parse valid entries");
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("[]")]
        [InlineData("null")]
        [InlineData("\"string\"")]
        [InlineData("123")]
        [InlineData("true")]
        public void ParseResponse_ValidJsonButWrongFormat_ShouldHandleGracefully(string json)
        {
            // Arrange
            var response = CreateMockResponse(json);

            // Act
            var exception = Record.Exception(() => _parser.ParseResponse(response));

            // Assert
            exception.Should().BeNull($"should handle {json} without crashing");
        }

        #endregion

        #region Concurrent Access Tests

        [Fact]
        public async Task GetSearchRequests_ConcurrentCalls_ShouldBeThreadSafe()
        {
            // Arrange
            var tasks = new List<Task<bool>>();
            var random = new Random();

            // Act
            for (int i = 0; i < 100; i++)
            {
                var taskNum = i;
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        var criteria = new AlbumSearchCriteria
                        {
                            Artist = new Artist { Name = $"Artist{taskNum}" },
                            AlbumTitle = $"Album{taskNum}"
                        };
                        
                        var result = _requestGenerator.GetSearchRequests(criteria);
                        return result != null;
                    }
                    catch
                    {
                        return false;
                    }
                }));
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            results.Should().AllSatisfy(r => r.Should().BeTrue("all concurrent calls should succeed"));
        }

        #endregion

        #region Cache Behavior Tests

        [Fact]
        public void SubstringCache_RepeatedSearches_ShouldUtilizeCache()
        {
            // Arrange
            var cache = new QobuzSubstringCache(_mockLogger.Object);
            var artist = "Test Artist";
            var album = "Test Album";

            // Act - First search
            var result1 = cache.FindCachedResults(artist, album);
            
            // Simulate adding to cache
            cache.AddToCache(artist, new List<QobuzAlbum> 
            { 
                new QobuzAlbum { Title = album, Id = "123" } 
            });
            
            // Second search
            var result2 = cache.FindCachedResults(artist, album);

            // Assert
            result1.Should().BeNull("first search should miss cache");
            result2.Should().NotBeNull("second search should hit cache");
            result2?.Confidence.Should().BeGreaterThan(0.7, "cache hit should have high confidence");
        }

        #endregion

        #region Boundary Value Tests

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(1)]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(int.MaxValue)]
        public void Settings_ApiRateLimit_ShouldHandleAllValues(int rateLimit)
        {
            // Arrange
            var settings = new QobuzIndexerSettings { ApiRateLimit = rateLimit };
            var generator = new QobuzRequestGenerator(settings, _mockLogger.Object);

            // Act
            var exception = Record.Exception(() =>
            {
                var criteria = new AlbumSearchCriteria
                {
                    Artist = new Artist { Name = "Test" },
                    AlbumTitle = "Test"
                };
                generator.GetSearchRequests(criteria);
            });

            // Assert
            exception.Should().BeNull($"should handle rate limit of {rateLimit}");
        }

        #endregion

        #region Helper Methods

        private NzbDrone.Core.Indexers.IndexerResponse CreateMockResponse(string content, 
            System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK)
        {
            var httpRequest = new NzbDrone.Common.Http.HttpRequest("https://api.qobuz.com/test");
            var httpResponse = new NzbDrone.Common.Http.HttpResponse(httpRequest)
            {
                StatusCode = statusCode,
                Headers = new NzbDrone.Common.Http.HttpHeader()
            };
            
            return new NzbDrone.Core.Indexers.IndexerResponse(
                new NzbDrone.Core.Indexers.IndexerRequest(httpRequest), 
                httpResponse, 
                content);
        }

        #endregion
    }
}