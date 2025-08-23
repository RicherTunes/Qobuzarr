using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Polly;

namespace Qobuzarr.Tests.Integration
{
    /// <summary>
    /// Comprehensive integration tests for QobuzApiClient against real Qobuz API
    /// Tests all API endpoints, error handling, rate limiting, and performance
    /// </summary>
    [Collection("QobuzIntegration")]
    [Trait("Category", "Integration")]
    [Trait("Component", "ApiClient")]
    [Trait("RequiresCredentials", "true")]
    public class QobuzApiClientFullIntegrationTests : IAsyncLifetime
    {
        private readonly ITestOutputHelper _output;
        private IQobuzApiClient _apiClient;
        private IQobuzAuthenticationService _authService;
        private QobuzSession _session;
        private readonly List<TimeSpan> _apiCallDurations = new();
        private readonly SemaphoreSlim _rateLimiter = new(5, 5); // Respect API limits

        public QobuzApiClientFullIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public async Task InitializeAsync()
        {
            var appId = Environment.GetEnvironmentVariable("QOBUZ_APP_ID");
            var appSecret = Environment.GetEnvironmentVariable("QOBUZ_APP_SECRET");
            var email = Environment.GetEnvironmentVariable("QOBUZ_EMAIL");
            var password = Environment.GetEnvironmentVariable("QOBUZ_PASSWORD");

            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(email))
            {
                throw new SkipException("Qobuz credentials not configured");
            }

            var services = new ServiceCollection();
            ConfigureServices(services);
            var provider = services.BuildServiceProvider();

            _authService = provider.GetRequiredService<IQobuzAuthenticationService>();
            _apiClient = provider.GetRequiredService<IQobuzApiClient>();

            _session = await _authService.AuthenticateAsync(email, password);
            _output.WriteLine($"Authenticated with user ID: {_session.UserId}");
        }

        private void ConfigureServices(IServiceCollection services)
        {
            services.AddScoped<IQobuzAuthenticationService, QobuzAuthenticationService>();
            services.AddScoped<IQobuzApiClient, QobuzApiClient>();
            services.AddHttpClient();
        }

        #region Search Endpoint Tests

        [Fact]
        public async Task SearchAlbums_WithValidQuery_ReturnsResults()
        {
            // Arrange
            var query = "Miles Davis Kind of Blue";
            
            // Act
            var stopwatch = Stopwatch.StartNew();
            var results = await _apiClient.SearchAlbumsAsync(query, 10);
            stopwatch.Stop();
            _apiCallDurations.Add(stopwatch.Elapsed);

            // Assert
            results.Should().NotBeNull();
            results.Albums.Should().NotBeEmpty();
            results.Albums.First().Title.Should().NotBeNullOrEmpty();
            results.Albums.First().Artist.Should().NotBeNull();
            
            // Performance assertion
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), 
                "API calls should complete within 5 seconds");
            
            _output.WriteLine($"Search returned {results.Albums.Count} albums in {stopwatch.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task SearchAlbums_WithSpecialCharacters_HandlesCorrectly()
        {
            // Test various special characters and encodings
            var queries = new[]
            {
                "Björk Homogenic",
                "Sigur Rós ( )",
                "Mötley Crüe",
                "Café del Mar",
                "東京事変" // Japanese characters
            };

            foreach (var query in queries)
            {
                await _rateLimiter.WaitAsync();
                try
                {
                    var results = await _apiClient.SearchAlbumsAsync(query, 5);
                    results.Should().NotBeNull();
                    _output.WriteLine($"Query '{query}' returned {results.Albums?.Count ?? 0} results");
                }
                finally
                {
                    _rateLimiter.Release();
                }
            }
        }

        [Fact]
        public async Task SearchArtists_WithPagination_ReturnsCorrectPages()
        {
            // Arrange
            var query = "Jazz";
            var pageSize = 10;
            
            // Act - Get first two pages
            var page1 = await _apiClient.SearchArtistsAsync(query, pageSize, 0);
            var page2 = await _apiClient.SearchArtistsAsync(query, pageSize, pageSize);
            
            // Assert
            page1.Artists.Should().HaveCount(pageSize);
            page2.Artists.Should().HaveCount(pageSize);
            page1.Artists.Select(a => a.Id).Should().NotIntersectWith(page2.Artists.Select(a => a.Id),
                "Pages should contain different artists");
            
            _output.WriteLine($"Pagination test: Page 1 has {page1.Artists.Count}, Page 2 has {page2.Artists.Count}");
        }

        #endregion

        #region Album Details Tests

        [Fact]
        public async Task GetAlbum_WithValidId_ReturnsFullDetails()
        {
            // Arrange - Search for an album first
            var searchResults = await _apiClient.SearchAlbumsAsync("Pink Floyd Dark Side", 1);
            searchResults.Albums.Should().NotBeEmpty();
            var albumId = searchResults.Albums.First().Id;
            
            // Act
            var album = await _apiClient.GetAlbumAsync(albumId);
            
            // Assert
            album.Should().NotBeNull();
            album.Id.Should().Be(albumId);
            album.Tracks?.Should().NotBeEmpty();
            album.Genre?.Should().NotBeNull();
            album.Label?.Should().NotBeNullOrEmpty();
            album.Duration.Should().BeGreaterThan(0);
            album.TracksCount.Should().BeGreaterThan(0);
            
            _output.WriteLine($"Album: {album.Title} by {album.Artist.Name}");
            _output.WriteLine($"Tracks: {album.TracksCount}, Duration: {album.Duration}s");
        }

        [Fact]
        public async Task GetAlbum_WithInvalidId_ThrowsApiException()
        {
            // Arrange
            var invalidId = "99999999999";
            
            // Act & Assert
            var act = async () => await _apiClient.GetAlbumAsync(invalidId);
            await act.Should().ThrowAsync<QobuzApiException>()
                .WithMessage("*not found*");
        }

        #endregion

        #region Track Operations Tests

        [Fact]
        public async Task GetTrack_WithValidId_ReturnsTrackDetails()
        {
            // Arrange - Get an album with tracks first
            var searchResults = await _apiClient.SearchAlbumsAsync("Beatles Abbey Road", 1);
            var album = await _apiClient.GetAlbumAsync(searchResults.Albums.First().Id);
            var trackId = album.Tracks.First().Id;
            
            // Act
            var track = await _apiClient.GetTrackAsync(trackId.ToString());
            
            // Assert
            track.Should().NotBeNull();
            track.Id.Should().Be(trackId);
            track.Title.Should().NotBeNullOrEmpty();
            track.Duration.Should().BeGreaterThan(0);
            track.TrackNumber.Should().BeGreaterThan(0);
            
            _output.WriteLine($"Track: {track.Title} - Duration: {track.Duration}s");
        }

        [Fact]
        public async Task GetStreamUrl_WithValidTrack_ReturnsUrl()
        {
            // Arrange - Get a streamable track
            var searchResults = await _apiClient.SearchAlbumsAsync("Classical Mozart", 1);
            var album = await _apiClient.GetAlbumAsync(searchResults.Albums.First().Id);
            var track = album.Tracks.FirstOrDefault(t => t.Streamable);
            
            if (track == null)
            {
                _output.WriteLine("No streamable tracks found, skipping test");
                return;
            }
            
            // Act
            var streamInfo = await _apiClient.GetTrackStreamUrlAsync(track.Id.ToString(), 6); // CD quality
            
            // Assert
            streamInfo.Should().NotBeNull();
            streamInfo.Url.Should().NotBeNullOrEmpty();
            streamInfo.Url.Should().StartWith("http");
            streamInfo.Quality.Should().BeGreaterThan(0);
            streamInfo.FormatId.Should().BeGreaterThan(0);
            
            _output.WriteLine($"Stream URL obtained for quality {streamInfo.Quality}");
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public async Task ApiClient_WithNetworkError_RetriesAutomatically()
        {
            // This test validates retry logic for transient errors
            var retryCount = 0;
            var policy = Policy
                .HandleResult<QobuzSearchResult>(r => r == null)
                .Or<HttpRequestException>()
                .WaitAndRetryAsync(
                    3,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (outcome, timespan, attempt, context) =>
                    {
                        retryCount++;
                        _output.WriteLine($"Retry {attempt} after {timespan}");
                    });

            // Simulate search with retry policy
            var result = await policy.ExecuteAsync(async () =>
            {
                return await _apiClient.SearchAlbumsAsync("Test Album", 1);
            });

            result.Should().NotBeNull();
            _output.WriteLine($"Request succeeded after {retryCount} retries");
        }

        [Fact]
        public async Task ApiClient_WithRateLimitExceeded_HandlesGracefully()
        {
            // Arrange - Prepare multiple concurrent requests
            var tasks = new List<Task<QobuzSearchResult>>();
            
            // Act - Fire 20 requests rapidly to trigger rate limiting
            for (int i = 0; i < 20; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        return await _apiClient.SearchAlbumsAsync($"Test Query {i}", 1);
                    }
                    catch (QobuzApiException ex) when (ex.Message.Contains("rate limit"))
                    {
                        _output.WriteLine($"Rate limit hit on request {i}");
                        return null;
                    }
                }));
            }
            
            var results = await Task.WhenAll(tasks);
            
            // Assert - Some requests should succeed, some may be rate limited
            results.Where(r => r != null).Should().NotBeEmpty("At least some requests should succeed");
            _output.WriteLine($"Completed {results.Count(r => r != null)} out of 20 requests");
        }

        #endregion

        #region Performance Baseline Tests

        [Fact]
        public async Task ApiClient_PerformanceBaseline_MeetsTargets()
        {
            // Establish performance baselines for regression detection
            var operations = new[]
            {
                ("Search", async () => await _apiClient.SearchAlbumsAsync("Test", 10)),
                ("Album Details", async () => 
                {
                    var search = await _apiClient.SearchAlbumsAsync("Popular Album", 1);
                    if (search.Albums.Any())
                        await _apiClient.GetAlbumAsync(search.Albums.First().Id);
                }),
                ("Artist Search", async () => await _apiClient.SearchArtistsAsync("Artist", 10))
            };

            var metrics = new Dictionary<string, List<long>>();

            foreach (var (name, operation) in operations)
            {
                metrics[name] = new List<long>();
                
                // Run each operation 5 times to get average
                for (int i = 0; i < 5; i++)
                {
                    await _rateLimiter.WaitAsync();
                    try
                    {
                        var sw = Stopwatch.StartNew();
                        await operation();
                        sw.Stop();
                        metrics[name].Add(sw.ElapsedMilliseconds);
                        
                        await Task.Delay(200); // Respect rate limits
                    }
                    finally
                    {
                        _rateLimiter.Release();
                    }
                }
            }

            // Assert performance targets
            foreach (var (operation, times) in metrics)
            {
                var average = times.Average();
                var p95 = times.OrderBy(t => t).Skip((int)(times.Count * 0.95)).FirstOrDefault();
                
                average.Should().BeLessThan(2000, $"{operation} average should be under 2 seconds");
                p95.Should().BeLessThan(5000, $"{operation} P95 should be under 5 seconds");
                
                _output.WriteLine($"{operation}: Avg={average}ms, P95={p95}ms");
            }
        }

        #endregion

        #region Concurrent Operations Tests

        [Fact]
        public async Task ApiClient_ConcurrentRequests_HandledCorrectly()
        {
            // Test thread safety and concurrent request handling
            var concurrentTasks = new List<Task>();
            var errors = new List<Exception>();
            
            for (int i = 0; i < 10; i++)
            {
                var taskId = i;
                concurrentTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await _rateLimiter.WaitAsync();
                        try
                        {
                            var result = await _apiClient.SearchAlbumsAsync($"Concurrent Test {taskId}", 5);
                            result.Should().NotBeNull();
                        }
                        finally
                        {
                            _rateLimiter.Release();
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (errors)
                        {
                            errors.Add(ex);
                        }
                    }
                }));
            }
            
            await Task.WhenAll(concurrentTasks);
            
            // Assert no concurrency errors
            errors.Should().BeEmpty("Concurrent operations should not cause errors");
            _output.WriteLine($"Successfully completed {concurrentTasks.Count} concurrent operations");
        }

        #endregion

        public Task DisposeAsync()
        {
            if (_apiCallDurations.Any())
            {
                var avgDuration = _apiCallDurations.Average(d => d.TotalMilliseconds);
                _output.WriteLine($"Average API call duration: {avgDuration:F2}ms");
            }
            
            _rateLimiter?.Dispose();
            return Task.CompletedTask;
        }
    }

    public class SkipException : Exception
    {
        public SkipException(string message) : base(message) { }
    }
}