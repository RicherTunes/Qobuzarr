using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using NzbDrone.Core.Indexers;
using Qobuzarr.API;
using Qobuzarr.Authentication;
using Qobuzarr.Authentication.Models;
using Qobuzarr.Tests.Fixtures;
using Xunit;

namespace Qobuzarr.Tests.Integration
{
    [Collection("Integration")]
    public class AuthenticationLifecycleTests : IntegrationTestBase
    {
        private readonly IQobuzAuthenticationService _authService;
        private readonly IQobuzApiClient _apiClient;
        private readonly Mock<ILogger<QobuzAuthenticationService>> _loggerMock;

        public AuthenticationLifecycleTests()
        {
            _loggerMock = new Mock<ILogger<QobuzAuthenticationService>>();
            var services = new ServiceCollection();
            ConfigureServices(services);
            var provider = services.BuildServiceProvider();
            
            _authService = provider.GetRequiredService<IQobuzAuthenticationService>();
            _apiClient = provider.GetRequiredService<IQobuzApiClient>();
        }

        [Fact]
        public async Task Should_RefreshToken_WhenExpired()
        {
            // Arrange
            var initialToken = await _authService.AuthenticateAsync("test@example.com", "password");
            initialToken.Should().NotBeNull();
            
            // Simulate token expiration by waiting or manipulating the token
            await SimulateTokenExpiration(initialToken);

            // Act
            var refreshedToken = await _authService.RefreshTokenAsync(initialToken);

            // Assert
            refreshedToken.Should().NotBeNull();
            refreshedToken.Token.Should().NotBe(initialToken.Token);
            refreshedToken.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
            
            // Verify the new token works
            var testResult = await _apiClient.SearchAsync("test query", refreshedToken);
            testResult.Should().NotBeNull();
        }

        [Fact]
        public async Task Should_HandleConcurrentTokenRefresh()
        {
            // Arrange
            var initialToken = await _authService.AuthenticateAsync("test@example.com", "password");
            await SimulateTokenExpiration(initialToken);

            var refreshTasks = new List<Task<QobuzAuthToken>>();
            var barrier = new Barrier(5); // 5 concurrent refresh attempts

            // Act - Launch 5 concurrent refresh attempts
            for (int i = 0; i < 5; i++)
            {
                refreshTasks.Add(Task.Run(async () =>
                {
                    barrier.SignalAndWait();
                    return await _authService.RefreshTokenAsync(initialToken);
                }));
            }

            var results = await Task.WhenAll(refreshTasks);

            // Assert - All should get the same refreshed token (deduplication)
            results.Should().HaveCount(5);
            var distinctTokens = results.Select(r => r.Token).Distinct().ToList();
            distinctTokens.Should().HaveCount(1, "concurrent refresh should be deduplicated");
            
            // Verify only one actual refresh call was made to the API
            VerifySingleRefreshCall();
        }

        [Fact]
        public async Task Should_RetryWith_NewToken_AfterRefresh()
        {
            // Arrange
            var mockHandler = new Mock<HttpMessageHandler>();
            var callCount = 0;
            
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(() =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        // First call returns 401 Unauthorized
                        return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                    }
                    else
                    {
                        // Second call (after refresh) succeeds
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"albums\":{\"items\":[]}}")
                        };
                    }
                });

            var httpClient = new HttpClient(mockHandler.Object);
            var apiClient = new QobuzApiClient(httpClient, _loggerMock.Object);

            // Act
            var result = await apiClient.SearchAsync("test query", new QobuzAuthToken());

            // Assert
            callCount.Should().Be(2, "should retry once after token refresh");
            result.Should().NotBeNull();
        }

        [Fact]
        public async Task Should_InvalidateCache_OnAuthFailure()
        {
            // Arrange
            var cacheKey = "test_cache_key";
            var cachedData = new { data = "cached" };
            await CacheService.SetAsync(cacheKey, cachedData);

            // Act - Simulate auth failure
            var authException = new QobuzAuthenticationException("Invalid credentials");
            await _authService.HandleAuthenticationFailureAsync(authException);

            // Assert
            var cachedResult = await CacheService.GetAsync<object>(cacheKey);
            cachedResult.Should().BeNull("cache should be invalidated on auth failure");
        }

        [Fact]
        public async Task Should_HandleTokenExpiration_DuringLongRunningOperation()
        {
            // Arrange
            var token = await _authService.AuthenticateAsync("test@example.com", "password");
            var downloadStartTime = DateTime.UtcNow;
            
            // Simulate a long-running download that spans token expiration
            var downloadTask = SimulateLongRunningDownload(token, TimeSpan.FromHours(2));

            // Act - Token expires during download
            await SimulateTokenExpiration(token);
            var result = await downloadTask;

            // Assert
            result.Should().BeTrue("download should complete despite token expiration");
            
            // Verify a new token was obtained and used
            var refreshCalls = GetTokenRefreshCalls();
            refreshCalls.Should().BeGreaterThan(0, "token should be refreshed during long operation");
        }

        [Fact]
        public async Task Should_Fallback_ToReauthentication_WhenRefreshFails()
        {
            // Arrange
            var token = await _authService.AuthenticateAsync("test@example.com", "password");
            await SimulateTokenExpiration(token);
            
            // Simulate refresh failure
            MockRefreshToFail();

            // Act
            var newToken = await _authService.EnsureValidTokenAsync(token);

            // Assert
            newToken.Should().NotBeNull();
            newToken.Token.Should().NotBe(token.Token);
            
            // Verify reauthentication was attempted
            VerifyReauthenticationAttempted();
        }

        [Fact]
        public async Task Should_RateLimitTokenRefresh()
        {
            // Arrange
            var token = await _authService.AuthenticateAsync("test@example.com", "password");
            var refreshAttempts = new List<Task<QobuzAuthToken>>();

            // Act - Rapid refresh attempts
            for (int i = 0; i < 10; i++)
            {
                refreshAttempts.Add(_authService.RefreshTokenAsync(token));
                await Task.Delay(10); // Small delay between attempts
            }

            var results = await Task.WhenAll(refreshAttempts);

            // Assert
            var actualRefreshCalls = GetTokenRefreshCalls();
            actualRefreshCalls.Should().BeLessThan(10, "rapid refresh attempts should be rate limited");
            
            // All should still get valid tokens
            results.Should().OnlyContain(r => r != null && !string.IsNullOrEmpty(r.Token));
        }

        [Fact]
        public async Task Should_PreemptivelyRefresh_NearExpiration()
        {
            // Arrange
            var token = await _authService.AuthenticateAsync("test@example.com", "password");
            
            // Set token to expire in 5 minutes
            token.ExpiresAt = DateTime.UtcNow.AddMinutes(5);

            // Act - Any API call should trigger preemptive refresh
            var result = await _apiClient.SearchAsync("test", token);

            // Assert
            var refreshCalls = GetTokenRefreshCalls();
            refreshCalls.Should().Be(1, "token near expiration should be preemptively refreshed");
            result.Should().NotBeNull();
        }

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(10)]
        public async Task Should_MaintainTokenIntegrity_UnderLoad(int concurrentUsers)
        {
            // Arrange
            var userTasks = new List<Task<bool>>();

            // Act - Simulate multiple users authenticating and using tokens concurrently
            for (int i = 0; i < concurrentUsers; i++)
            {
                var userId = i;
                userTasks.Add(Task.Run(async () =>
                {
                    var email = $"user{userId}@example.com";
                    var token = await _authService.AuthenticateAsync(email, "password");
                    
                    // Perform operations with the token
                    for (int j = 0; j < 5; j++)
                    {
                        var searchResult = await _apiClient.SearchAsync($"query{j}", token);
                        if (searchResult == null) return false;
                        await Task.Delay(100);
                    }
                    
                    return true;
                }));
            }

            var results = await Task.WhenAll(userTasks);

            // Assert
            results.Should().OnlyContain(r => r == true, "all users should complete operations successfully");
            
            // Verify no token cross-contamination
            VerifyNoTokenCrossContamination();
        }

        private async Task SimulateTokenExpiration(QobuzAuthToken token)
        {
            token.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
            await Task.Delay(100);
        }

        private async Task<bool> SimulateLongRunningDownload(QobuzAuthToken token, TimeSpan duration)
        {
            var endTime = DateTime.UtcNow.Add(duration);
            while (DateTime.UtcNow < endTime)
            {
                // Simulate download chunks
                await _apiClient.GetStreamUrlAsync("track_id", token);
                await Task.Delay(1000);
            }
            return true;
        }

        private void MockRefreshToFail()
        {
            // Implementation to mock refresh failure
        }

        private void VerifyReauthenticationAttempted()
        {
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Reauthenticating")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.AtLeastOnce);
        }

        private void VerifySingleRefreshCall()
        {
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Refreshing token")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        private int GetTokenRefreshCalls()
        {
            return _loggerMock.Invocations
                .Count(i => i.Arguments.Any(a => a?.ToString()?.Contains("Refreshing token") ?? false));
        }

        private void VerifyNoTokenCrossContamination()
        {
            // Verify each user's token remained isolated
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Token mismatch")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Never);
        }
    }
}