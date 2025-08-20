using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace Qobuzarr.Tests.Integration
{
    /// <summary>
    /// Integration tests for QobuzApiClient against real Qobuz API endpoints
    /// Tests authentication, search, album retrieval, and error handling
    /// </summary>
    [Collection("QobuzIntegration")]
    [Trait("Category", "Integration")]
    [Trait("RequiresQobuzAPI", "true")]
    public class QobuzApiClientIntegrationTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly QobuzApiClient _apiClient;
        private readonly Mock<IQobuzAuthenticationService> _mockAuthService;
        private readonly QobuzSession _testSession;
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _rateLimiter;

        public QobuzApiClientIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _httpClient = new HttpClient();
            _mockAuthService = new Mock<IQobuzAuthenticationService>();
            _rateLimiter = new SemaphoreSlim(1, 1);

            // Setup test session (would use environment variables in CI)
            _testSession = new QobuzSession
            {
                UserId = Environment.GetEnvironmentVariable("QOBUZ_TEST_USER_ID") ?? "test_user",
                AuthToken = Environment.GetEnvironmentVariable("QOBUZ_TEST_AUTH_TOKEN") ?? "test_token",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };

            _mockAuthService.Setup(x => x.GetCachedSession()).Returns(_testSession);
            _mockAuthService.Setup(x => x.RefreshSessionAsync()).ReturnsAsync(_testSession);

            var mockLogger = new Mock<ILogger<QobuzApiClient>>();
            _apiClient = new QobuzApiClient(_httpClient, _mockAuthService.Object, mockLogger.Object);
        }

        [Fact]
        [Trait("Priority", "Critical")]
        public async Task SearchAlbums_WithValidQuery_ShouldReturnResults()
        {
            // Arrange
            await _rateLimiter.WaitAsync();
            try
            {
                var searchQuery = "Miles Davis Kind of Blue";
                var stopwatch = Stopwatch.StartNew();

                // Act
                var results = await _apiClient.SearchAlbumsAsync(searchQuery, limit: 10);
                stopwatch.Stop();

                // Assert
                results.Should().NotBeNull();
                results.Albums.Should().NotBeNull();
                results.Albums.Items.Should().NotBeEmpty();
                
                var firstAlbum = results.Albums.Items.First();
                firstAlbum.Title.Should().NotBeNullOrEmpty();
                firstAlbum.Id.Should().NotBeNullOrEmpty();
                
                // Performance assertion
                stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000, "API response should be under 5 seconds");
                
                _output.WriteLine($"Search returned {results.Albums.Items.Count} albums in {stopwatch.ElapsedMilliseconds}ms");
            }
            finally
            {
                _rateLimiter.Release();
            }
        }

        [Fact]
        [Trait("Priority", "Critical")]
        public async Task GetAlbum_WithValidId_ShouldReturnAlbumDetails()
        {
            // Arrange
            await _rateLimiter.WaitAsync();
            try
            {
                var albumId = "0060254788359"; // Random Access Memories
                
                // Act
                var album = await _apiClient.GetAlbumAsync(albumId);
                
                // Assert
                album.Should().NotBeNull();
                album.Id.Should().Be(albumId);
                album.Title.Should().NotBeNullOrEmpty();
                album.Artist.Should().NotBeNull();
                album.Tracks.Should().NotBeNull();
                album.Tracks.Items.Should().NotBeEmpty();
                
                // Validate track structure
                var firstTrack = album.Tracks.Items.First();
                firstTrack.Title.Should().NotBeNullOrEmpty();
                firstTrack.Duration.Should().BeGreaterThan(0);
                firstTrack.TrackNumber.Should().BeGreaterThan(0);
                
                _output.WriteLine($"Album '{album.Title}' has {album.Tracks.Items.Count} tracks");
            }
            finally
            {
                _rateLimiter.Release();
            }
        }

        [Fact]
        [Trait("Priority", "High")]
        public async Task GetAlbum_WithInvalidId_ShouldThrowApiException()
        {
            // Arrange
            await _rateLimiter.WaitAsync();
            try
            {
                var invalidAlbumId = "invalid_album_id_12345";
                
                // Act & Assert
                var exception = await Assert.ThrowsAsync<QobuzApiException>(
                    () => _apiClient.GetAlbumAsync(invalidAlbumId));
                
                exception.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
                exception.Message.Should().NotBeNullOrEmpty();
                
                _output.WriteLine($"Expected exception: {exception.Message}");
            }
            finally
            {
                _rateLimiter.Release();
            }
        }

        [Fact]
        [Trait("Priority", "Critical")]
        public async Task GetStreamUrl_WithValidTrackId_ShouldReturnStreamUrl()
        {
            // Arrange
            await _rateLimiter.WaitAsync();
            try
            {
                // First get an album to get a valid track ID
                var album = await _apiClient.GetAlbumAsync("0060254788359");
                var trackId = album.Tracks.Items.First().Id;
                
                // Act
                var streamUrl = await _apiClient.GetStreamUrlAsync(trackId, "27"); // FLAC quality
                
                // Assert
                streamUrl.Should().NotBeNull();
                streamUrl.Url.Should().NotBeNullOrEmpty();
                streamUrl.Url.Should().StartWith("https://");
                streamUrl.Quality.Should().NotBeNullOrEmpty();
                streamUrl.SampleRate.Should().BeGreaterThan(0);
                streamUrl.BitDepth.Should().BeGreaterThan(0);
                
                _output.WriteLine($"Stream URL: {streamUrl.Url.Substring(0, 50)}...");
            }
            finally
            {
                _rateLimiter.Release();
            }
        }

        [Fact]
        [Trait("Priority", "High")]
        public async Task SearchAlbums_WithRateLimiting_ShouldHandleThrottling()
        {
            // Arrange
            var tasks = new List<Task<QobuzSearchResult>>();
            var stopwatch = Stopwatch.StartNew();
            
            // Act - Send multiple concurrent requests to test rate limiting
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await _rateLimiter.WaitAsync();
                    try
                    {
                        return await _apiClient.SearchAlbumsAsync($"test query {i}", limit: 1);
                    }
                    finally
                    {
                        _rateLimiter.Release();
                    }
                }));
            }
            
            var results = await Task.WhenAll(tasks);
            stopwatch.Stop();
            
            // Assert
            results.Should().AllSatisfy(r => r.Should().NotBeNull());
            stopwatch.ElapsedMilliseconds.Should().BeGreaterThan(1000, "Rate limiting should space out requests");
            
            _output.WriteLine($"5 requests completed in {stopwatch.ElapsedMilliseconds}ms");
        }

        [Fact]
        [Trait("Priority", "Critical")]
        public async Task AuthenticationFlow_WithExpiredToken_ShouldRefreshAutomatically()
        {
            // Arrange
            var expiredSession = new QobuzSession
            {
                UserId = "test_user",
                AuthToken = "expired_token",
                ExpiresAt = DateTime.UtcNow.AddMinutes(-5) // Expired 5 minutes ago
            };
            
            var refreshedSession = new QobuzSession
            {
                UserId = "test_user",
                AuthToken = "new_token",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };
            
            _mockAuthService.SetupSequence(x => x.GetCachedSession())
                .Returns(expiredSession)
                .Returns(refreshedSession);
            
            _mockAuthService.Setup(x => x.RefreshSessionAsync())
                .ReturnsAsync(refreshedSession);
            
            // Act
            await _rateLimiter.WaitAsync();
            try
            {
                var results = await _apiClient.SearchAlbumsAsync("test", limit: 1);
                
                // Assert
                _mockAuthService.Verify(x => x.RefreshSessionAsync(), Times.Once);
                results.Should().NotBeNull();
            }
            finally
            {
                _rateLimiter.Release();
            }
        }

        [Fact]
        [Trait("Priority", "High")]
        public async Task GetAlbum_WithNetworkTimeout_ShouldRetryAndRecover()
        {
            // Arrange
            var httpClient = new HttpClient(new TimeoutHandler(3)); // Fail first 3 requests
            var apiClient = new QobuzApiClient(httpClient, _mockAuthService.Object, 
                Mock.Of<ILogger<QobuzApiClient>>());
            
            // Act
            var stopwatch = Stopwatch.StartNew();
            var album = await apiClient.GetAlbumAsync("0060254788359");
            stopwatch.Stop();
            
            // Assert
            album.Should().NotBeNull();
            stopwatch.ElapsedMilliseconds.Should().BeGreaterThan(3000, "Should have retried multiple times");
            
            _output.WriteLine($"Recovered after {stopwatch.ElapsedMilliseconds}ms");
        }

        [Fact]
        [Trait("Priority", "High")]
        public async Task ConcurrentAlbumRequests_ShouldMaintainDataIntegrity()
        {
            // Arrange
            var albumIds = new[] { "0060254788359", "0060254734819", "0825646318537" };
            var tasks = new List<Task<QobuzAlbum>>();
            
            // Act
            foreach (var albumId in albumIds)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await _rateLimiter.WaitAsync();
                    try
                    {
                        return await _apiClient.GetAlbumAsync(albumId);
                    }
                    finally
                    {
                        _rateLimiter.Release();
                    }
                }));
            }
            
            var albums = await Task.WhenAll(tasks);
            
            // Assert
            albums.Should().HaveCount(3);
            albums.Should().OnlyHaveUniqueItems(a => a.Id);
            albums.Should().AllSatisfy(album =>
            {
                album.Should().NotBeNull();
                album.Title.Should().NotBeNullOrEmpty();
                album.Tracks.Items.Should().NotBeEmpty();
            });
            
            _output.WriteLine($"Successfully fetched {albums.Length} albums concurrently");
        }

        [Fact]
        [Trait("Priority", "Medium")]
        public async Task SearchWithPagination_ShouldReturnConsistentResults()
        {
            // Arrange
            await _rateLimiter.WaitAsync();
            try
            {
                var query = "Electronic";
                var pageSize = 10;
                var totalPages = 3;
                var allResults = new List<QobuzAlbum>();
                
                // Act
                for (int page = 0; page < totalPages; page++)
                {
                    var results = await _apiClient.SearchAlbumsAsync(query, 
                        limit: pageSize, 
                        offset: page * pageSize);
                    
                    allResults.AddRange(results.Albums.Items);
                    
                    // Small delay between pages to respect rate limits
                    await Task.Delay(500);
                }
                
                // Assert
                allResults.Should().HaveCount(pageSize * totalPages);
                allResults.Select(a => a.Id).Should().OnlyHaveUniqueItems();
                
                _output.WriteLine($"Fetched {allResults.Count} unique albums across {totalPages} pages");
            }
            finally
            {
                _rateLimiter.Release();
            }
        }

        [Fact]
        [Trait("Priority", "High")]
        public async Task GetAlbum_MeasureResponseTimes_ShouldMeetPerformanceTargets()
        {
            // Arrange
            var albumIds = new[] { "0060254788359", "0060254734819", "0825646318537" };
            var responseTimes = new List<long>();
            
            // Act
            foreach (var albumId in albumIds)
            {
                await _rateLimiter.WaitAsync();
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    await _apiClient.GetAlbumAsync(albumId);
                    stopwatch.Stop();
                    responseTimes.Add(stopwatch.ElapsedMilliseconds);
                    
                    await Task.Delay(1000); // Rate limiting
                }
                finally
                {
                    _rateLimiter.Release();
                }
            }
            
            // Assert
            var averageTime = responseTimes.Average();
            var maxTime = responseTimes.Max();
            
            averageTime.Should().BeLessThan(2000, "Average response time should be under 2 seconds");
            maxTime.Should().BeLessThan(5000, "Max response time should be under 5 seconds");
            
            _output.WriteLine($"Response times - Avg: {averageTime}ms, Max: {maxTime}ms");
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            _rateLimiter?.Dispose();
        }

        // Helper class for simulating network timeouts
        private class TimeoutHandler : DelegatingHandler
        {
            private int _requestCount = 0;
            private readonly int _failCount;

            public TimeoutHandler(int failCount)
            {
                _failCount = failCount;
                InnerHandler = new HttpClientHandler();
            }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                _requestCount++;
                if (_requestCount <= _failCount)
                {
                    await Task.Delay(100);
                    throw new TaskCanceledException("Simulated timeout");
                }
                
                return await base.SendAsync(request, cancellationToken);
            }
        }
    }
}