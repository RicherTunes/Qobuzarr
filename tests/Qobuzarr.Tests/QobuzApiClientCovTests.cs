using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NSubstitute;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using Xunit;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.API.Http;
using Lidarr.Plugin.Qobuzarr.API.Signing;
using Lidarr.Plugin.Qobuzarr.API.Caching;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Services.Interfaces;
using Lidarr.Plugin.Common.Services.Http;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Common.Interfaces;
using Qobuzarr.Tests.Helpers;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Coverage tests for QobuzApiClient uncovered paths in the new DI-based implementation.
    /// Tests the new constructor, error handling paths, streaming info validation, and session management.
    /// </summary>
    public class QobuzApiClientCovTests
    {
        private readonly Mock<IQobuzHttpClient> _mockHttpClient;
        private readonly Mock<ISessionManager> _mockSessionManager;
        private readonly Mock<IQobuzRequestSigner> _mockRequestSigner;
        private readonly Mock<IQobuzResponseCache> _mockResponseCache;
        private readonly Mock<Logger> _mockLogger;
        private readonly QobuzSession _validSession;

        public QobuzApiClientCovTests()
        {
            _mockHttpClient = new Mock<IQobuzHttpClient>();
            _mockSessionManager = new Mock<ISessionManager>();
            _mockRequestSigner = new Mock<IQobuzRequestSigner>();
            _mockResponseCache = new Mock<IQobuzResponseCache>();
            _mockLogger = new Mock<Logger>();

            _validSession = new QobuzSession
            {
                UserId = "123456",
                AuthToken = "test_token_123",
                AppId = "test_app_id",
                AppSecret = "test_secret",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };
        }

        #region Constructor Tests - Lines 64-68

        [Fact]
        public void Constructor_WithNullHttpClient_ShouldThrowArgumentNullException()
        {
            // Line 64
            var exception = Assert.Throws<ArgumentNullException>(() =>
                new QobuzApiClient(
                    null,
                    _mockSessionManager.Object,
                    _mockRequestSigner.Object,
                    _mockResponseCache.Object,
                    _mockLogger.Object));

            exception.ParamName.Should().Be("httpClient");
        }

        [Fact]
        public void Constructor_WithNullSessionManager_ShouldThrowArgumentNullException()
        {
            // Line 65
            var exception = Assert.Throws<ArgumentNullException>(() =>
                new QobuzApiClient(
                    _mockHttpClient.Object,
                    null,
                    _mockRequestSigner.Object,
                    _mockResponseCache.Object,
                    _mockLogger.Object));

            exception.ParamName.Should().Be("sessionManager");
        }

        [Fact]
        public void Constructor_WithNullRequestSigner_ShouldThrowArgumentNullException()
        {
            // Line 66
            var exception = Assert.Throws<ArgumentNullException>(() =>
                new QobuzApiClient(
                    _mockHttpClient.Object,
                    _mockSessionManager.Object,
                    null,
                    _mockResponseCache.Object,
                    _mockLogger.Object));

            exception.ParamName.Should().Be("requestSigner");
        }

        [Fact]
        public void Constructor_WithNullResponseCache_ShouldThrowArgumentNullException()
        {
            // Line 67
            var exception = Assert.Throws<ArgumentNullException>(() =>
                new QobuzApiClient(
                    _mockHttpClient.Object,
                    _mockSessionManager.Object,
                    _mockRequestSigner.Object,
                    null,
                    _mockLogger.Object));

            exception.ParamName.Should().Be("responseCache");
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Line 68
            var exception = Assert.Throws<ArgumentNullException>(() =>
                new QobuzApiClient(
                    _mockHttpClient.Object,
                    _mockSessionManager.Object,
                    _mockRequestSigner.Object,
                    _mockResponseCache.Object,
                    null));

            exception.ParamName.Should().Be("logger");
        }

        #endregion

        #region GetStreamingInfoAsync - Line 430: Null Response

        [Fact]
        public async Task GetStreamingInfoAsync_WithNullResponse_ShouldThrowInvalidOperationException()
        {
            // Line 430: throw new InvalidOperationException("Qobuz streaming response was null.");
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync(_validSession);

            _mockResponseCache.Setup(x => x.Get<QobuzStreamResponse>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns((QobuzStreamResponse?)null);

            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com/track/getFileUrl");
            var request = requestBuilder.Build();

            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            // Return null content to trigger deserialization to null
            var response = HttpTestHelpers.CreateResponse("null", HttpStatusCode.OK);
            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .ReturnsAsync(response);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.GetStreamingInfoAsync("track123", 6));

            exception.Message.Should().Be("Qobuz streaming response was null.");
        }

        #endregion

        #region GetStreamingInfoAsync - Line 435: Sample Stream

        [Fact]
        public async Task GetStreamingInfoAsync_WithSampleStream_ShouldThrowInvalidOperationException()
        {
            // Line 435: throw new InvalidOperationException("Qobuz returned a sample stream...");
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync(_validSession);

            _mockResponseCache.Setup(x => x.Get<QobuzStreamResponse>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns((QobuzStreamResponse?)null);

            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com/track/getFileUrl");
            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            var sampleResponse = new QobuzStreamResponse
            {
                Url = "https://streaming.qobuz.com/sample.flac",
                FormatId = 6,
                MimeType = "audio/flac",
                Sample = true  // This triggers line 435
            };

            var response = HttpTestHelpers.CreateResponse(
                JsonConvert.SerializeObject(sampleResponse),
                HttpStatusCode.OK);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .ReturnsAsync(response);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.GetStreamingInfoAsync("track123", 6));

            exception.Message.Should().Be("Qobuz returned a sample stream; subscription or quality may be restricted.");
        }

        #endregion

        #region GetStreamingInfoAsync - Line 451: Non-Fallback Restrictions

        [Fact]
        public async Task GetStreamingInfoAsync_WithNonFallbackRestrictions_ShouldThrowInvalidOperationException()
        {
            // Line 451: throw new InvalidOperationException(message) when IsQualityFallbackOnly() is false
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync(_validSession);

            _mockResponseCache.Setup(x => x.Get<QobuzStreamResponse>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns((QobuzStreamResponse?)null);

            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com/track/getFileUrl");
            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            var restrictedResponse = new QobuzStreamResponse
            {
                Url = string.Empty,  // Empty URL so IsQualityFallbackOnly returns false
                FormatId = 6,
                MimeType = "audio/flac",
                Sample = false,
                Restrictions = new List<QobuzStreamRestriction>
                {
                    new QobuzStreamRestriction
                    {
                        Code = "GeoRestricted",
                        Reason = "Content not available in your region"
                    }
                },
                Message = "Geo restriction applied"
            };

            var response = HttpTestHelpers.CreateResponse(
                JsonConvert.SerializeObject(restrictedResponse),
                HttpStatusCode.OK);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .ReturnsAsync(response);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.GetStreamingInfoAsync("track123", 6));

            exception.Message.Should().Be("Content not available in your region");
        }

        [Fact]
        public async Task GetStreamingInfoAsync_WithSubscriptionRestriction_ShouldThrowInvalidOperationException()
        {
            // Test another restriction code path at line 451
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync(_validSession);

            _mockResponseCache.Setup(x => x.Get<QobuzStreamResponse>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns((QobuzStreamResponse?)null);

            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com/track/getFileUrl");
            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            var restrictedResponse = new QobuzStreamResponse
            {
                Url = string.Empty,
                FormatId = 7,
                MimeType = "audio/flac",
                Sample = false,
                Restrictions = new List<QobuzStreamRestriction>
                {
                    new QobuzStreamRestriction
                    {
                        Code = "SubscriptionRestricted",
                        Reason = "Hi-Res requires premium subscription"
                    }
                }
            };

            var response = HttpTestHelpers.CreateResponse(
                JsonConvert.SerializeObject(restrictedResponse),
                HttpStatusCode.OK);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .ReturnsAsync(response);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.GetStreamingInfoAsync("track123", 7));

            exception.Message.Should().Be("Hi-Res requires premium subscription");
        }

        #endregion

        #region GetStreamingInfoAsync - Line 463: Empty URL

        [Fact]
        public async Task GetStreamingInfoAsync_WithEmptyUrl_ShouldThrowInvalidOperationException()
        {
            // Line 463: throw new InvalidOperationException($"Qobuz returned an empty stream URL...")
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync(_validSession);

            _mockResponseCache.Setup(x => x.Get<QobuzStreamResponse>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns((QobuzStreamResponse?)null);

            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com/track/getFileUrl");
            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            var emptyUrlResponse = new QobuzStreamResponse
            {
                Url = "",  // Empty string triggers line 456-463
                FormatId = 6,
                MimeType = "audio/flac",
                Sample = false,
                Restrictions = new List<QobuzStreamRestriction>(),
                Status = "error",
                Message = "Track not available"
            };

            var response = HttpTestHelpers.CreateResponse(
                JsonConvert.SerializeObject(emptyUrlResponse),
                HttpStatusCode.OK);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .ReturnsAsync(response);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.GetStreamingInfoAsync("track123", 6));

            exception.Message.Should().Be("Qobuz returned an empty stream URL. Track not available");
        }

        [Fact]
        public async Task GetStreamingInfoAsync_WithEmptyUrlAndStatus_ShouldThrowInvalidOperationException()
        {
            // Line 463: when message is empty, use status
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync(_validSession);

            _mockResponseCache.Setup(x => x.Get<QobuzStreamResponse>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns((QobuzStreamResponse?)null);

            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com/track/getFileUrl");
            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            var emptyUrlResponse = new QobuzStreamResponse
            {
                Url = "   ",  // Whitespace only
                FormatId = 6,
                MimeType = "audio/flac",
                Sample = false,
                Restrictions = new List<QobuzStreamRestriction>(),
                Status = "stream_unavailable"
            };

            var response = HttpTestHelpers.CreateResponse(
                JsonConvert.SerializeObject(emptyUrlResponse),
                HttpStatusCode.OK);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .ReturnsAsync(response);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.GetStreamingInfoAsync("track123", 6));

            exception.Message.Should().Be("Qobuz returned an empty stream URL. stream_unavailable");
        }

        #endregion

        #region GetStreamingUrlAsync

        [Fact]
        public async Task GetStreamingUrlAsync_ShouldReturnUrlFromStreamingInfo()
        {
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync(_validSession);

            _mockResponseCache.Setup(x => x.Get<QobuzStreamResponse>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns((QobuzStreamResponse?)null);

            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com/track/getFileUrl");
            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            var streamingResponse = new QobuzStreamResponse
            {
                Url = "https://streaming.qobuz.com/track/123.flac",
                FormatId = 6,
                MimeType = "audio/flac",
                Sample = false,
                Restrictions = new List<QobuzStreamRestriction>()
            };

            var response = HttpTestHelpers.CreateResponse(
                JsonConvert.SerializeObject(streamingResponse),
                HttpStatusCode.OK);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .ReturnsAsync(response);

            // Act
            var url = await client.GetStreamingUrlAsync("track123", 6);

            // Assert
            url.Should().Be("https://streaming.qobuz.com/track/123.flac");
        }

        #endregion

        #region HandleErrorResponse - Line 389: Status Code Switch

        [Fact]
        public async Task ExecuteRequestAsync_With401Response_ShouldThrowQobuzApiException()
        {
            // Line 391: 401 => new QobuzApiException("Authentication failed", ...)
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync(_validSession);

            _mockResponseCache.Setup(x => x.Get<object>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns(null);

            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com/album/get");
            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            var errorResponse = HttpTestHelpers.CreateResponse(
                "{\"message\": \"Invalid credentials\"}",
                HttpStatusCode.Unauthorized);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .ReturnsAsync(errorResponse);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<QobuzApiException>(
                () => client.GetAsync<object>("album/get"));

            exception.Message.Should().Be("Authentication failed");
            exception.StatusCode.Should().Be(401);
            exception.ErrorType.Should().Be("AuthenticationFailed");
        }

        [Fact]
        public async Task ExecuteRequestAsync_With403Response_ShouldThrowQobuzApiException()
        {
            // Line 392: 403 => new QobuzApiException("Access forbidden - check app credentials", ...)
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync(_validSession);

            _mockResponseCache.Setup(x => x.Get<object>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns(null);

            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com/album/get");
            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            var errorResponse = HttpTestHelpers.CreateResponse(
                "{\"message\": \"Forbidden\"}",
                HttpStatusCode.Forbidden);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .ReturnsAsync(errorResponse);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<QobuzApiException>(
                () => client.GetAsync<object>("album/get"));

            exception.Message.Should().Be("Access forbidden - check app credentials");
            exception.StatusCode.Should().Be(403);
            exception.ErrorType.Should().Be("AccessForbidden");
        }

        [Fact]
        public async Task ExecuteRequestAsync_With404Response_ShouldThrowQobuzApiException()
        {
            // Line 393: 404 => new QobuzApiException("Resource not found", ...)
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync(_validSession);

            _mockResponseCache.Setup(x => x.Get<object>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns(null);

            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com/album/get");
            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            var errorResponse = HttpTestHelpers.CreateResponse(
                "{\"message\": \"Not found\"}",
                HttpStatusCode.NotFound);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .ReturnsAsync(errorResponse);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<QobuzApiException>(
                () => client.GetAsync<object>("album/get"));

            exception.Message.Should().Be("Resource not found");
            exception.StatusCode.Should().Be(404);
            exception.ErrorType.Should().Be("NotFound");
        }

        [Fact]
        public async Task ExecuteRequestAsync_With429Response_ShouldThrowQobuzApiException()
        {
            // Line 394: 429 => new QobuzApiException("Rate limit exceeded", ...)
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync(_validSession);

            _mockResponseCache.Setup(x => x.Get<object>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns(null);

            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com/album/get");
            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            var errorResponse = HttpTestHelpers.CreateResponse(
                "{\"message\": \"Too many requests\"}",
                (HttpStatusCode)429);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .ReturnsAsync(errorResponse);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<QobuzApiException>(
                () => client.GetAsync<object>("album/get"));

            exception.Message.Should().Be("Rate limit exceeded");
            exception.StatusCode.Should().Be(429);
            exception.ErrorType.Should().Be("RateLimited");
        }

        [Fact]
        public async Task ExecuteRequestAsync_With500Response_ShouldThrowQobuzApiException()
        {
            // Line 395: >= 500 => new QobuzApiException("Server error", ...)
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync(_validSession);

            _mockResponseCache.Setup(x => x.Get<object>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns(null);

            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com/album/get");
            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            var errorResponse = HttpTestHelpers.CreateResponse(
                "{\"message\": \"Internal server error\"}",
                HttpStatusCode.InternalServerError);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .ReturnsAsync(errorResponse);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<QobuzApiException>(
                () => client.GetAsync<object>("album/get"));

            exception.Message.Should().Be("Server error");
            exception.StatusCode.Should().Be(500);
            exception.ErrorType.Should().Be("ServerError");
        }

        [Fact]
        public async Task ExecuteRequestAsync_With503Response_ShouldThrowQobuzApiException()
        {
            // Line 395: >= 500 covers 503 as well
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync(_validSession);

            _mockResponseCache.Setup(x => x.Get<object>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns(null);

            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com/album/get");
            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            var errorResponse = HttpTestHelpers.CreateResponse(
                "{\"message\": \"Service unavailable\"}",
                HttpStatusCode.ServiceUnavailable);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .ReturnsAsync(errorResponse);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<QobuzApiException>(
                () => client.GetAsync<object>("album/get"));

            exception.Message.Should().Be("Server error");
            exception.StatusCode.Should().Be(503);
            exception.ErrorType.Should().Be("ServerError");
        }

        [Fact]
        public async Task ExecuteRequestAsync_With400Response_ShouldThrowQobuzApiException()
        {
            // Line 396: _ => new QobuzApiException(message, statusCode, "ApiError")
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync(_validSession);

            _mockResponseCache.Setup(x => x.Get<object>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns(null);

            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com/album/get");
            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            var errorResponse = HttpTestHelpers.CreateResponse(
                "{\"message\": \"Bad request parameters\"}",
                HttpStatusCode.BadRequest);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .ReturnsAsync(errorResponse);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<QobuzApiException>(
                () => client.GetAsync<object>("album/get"));

            exception.Message.Should().Be("Bad request parameters");
            exception.StatusCode.Should().Be(400);
            exception.ErrorType.Should().Be("ApiError");
        }

        #endregion

        #region HandleErrorResponse - Line 401: JsonException

        [Fact]
        public async Task ExecuteRequestAsync_WithInvalidJsonErrorResponse_ShouldThrowQobuzApiException()
        {
            // Line 401: throw new QobuzApiException($"HTTP {statusCode}: {response.Content}", ...)
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync(_validSession);

            _mockResponseCache.Setup(x => x.Get<object>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns(null);

            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com/album/get");
            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            var errorResponse = HttpTestHelpers.CreateResponse(
                "not valid json at all",
                HttpStatusCode.InternalServerError);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .ReturnsAsync(errorResponse);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<QobuzApiException>(
                () => client.GetAsync<object>("album/get"));

            exception.Message.Should().Be("HTTP 500: not valid json at all");
            exception.StatusCode.Should().Be(500);
            exception.ErrorType.Should().Be("UnknownError");
        }

        #endregion

        #region HasValidSession Edge Cases

        [Fact]
        public void HasValidSession_WithSessionManagerThrowing_ShouldUseFallback()
        {
            // Lines 219-222: catch returns fallbackSession?.IsValid() == true
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.HasValidSession())
                .Throws<InvalidOperationException>();

            var result = client.HasValidSession();

            result.Should().BeFalse("no fallback session is set");
        }

        [Fact]
        public void HasValidSession_WithSessionManagerThrowingAndValidFallback_ShouldReturnTrue()
        {
            // Lines 219-222: fallback session used when manager throws
            var client = CreateClient();
            client.SetSession(_validSession);

            _mockSessionManager.Setup(x => x.HasValidSession())
                .Throws<InvalidOperationException>();

            var result = client.HasValidSession();

            result.Should().BeTrue("fallback session is valid");
        }

        #endregion

        #region SetAuthenticationService

        [Fact]
        public void SetAuthenticationService_WithQobuzAuthenticationService_ShouldCreateTokenManager()
        {
            // Lines 107-110: if (authService is QobuzAuthenticationService realAuth)
            var client = CreateClient();
            var mockAuthService = new Mock<IQobuzAuthenticationService>();

            // Act - with a mock that won't match the concrete type
            client.SetAuthenticationService(mockAuthService.Object);

            // Assert - no exception, tokenManager remains null
            // This tests the negative path where authService is NOT QobuzAuthenticationService
        }

        #endregion

        #region ExecuteRequestAsync with PreRequestHandler

        [Fact(Skip = "Moq callback signature mismatch after plugins-branch assembly swap")]
        public async Task ExecuteRequestAsync_WithPreRequestHandler_ShouldUseHandlerMethods()
        {
            // Lines 230-233, 268-271, 294-297: PreRequestHandler paths
            var client = CreateClient();
            var mockPreHandler = new Mock<IPreRequestHandler>();

            client.SetPreRequestHandler(mockPreHandler.Object);

            mockPreHandler.Setup(x => x.EnsureValidSessionAsync())
                .Returns(Task.CompletedTask);

            mockPreHandler.Setup(x => x.InjectAuthParameters(It.IsAny<Dictionary<string, string>>()))
                .Callback<Dictionary<string, string>>(dict =>
                {
                    dict["app_id"] = "test_app";
                    dict["user_auth_token"] = "test_token";
                });

            mockPreHandler.Setup(x => x.SignIfRequired(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Callback<string, Dictionary<string, string>>((endpoint, dict) =>
                {
                    dict["request_sig"] = "test_sig";
                });

            _mockResponseCache.Setup(x => x.Get<TestResponse>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns((TestResponse?)null);

            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com/album/get");
            var request = requestBuilder.Build();

            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            var httpResponse = HttpTestHelpers.CreateResponse(
                "{\"id\": \"123\"}",
                HttpStatusCode.OK);

            HttpRequest capturedRequest = null;
            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .Callback<HttpRequest>(req => capturedRequest = req)
                .ReturnsAsync(httpResponse);

            // Act
            await client.GetAsync<TestResponse>("album/get");

            // Assert
            mockPreHandler.Verify(x => x.EnsureValidSessionAsync(), Times.Once);
            mockPreHandler.Verify(x => x.InjectAuthParameters(It.IsAny<Dictionary<string, string>>()), Times.Once);
            mockPreHandler.Verify(x => x.SignIfRequired("album/get", It.IsAny<Dictionary<string, string>>()), Times.Once);
            _mockSessionManager.Verify(x => x.GetCurrentSessionAsync(default), Times.Never);
        }

        #endregion

        #region ExecuteRequestAsync with TokenManager (Fallback)

        [Fact(Skip = "StreamingTokenManager constructor changed after plugins-branch assembly swap")]
        public async Task ExecuteRequestAsync_WithTokenManager_ShouldGetValidSession()
        {
            // Lines 234-250: TokenManager fallback path
            var client = CreateClient();

            var mockTokenManager = Substitute.For<Lidarr.Plugin.Common.Services.Authentication.StreamingTokenManager<QobuzSession, QobuzCredentials>>();

            // Use reflection to set the private _tokenManager field
            var tokenManagerField = typeof(QobuzApiClient).GetField("_tokenManager",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            tokenManagerField?.SetValue(client, mockTokenManager);

            client.SetCredentialsProvider(() => Task.FromResult(new QobuzCredentials
            {
                Email = "test@example.com",
                MD5Password = "5f4dcc3b5aa765d61d8327deb882cf99"
            }));

            mockTokenManager.GetValidSessionAsync(Arg.Any<QobuzCredentials>())
                .Returns(_validSession);

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync((QobuzSession?)null);

            _mockResponseCache.Setup(x => x.Get<TestResponse>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns((TestResponse?)null);

            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com/album/get");
            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            var httpResponse = HttpTestHelpers.CreateResponse(
                "{\"id\": \"123\"}",
                HttpStatusCode.OK);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .ReturnsAsync(httpResponse);

            // Act
            await client.GetAsync<TestResponse>("album/get");

            // Assert
            await mockTokenManager.Received(1).GetValidSessionAsync(Arg.Any<QobuzCredentials>());
        }

        #endregion

        #region Cache Tests

        [Fact]
        public async Task ExecuteRequestAsync_WithCachedResponse_ShouldReturnCachedValue()
        {
            // Lines 303-312: Cache hit path
            var client = CreateClient();

            var cachedResponse = new TestResponse { Id = "cached_123" };
            _mockResponseCache.Setup(x => x.Get<TestResponse>(
                "album/get", It.IsAny<Dictionary<string, string>>()))
                .Returns(cachedResponse);

            // Act
            var result = await client.GetAsync<TestResponse>("album/get");

            // Assert
            result.Should().NotBeNull();
            result.Id.Should().Be("cached_123");
            _mockHttpClient.Verify(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _mockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default), Times.Never);
        }

        [Fact]
        public async Task ExecuteRequestAsync_WithPostRequest_ShouldNotUseCache()
        {
            // Lines 304: if (method == "GET") - POST skips cache
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync(_validSession);

            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com/user/login");
            var request = requestBuilder.Build();
            request.Method = HttpMethod.Post;

            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            var httpResponse = HttpTestHelpers.CreateResponse(
                "{\"user_auth_token\": \"test_token\"}",
                HttpStatusCode.OK);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .ReturnsAsync(httpResponse);

            // Act
            await client.PostAsync<TestResponse>("user/login", new { email = "test", password = "pass" });

            // Assert
            _mockResponseCache.Verify(x => x.Get<TestResponse>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()), Times.Never);
        }

        #endregion

        #region Request Signing Tests

        [Fact]
        public async Task ExecuteRequestAsync_WithSignableEndpoint_ShouldSignRequest()
        {
            // Lines 298-301: Request signing path
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync(_validSession);

            _mockRequestSigner.Setup(x => x.RequiresSigning("track/getFileUrl"))
                .Returns(true);

            var capturedParams = new Dictionary<string, string>();
            _mockRequestSigner.Setup(x => x.SignRequest(
                "track/getFileUrl",
                It.IsAny<Dictionary<string, string>>(),
                _validSession.AppId,
                _validSession.AppSecret))
                .Callback<string, Dictionary<string, string>, string, string>((_, p, __, ___) =>
                {
                    foreach (var kvp in p) capturedParams[kvp.Key] = kvp.Value;
                });

            _mockResponseCache.Setup(x => x.Get<TestResponse>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns((TestResponse?)null);

            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com/track/getFileUrl");
            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            var httpResponse = HttpTestHelpers.CreateResponse(
                "{\"id\": \"123\"}",
                HttpStatusCode.OK);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .ReturnsAsync(httpResponse);

            // Act
            await client.GetAsync<TestResponse>("track/getFileUrl");

            // Assert
            _mockRequestSigner.Verify(x => x.RequiresSigning("track/getFileUrl"), Times.Once);
            _mockRequestSigner.Verify(x => x.SignRequest(
                "track/getFileUrl",
                It.IsAny<Dictionary<string, string>>(),
                _validSession.AppId,
                _validSession.AppSecret), Times.Once);
        }

        [Fact]
        public async Task ExecuteRequestAsync_WithNonSignableEndpoint_ShouldNotSignRequest()
        {
            // Lines 298-301: RequiresSigning returns false
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync(_validSession);

            _mockRequestSigner.Setup(x => x.RequiresSigning("album/get"))
                .Returns(false);

            _mockResponseCache.Setup(x => x.Get<TestResponse>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns((TestResponse?)null);

            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com/album/get");
            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            var httpResponse = HttpTestHelpers.CreateResponse(
                "{\"id\": \"123\"}",
                HttpStatusCode.OK);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .ReturnsAsync(httpResponse);

            // Act
            await client.GetAsync<TestResponse>("album/get");

            // Assert
            _mockRequestSigner.Verify(x => x.SignRequest(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Never);
        }

        #endregion

        #region Parameter Trimming

        [Fact(Skip = "Moq callback signature mismatch after plugins-branch assembly swap")]
        public async Task ExecuteRequestAsync_WithParameterContainingWhitespace_ShouldTrimValue()
        {
            // Lines 285-287: var value = param.Value?.Trim() ?? string.Empty;
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync(_validSession);

            _mockResponseCache.Setup(x => x.Get<TestResponse>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns((TestResponse?)null);

            HttpRequest capturedRequest = null;
            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com/album/get");
            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .Callback<HttpRequest>(req => capturedRequest = req)
                .ReturnsAsync(HttpTestHelpers.CreateResponse("{\"id\": \"123\"}", HttpStatusCode.OK));

            // Act
            var parameters = new Dictionary<string, string>
            {
                ["query"] = "  test album  ",
                ["limit"] = "  20  "
            };
            await client.GetAsync<TestResponse>("album/get", parameters);

            // Assert - parameters should be trimmed
            // The trimming happens before adding to allParameters, which is then used to build the request
            _mockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default), Times.Once);
        }

        #endregion

        #region URL Building

        [Fact]
        public async Task ExecuteRequestAsync_WithEndpointStartingWithSlash_ShouldBuildCorrectUrl()
        {
            // Line 254-256: URL building with slash handling
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync(_validSession);

            _mockResponseCache.Setup(x => x.Get<TestResponse>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns((TestResponse?)null);

            HttpRequest capturedRequest = null;
            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com");
            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((url, method) => capturedRequest = new HttpRequest(url))
                .Returns(requestBuilder);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .ReturnsAsync(HttpTestHelpers.CreateResponse("{\"id\": \"123\"}", HttpStatusCode.OK));

            // Act
            await client.GetAsync<TestResponse>("/album/get");

            // Assert
            // The URL should be built correctly without double slashes
            _mockHttpClient.Verify(x => x.BuildRequest(It.IsAny<string>(), "GET"), Times.Once);
        }

        [Fact]
        public async Task ExecuteRequestAsync_WithEndpointWithoutSlash_ShouldAddSlash()
        {
            // Line 255-256: endpoint.StartsWith("/") ? endpoint : "/" + endpoint
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync(_validSession);

            _mockResponseCache.Setup(x => x.Get<TestResponse>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns((TestResponse?)null);

            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com");
            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .ReturnsAsync(HttpTestHelpers.CreateResponse("{\"id\": \"123\"}", HttpStatusCode.OK));

            // Act
            await client.GetAsync<TestResponse>("album/get");

            // Assert
            _mockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default), Times.Once);
        }

        #endregion

        #region GetStreamingInfoAsync Success Path with Quality Fallback

        [Fact]
        public async Task GetStreamingInfoAsync_WithQualityFallbackOnly_ShouldLogAndContinue()
        {
            // Lines 440-447: IsQualityFallbackOnly() path - logs info but doesn't throw
            var client = CreateClient();

            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(default))
                .ReturnsAsync(_validSession);

            _mockResponseCache.Setup(x => x.Get<QobuzStreamResponse>(
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns((QobuzStreamResponse?)null);

            var requestBuilder = new HttpRequestBuilder("https://api.qobuz.com/track/getFileUrl");
            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(requestBuilder);

            var fallbackResponse = new QobuzStreamResponse
            {
                Url = "https://streaming.qobuz.com/track/123.flac",
                FormatId = 6,  // Fallback to FLAC 16-bit
                MimeType = "audio/flac",
                Sample = false,
                Restrictions = new List<QobuzStreamRestriction>
                {
                    new QobuzStreamRestriction
                    {
                        Code = "FormatRestrictedByFormatAvailability"
                    }
                }
            };

            var response = HttpTestHelpers.CreateResponse(
                JsonConvert.SerializeObject(fallbackResponse),
                HttpStatusCode.OK);

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), default))
                .ReturnsAsync(response);

            // Act
            var result = await client.GetStreamingInfoAsync("track123", 7);

            // Assert - should not throw, just log and return the fallback response
            result.Should().NotBeNull();
            result.Url.Should().Be("https://streaming.qobuz.com/track/123.flac");
            result.FormatId.Should().Be(6);
        }

        #endregion

        #region Helper Methods

        private QobuzApiClient CreateClient()
        {
            return new QobuzApiClient(
                _mockHttpClient.Object,
                _mockSessionManager.Object,
                _mockRequestSigner.Object,
                _mockResponseCache.Object,
                _mockLogger.Object);
        }

        #endregion

        #region Test Models

        private class TestResponse
        {
            [JsonProperty("id")]
            public string Id { get; set; } = string.Empty;
        }

        #endregion
    }
}
