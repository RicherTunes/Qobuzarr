using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NSubstitute;
using Xunit;
using Lidarr.Plugin.Qobuzarr.API;
// DISABLED: AdaptiveQobuzApiClient and IAdaptiveRateLimiter have been removed
// using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace Qobuzarr.Tests.Unit.API
{
    // DISABLED: AdaptiveQobuzApiClient has been removed - functionality consolidated into main API client
    /*
    public class AdaptiveQobuzApiClientTests : IDisposable
    {
        private AdaptiveQobuzApiClient _client;
        private IQobuzApiClient _innerClient;
        private IAdaptiveRateLimiter _rateLimiter;
        private Logger _logger;

        public AdaptiveQobuzApiClientTests()
        {
            _innerClient = Substitute.For<IQobuzApiClient>();
            _rateLimiter = Substitute.For<IAdaptiveRateLimiter>();
            _logger = Substitute.For<Logger>();
            
            _client = new AdaptiveQobuzApiClient(_innerClient, _rateLimiter, _logger);
        }

        [Fact]
        public async Task GetAsync_ShouldApplyRateLimitingBeforeRequest()
        {
            // Arrange
            var endpoint = "/album/search";
            var parameters = new Dictionary<string, string> { { "query", "test" } };
            var expectedResult = new { albums = new[] { new { id = "123" } } };
            
            _rateLimiter.WaitIfNeededAsync(endpoint, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(true));
            _innerClient.GetAsync<object>(endpoint, parameters)
                .Returns(Task.FromResult((object)expectedResult));
            
            // Act
            var result = await _client.GetAsync<object>(endpoint, parameters);
            
            // Assert
            await _rateLimiter.Received(1).WaitIfNeededAsync(endpoint, Arg.Any<CancellationToken>());
            await _innerClient.Received(1).GetAsync<object>(endpoint, parameters);
            result.Should().BeEquivalentTo(expectedResult);
        }

        [Fact]
        public async Task GetAsync_Success_ShouldRecordSuccessResponse()
        {
            // Arrange
            var endpoint = "/album/get";
            _rateLimiter.WaitIfNeededAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(true));
            _innerClient.GetAsync<object>(endpoint, null)
                .Returns(Task.FromResult((object)new { }));
            
            // Act
            await _client.GetAsync<object>(endpoint);
            
            // Assert
            _rateLimiter.Received(1).RecordResponse(
                endpoint, 
                Arg.Is<HttpResponseMessage>(r => r.StatusCode == HttpStatusCode.OK));
        }

        [Fact]
        public async Task GetAsync_RateLimitError_ShouldRecordRateLimitResponse()
        {
            // Arrange
            var endpoint = "/album/search";
            _rateLimiter.WaitIfNeededAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(true));
            _innerClient.GetAsync<object>(endpoint, null)
                .Returns(Task.FromException<object>(new Exception("429 Too Many Requests")));
            
            // Act & Assert
            await _client.Invoking(c => c.GetAsync<object>(endpoint))
                .Should().ThrowAsync<Exception>();
            
            _rateLimiter.Received(1).RecordResponse(
                endpoint, 
                Arg.Is<HttpResponseMessage>(r => r.StatusCode == HttpStatusCode.TooManyRequests));
        }

        [Fact]
        public async Task PostAsync_ShouldApplyRateLimitingBeforeRequest()
        {
            // Arrange
            var endpoint = "/user/login";
            var data = new { username = "test", password = "pass" };
            var expectedResult = new { user_auth_token = "token123" };
            
            _rateLimiter.WaitIfNeededAsync(endpoint, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(true));
            _innerClient.PostAsync<object>(endpoint, data)
                .Returns(Task.FromResult((object)expectedResult));
            
            // Act
            var result = await _client.PostAsync<object>(endpoint, data);
            
            // Assert
            await _rateLimiter.Received(1).WaitIfNeededAsync(endpoint, Arg.Any<CancellationToken>());
            await _innerClient.Received(1).PostAsync<object>(endpoint, data);
            result.Should().BeEquivalentTo(expectedResult);
        }

        [Fact]
        public void SetSession_ShouldDelegateToInnerClient()
        {
            // Arrange
            var session = new QobuzSession 
            { 
                AuthToken = "token123",
                UserId = "user123",
                AppId = "app123"
            };
            
            // Act
            _client.SetSession(session);
            
            // Assert
            _innerClient.Received(1).SetSession(session);
        }

        [Fact]
        public void ClearSession_ShouldDelegateToInnerClient()
        {
            // Act
            _client.ClearSession();
            
            // Assert
            _innerClient.Received(1).ClearSession();
        }

        [Fact]
        public void HasValidSession_ShouldDelegateToInnerClient()
        {
            // Arrange
            _innerClient.HasValidSession().Returns(true);
            
            // Act
            var result = _client.HasValidSession();
            
            // Assert
            result.Should().BeTrue();
            _innerClient.Received(1).HasValidSession();
        }

        [Fact]
        public void GetRateLimitStats_ShouldReturnStatsFromRateLimiter()
        {
            // Arrange
            var expectedStats = new RateLimitStats();
            _rateLimiter.GetStats().Returns(expectedStats);
            
            // Act
            var stats = _client.GetRateLimitStats();
            
            // Assert
            stats.Should().BeSameAs(expectedStats);
            _rateLimiter.Received(1).GetStats();
        }

        [Fact]
        public void GetCurrentRateLimit_ShouldReturnLimitFromRateLimiter()
        {
            // Arrange
            var endpoint = "/album/search";
            var expectedLimit = 45;
            _rateLimiter.GetCurrentLimit(endpoint).Returns(expectedLimit);
            
            // Act
            var limit = _client.GetCurrentRateLimit(endpoint);
            
            // Assert
            limit.Should().Be(expectedLimit);
            _rateLimiter.Received(1).GetCurrentLimit(endpoint);
        }

        [Fact]
        public async Task GetAsync_UnauthorizedError_ShouldRecordUnauthorizedResponse()
        {
            // Arrange
            var endpoint = "/user/favorites";
            _rateLimiter.WaitIfNeededAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(true));
            _innerClient.GetAsync<object>(endpoint, null)
                .Returns(Task.FromException<object>(new Exception("401 Unauthorized")));
            
            // Act & Assert
            await _client.Invoking(c => c.GetAsync<object>(endpoint))
                .Should().ThrowAsync<Exception>();
            
            _rateLimiter.Received(1).RecordResponse(
                endpoint, 
                Arg.Is<HttpResponseMessage>(r => r.StatusCode == HttpStatusCode.Unauthorized));
        }

        [Fact]
        public async Task PostAsync_GenericError_ShouldRecordErrorResponse()
        {
            // Arrange
            var endpoint = "/track/report";
            _rateLimiter.WaitIfNeededAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(true));
            _innerClient.PostAsync<object>(endpoint, null)
                .Returns(Task.FromException<object>(new Exception("Network error")));
            
            // Act & Assert
            await _client.Invoking(c => c.PostAsync<object>(endpoint))
                .Should().ThrowAsync<Exception>();
            
            _rateLimiter.Received(1).RecordResponse(
                endpoint, 
                Arg.Is<HttpResponseMessage>(r => r.StatusCode == HttpStatusCode.InternalServerError));
        }
        
        public void Dispose()
        {
            // Clean up any resources if needed
        }
    }
    */
}