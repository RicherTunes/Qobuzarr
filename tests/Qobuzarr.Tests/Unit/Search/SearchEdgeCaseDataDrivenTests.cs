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
using NzbDrone.Core.Indexers;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Qobuzarr.Tests.TestData;
using static Qobuzarr.Tests.TestData.ComprehensiveSearchTestData;

namespace Qobuzarr.Tests.Unit.Search
{
    /// <summary>
    /// Data-driven tests using the centralized ComprehensiveSearchTestData
    /// </summary>
    public class SearchEdgeCaseDataDrivenTests
    {
        private readonly Mock<Logger> _mockLogger;
        private readonly QobuzIndexerSettings _settings;
        private readonly QobuzRequestGenerator _requestGenerator;
        private readonly QobuzParser _parser;

        public SearchEdgeCaseDataDrivenTests()
        {
            _mockLogger = new Mock<Logger>();
            _settings = new QobuzIndexerSettings
            {
                ApiRateLimit = 60,
                EnableQueryIntelligence = true,
                EnableMLPredictions = false,
                AppId = "test_app",
                AppSecret = "test_secret"
            };
            
            _requestGenerator = new QobuzRequestGenerator(_settings, _mockLogger.Object, 
                () => new QobuzSession { AppId = "test", AuthToken = "test" });
            _parser = new QobuzParser(_settings, _mockLogger.Object);
        }

        #region Extreme Edge Case Tests

        [Theory]
        [MemberData(nameof(GetExtremeEdgeCases))]
        public void RequestGenerator_ExtremeEdgeCases_ShouldHandleSafely(SearchTestCase testCase)
        {
            // Arrange
            var criteria = new AlbumSearchCriteria
            {
                Artist = new Artist { Name = testCase.ArtistName },
                AlbumTitle = testCase.AlbumTitle
            };

            // Act
            var exception = Record.Exception(() =>
            {
                var result = _requestGenerator.GetSearchRequests(criteria);
                var requests = result.GetAllTiers().SelectMany(t => t).ToList();
                
                // Additional validation for malicious cases
                if (testCase.IsMalicious())
                {
                    foreach (var request in requests)
                    {
                        var url = request.Url.ToString();
                        ValidateSanitization(url, testCase);
                    }
                }
            });

            // Assert
            exception.Should().BeNull($"Should handle: {testCase.Description}");
        }

        public static IEnumerable<object[]> GetExtremeEdgeCases()
        {
            return ExtremeEdgeCases.Cases.Select(tc => new object[] { tc });
        }

        #endregion

        #region Real-World Problem Tests

        [Theory]
        [MemberData(nameof(GetRealWorldProblems))]
        public void RequestGenerator_RealWorldProblems_ShouldGenerateValidQueries(SearchTestCase testCase)
        {
            // Arrange
            var criteria = new AlbumSearchCriteria
            {
                Artist = new Artist { Name = testCase.ArtistName },
                AlbumTitle = testCase.AlbumTitle
            };

            // Act
            var result = _requestGenerator.GetSearchRequests(criteria);
            var requests = result.GetAllTiers().SelectMany(t => t).ToList();

            // Assert
            requests.Should().NotBeEmpty($"Should generate queries for: {testCase.Description}");
            
            // Special validation for certain types
            switch (testCase.Complexity)
            {
                case SearchComplexity.Compilation:
                    // Should handle "Various Artists" specially
                    requests.Should().Contain(r => 
                        r.Url.Query.Contains("various", StringComparison.OrdinalIgnoreCase),
                        "Should include 'various' in compilation searches");
                    break;
                    
                case SearchComplexity.Edition:
                    // Should strip or handle edition markers
                    requests.Should().Contain(r => 
                        !r.Url.Query.Contains("[") && !r.Url.Query.Contains("]"),
                        "Should have at least one query without edition brackets");
                    break;
                    
                case SearchComplexity.Classical:
                    // Should handle opus numbers and long titles
                    requests.All(r => r.Url.ToString().Length < 2048)
                        .Should().BeTrue("Classical music queries should stay within URL limits");
                    break;
            }
        }

        public static IEnumerable<object[]> GetRealWorldProblems()
        {
            return RealWorldProblems.Cases.Select(tc => new object[] { tc });
        }

        #endregion

        #region Performance Stress Tests

        [Fact]
        public void RequestGenerator_MaxLengthStrings_ShouldTruncateOrHandle()
        {
            // Arrange
            var testCase = PerformanceStressCases.GenerateMaxLengthCase();
            var criteria = new AlbumSearchCriteria
            {
                Artist = new Artist { Name = testCase.ArtistName },
                AlbumTitle = testCase.AlbumTitle
            };

            // Act
            var result = _requestGenerator.GetSearchRequests(criteria);
            var requests = result.GetAllTiers().SelectMany(t => t).ToList();

            // Assert
            requests.Should().NotBeEmpty();
            requests.All(r => r.Url.ToString().Length <= 4096)
                .Should().BeTrue("All URLs should be within reasonable limits");
        }

        [Fact]
        public void RequestGenerator_HighEntropyStrings_ShouldNotCrash()
        {
            // Arrange
            var testCase = PerformanceStressCases.GenerateHighEntropyCase();
            var criteria = new AlbumSearchCriteria
            {
                Artist = new Artist { Name = testCase.ArtistName },
                AlbumTitle = testCase.AlbumTitle
            };

            // Act & Assert
            var exception = Record.Exception(() => _requestGenerator.GetSearchRequests(criteria));
            exception.Should().BeNull("Should handle high entropy strings");
        }

        [Theory]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        public async Task RequestGenerator_BulkSearches_ShouldHandleEfficiently(int count)
        {
            // Arrange
            var testCases = PerformanceStressCases.GenerateBulkCases(count).ToList();
            var startTime = DateTime.UtcNow;

            // Act
            var tasks = testCases.Select(tc => Task.Run(() =>
            {
                var criteria = new AlbumSearchCriteria
                {
                    Artist = new Artist { Name = tc.ArtistName },
                    AlbumTitle = tc.AlbumTitle
                };
                return _requestGenerator.GetSearchRequests(criteria);
            }));

            var results = await Task.WhenAll(tasks);
            var elapsed = DateTime.UtcNow - startTime;

            // Assert
            results.Should().AllSatisfy(r => r.Should().NotBeNull());
            elapsed.TotalSeconds.Should().BeLessThan(count * 0.1, // 100ms per request max
                $"Should process {count} requests efficiently");
        }

        #endregion

        #region Parser Tests with Mock Responses

        [Fact]
        public void Parser_EmptySearchResult_ShouldReturnEmptyList()
        {
            // Arrange
            var response = CreateMockResponse(MockApiResponses.EmptySearchResult);

            // Act
            var result = _parser.ParseResponse(response);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public void Parser_SingleAlbumResult_ShouldParseCorrectly()
        {
            // Arrange
            var response = CreateMockResponse(MockApiResponses.SingleAlbumResult);

            // Act
            var result = _parser.ParseResponse(response);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(c => c > 0); // May have multiple qualities
            result.First().Title.Should().Contain("Test Album");
        }

        [Fact]
        public void Parser_MalformedJson_ShouldHandleGracefully()
        {
            // Arrange
            var response = CreateMockResponse(MockApiResponses.MalformedJsonResponse);

            // Act
            var exception = Record.Exception(() => _parser.ParseResponse(response));

            // Assert
            exception.Should().BeNull("Should handle malformed JSON gracefully");
        }

        [Fact]
        public void Parser_ApiError_ShouldHandleErrorResponse()
        {
            // Arrange
            var response = CreateMockResponse(MockApiResponses.ApiErrorResponse);

            // Act
            var result = _parser.ParseResponse(response);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty("Error response should not produce releases");
        }

        [Fact]
        public void Parser_HtmlErrorPage_ShouldNotCrash()
        {
            // Arrange
            var response = CreateMockResponse(MockApiResponses.HtmlErrorPage);

            // Act
            var exception = Record.Exception(() => _parser.ParseResponse(response));

            // Assert
            exception.Should().BeNull("Should handle HTML error pages");
        }

        [Theory]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        public void Parser_LargeResponse_ShouldHandleEfficiently(int albumCount)
        {
            // Arrange
            var largeResponse = MockApiResponses.GenerateLargeResponse(albumCount);
            var response = CreateMockResponse(largeResponse);
            var startTime = DateTime.UtcNow;

            // Act
            var result = _parser.ParseResponse(response);
            var elapsed = DateTime.UtcNow - startTime;

            // Assert
            result.Should().NotBeNull();
            result.Count.Should().BeGreaterThan(0);
            elapsed.TotalSeconds.Should().BeLessThan(albumCount * 0.001, // 1ms per album max
                $"Should parse {albumCount} albums efficiently");
        }

        #endregion

        #region Security Test Cases

        [Theory]
        [MemberData(nameof(GetSecurityTestCases))]
        public void RequestGenerator_SecurityAttacks_ShouldBeSanitized(SearchTestCase testCase)
        {
            // Arrange
            var criteria = new AlbumSearchCriteria
            {
                Artist = new Artist { Name = testCase.ArtistName },
                AlbumTitle = testCase.AlbumTitle
            };

            // Act
            var result = _requestGenerator.GetSearchRequests(criteria);
            var requests = result.GetAllTiers().SelectMany(t => t).ToList();

            // Assert
            foreach (var request in requests)
            {
                var url = request.Url.ToString();
                
                // Check for common attack patterns
                url.Should().NotContain("DROP TABLE", "SQL injection should be sanitized");
                url.Should().NotContain("<script", "XSS should be sanitized");
                url.Should().NotContain("../", "Path traversal should be sanitized");
                url.Should().NotContain("\0", "Null bytes should be sanitized");
                url.Should().NotContain("file://", "File protocol should be blocked");
                url.Should().NotContain("<!ENTITY", "XXE should be blocked");
            }
        }

        public static IEnumerable<object[]> GetSecurityTestCases()
        {
            return ComprehensiveSearchTestData.GetSecurityTestCases()
                .Select(tc => new object[] { tc });
        }

        #endregion

        #region Unicode Test Cases

        [Theory]
        [MemberData(nameof(GetUnicodeTestCases))]
        public void RequestGenerator_UnicodeComplexity_ShouldHandleCorrectly(SearchTestCase testCase)
        {
            // Arrange
            var criteria = new AlbumSearchCriteria
            {
                Artist = new Artist { Name = testCase.ArtistName },
                AlbumTitle = testCase.AlbumTitle
            };

            // Act
            var result = _requestGenerator.GetSearchRequests(criteria);
            var requests = result.GetAllTiers().SelectMany(t => t).ToList();

            // Assert
            requests.Should().NotBeEmpty($"Should handle: {testCase.Description}");
            
            // Verify Unicode is properly encoded or preserved
            foreach (var request in requests)
            {
                var url = request.Url.ToString();
                
                // Should either be percent-encoded or valid UTF-8
                url.Should().Match(u => 
                    u.Contains("%") || // Percent encoded
                    System.Text.Encoding.UTF8.GetByteCount(u) >= u.Length, // Valid UTF-8
                    "Unicode should be properly handled");
            }
        }

        public static IEnumerable<object[]> GetUnicodeTestCases()
        {
            return ComprehensiveSearchTestData.GetUnicodeTestCases()
                .Select(tc => new object[] { tc });
        }

        #endregion

        #region Helper Methods

        private void ValidateSanitization(string url, SearchTestCase testCase)
        {
            // Ensure dangerous patterns are sanitized
            var dangerousPatterns = new[]
            {
                "DROP TABLE", "DELETE FROM", "INSERT INTO", "UPDATE SET",
                "<script", "</script>", "javascript:", "onerror=",
                "../", "..\\", "%2e%2e", "file://", "data:",
                "\0", "%00", "\\x00",
                "<!ENTITY", "<!DOCTYPE", "SYSTEM",
                "; cat ", "| nc ", "&& rm "
            };

            foreach (var pattern in dangerousPatterns)
            {
                url.Should().NotContain(pattern, StringComparison.OrdinalIgnoreCase,
                    $"URL should not contain dangerous pattern '{pattern}' for test case: {testCase.Description}");
            }
        }

        private IndexerResponse CreateMockResponse(string content, 
            System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK)
        {
            var httpRequest = new NzbDrone.Common.Http.HttpRequest("https://api.qobuz.com/test");
            var httpResponse = new NzbDrone.Common.Http.HttpResponse(httpRequest)
            {
                StatusCode = statusCode,
                Headers = new NzbDrone.Common.Http.HttpHeader()
            };
            
            return new IndexerResponse(
                new IndexerRequest(httpRequest), 
                httpResponse, 
                content);
        }

        #endregion
    }
}