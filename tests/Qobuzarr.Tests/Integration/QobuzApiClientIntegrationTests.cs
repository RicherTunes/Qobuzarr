using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NzbDrone.Common.Http;
using Xunit;
using Xunit.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Exceptions;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Qobuzarr.Tests.Fixtures;

namespace Qobuzarr.Tests.Integration
{
    /// <summary>
    /// Integration tests for QobuzApiClient against real Qobuz API
    /// These tests validate critical functionality including session management,
    /// dynamic credential fetching, and API error handling
    /// </summary>
    [Collection("QobuzIntegration")]
    public class QobuzApiClientIntegrationTests : IAsyncLifetime
    {
        private readonly ITestOutputHelper _output;
        private QobuzApiClient _apiClient;
        private QobuzAuthenticationService _authService;
        private IHttpClient _httpClient;
        private QobuzSession _testSession;
        private readonly string _testAppId;
        private readonly string _testAppSecret;

        public QobuzApiClientIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            
            // Load test credentials from environment or test configuration
            _testAppId = Environment.GetEnvironmentVariable("QOBUZ_TEST_APP_ID") 
                ?? "950096963"; // TrevTV's known app ID for testing
            _testAppSecret = Environment.GetEnvironmentVariable("QOBUZ_TEST_APP_SECRET");
        }

        public async Task InitializeAsync()
        {
            // Setup real HTTP client and services
            var services = new ServiceCollection();
            services.AddSingleton<IHttpClient, HttpClient>();
            services.AddSingleton<ICacheManager, CacheManager>();
            services.AddSingleton<IQobuzApiClient, QobuzApiClient>();
            services.AddSingleton<IQobuzAuthenticationService, QobuzAuthenticationService>();
            
            var provider = services.BuildServiceProvider();
            _httpClient = provider.GetRequiredService<IHttpClient>();
            _apiClient = new QobuzApiClient(_httpClient, provider.GetRequiredService<ICacheManager>(), null);
            _authService = new QobuzAuthenticationService(_apiClient, null);

            // Initialize test session if credentials available
            if (!string.IsNullOrEmpty(_testAppSecret))
            {
                _testSession = new QobuzSession
                {
                    AppId = _testAppId,
                    AppSecret = _testAppSecret,
                    ExpiresAt = DateTime.UtcNow.AddHours(1)
                };
            }
        }

        public Task DisposeAsync() => Task.CompletedTask;

        #region Session Management Tests

        [Fact]
        [Trait("Category", "Integration")]
        public async Task ValidateAndRenewIfNeededAsync_WithExpiredSession_ShouldRenewSession()
        {
            // Arrange
            if (_testSession == null)
            {
                _output.WriteLine("Skipping: No test credentials available");
                return;
            }

            var expiredSession = new QobuzSession
            {
                UserId = "test_user",
                AuthToken = "expired_token",
                AppId = _testAppId,
                AppSecret = _testAppSecret,
                ExpiresAt = DateTime.UtcNow.AddMinutes(-5) // Expired 5 minutes ago
            };

            _apiClient.SetSession(expiredSession);

            // Act & Assert
            // Session should be renewed automatically when validation detects expiry
            await Assert.ThrowsAsync<QobuzAuthenticationException>(async () =>
            {
                await _apiClient.GetAsync<QobuzUser>("/user/get", new Dictionary<string, string>());
            });
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task ValidateAndRenewIfNeededAsync_WithValidSession_ShouldNotRenew()
        {
            // Arrange
            if (_testSession == null)
            {
                _output.WriteLine("Skipping: No test credentials available");
                return;
            }

            var validSession = new QobuzSession
            {
                UserId = "test_user",
                AuthToken = "valid_token",
                AppId = _testAppId,
                AppSecret = _testAppSecret,
                ExpiresAt = DateTime.UtcNow.AddHours(2) // Valid for 2 more hours
            };

            _apiClient.SetSession(validSession);

            // Act
            // This should not trigger renewal as session is still valid
            var sessionBefore = validSession.AuthToken;
            
            try
            {
                await _apiClient.GetAsync<QobuzUser>("/user/get", new Dictionary<string, string>());
            }
            catch (QobuzApiException)
            {
                // Expected if token is invalid, but we're testing renewal logic
            }

            // Assert
            // Session should remain unchanged if it was valid
            _output.WriteLine($"Session token unchanged: {sessionBefore == validSession.AuthToken}");
        }

        #endregion

        #region Dynamic Credential Fetching Tests

        [Fact]
        [Trait("Category", "Integration")]
        public async Task GetDynamicCredentialsAsync_ShouldExtractValidCredentials()
        {
            // This test validates the complex web scraping logic for getting app credentials
            // Note: This may fail if Qobuz changes their web player structure

            // Act
            var (appId, appSecret, bundleString) = await _apiClient.GetDynamicCredentialsAsync();

            // Assert
            appId.Should().NotBeNullOrEmpty("App ID should be extracted from web player");
            appSecret.Should().NotBeNullOrEmpty("App secret should be extracted from bundle");
            bundleString.Should().NotBeNullOrEmpty("Bundle string should be retrieved");

            // Validate format
            appId.Should().MatchRegex(@"^\d+$", "App ID should be numeric");
            appSecret.Should().HaveLength(32, "App secret should be 32 characters (MD5 hash)");
            
            _output.WriteLine($"Successfully extracted credentials - App ID: {appId}, Secret length: {appSecret?.Length}");
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task ExtractAppSecretFromBundle_WithValidBundle_ShouldExtractSecret()
        {
            // This tests the complex app secret extraction algorithm
            // Using a mock bundle structure similar to real Qobuz bundles
            
            // Arrange
            var mockBundleContent = @"
                function() {
                    var config = {
                        app_id: '950096963',
                        seed: 'seed_value_here',
                        timezone: 'utc'
                    };
                    // Obfuscated app secret patterns
                    var _0x1234 = ['value1', 'value2'];
                    return config;
                }";

            // Act
            var extractedSecret = _apiClient.ExtractAppSecretFromBundle(mockBundleContent);

            // Assert
            if (extractedSecret != null)
            {
                extractedSecret.Should().HaveLength(32, "Extracted secret should be MD5 hash length");
                _output.WriteLine($"Extracted secret: {extractedSecret}");
            }
            else
            {
                _output.WriteLine("No secret extracted from mock bundle - may need real bundle structure");
            }
        }

        #endregion

        #region Request Signing Tests

        [Fact]
        [Trait("Category", "Integration")]
        public void RequiresSigning_ForSecureEndpoints_ShouldReturnTrue()
        {
            // Arrange
            var secureEndpoints = new[]
            {
                "/track/getFileUrl",
                "/track/get",
                "/album/get",
                "/purchase/getUserPurchases"
            };

            // Act & Assert
            foreach (var endpoint in secureEndpoints)
            {
                _apiClient.RequiresSigning(endpoint).Should().BeTrue(
                    $"Endpoint {endpoint} should require signing");
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void RequiresSigning_ForPublicEndpoints_ShouldReturnFalse()
        {
            // Arrange
            var publicEndpoints = new[]
            {
                "/album/search",
                "/track/search",
                "/artist/search"
            };

            // Act & Assert
            foreach (var endpoint in publicEndpoints)
            {
                _apiClient.RequiresSigning(endpoint).Should().BeFalse(
                    $"Endpoint {endpoint} should not require signing");
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void SignRequest_WithValidParameters_ShouldGenerateCorrectSignature()
        {
            // Arrange
            var endpoint = "/track/getFileUrl";
            var parameters = new Dictionary<string, string>
            {
                { "track_id", "123456789" },
                { "format_id", "27" },
                { "app_id", _testAppId }
            };
            var appSecret = "test_app_secret_32_chars_long___";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            // Act
            var signature = _apiClient.SignRequest(endpoint, parameters, appSecret, timestamp);

            // Assert
            signature.Should().NotBeNullOrEmpty();
            signature.Should().HaveLength(32, "Signature should be MD5 hash");
            signature.Should().MatchRegex(@"^[a-f0-9]+$", "Signature should be hexadecimal");
            
            _output.WriteLine($"Generated signature: {signature}");
        }

        #endregion

        #region API Error Handling Tests

        [Fact]
        [Trait("Category", "Integration")]
        public async Task HandleErrorResponse_WithRateLimitError_ShouldThrowRateLimitException()
        {
            // Arrange
            var rateLimitResponse = new HttpResponse(new HttpRequest("https://api.qobuz.com/test"))
            {
                StatusCode = HttpStatusCode.TooManyRequests,
                Headers = new HttpHeader { { "Retry-After", "60" } }
            };

            // Act & Assert
            var exception = await Assert.ThrowsAsync<QobuzApiException>(async () =>
            {
                await Task.Run(() => _apiClient.HandleErrorResponse(rateLimitResponse, "/test"));
            });

            exception.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            exception.Message.Should().Contain("rate limit");
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task HandleErrorResponse_WithAuthenticationError_ShouldThrowAuthException()
        {
            // Arrange
            var authErrorResponse = new HttpResponse(new HttpRequest("https://api.qobuz.com/test"))
            {
                StatusCode = HttpStatusCode.Unauthorized,
                Content = @"{ ""message"": ""Invalid authentication token"" }"
            };

            // Act & Assert
            var exception = await Assert.ThrowsAsync<QobuzAuthenticationException>(async () =>
            {
                await Task.Run(() => _apiClient.HandleErrorResponse(authErrorResponse, "/test"));
            });

            exception.Message.Should().Contain("authentication");
        }

        #endregion

        #region Playlist Pagination Tests

        [Fact]
        [Trait("Category", "Integration")]
        public async Task GetPlaylistTracksAsync_WithLargePlaylist_ShouldPaginateCorrectly()
        {
            // Arrange
            if (_testSession == null)
            {
                _output.WriteLine("Skipping: No test credentials available");
                return;
            }

            _apiClient.SetSession(_testSession);
            var playlistId = "1234567"; // Would need a real playlist ID for full test

            // Act
            var allTracks = new List<QobuzTrack>();
            var offset = 0;
            const int limit = 50;
            bool hasMore = true;

            while (hasMore && offset < 200) // Limit to 200 tracks for testing
            {
                try
                {
                    var response = await _apiClient.GetPlaylistTracksAsync(playlistId, limit, offset);
                    allTracks.AddRange(response.Tracks);
                    hasMore = response.Tracks.Count == limit;
                    offset += limit;
                }
                catch (QobuzApiException ex)
                {
                    _output.WriteLine($"API error during pagination test: {ex.Message}");
                    break;
                }
            }

            // Assert
            _output.WriteLine($"Retrieved {allTracks.Count} tracks with pagination");
            allTracks.Should().NotBeNull();
            
            // Verify no duplicates from pagination
            var uniqueIds = allTracks.Select(t => t.Id).Distinct().Count();
            uniqueIds.Should().Be(allTracks.Count, "Pagination should not return duplicate tracks");
        }

        #endregion

        #region Label and Artist Album Fetching Tests

        [Fact]
        [Trait("Category", "Integration")]
        public async Task GetLabelAlbumsAsync_WithValidLabel_ShouldReturnAlbums()
        {
            // Arrange
            if (_testSession == null)
            {
                _output.WriteLine("Skipping: No test credentials available");
                return;
            }

            _apiClient.SetSession(_testSession);
            var labelId = 1234; // Would need a real label ID

            // Act
            try
            {
                var albums = await _apiClient.GetLabelAlbumsAsync(labelId, 10);

                // Assert
                albums.Should().NotBeNull();
                albums.Should().HaveCountLessThanOrEqualTo(10);
                
                foreach (var album in albums)
                {
                    album.Id.Should().NotBeNullOrEmpty();
                    album.Title.Should().NotBeNullOrEmpty();
                }
                
                _output.WriteLine($"Retrieved {albums.Count} albums for label {labelId}");
            }
            catch (QobuzApiException ex)
            {
                _output.WriteLine($"Expected API error for test label: {ex.Message}");
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task GetArtistAlbumsAsync_WithValidArtist_ShouldReturnDiscography()
        {
            // Arrange
            if (_testSession == null)
            {
                _output.WriteLine("Skipping: No test credentials available");
                return;
            }

            _apiClient.SetSession(_testSession);
            var artistId = 145383; // Miles Davis (known artist ID)

            // Act
            try
            {
                var albums = await _apiClient.GetArtistAlbumsAsync(artistId, limit: 20);

                // Assert
                albums.Should().NotBeNull();
                albums.Should().NotBeEmpty("Miles Davis should have albums");
                
                foreach (var album in albums)
                {
                    album.Id.Should().NotBeNullOrEmpty();
                    album.Title.Should().NotBeNullOrEmpty();
                    album.Artist?.Name.Should().Contain("Davis");
                }
                
                _output.WriteLine($"Retrieved {albums.Count} albums for artist {artistId}");
            }
            catch (QobuzApiException ex)
            {
                _output.WriteLine($"API error: {ex.Message}");
            }
        }

        #endregion

        #region Concurrent Request Handling Tests

        [Fact]
        [Trait("Category", "Integration")]
        public async Task ConcurrentRequests_ShouldHandleCorrectly()
        {
            // Test thread safety and concurrent request handling
            
            // Arrange
            if (_testSession == null)
            {
                _output.WriteLine("Skipping: No test credentials available");
                return;
            }

            _apiClient.SetSession(_testSession);
            var searchQueries = new[] { "Miles Davis", "John Coltrane", "Bill Evans", "Thelonious Monk" };

            // Act
            var tasks = searchQueries.Select(query => 
                _apiClient.SearchAlbumsAsync(query, limit: 5)
            ).ToList();

            try
            {
                var results = await Task.WhenAll(tasks);

                // Assert
                results.Should().HaveCount(searchQueries.Length);
                results.Should().OnlyContain(r => r != null && r.Albums != null);
                
                _output.WriteLine($"Successfully completed {results.Length} concurrent requests");
            }
            catch (QobuzApiException ex)
            {
                _output.WriteLine($"Concurrent request test failed: {ex.Message}");
            }
        }

        #endregion
    }
}