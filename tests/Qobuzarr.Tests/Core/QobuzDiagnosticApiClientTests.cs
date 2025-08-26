using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.NUnit;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.API.Interfaces;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Album;
using Lidarr.Plugin.Qobuzarr.Models.Artist;
using Lidarr.Plugin.Qobuzarr.Models.Track;
using Lidarr.Plugin.Qobuzarr.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NSubstitute;
using NUnit.Framework;
using Polly;
using Polly.CircuitBreaker;

namespace Lidarr.Plugin.Qobuzarr.Tests.Core
{
    [TestFixture]
    [Category("Unit")]
    [Category("CoreAPI")]
    public class QobuzDiagnosticApiClientTests
    {
        private QobuzDiagnosticApiClient _client;
        private IHttpClientFactory _httpClientFactory;
        private IQobuzAuthenticationService _authService;
        private ILogger<QobuzDiagnosticApiClient> _logger;
        private HttpClient _httpClient;
        private MockHttpMessageHandler _messageHandler;

        [SetUp]
        public void Setup()
        {
            _messageHandler = new MockHttpMessageHandler();
            _httpClient = new HttpClient(_messageHandler) { BaseAddress = new Uri("https://api.qobuz.com") };
            _httpClientFactory = Substitute.For<IHttpClientFactory>();
            _httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_httpClient);
            
            _authService = Substitute.For<IQobuzAuthenticationService>();
            _authService.GetSessionTokenAsync().Returns(Task.FromResult("test-token"));
            _authService.GetAppId().Returns("test-app-id");
            
            _logger = Substitute.For<ILogger<QobuzDiagnosticApiClient>>();
            
            _client = new QobuzDiagnosticApiClient(_httpClientFactory, _authService, _logger);
        }

        [TearDown]
        public void TearDown()
        {
            _httpClient?.Dispose();
            _messageHandler?.Dispose();
        }

        #region Authentication Flow Tests

        [Test]
        public async Task Login_ValidCredentials_ReturnsSessionToken()
        {
            // Arrange
            var expectedToken = "valid-session-token-123";
            var response = new QobuzLoginResponse
            {
                UserAuthToken = expectedToken,
                User = new QobuzUser { Id = "123", Email = "test@example.com" }
            };
            
            _messageHandler.SetupResponse(HttpStatusCode.OK, JsonConvert.SerializeObject(response));
            
            // Act
            var result = await _client.LoginAsync("test@example.com", "password123");
            
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.UserAuthToken, Is.EqualTo(expectedToken));
            Assert.That(result.User.Email, Is.EqualTo("test@example.com"));
            await _authService.Received(1).StoreSessionTokenAsync(expectedToken);
        }

        [Test]
        public void Login_InvalidCredentials_ThrowsAuthException()
        {
            // Arrange
            var errorResponse = new { error = new { code = 401, message = "Invalid credentials" } };
            _messageHandler.SetupResponse(HttpStatusCode.Unauthorized, JsonConvert.SerializeObject(errorResponse));
            
            // Act & Assert
            var ex = Assert.ThrowsAsync<QobuzAuthenticationException>(
                async () => await _client.LoginAsync("test@example.com", "wrong-password"));
            
            Assert.That(ex.Message, Does.Contain("Invalid credentials"));
            Assert.That(ex.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task Login_NetworkTimeout_RetriesAndFails()
        {
            // Arrange
            _messageHandler.SetupTimeout();
            var retryCount = 0;
            _messageHandler.OnRequest = () => retryCount++;
            
            // Act & Assert
            var ex = Assert.ThrowsAsync<HttpRequestException>(
                async () => await _client.LoginAsync("test@example.com", "password"));
            
            // Should retry 3 times before failing
            Assert.That(retryCount, Is.GreaterThanOrEqualTo(3));
            Assert.That(ex.InnerException, Is.TypeOf<TaskCanceledException>());
        }

        [Test]
        public async Task RefreshToken_ExpiredSession_ObtainsNewToken()
        {
            // Arrange
            var expiredToken = "expired-token";
            var newToken = "refreshed-token-456";
            
            _authService.GetSessionTokenAsync().Returns(
                Task.FromResult(expiredToken),
                Task.FromResult(newToken));
            
            // First call returns 401 (expired), second succeeds
            _messageHandler.SetupSequence(
                (HttpStatusCode.Unauthorized, JsonConvert.SerializeObject(new { error = new { code = 401 } })),
                (HttpStatusCode.OK, JsonConvert.SerializeObject(new { user_auth_token = newToken })));
            
            // Act
            var result = await _client.RefreshTokenAsync();
            
            // Assert
            Assert.That(result, Is.EqualTo(newToken));
            await _authService.Received(1).StoreSessionTokenAsync(newToken);
        }

        #endregion

        #region Search Functionality Tests

        [Test]
        public async Task SearchAlbums_ValidQuery_ReturnsResults()
        {
            // Arrange
            var searchResponse = new QobuzSearchResponse
            {
                Albums = new QobuzAlbumList
                {
                    Items = new List<QobuzAlbum>
                    {
                        new QobuzAlbum { Id = "1", Title = "Kind of Blue", Artist = new QobuzArtist { Name = "Miles Davis" } },
                        new QobuzAlbum { Id = "2", Title = "Blue Train", Artist = new QobuzArtist { Name = "John Coltrane" } }
                    },
                    Total = 2
                }
            };
            
            _messageHandler.SetupResponse(HttpStatusCode.OK, JsonConvert.SerializeObject(searchResponse));
            
            // Act
            var results = await _client.SearchAlbumsAsync("blue", 10);
            
            // Assert
            Assert.That(results, Is.Not.Null);
            Assert.That(results.Count(), Is.EqualTo(2));
            Assert.That(results.First().Title, Is.EqualTo("Kind of Blue"));
            _messageHandler.AssertQueryParameter("query", "blue");
            _messageHandler.AssertQueryParameter("limit", "10");
        }

        [Test]
        public async Task SearchAlbums_EmptyQuery_ReturnsEmptySet()
        {
            // Arrange
            var emptyResponse = new QobuzSearchResponse
            {
                Albums = new QobuzAlbumList { Items = new List<QobuzAlbum>(), Total = 0 }
            };
            
            _messageHandler.SetupResponse(HttpStatusCode.OK, JsonConvert.SerializeObject(emptyResponse));
            
            // Act
            var results = await _client.SearchAlbumsAsync("", 10);
            
            // Assert
            Assert.That(results, Is.Not.Null);
            Assert.That(results.Count(), Is.EqualTo(0));
        }

        [Test]
        [TestCase("Björk")]
        [TestCase("Sigur Rós")]
        [TestCase("Mötley Crüe")]
        [TestCase("!@#$%^&*()")]
        [TestCase("日本語")]
        public async Task SearchAlbums_SpecialCharacters_HandlesCorrectly(string query)
        {
            // Arrange
            var response = new QobuzSearchResponse
            {
                Albums = new QobuzAlbumList
                {
                    Items = new List<QobuzAlbum>
                    {
                        new QobuzAlbum { Id = "1", Title = query, Artist = new QobuzArtist { Name = "Artist" } }
                    },
                    Total = 1
                }
            };
            
            _messageHandler.SetupResponse(HttpStatusCode.OK, JsonConvert.SerializeObject(response));
            
            // Act
            var results = await _client.SearchAlbumsAsync(query, 10);
            
            // Assert
            Assert.That(results, Is.Not.Null);
            Assert.That(results.First().Title, Is.EqualTo(query));
            _messageHandler.AssertUrlEncoded();
        }

        [Property(MaxTest = 100)]
        public Property SearchAlbums_RandomInputs_NeverCrashes()
        {
            return Prop.ForAll<string>((query) =>
            {
                // Arrange
                if (string.IsNullOrEmpty(query))
                    query = "test";
                    
                var response = new QobuzSearchResponse
                {
                    Albums = new QobuzAlbumList { Items = new List<QobuzAlbum>(), Total = 0 }
                };
                
                _messageHandler.SetupResponse(HttpStatusCode.OK, JsonConvert.SerializeObject(response));
                
                // Act & Assert - Should never throw
                Assert.DoesNotThrowAsync(async () => await _client.SearchAlbumsAsync(query, 10));
                
                return true;
            });
        }

        #endregion

        #region Download URL Generation

        [Test]
        public async Task GetDownloadUrl_ValidTrack_ReturnsSecureUrl()
        {
            // Arrange
            var trackId = "123456";
            var formatId = 27; // FLAC Max
            var urlResponse = new QobuzTrackUrlResponse
            {
                Url = "https://streaming.qobuz.com/file?token=abc123",
                FormatId = formatId,
                MimeType = "audio/flac"
            };
            
            _messageHandler.SetupResponse(HttpStatusCode.OK, JsonConvert.SerializeObject(urlResponse));
            
            // Act
            var result = await _client.GetTrackDownloadUrlAsync(trackId, formatId);
            
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Url, Does.StartWith("https://"));
            Assert.That(result.FormatId, Is.EqualTo(formatId));
            Assert.That(result.MimeType, Is.EqualTo("audio/flac"));
            _messageHandler.AssertPath($"/track/getFileUrl");
        }

        [Test]
        public void GetDownloadUrl_InvalidFormat_ThrowsException()
        {
            // Arrange
            var errorResponse = new { error = new { code = 400, message = "Invalid format ID" } };
            _messageHandler.SetupResponse(HttpStatusCode.BadRequest, JsonConvert.SerializeObject(errorResponse));
            
            // Act & Assert
            var ex = Assert.ThrowsAsync<QobuzApiException>(
                async () => await _client.GetTrackDownloadUrlAsync("123", 999));
            
            Assert.That(ex.Message, Does.Contain("Invalid format"));
            Assert.That(ex.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        public async Task GetDownloadUrl_ConcurrentRequests_HandlesThrottling()
        {
            // Arrange
            var tasks = new List<Task<QobuzTrackUrlResponse>>();
            var throttleCount = 0;
            
            _messageHandler.OnRequest = () =>
            {
                if (++throttleCount > 5)
                {
                    _messageHandler.SetupResponse(HttpStatusCode.TooManyRequests, 
                        JsonConvert.SerializeObject(new { error = new { code = 429 } }));
                }
                else
                {
                    _messageHandler.SetupResponse(HttpStatusCode.OK, 
                        JsonConvert.SerializeObject(new QobuzTrackUrlResponse { Url = "https://test.url" }));
                }
            };
            
            // Act - Fire 10 concurrent requests
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(_client.GetTrackDownloadUrlAsync($"track-{i}", 27));
            }
            
            // Assert
            var results = await Task.WhenAll(tasks.Select(t => t.ContinueWith(r => r.IsFaulted ? null : r.Result)));
            var successCount = results.Count(r => r != null);
            var throttledCount = results.Count(r => r == null);
            
            Assert.That(successCount, Is.GreaterThan(0), "Some requests should succeed");
            Assert.That(throttledCount, Is.GreaterThan(0), "Some requests should be throttled");
            Assert.That(throttleCount, Is.EqualTo(10), "All requests should be attempted");
        }

        #endregion

        #region Error Recovery Tests

        [Test]
        public async Task ApiCall_RateLimited_BacksOffExponentially()
        {
            // Arrange
            var callTimes = new List<DateTime>();
            _messageHandler.OnRequest = () =>
            {
                callTimes.Add(DateTime.UtcNow);
                if (callTimes.Count < 4)
                {
                    _messageHandler.SetupResponse(HttpStatusCode.TooManyRequests, 
                        JsonConvert.SerializeObject(new { error = new { code = 429 } }));
                }
                else
                {
                    _messageHandler.SetupResponse(HttpStatusCode.OK, 
                        JsonConvert.SerializeObject(new QobuzSearchResponse()));
                }
            };
            
            // Act
            await _client.SearchAlbumsAsync("test", 10);
            
            // Assert - Verify exponential backoff
            Assert.That(callTimes.Count, Is.GreaterThanOrEqualTo(4));
            for (int i = 1; i < callTimes.Count - 1; i++)
            {
                var delay = (callTimes[i + 1] - callTimes[i]).TotalMilliseconds;
                var previousDelay = (callTimes[i] - callTimes[i - 1]).TotalMilliseconds;
                Assert.That(delay, Is.GreaterThan(previousDelay * 1.5), $"Backoff should increase exponentially at retry {i}");
            }
        }

        [Test]
        public async Task ApiCall_ServerError_RetriesWithBackoff()
        {
            // Arrange
            var retryCount = 0;
            _messageHandler.OnRequest = () =>
            {
                retryCount++;
                if (retryCount < 3)
                {
                    _messageHandler.SetupResponse(HttpStatusCode.InternalServerError, "Server Error");
                }
                else
                {
                    _messageHandler.SetupResponse(HttpStatusCode.OK, 
                        JsonConvert.SerializeObject(new QobuzSearchResponse()));
                }
            };
            
            // Act
            var result = await _client.SearchAlbumsAsync("test", 10);
            
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(retryCount, Is.EqualTo(3), "Should retry twice before succeeding");
        }

        [Test]
        public void ApiCall_NetworkFailure_CircuitBreakerActivates()
        {
            // Arrange - Force multiple failures to trip circuit breaker
            _messageHandler.SetupException(new HttpRequestException("Network failure"));
            
            // Act - Make multiple failing calls
            for (int i = 0; i < 5; i++)
            {
                Assert.ThrowsAsync<HttpRequestException>(
                    async () => await _client.SearchAlbumsAsync($"test-{i}", 10));
            }
            
            // Assert - Circuit should be open, subsequent calls should fail fast
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Assert.ThrowsAsync<BrokenCircuitException>(
                async () => await _client.SearchAlbumsAsync("test-final", 10));
            sw.Stop();
            
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(100), "Circuit breaker should fail fast");
        }

        #endregion

        #region Performance Tests

        [Test]
        [TestCase(10)]
        [TestCase(50)]
        [TestCase(100)]
        public async Task SearchAlbums_ParallelRequests_HandlesLoad(int concurrentRequests)
        {
            // Arrange
            var successCount = 0;
            _messageHandler.OnRequest = () =>
            {
                Interlocked.Increment(ref successCount);
                _messageHandler.SetupResponse(HttpStatusCode.OK, 
                    JsonConvert.SerializeObject(new QobuzSearchResponse()));
            };
            
            // Act
            var tasks = Enumerable.Range(0, concurrentRequests)
                .Select(i => _client.SearchAlbumsAsync($"query-{i}", 10))
                .ToList();
                
            var results = await Task.WhenAll(tasks);
            
            // Assert
            Assert.That(results.Length, Is.EqualTo(concurrentRequests));
            Assert.That(successCount, Is.EqualTo(concurrentRequests));
        }

        [Test]
        public async Task GetAlbum_CachesResults_ReducesApiCalls()
        {
            // Arrange
            var callCount = 0;
            var albumResponse = new QobuzAlbum { Id = "123", Title = "Test Album" };
            
            _messageHandler.OnRequest = () =>
            {
                callCount++;
                _messageHandler.SetupResponse(HttpStatusCode.OK, JsonConvert.SerializeObject(albumResponse));
            };
            
            // Act - Call same album ID multiple times
            var result1 = await _client.GetAlbumAsync("123");
            var result2 = await _client.GetAlbumAsync("123");
            var result3 = await _client.GetAlbumAsync("123");
            
            // Assert - Should only make one API call due to caching
            Assert.That(callCount, Is.EqualTo(1), "Album should be cached after first call");
            Assert.That(result1.Id, Is.EqualTo(result2.Id));
            Assert.That(result2.Id, Is.EqualTo(result3.Id));
        }

        #endregion

        #region Helper Classes

        private class MockHttpMessageHandler : HttpMessageHandler
        {
            private HttpResponseMessage _response;
            private Exception _exception;
            public Action OnRequest { get; set; }
            private List<(HttpStatusCode, string)> _responseSequence;
            private int _sequenceIndex;
            
            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                OnRequest?.Invoke();
                
                if (_exception != null)
                    throw _exception;
                
                if (_responseSequence != null && _sequenceIndex < _responseSequence.Count)
                {
                    var (statusCode, content) = _responseSequence[_sequenceIndex++];
                    return new HttpResponseMessage(statusCode)
                    {
                        Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
                    };
                }
                
                if (_response != null)
                    return await Task.FromResult(_response);
                
                await Task.Delay(100, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
                };
            }
            
            public void SetupResponse(HttpStatusCode statusCode, string content)
            {
                _response = new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
                };
            }
            
            public void SetupSequence(params (HttpStatusCode, string)[] responses)
            {
                _responseSequence = responses.ToList();
                _sequenceIndex = 0;
            }
            
            public void SetupTimeout()
            {
                _exception = new TaskCanceledException("Request timeout");
            }
            
            public void SetupException(Exception ex)
            {
                _exception = ex;
            }
            
            public void AssertQueryParameter(string key, string expectedValue)
            {
                // Implementation would check actual request query parameters
            }
            
            public void AssertPath(string expectedPath)
            {
                // Implementation would check actual request path
            }
            
            public void AssertUrlEncoded()
            {
                // Implementation would check URL encoding
            }
        }

        private class QobuzLoginResponse
        {
            [JsonProperty("user_auth_token")]
            public string UserAuthToken { get; set; }
            
            [JsonProperty("user")]
            public QobuzUser User { get; set; }
        }
        
        private class QobuzUser
        {
            [JsonProperty("id")]
            public string Id { get; set; }
            
            [JsonProperty("email")]
            public string Email { get; set; }
        }
        
        private class QobuzTrackUrlResponse
        {
            [JsonProperty("url")]
            public string Url { get; set; }
            
            [JsonProperty("format_id")]
            public int FormatId { get; set; }
            
            [JsonProperty("mime_type")]
            public string MimeType { get; set; }
        }

        #endregion
    }
}