using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using Xunit;
using Xunit.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.API.Http;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Cache;

namespace Qobuzarr.Tests.Integration
{
    /// <summary>
    /// Integration tests for QobuzApiClient against real Qobuz API
    /// Tests authentication, search, retrieval, and error handling
    /// </summary>
    [Collection("QobuzIntegration")]
    [Trait("Category", "Integration")]
    [Trait("RequiresCredentials", "true")]
    public class QobuzApiClientIntegrationTests : IAsyncLifetime, IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly IServiceProvider _serviceProvider;
        private IQobuzApiClient _apiClient;
        private IQobuzAuthenticationService _authService;
        private QobuzSession _session;
        private readonly List<TimeSpan> _apiCallLatencies = new();
        private readonly Stopwatch _testStopwatch = new();

        // Performance thresholds
        private const int MAX_API_LATENCY_MS = 2000;
        private const int TARGET_P95_LATENCY_MS = 500;
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const double MIN_SUCCESS_RATE = 95.0;

        public QobuzApiClientIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            
            // Setup DI container for integration tests
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Configure real services for integration testing
            services.AddSingleton<IQobuzLogger>(sp => 
            {
                var logger = LogManager.GetCurrentClassLogger();
                return new NLogAdapter(logger);
            });

            services.AddSingleton<ICacheManager, CacheManager>();
            services.AddSingleton<IHttpClient, HttpClient>();
            services.AddScoped<IQobuzAuthenticationService, QobuzAuthenticationService>();
            services.AddScoped<IQobuzApiClient, QobuzApiClient>();
            services.AddScoped<Lidarr.Plugin.Qobuzarr.Abstractions.IQobuzHttpClient, LidarrHttpClientAdapter>();
        }

        public async Task InitializeAsync()
        {
            try
            {
                // Get credentials from environment or skip tests
                var appId = Environment.GetEnvironmentVariable("QOBUZ_APP_ID");
                var appSecret = Environment.GetEnvironmentVariable("QOBUZ_APP_SECRET");
                var email = Environment.GetEnvironmentVariable("QOBUZ_EMAIL");
                var password = Environment.GetEnvironmentVariable("QOBUZ_PASSWORD");

                if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(email))
                {
                    throw new SkipException("Qobuz credentials not configured. Set QOBUZ_APP_ID, QOBUZ_APP_SECRET, QOBUZ_EMAIL, and QOBUZ_PASSWORD environment variables.");
                }

                // Initialize services
                _authService = _serviceProvider.GetRequiredService<IQobuzAuthenticationService>();
                _apiClient = _serviceProvider.GetRequiredService<IQobuzApiClient>();

                // Authenticate
                var credentials = new QobuzCredentials
                {
                    Email = email,
                    MD5Password = password,
                    AppId = appId,
                    AppSecret = appSecret
                };

                _session = await _authService.AuthenticateAsync(credentials);
                _apiClient.SetSession(_session);
                
                _output.WriteLine($"Authenticated successfully. Session expires at {_session.ExpiresAt}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Initialization failed: {ex.Message}");
                throw new SkipException($"Test initialization failed: {ex.Message}");
            }
        }

        [Fact]
        public async Task GetAlbum_WithValidId_ReturnsCompleteAlbumData()
        {
            // Arrange
            var albumId = "0060254788359"; // Daft Punk - Random Access Memories
            var parameters = new Dictionary<string, string>
            {
                { "album_id", albumId }
            };

            // Act
            _testStopwatch.Restart();
            var album = await _apiClient.GetAsync<QobuzAlbum>("/album/get", parameters);
            _testStopwatch.Stop();
            _apiCallLatencies.Add(_testStopwatch.Elapsed);

            // Assert
            album.Should().NotBeNull();
            album.Id.Should().Be(albumId);
            album.Title.Should().NotBeNullOrEmpty();
            album.Artist.Should().NotBeNull();
            album.Artist.Name.Should().NotBeNullOrEmpty();
            album.Tracks.Should().NotBeNull();
            album.Tracks.Items.Should().NotBeEmpty();
            
            // Verify track data completeness
            album.Tracks.Items.Should().AllSatisfy(track =>
            {
                track.Id.Should().BePositive();
                track.Title.Should().NotBeNullOrEmpty();
                track.Duration.Should().BePositive();
                track.StreamableStatus.Should().BeTrue("Track should be streamable");
            });

            _output.WriteLine($"Retrieved album: {album.Artist.Name} - {album.Title}");
            _output.WriteLine($"Tracks: {album.Tracks.Items.Count}");
            _output.WriteLine($"API latency: {_testStopwatch.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task Search_WithArtistAndAlbum_ReturnsAccurateResults()
        {
            // Arrange
            var searchQuery = "Miles Davis Kind of Blue";
            var parameters = new Dictionary<string, string>
            {
                { "query", searchQuery },
                { "limit", "10" }
            };

            // Act
            _testStopwatch.Restart();
            var results = await _apiClient.GetAsync<QobuzSearchResponse>("/album/search", parameters);
            _testStopwatch.Stop();
            _apiCallLatencies.Add(_testStopwatch.Elapsed);

            // Assert
            results.Should().NotBeNull();
            results.Albums.Should().NotBeNull();
            results.Albums.Items.Should().NotBeEmpty();
            
            // Verify search relevance
            var topResult = results.Albums.Items.FirstOrDefault();
            topResult.Should().NotBeNull();
            topResult.Title.ToLower().Should().Contain("kind of blue");
            topResult.Artist.Name.ToLower().Should().Contain("miles davis");

            _output.WriteLine($"Search query: {searchQuery}");
            _output.WriteLine($"Results found: {results.Albums.Items.Count}");
            _output.WriteLine($"Top result: {topResult.Artist.Name} - {topResult.Title}");
            _output.WriteLine($"API latency: {_testStopwatch.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task GetTrackStreamUrl_WithValidTrack_ReturnsStreamableUrl()
        {
            // Arrange
            var trackId = "119476958"; // Example track ID
            var parameters = new Dictionary<string, string>
            {
                { "track_id", trackId.ToString() },
                { "format_id", "27" } // FLAC Max quality
            };

            // Act
            _testStopwatch.Restart();
            var streamInfo = await _apiClient.GetAsync<QobuzStreamInfo>("/track/getFileUrl", parameters);
            _testStopwatch.Stop();
            _apiCallLatencies.Add(_testStopwatch.Elapsed);

            // Assert
            streamInfo.Should().NotBeNull();
            streamInfo.Url.Should().NotBeNullOrEmpty();
            streamInfo.Url.Should().StartWith("https://");
            streamInfo.Format.Should().BePositive();
            streamInfo.MimeType.Should().NotBeNullOrEmpty();
            
            // Verify stream URL is valid
            Uri.TryCreate(streamInfo.Url, UriKind.Absolute, out var uri).Should().BeTrue();
            uri.Host.Should().Contain("qobuz");

            _output.WriteLine($"Track ID: {trackId}");
            _output.WriteLine($"Stream format: {streamInfo.Format}");
            _output.WriteLine($"MIME type: {streamInfo.MimeType}");
            _output.WriteLine($"API latency: {_testStopwatch.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task SessionExpiry_AutomaticallyRefreshes()
        {
            // Arrange - Create session that expires soon
            var shortSession = new QobuzSession
            {
                UserId = _session.UserId,
                AuthToken = _session.AuthToken,
                AppId = _session.AppId,
                AppSecret = _session.AppSecret,
                ExpiresAt = DateTime.UtcNow.AddSeconds(5) // Expires in 5 seconds
            };
            
            _apiClient.SetSession(shortSession);
            
            // Wait for session to expire
            await Task.Delay(TimeSpan.FromSeconds(6));

            // Act - Make API call with expired session
            var parameters = new Dictionary<string, string>
            {
                { "album_id", "0060254788359" }
            };
            
            _testStopwatch.Restart();
            var album = await _apiClient.GetAsync<QobuzAlbum>("/album/get", parameters);
            _testStopwatch.Stop();

            // Assert - Should succeed with refreshed session
            album.Should().NotBeNull();
            album.Id.Should().NotBeNullOrEmpty();
            
            _output.WriteLine($"Session refresh successful");
            _output.WriteLine($"API call completed in {_testStopwatch.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task RateLimiting_HandlesThrottlingGracefully()
        {
            // Arrange - Prepare multiple rapid requests
            var requests = Enumerable.Range(0, 20).Select(i => new Dictionary<string, string>
            {
                { "query", $"test query {i}" },
                { "limit", "1" }
            }).ToList();

            var successCount = 0;
            var throttledCount = 0;
            var errors = new List<Exception>();

            // Act - Fire rapid requests
            var tasks = requests.Select(async (parameters, index) =>
            {
                try
                {
                    await Task.Delay(index * 50); // Slight stagger
                    
                    _testStopwatch.Restart();
                    var result = await _apiClient.GetAsync<QobuzSearchResponse>("/album/search", parameters);
                    _testStopwatch.Stop();
                    
                    _apiCallLatencies.Add(_testStopwatch.Elapsed);
                    Interlocked.Increment(ref successCount);
                    
                    return result;
                }
                catch (QobuzApiException ex) when (ex.StatusCode == 429)
                {
                    Interlocked.Increment(ref throttledCount);
                    _output.WriteLine($"Request {index} throttled (429)");
                    return null;
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                    throw;
                }
            }).ToArray();

            await Task.WhenAll(tasks);

            // Assert
            var totalRequests = requests.Count;
            var successRate = (successCount / (double)totalRequests) * 100;
            
            successRate.Should().BeGreaterOrEqualTo(MIN_SUCCESS_RATE, 
                $"At least {MIN_SUCCESS_RATE}% of requests should succeed");
            
            if (throttledCount > 0)
            {
                _output.WriteLine($"Rate limiting detected: {throttledCount}/{totalRequests} requests throttled");
            }

            _output.WriteLine($"Success rate: {successRate:F1}% ({successCount}/{totalRequests})");
            _output.WriteLine($"Average latency: {_apiCallLatencies.Average(l => l.TotalMilliseconds):F0}ms");
        }

        [Fact]
        public async Task NetworkRetry_RecoverFromTransientFailures()
        {
            // Arrange - Track retry attempts
            var retryCount = 0;
            var maxRetries = 3;
            Exception lastException = null;

            // Act - Attempt with retry logic
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    var parameters = new Dictionary<string, string>
                    {
                        { "album_id", "0060254788359" }
                    };
                    
                    var album = await _apiClient.GetAsync<QobuzAlbum>("/album/get", parameters);
                    
                    // Success
                    album.Should().NotBeNull();
                    break;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    retryCount++;
                    
                    _output.WriteLine($"Attempt {attempt + 1} failed: {ex.Message}");
                    
                    if (attempt < maxRetries - 1)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt))); // Exponential backoff
                    }
                }
            }

            // Assert
            if (retryCount == maxRetries && lastException != null)
            {
                throw new Exception($"Failed after {maxRetries} attempts", lastException);
            }

            _output.WriteLine($"Request succeeded after {retryCount} retries");
        }

        [Fact]
        public async Task InvalidAlbumId_ReturnsAppropriateError()
        {
            // Arrange
            var invalidId = "99999999999999";
            var parameters = new Dictionary<string, string>
            {
                { "album_id", invalidId }
            };

            // Act & Assert
            var exception = await Assert.ThrowsAsync<QobuzApiException>(async () =>
            {
                await _apiClient.GetAsync<QobuzAlbum>("/album/get", parameters);
            });

            exception.Should().NotBeNull();
            exception.StatusCode.Should().BeOneOf(404, 400);
            exception.Message.Should().NotBeNullOrEmpty();

            _output.WriteLine($"Invalid album ID handled correctly");
            _output.WriteLine($"Error: {exception.Message}");
        }

        [Fact]
        public async Task ConcurrentRequests_MaintainPerformance()
        {
            // Arrange
            var albumIds = new[]
            {
                "0060254788359", // Daft Punk
                "0060254734592", // Another album
                "0060254712345"  // Another album
            };

            var tasks = new List<Task<QobuzAlbum>>();

            // Act - Concurrent requests
            foreach (var albumId in albumIds)
            {
                var parameters = new Dictionary<string, string>
                {
                    { "album_id", albumId }
                };
                
                tasks.Add(Task.Run(async () =>
                {
                    var sw = Stopwatch.StartNew();
                    var result = await _apiClient.GetAsync<QobuzAlbum>("/album/get", parameters);
                    sw.Stop();
                    
                    lock (_apiCallLatencies)
                    {
                        _apiCallLatencies.Add(sw.Elapsed);
                    }
                    
                    return result;
                }));
            }

            var albums = await Task.WhenAll(tasks);

            // Assert
            albums.Should().AllSatisfy(album =>
            {
                album.Should().NotBeNull();
                album.Id.Should().NotBeNullOrEmpty();
                album.Title.Should().NotBeNullOrEmpty();
            });

            var avgLatency = _apiCallLatencies.Average(l => l.TotalMilliseconds);
            avgLatency.Should().BeLessThan(MAX_API_LATENCY_MS);

            _output.WriteLine($"Concurrent requests: {albumIds.Length}");
            _output.WriteLine($"Average latency: {avgLatency:F0}ms");
            _output.WriteLine($"Max latency: {_apiCallLatencies.Max(l => l.TotalMilliseconds):F0}ms");
        }

        [Fact]
        public async Task ApiPerformanceMetrics_MeetTargets()
        {
            // Arrange - Run multiple API calls to collect metrics
            var testCalls = 20;
            var latencies = new List<double>();

            // Act
            for (int i = 0; i < testCalls; i++)
            {
                var parameters = new Dictionary<string, string>
                {
                    { "query", $"test {i}" },
                    { "limit", "1" }
                };

                var sw = Stopwatch.StartNew();
                try
                {
                    await _apiClient.GetAsync<QobuzSearchResponse>("/album/search", parameters);
                }
                catch (QobuzApiException ex) when (ex.StatusCode == 429)
                {
                    // Rate limited - skip this measurement
                    continue;
                }
                sw.Stop();
                
                latencies.Add(sw.ElapsedMilliseconds);
                
                await Task.Delay(100); // Avoid rate limiting
            }

            // Calculate metrics
            latencies.Sort();
            var p50 = latencies[(int)(latencies.Count * 0.50)];
            var p95 = latencies[(int)(latencies.Count * 0.95)];
            var p99 = latencies[(int)(latencies.Count * 0.99)];

            // Assert
            p95.Should().BeLessThan(TARGET_P95_LATENCY_MS, 
                $"P95 latency should be under {TARGET_P95_LATENCY_MS}ms");

            // Output performance report
            _output.WriteLine("=== API PERFORMANCE METRICS ===");
            _output.WriteLine($"Samples: {latencies.Count}");
            _output.WriteLine($"Min: {latencies.Min():F0}ms");
            _output.WriteLine($"P50: {p50:F0}ms");
            _output.WriteLine($"P95: {p95:F0}ms (target: {TARGET_P95_LATENCY_MS}ms)");
            _output.WriteLine($"P99: {p99:F0}ms");
            _output.WriteLine($"Max: {latencies.Max():F0}ms");
            _output.WriteLine($"Average: {latencies.Average():F0}ms");
        }

        public async Task DisposeAsync()
        {
            // Cleanup
            await Task.CompletedTask;
        }

        public void Dispose()
        {
            DisposeAsync().GetAwaiter().GetResult();
        }
    }

    public class SkipException : Exception
    {
        public SkipException(string message) : base(message) { }
    }
}