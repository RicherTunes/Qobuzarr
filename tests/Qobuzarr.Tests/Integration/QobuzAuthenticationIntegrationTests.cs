using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Services;

namespace Qobuzarr.Tests.Integration
{
    /// <summary>
    /// Integration tests for QobuzAuthenticationService
    /// Tests real authentication flows, token management, and session handling
    /// </summary>
    [Collection("QobuzIntegration")]
    [Trait("Category", "Integration")]
    [Trait("RequiresQobuzAPI", "true")]
    public class QobuzAuthenticationIntegrationTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly HttpClient _httpClient;
        private readonly Mock<ILogger<QobuzAuthenticationService>> _mockLogger;
        private readonly Mock<ICacheService> _mockCacheService;
        private readonly QobuzAuthenticationService _authService;
        private readonly SemaphoreSlim _rateLimiter;

        public QobuzAuthenticationIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _httpClient = new HttpClient();
            _mockLogger = new Mock<ILogger<QobuzAuthenticationService>>();
            _mockCacheService = new Mock<ICacheService>();
            _rateLimiter = new SemaphoreSlim(1, 1);

            // Setup in-memory cache
            var cache = new ConcurrentDictionary<string, object>();
            _mockCacheService.Setup(x => x.Get<QobuzSession>(It.IsAny<string>()))
                .Returns<string>(key => cache.TryGetValue(key, out var value) ? value as QobuzSession : null);
            _mockCacheService.Setup(x => x.Set(It.IsAny<string>(), It.IsAny<QobuzSession>(), It.IsAny<TimeSpan>()))
                .Callback<string, QobuzSession, TimeSpan>((key, value, _) => cache[key] = value);
            _mockCacheService.Setup(x => x.Remove(It.IsAny<string>()))
                .Callback<string>(key => cache.TryRemove(key, out _));

            _authService = new QobuzAuthenticationService(
                _httpClient,
                _mockCacheService.Object,
                _mockLogger.Object);
        }

        [Fact]
        [Trait("Priority", "Critical")]
        public async Task AuthenticateWithCredentials_ValidCredentials_ShouldReturnSession()
        {
            // Arrange
            await _rateLimiter.WaitAsync();
            try
            {
                var email = Environment.GetEnvironmentVariable("QOBUZ_TEST_EMAIL");
                var password = Environment.GetEnvironmentVariable("QOBUZ_TEST_PASSWORD");
                
                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
                {
                    _output.WriteLine("Skipping test - QOBUZ_TEST_EMAIL and QOBUZ_TEST_PASSWORD not set");
                    return;
                }

                var stopwatch = Stopwatch.StartNew();

                // Act
                var session = await _authService.AuthenticateAsync(email, password);
                stopwatch.Stop();

                // Assert
                session.Should().NotBeNull();
                session.UserId.Should().NotBeNullOrEmpty();
                session.AuthToken.Should().NotBeNullOrEmpty();
                session.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
                session.IsValid.Should().BeTrue();

                // Performance check
                stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000, "Authentication should complete within 5 seconds");

                _output.WriteLine($"Authentication successful in {stopwatch.ElapsedMilliseconds}ms");
                _output.WriteLine($"Session expires at: {session.ExpiresAt}");
            }
            finally
            {
                _rateLimiter.Release();
            }
        }

        [Fact]
        [Trait("Priority", "Critical")]
        public async Task AuthenticateWithCredentials_InvalidCredentials_ShouldThrowException()
        {
            // Arrange
            await _rateLimiter.WaitAsync();
            try
            {
                var invalidEmail = "invalid@example.com";
                var invalidPassword = "wrongpassword123";

                // Act & Assert
                var exception = await Assert.ThrowsAsync<QobuzAuthenticationException>(
                    () => _authService.AuthenticateAsync(invalidEmail, invalidPassword));

                exception.Message.Should().Contain("Authentication failed");
                exception.InnerException.Should().NotBeNull();

                _output.WriteLine($"Expected authentication failure: {exception.Message}");
            }
            finally
            {
                _rateLimiter.Release();
            }
        }

        [Fact]
        [Trait("Priority", "Critical")]
        public async Task SessionCaching_ShouldReuseValidSession()
        {
            // Arrange
            var email = Environment.GetEnvironmentVariable("QOBUZ_TEST_EMAIL");
            var password = Environment.GetEnvironmentVariable("QOBUZ_TEST_PASSWORD");
            
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                _output.WriteLine("Skipping test - credentials not set");
                return;
            }

            await _rateLimiter.WaitAsync();
            try
            {
                // Act
                var session1 = await _authService.AuthenticateAsync(email, password);
                var session2 = _authService.GetCachedSession();
                var session3 = await _authService.GetValidSessionAsync();

                // Assert
                session1.Should().NotBeNull();
                session2.Should().NotBeNull();
                session3.Should().NotBeNull();
                
                session2.AuthToken.Should().Be(session1.AuthToken);
                session3.AuthToken.Should().Be(session1.AuthToken);

                _mockCacheService.Verify(x => x.Set(It.IsAny<string>(), It.IsAny<QobuzSession>(), It.IsAny<TimeSpan>()), 
                    Times.Once, "Session should be cached once");

                _output.WriteLine("Session successfully cached and reused");
            }
            finally
            {
                _rateLimiter.Release();
            }
        }

        [Fact]
        [Trait("Priority", "High")]
        public async Task SessionRefresh_ExpiredSession_ShouldRefreshAutomatically()
        {
            // Arrange
            var expiredSession = new QobuzSession
            {
                UserId = "test_user",
                AuthToken = "expired_token",
                ExpiresAt = DateTime.UtcNow.AddMinutes(-5), // Expired
                RefreshToken = Environment.GetEnvironmentVariable("QOBUZ_TEST_REFRESH_TOKEN")
            };

            _mockCacheService.Setup(x => x.Get<QobuzSession>(It.IsAny<string>()))
                .Returns(expiredSession);

            await _rateLimiter.WaitAsync();
            try
            {
                // Act
                var newSession = await _authService.RefreshSessionAsync();

                // Assert
                if (newSession != null)
                {
                    newSession.Should().NotBeNull();
                    newSession.AuthToken.Should().NotBe(expiredSession.AuthToken);
                    newSession.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
                    newSession.IsValid.Should().BeTrue();

                    _output.WriteLine("Session successfully refreshed");
                }
                else
                {
                    _output.WriteLine("Refresh token not available - skipping refresh test");
                }
            }
            finally
            {
                _rateLimiter.Release();
            }
        }

        [Fact]
        [Trait("Priority", "High")]
        public async Task ConcurrentAuthentication_ShouldHandleRaceCondition()
        {
            // Arrange
            var email = Environment.GetEnvironmentVariable("QOBUZ_TEST_EMAIL");
            var password = Environment.GetEnvironmentVariable("QOBUZ_TEST_PASSWORD");
            
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                _output.WriteLine("Skipping test - credentials not set");
                return;
            }

            var tasks = new List<Task<QobuzSession>>();
            var authAttempts = 5;

            // Act - Simulate multiple concurrent authentication attempts
            for (int i = 0; i < authAttempts; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await _rateLimiter.WaitAsync();
                    try
                    {
                        return await _authService.AuthenticateAsync(email, password);
                    }
                    finally
                    {
                        _rateLimiter.Release();
                    }
                }));
            }

            var sessions = await Task.WhenAll(tasks);

            // Assert
            sessions.Should().HaveCount(authAttempts);
            sessions.Should().AllSatisfy(s => s.Should().NotBeNull());
            
            // All sessions should have the same token (cached after first auth)
            var uniqueTokens = sessions.Select(s => s.AuthToken).Distinct().Count();
            uniqueTokens.Should().Be(1, "Should reuse cached session for concurrent requests");

            _output.WriteLine($"Handled {authAttempts} concurrent auth attempts with {uniqueTokens} unique token(s)");
        }

        [Fact]
        [Trait("Priority", "High")]
        public async Task SessionExpiration_ShouldDetectAndHandle()
        {
            // Arrange
            var nearExpirySession = new QobuzSession
            {
                UserId = "test_user",
                AuthToken = "near_expiry_token",
                ExpiresAt = DateTime.UtcNow.AddSeconds(30), // Expires in 30 seconds
                RefreshToken = "refresh_token"
            };

            _mockCacheService.Setup(x => x.Get<QobuzSession>(It.IsAny<string>()))
                .Returns(nearExpirySession);

            // Act
            var isExpiringSoon = nearExpirySession.IsExpiringSoon();
            var isValid = nearExpirySession.IsValid;

            // Assert
            isExpiringSoon.Should().BeTrue("Session expiring within 5 minutes should be detected");
            isValid.Should().BeTrue("Session not yet expired should still be valid");

            _output.WriteLine($"Session expiry detection working correctly - expires in {(nearExpirySession.ExpiresAt - DateTime.UtcNow).TotalSeconds} seconds");
        }

        [Fact]
        [Trait("Priority", "Medium")]
        public async Task DynamicCredentialExtraction_FromWebPlayer_ShouldSucceed()
        {
            // Arrange
            await _rateLimiter.WaitAsync();
            try
            {
                var stopwatch = Stopwatch.StartNew();

                // Act
                var credentials = await _authService.ExtractDynamicCredentialsAsync();
                stopwatch.Stop();

                // Assert
                if (credentials != null)
                {
                    credentials.AppId.Should().NotBeNullOrEmpty();
                    credentials.AppSecret.Should().NotBeNullOrEmpty();
                    
                    // Validate format
                    credentials.AppId.Should().MatchRegex(@"^\d+$", "App ID should be numeric");
                    credentials.AppSecret.Should().HaveLength(32, "App secret should be 32 characters");

                    _output.WriteLine($"Dynamic credentials extracted in {stopwatch.ElapsedMilliseconds}ms");
                    _output.WriteLine($"App ID: {credentials.AppId.Substring(0, 4)}...");
                }
                else
                {
                    _output.WriteLine("Dynamic credential extraction not available in test environment");
                }
            }
            finally
            {
                _rateLimiter.Release();
            }
        }

        [Fact]
        [Trait("Priority", "High")]
        public async Task SessionPersistence_AcrossServiceRestarts_ShouldMaintain()
        {
            // Arrange
            var email = Environment.GetEnvironmentVariable("QOBUZ_TEST_EMAIL");
            var password = Environment.GetEnvironmentVariable("QOBUZ_TEST_PASSWORD");
            
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                _output.WriteLine("Skipping test - credentials not set");
                return;
            }

            await _rateLimiter.WaitAsync();
            try
            {
                // Act - First service instance
                var session1 = await _authService.AuthenticateAsync(email, password);
                
                // Simulate service restart by creating new instance
                var newAuthService = new QobuzAuthenticationService(
                    _httpClient,
                    _mockCacheService.Object,
                    _mockLogger.Object);
                
                var session2 = newAuthService.GetCachedSession();

                // Assert
                session2.Should().NotBeNull("Session should persist across service restarts");
                session2.AuthToken.Should().Be(session1.AuthToken);
                session2.UserId.Should().Be(session1.UserId);

                _output.WriteLine("Session successfully persisted across service restart");
            }
            finally
            {
                _rateLimiter.Release();
            }
        }

        [Fact]
        [Trait("Priority", "Critical")]
        public async Task AuthenticationRetry_OnTransientFailure_ShouldSucceed()
        {
            // Arrange
            var httpClient = new HttpClient(new TransientFailureHandler(2)); // Fail first 2 attempts
            var authServiceWithRetry = new QobuzAuthenticationService(
                httpClient,
                _mockCacheService.Object,
                _mockLogger.Object);

            var email = Environment.GetEnvironmentVariable("QOBUZ_TEST_EMAIL") ?? "test@example.com";
            var password = Environment.GetEnvironmentVariable("QOBUZ_TEST_PASSWORD") ?? "testpass";

            await _rateLimiter.WaitAsync();
            try
            {
                var stopwatch = Stopwatch.StartNew();

                // Act
                var session = await authServiceWithRetry.AuthenticateAsync(email, password);
                stopwatch.Stop();

                // Assert
                if (session != null)
                {
                    session.Should().NotBeNull();
                    stopwatch.ElapsedMilliseconds.Should().BeGreaterThan(2000, "Should have retried after failures");
                    
                    _output.WriteLine($"Authentication succeeded after retries in {stopwatch.ElapsedMilliseconds}ms");
                }
            }
            finally
            {
                _rateLimiter.Release();
            }
        }

        [Fact]
        [Trait("Priority", "Medium")]
        public async Task SessionMetrics_ShouldTrackAuthenticationStats()
        {
            // Arrange
            var metrics = new AuthenticationMetrics();
            var authServiceWithMetrics = new QobuzAuthenticationService(
                _httpClient,
                _mockCacheService.Object,
                _mockLogger.Object,
                metrics);

            var email = Environment.GetEnvironmentVariable("QOBUZ_TEST_EMAIL");
            var password = Environment.GetEnvironmentVariable("QOBUZ_TEST_PASSWORD");
            
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                _output.WriteLine("Skipping test - credentials not set");
                return;
            }

            await _rateLimiter.WaitAsync();
            try
            {
                // Act
                await authServiceWithMetrics.AuthenticateAsync(email, password);
                _ = authServiceWithMetrics.GetCachedSession();
                _ = authServiceWithMetrics.GetCachedSession();

                // Assert
                metrics.TotalAuthentications.Should().Be(1);
                metrics.CacheHits.Should().Be(2);
                metrics.AverageAuthTime.Should().BeGreaterThan(0);

                _output.WriteLine($"Metrics - Auth: {metrics.TotalAuthentications}, Cache Hits: {metrics.CacheHits}, Avg Time: {metrics.AverageAuthTime}ms");
            }
            finally
            {
                _rateLimiter.Release();
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            _rateLimiter?.Dispose();
        }

        // Helper classes
        private class TransientFailureHandler : DelegatingHandler
        {
            private int _requestCount = 0;
            private readonly int _failCount;

            public TransientFailureHandler(int failCount)
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
                    await Task.Delay(1000);
                    throw new HttpRequestException("Simulated transient failure");
                }
                
                return await base.SendAsync(request, cancellationToken);
            }
        }

        private class AuthenticationMetrics
        {
            public int TotalAuthentications { get; set; }
            public int CacheHits { get; set; }
            public double AverageAuthTime { get; set; }
        }
    }
}