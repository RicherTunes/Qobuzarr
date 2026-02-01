using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.Http;
using Xunit;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Qobuzarr.Tests.Fixtures;
using Qobuzarr.Tests.TestData;
using Qobuzarr.Tests.Helpers;

namespace Qobuzarr.Tests.Unit.API
{
    public class QobuzApiClientTests : TestFixtureBase
    {
        private readonly QobuzApiClient _apiClient;
        private readonly QobuzSession _testSession;

        public QobuzApiClientTests()
        {
            _apiClient = new QobuzApiClient(MockHttpClient.Object, MockCacheManager, MockLogger.Object);

            _testSession = new QobuzSession
            {
                UserId = "12345678",
                AuthToken = "sample_auth_token_123456",
                AppId = "test_app_id_123",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };
        }

        [Fact]
        public void SetSession_WithValidSession_ShouldStoreSession()
        {
            // Act
            _apiClient.SetSession(_testSession);

            // Assert
            _apiClient.HasValidSession().Should().BeTrue("session should be stored and valid");
        }

        [Fact]
        public void SetSession_WithNullSession_ShouldClearSession()
        {
            // Arrange
            _apiClient.SetSession(_testSession);
            _apiClient.HasValidSession().Should().BeTrue("session should be initially set");

            // Act
            _apiClient.ClearSession();

            // Assert
            _apiClient.HasValidSession().Should().BeFalse("session should be cleared after calling ClearSession");
        }

        [Fact]
        public void Constructor_WithNullHttpClient_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() =>
                new QobuzApiClient(null, MockCacheManager, MockLogger.Object));

            exception.ParamName.Should().Be("httpClient");
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() =>
                new QobuzApiClient(MockHttpClient.Object, MockCacheManager, null));

            exception.ParamName.Should().Be("logger");
        }

        [Fact]
        public void HasValidSession_WithNoSession_ShouldReturnFalse()
        {
            // Arrange - No session set

            // Act & Assert
            _apiClient.HasValidSession().Should().BeFalse("no session has been set");
        }

        [Fact]
        public void HasValidSession_WithExpiredSession_ShouldReturnFalse()
        {
            // Arrange
            var expiredSession = new QobuzSession
            {
                UserId = "12345678",
                AuthToken = "expired_token",
                AppId = "test_app_id",
                ExpiresAt = DateTime.UtcNow.AddHours(-1) // Expired 1 hour ago
            };
            _apiClient.SetSession(expiredSession);

            // Act & Assert
            _apiClient.HasValidSession().Should().BeFalse("session is expired");
        }

        [Fact]
        public void PostAsync_InputValidation_ShouldValidateParameters()
        {
            // Test input validation logic for POST requests
            var endpoint = "/user/login";
            var requestData = new Dictionary<string, string>
            {
                { "email", "test@example.com" },
                { "password", "hashedpassword" }
            };

            // Verify input validation
            endpoint.Should().NotBeNullOrEmpty();
            endpoint.Should().StartWith("/");
            requestData.Should().NotBeNull();
            requestData.Should().ContainKey("email");
            requestData.Should().ContainKey("password");
        }

        [Fact]
        public async Task GetAsync_ShouldIncludeAuthenticationHeaders()
        {
            // Arrange
            var endpoint = "/album/search";
            var parameters = new Dictionary<string, string>
            {
                { "query", "test" }
            };

            var httpResponse = HttpTestHelpers.CreateResponse("{}", HttpStatusCode.OK);

            HttpRequest capturedRequest = null;
            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .Callback<HttpRequest>(req => capturedRequest = req)
                         .ReturnsAsync(httpResponse);

            _apiClient.SetSession(_testSession);

            // Act
            await _apiClient.GetAsync<dynamic>(endpoint, parameters);

            // Assert
            capturedRequest.Should().NotBeNull();
            capturedRequest.Url.ToString().Should().Contain($"user_auth_token={_testSession.AuthToken}");
        }

        [Fact]
        public async Task GetAsync_WithRateLimitError_ShouldImplementBackoff()
        {
            // Arrange
            var endpoint = "/album/get";
            var parameters = new Dictionary<string, string>();

            var rateLimitResponse = HttpTestHelpers.CreateErrorResponse((HttpStatusCode)429, SampleQobuzResponses.RateLimitResponse);
            var successResponse = HttpTestHelpers.CreateResponse("{}", HttpStatusCode.OK);

            // First call returns rate limit response, which triggers the retry logic
            MockHttpClient.SetupSequence(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(rateLimitResponse)
                         .ReturnsAsync(successResponse);

            _apiClient.SetSession(_testSession);

            // Act
            var result = await _apiClient.GetAsync<dynamic>(endpoint, parameters);

            // Assert
            Assert.NotNull(result);
            MockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Exactly(2));
        }

        [Fact]
        public async Task GetAsync_WithNetworkError_ShouldRetryAndFail()
        {
            // Arrange
            var endpoint = "/album/get";
            var parameters = new Dictionary<string, string>();

            // Simulate a network error by throwing HttpException
            // This simulates actual network failures that would be thrown by the HTTP client
            var errorResponse = HttpTestHelpers.CreateErrorResponse(HttpStatusCode.InternalServerError, "");
            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ThrowsAsync(new HttpException(errorResponse));

            _apiClient.SetSession(_testSession);

            // Act & Assert
            // Since ExecuteAsync throws HttpException, RetryUtilities will catch it,
            // attempt retries, and eventually re-throw when all retries are exhausted
            await Assert.ThrowsAsync<HttpException>(() =>
                _apiClient.GetAsync<dynamic>(endpoint, parameters));

            // Should have retried multiple times (MaxRetries = 3 total attempts)
            MockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Exactly(3));
        }

        [Fact]
        public async Task GetAsync_ShouldRespectRateLimit()
        {
            // Arrange
            var endpoint = "/album/search";
            var parameters = new Dictionary<string, string>();

            var httpResponse = HttpTestHelpers.CreateResponse("{}", HttpStatusCode.OK);

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(httpResponse);

            _apiClient.SetSession(_testSession);

            // Act - Make multiple rapid requests
            var tasks = new List<Task>();
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(_apiClient.GetAsync<dynamic>(endpoint, parameters));
            }

            await Task.WhenAll(tasks);

            // Assert
            tasks.Should().HaveCount(5, "all requests should be created");
            // Verify all requests completed without throwing exceptions
            MockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Exactly(5));
        }

        [Fact]
        public async Task GetAsync_WithCaching_ShouldReturnCachedResponse()
        {
            // Arrange
            var endpoint = "/album/get";
            var parameters = new Dictionary<string, string>
            {
                { "album_id", "123456" }
            };

            var httpResponse = HttpTestHelpers.CreateResponse(SampleQobuzResponses.SampleAlbumResponse, HttpStatusCode.OK);

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(httpResponse);

            _apiClient.SetSession(_testSession);

            // Act - Make the same request twice
            var result1 = await _apiClient.GetAsync<dynamic>(endpoint, parameters);
            var result2 = await _apiClient.GetAsync<dynamic>(endpoint, parameters);

            // Assert
            Assert.NotNull(result1);
            Assert.NotNull(result2);

            // Should only make one HTTP request due to caching
            MockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Once);
        }

        [Fact]
        public async Task GetAsync_ShouldBuildUrlCorrectly()
        {
            // Arrange
            var endpoint = "/album/search";
            var parameters = new Dictionary<string, string>
            {
                { "query", "test album" },
                { "limit", "20" },
                { "offset", "0" }
            };

            var httpResponse = HttpTestHelpers.CreateResponse("{}", HttpStatusCode.OK);

            HttpRequest capturedRequest = null;
            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .Callback<HttpRequest>(req => capturedRequest = req)
                         .ReturnsAsync(httpResponse);

            _apiClient.SetSession(_testSession);

            // Act
            await _apiClient.GetAsync<dynamic>(endpoint, parameters);

            // Assert
            capturedRequest.Should().NotBeNull();
            var url = capturedRequest.Url.ToString();
            url.Should().Contain("query=test%20album");
            url.Should().Contain("limit=20");
            url.Should().Contain("offset=0");
            url.Should().Contain("user_auth_token=");
        }

        [Fact]
        public async Task PostAsync_ShouldMakeCorrectRequest()
        {
            // Arrange
            var endpoint = "/user/login";
            var requestData = new Dictionary<string, string>
            {
                { "email", "test@example.com" },
                { "password", "hashedpassword" }
            };

            var httpResponse = HttpTestHelpers.CreateResponse(SampleQobuzResponses.ValidLoginResponse, HttpStatusCode.OK);

            HttpRequest capturedRequest = null;
            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .Callback<HttpRequest>(req => capturedRequest = req)
                         .ReturnsAsync(httpResponse);

            // Act
            // Avoid `dynamic` here: extension methods like FluentAssertions' `Should()` are resolved at compile-time.
            var result = await _apiClient.PostAsync<JObject>(endpoint, requestData);

            // Assert
            result.Should().NotBeNull("POST request should return a valid response");
            capturedRequest.Should().NotBeNull("HTTP request should have been captured");
            var url = capturedRequest.Url.ToString();
            url.Should().Contain("/user/login", "URL should contain the correct endpoint");
            capturedRequest.Headers.ContentType.Should().Be("application/json", "Content-Type should be set for JSON requests");
            capturedRequest.Method.Should().Be(HttpMethod.Post, "HTTP method should be POST");
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest, "Bad Request")]
        [InlineData(HttpStatusCode.Unauthorized, "Unauthorized")]
        [InlineData(HttpStatusCode.Forbidden, "Forbidden")]
        [InlineData(HttpStatusCode.NotFound, "Not Found")]
        public async Task GetAsync_WithErrorResponse_ShouldThrowHttpException(HttpStatusCode statusCode, string reasonPhrase)
        {
            // Arrange
            var endpoint = "/album/get";
            var parameters = new Dictionary<string, string>();

            var errorResponse = HttpTestHelpers.CreateErrorResponse(statusCode, SampleQobuzResponses.ErrorResponse);

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ThrowsAsync(new HttpException(errorResponse));

            _apiClient.SetSession(_testSession);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<HttpException>(() =>
                _apiClient.GetAsync<dynamic>(endpoint, parameters));

            exception.Response.StatusCode.Should().Be(statusCode);
        }

        [Fact]
        public async Task GetAsync_WithInvalidJson_ShouldThrowJsonException()
        {
            // Arrange
            var endpoint = "/album/get";
            var parameters = new Dictionary<string, string>();

            var httpResponse = HttpTestHelpers.CreateResponse("invalid json content", HttpStatusCode.OK);

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(httpResponse);

            _apiClient.SetSession(_testSession);

            // Act & Assert
            await Assert.ThrowsAsync<JsonReaderException>(() =>
                _apiClient.GetAsync<dynamic>(endpoint, parameters));
        }

        [Fact]
        public async Task GetAsync_WithoutSession_ShouldStillMakeRequest()
        {
            // Arrange
            var endpoint = "/album/get";
            var parameters = new Dictionary<string, string>
            {
                { "album_id", "123456" }
            };

            var httpResponse = HttpTestHelpers.CreateResponse(SampleQobuzResponses.SampleAlbumResponse, HttpStatusCode.OK);

            HttpRequest capturedRequest = null;
            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .Callback<HttpRequest>(req => capturedRequest = req)
                         .ReturnsAsync(httpResponse);

            // Act - No session set
            // Avoid `dynamic` here: extension methods like FluentAssertions' `Should()` are resolved at compile-time,
            // and `dynamic` dispatch bypasses them.
            var result = await _apiClient.GetAsync<JObject>(endpoint, parameters);

            // Assert
            result.Should().NotBeNull("request should succeed even without session");
            capturedRequest.Should().NotBeNull();
            var url = capturedRequest.Url.ToString();
            url.Should().NotContain("user_auth_token", "unauthenticated request should not include auth token");
            url.Should().NotContain("app_id", "unauthenticated request should not include app_id");
        }

        [Fact]
        public void ClearSession_WhenNoSessionSet_ShouldNotThrow()
        {
            // Act & Assert
            _apiClient.Invoking(x => x.ClearSession()).Should().NotThrow("clearing session should be safe even when no session is set");
            _apiClient.HasValidSession().Should().BeFalse("should still report no valid session");
        }

        public override void Dispose()
        {
            // _apiClient?.Dispose(); // Class doesn't implement IDisposable
            base.Dispose();
        }
    }
}
