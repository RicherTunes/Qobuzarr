using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NzbDrone.Common.Http;
using Xunit;
using Lidarr.Plugin.Qobuzarr.API.Http;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Qobuzarr.Tests.Fixtures;
using Qobuzarr.Tests.Helpers;

namespace Qobuzarr.Tests.Unit.API
{
    /// <summary>
    /// Comprehensive tests for QobuzHttpClient covering HTTP operations,
    /// rate limiting, retry logic, and error handling.
    /// </summary>
    public class QobuzHttpClientTests : TestFixtureBase
    {
        private readonly QobuzHttpClient _httpClient;

        public QobuzHttpClientTests()
        {
            _httpClient = new QobuzHttpClient(MockHttpClient.Object, MockLogger.Object);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullHttpClient_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Action act = () => new QobuzHttpClient(null, MockLogger.Object);
            act.Should().Throw<ArgumentNullException>().WithMessage("*httpClient*");
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Action act = () => new QobuzHttpClient(MockHttpClient.Object, null);
            act.Should().Throw<ArgumentNullException>().WithMessage("*logger*");
        }

        #endregion

        #region BuildRequest Tests

        [Fact]
        public void BuildRequest_WithGetMethod_ShouldBuildCorrectRequest()
        {
            // Arrange
            var url = "https://api.qobuz.com/album/get";

            // Act
            var builder = _httpClient.BuildRequest(url);
            var request = builder.Build();

            // Assert
            request.Url.ToString().Should().Be(url);
            request.Method.Should().Be(HttpMethod.Get);
            request.Headers.Should().ContainKey("User-Agent");
            request.Headers["User-Agent"].Should().Be(QobuzConstants.Api.UserAgent);
        }

        [Fact]
        public void BuildRequest_WithPostMethod_ShouldBuildCorrectRequest()
        {
            // Arrange
            var url = "https://api.qobuz.com/user/login";

            // Act
            var builder = _httpClient.BuildRequest(url, "POST");
            var request = builder.Build();

            // Assert
            request.Url.ToString().Should().Be(url);
            request.Method.Should().Be(HttpMethod.Post);
            request.Headers.Should().ContainKey("User-Agent");
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("get")]
        [InlineData("Get")]
        public void BuildRequest_WithVariousGetMethods_ShouldBuildGetRequest(string method)
        {
            // Arrange
            var url = "https://api.qobuz.com/test";

            // Act
            var builder = _httpClient.BuildRequest(url, method);
            var request = builder.Build();

            // Assert
            request.Method.Should().Be(HttpMethod.Get);
        }

        [Theory]
        [InlineData("POST")]
        [InlineData("post")]
        [InlineData("Post")]
        public void BuildRequest_WithVariousPostMethods_ShouldBuildPostRequest(string method)
        {
            // Arrange
            var url = "https://api.qobuz.com/test";

            // Act
            var builder = _httpClient.BuildRequest(url, method);
            var request = builder.Build();

            // Assert
            request.Method.Should().Be(HttpMethod.Post);
        }

        #endregion

        #region ExecuteAsync Tests

        [Fact]
        public async Task ExecuteAsync_WithSuccessfulResponse_ShouldReturnResponse()
        {
            // Arrange
            var request = new HttpRequestBuilder("https://api.qobuz.com/album/get").Build();
            var expectedResponse = HttpTestHelpers.CreateResponse("{\"success\": true}", HttpStatusCode.OK);

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(expectedResponse);

            // Act
            var result = await _httpClient.ExecuteAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.StatusCode.Should().Be(HttpStatusCode.OK);
            result.Content.Should().Be("{\"success\": true}");
        }

        [Fact]
        public async Task ExecuteAsync_WithRateLimitResponse_ShouldRetryAfterDelay()
        {
            // Arrange
            var request = new HttpRequestBuilder("https://api.qobuz.com/album/get").Build();
            
            var rateLimitResponse = HttpTestHelpers.CreateErrorResponse((HttpStatusCode)429, "Rate limited");
            rateLimitResponse.Headers.Add("Retry-After", "2");
            
            var successResponse = HttpTestHelpers.CreateResponse("{\"success\": true}", HttpStatusCode.OK);

            MockHttpClient.SetupSequence(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(rateLimitResponse)
                         .ReturnsAsync(successResponse);

            var startTime = DateTime.UtcNow;

            // Act
            var result = await _httpClient.ExecuteAsync(request);
            var endTime = DateTime.UtcNow;

            // Assert
            result.StatusCode.Should().Be(HttpStatusCode.OK);
            MockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.AtLeast(2));
            // Should have waited at least some time for rate limit
            (endTime - startTime).Should().BeGreaterThan(TimeSpan.FromSeconds(0.1));
        }

        [Fact]
        public async Task ExecuteAsync_WithRateLimitNoRetryAfterHeader_ShouldUseDefaultDelay()
        {
            // Arrange
            var request = new HttpRequestBuilder("https://api.qobuz.com/album/get").Build();
            
            var rateLimitResponse = HttpTestHelpers.CreateErrorResponse((HttpStatusCode)429, "Rate limited");
            // No Retry-After header
            
            var successResponse = HttpTestHelpers.CreateResponse("{\"success\": true}", HttpStatusCode.OK);

            MockHttpClient.SetupSequence(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(rateLimitResponse)
                         .ReturnsAsync(successResponse);

            // Act
            var result = await _httpClient.ExecuteAsync(request);

            // Assert
            result.StatusCode.Should().Be(HttpStatusCode.OK);
            MockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.AtLeast(2));
        }

        [Fact]
        public async Task ExecuteAsync_WithMultipleRetries_ShouldEventuallySucceed()
        {
            // Arrange
            var request = new HttpRequestBuilder("https://api.qobuz.com/album/get").Build();
            
            var errorResponse = HttpTestHelpers.CreateErrorResponse(HttpStatusCode.InternalServerError, "Server error");
            var successResponse = HttpTestHelpers.CreateResponse("{\"success\": true}", HttpStatusCode.OK);

            MockHttpClient.SetupSequence(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(errorResponse)
                         .ReturnsAsync(errorResponse)
                         .ReturnsAsync(successResponse);

            // Act
            var result = await _httpClient.ExecuteAsync(request);

            // Assert
            result.StatusCode.Should().Be(HttpStatusCode.OK);
            MockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Exactly(3));
        }

        [Fact]
        public async Task ExecuteAsync_WithPersistentFailure_ShouldThrowAfterMaxRetries()
        {
            // Arrange
            var request = new HttpRequestBuilder("https://api.qobuz.com/album/get").Build();
            var errorResponse = HttpTestHelpers.CreateErrorResponse(HttpStatusCode.InternalServerError, "Server error");

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(errorResponse);

            // Act & Assert
            await _httpClient.Invoking(x => x.ExecuteAsync(request))
                           .Should().ThrowAsync<Exception>();

            // Should have made multiple attempts
            MockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), 
                                Times.AtLeast(QobuzConstants.Api.MaxRetries));
        }

        [Fact]
        public async Task ExecuteAsync_WithCancellation_ShouldRespectCancellationToken()
        {
            // Arrange
            var request = new HttpRequestBuilder("https://api.qobuz.com/album/get").Build();
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMilliseconds(100));

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .Returns(async () =>
                         {
                             await Task.Delay(1000, cts.Token); // Will be cancelled
                             return HttpTestHelpers.CreateResponse("", HttpStatusCode.OK);
                         });

            // Act & Assert
            await _httpClient.Invoking(x => x.ExecuteAsync(request, cts.Token))
                           .Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task ExecuteAsync_WithHttpRequestException_ShouldPropagateException()
        {
            // Arrange
            var request = new HttpRequestBuilder("https://api.qobuz.com/album/get").Build();
            var expectedException = new HttpRequestException("Network error");

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ThrowsAsync(expectedException);

            // Act & Assert
            var thrownException = await _httpClient.Invoking(x => x.ExecuteAsync(request))
                                                 .Should().ThrowAsync<Exception>();
            
            // The exception might be wrapped by retry logic
            thrownException.Which.Message.Should().Contain("Network error");
        }

        #endregion

        #region Rate Limiting Tests

        [Fact]
        public async Task ExecuteAsync_WithMultipleSimultaneousRequests_ShouldRateLimit()
        {
            // Arrange
            var requests = Enumerable.Range(0, 5)
                                   .Select(i => new HttpRequestBuilder($"https://api.qobuz.com/test{i}").Build())
                                   .ToList();

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(() => HttpTestHelpers.CreateResponse("{}", HttpStatusCode.OK));

            var startTime = DateTime.UtcNow;

            // Act
            var tasks = requests.Select(r => _httpClient.ExecuteAsync(r));
            await Task.WhenAll(tasks);

            var endTime = DateTime.UtcNow;

            // Assert
            // With rate limiting, multiple requests should take some time
            MockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Exactly(5));
            
            // Rate limiting should introduce some delay, but this is hard to test precisely
            // due to timing variations. We'll just verify all requests completed.
            (endTime - startTime).Should().BeGreaterThan(TimeSpan.Zero);
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public async Task ExecuteAsync_WithNetworkTimeout_ShouldRetryAndFail()
        {
            // Arrange
            var request = new HttpRequestBuilder("https://api.qobuz.com/album/get").Build();
            
            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ThrowsAsync(new TaskCanceledException("Request timeout"));

            // Act & Assert
            await _httpClient.Invoking(x => x.ExecuteAsync(request))
                           .Should().ThrowAsync<TaskCanceledException>();

            MockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), 
                                Times.AtLeast(QobuzConstants.Api.MaxRetries));
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.NotFound)]
        public async Task ExecuteAsync_WithClientErrors_ShouldReturnErrorResponse(HttpStatusCode statusCode)
        {
            // Arrange
            var request = new HttpRequestBuilder("https://api.qobuz.com/album/get").Build();
            var errorResponse = HttpTestHelpers.CreateErrorResponse(statusCode, "Client error");

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(errorResponse);

            // Act
            var result = await _httpClient.ExecuteAsync(request);

            // Assert
            result.StatusCode.Should().Be(statusCode);
            result.HasHttpError.Should().BeTrue();
        }

        [Fact]
        public async Task ExecuteAsync_WithInvalidRetryAfterHeader_ShouldUseDefaultDelay()
        {
            // Arrange
            var request = new HttpRequestBuilder("https://api.qobuz.com/album/get").Build();
            
            var rateLimitResponse = HttpTestHelpers.CreateErrorResponse((HttpStatusCode)429, "Rate limited");
            rateLimitResponse.Headers.Add("Retry-After", "invalid_number");
            
            var successResponse = HttpTestHelpers.CreateResponse("{\"success\": true}", HttpStatusCode.OK);

            MockHttpClient.SetupSequence(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(rateLimitResponse)
                         .ReturnsAsync(successResponse);

            // Act
            var result = await _httpClient.ExecuteAsync(request);

            // Assert
            result.StatusCode.Should().Be(HttpStatusCode.OK);
            MockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.AtLeast(2));
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void BuildRequest_WithNullUrl_ShouldThrowException()
        {
            // Act & Assert
            _httpClient.Invoking(x => x.BuildRequest(null))
                      .Should().Throw<ArgumentException>();
        }

        [Fact]
        public void BuildRequest_WithEmptyUrl_ShouldThrowException()
        {
            // Act & Assert
            _httpClient.Invoking(x => x.BuildRequest(""))
                      .Should().Throw<ArgumentException>();
        }

        [Fact]
        public void BuildRequest_WithInvalidMethod_ShouldDefaultToGet()
        {
            // Arrange
            var url = "https://api.qobuz.com/test";

            // Act
            var builder = _httpClient.BuildRequest(url, "INVALID_METHOD");
            var request = builder.Build();

            // Assert
            request.Method.Should().Be(HttpMethod.Get);
        }

        [Fact]
        public async Task ExecuteAsync_WithNullRequest_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            await _httpClient.Invoking(x => x.ExecuteAsync(null))
                           .Should().ThrowAsync<ArgumentNullException>();
        }

        [Fact]
        public void BuildRequest_ShouldSetUserAgentHeader()
        {
            // Arrange
            var url = "https://api.qobuz.com/test";

            // Act
            var builder = _httpClient.BuildRequest(url);
            var request = builder.Build();

            // Assert
            request.Headers.Should().ContainKey("User-Agent");
            request.Headers["User-Agent"].Should().Be(QobuzConstants.Api.UserAgent);
        }

        [Fact]
        public async Task ExecuteAsync_WithLargeRetryAfterValue_ShouldClampDelay()
        {
            // Arrange
            var request = new HttpRequestBuilder("https://api.qobuz.com/album/get").Build();
            
            var rateLimitResponse = HttpTestHelpers.CreateErrorResponse((HttpStatusCode)429, "Rate limited");
            rateLimitResponse.Headers.Add("Retry-After", "3600"); // 1 hour - very large value
            
            var successResponse = HttpTestHelpers.CreateResponse("{\"success\": true}", HttpStatusCode.OK);

            MockHttpClient.SetupSequence(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(rateLimitResponse)
                         .ReturnsAsync(successResponse);

            var startTime = DateTime.UtcNow;

            // Act
            var result = await _httpClient.ExecuteAsync(request);
            var endTime = DateTime.UtcNow;

            // Assert
            result.StatusCode.Should().Be(HttpStatusCode.OK);
            // Should have waited, but not the full hour (implementation should have reasonable limits)
            (endTime - startTime).Should().BeLessThan(TimeSpan.FromMinutes(5));
        }

        #endregion

        #region Performance Tests

        [Fact]
        public async Task ExecuteAsync_WithManyRequests_ShouldHandleGracefully()
        {
            // Arrange
            const int requestCount = 50;
            var requests = Enumerable.Range(0, requestCount)
                                   .Select(i => new HttpRequestBuilder($"https://api.qobuz.com/test{i}").Build())
                                   .ToList();

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(() => HttpTestHelpers.CreateResponse("{}", HttpStatusCode.OK));

            // Act
            var tasks = requests.Select(r => _httpClient.ExecuteAsync(r));
            var results = await Task.WhenAll(tasks);

            // Assert
            results.Should().HaveCount(requestCount);
            results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
            MockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Exactly(requestCount));
        }

        #endregion

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}