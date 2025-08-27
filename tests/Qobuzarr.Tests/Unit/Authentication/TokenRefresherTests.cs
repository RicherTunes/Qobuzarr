using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Xunit;
using NSubstitute;
using FluentAssertions;
using NLog;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Services.Interfaces;

namespace Qobuzarr.Tests.Unit.Authentication
{
    public class TokenRefresherTests
    {
        private readonly IQobuzAuthenticationService _authService;
        private readonly Logger _logger;
        private readonly TokenRefresher _tokenRefresher;

        public TokenRefresherTests()
        {
            _authService = Substitute.For<IQobuzAuthenticationService>();
            _logger = Substitute.For<Logger>();
            _tokenRefresher = new TokenRefresher(_authService, _logger);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullAuthService_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var action = () => new TokenRefresher(null, _logger);
            action.Should().Throw<ArgumentNullException>()
                .WithParameterName("authService");
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var action = () => new TokenRefresher(_authService, null);
            action.Should().Throw<ArgumentNullException>()
                .WithParameterName("logger");
        }

        [Fact]
        public void Constructor_WithValidParameters_ShouldInitializeSuccessfully()
        {
            // Act
            var refresher = new TokenRefresher(_authService, _logger);

            // Assert
            refresher.Should().NotBeNull();
            var stats = refresher.GetStats();
            stats.TotalAttempts.Should().Be(0);
            stats.IsCircuitBreakerOpen.Should().BeFalse();
        }

        #endregion

        #region ShouldRefresh Tests

        [Fact]
        public void ShouldRefresh_WithNullSession_ShouldReturnFalse()
        {
            // Act
            var result = _tokenRefresher.ShouldRefresh(null);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void ShouldRefresh_WithInvalidSession_ShouldReturnFalse()
        {
            // Arrange
            var expiredSession = CreateExpiredSession();

            // Act
            var result = _tokenRefresher.ShouldRefresh(expiredSession);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void ShouldRefresh_WithSessionExpiringWithinBuffer_ShouldReturnTrue()
        {
            // Arrange
            var session = CreateSessionExpiringIn(TimeSpan.FromMinutes(20)); // Within 30-minute buffer

            // Act
            var result = _tokenRefresher.ShouldRefresh(session);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void ShouldRefresh_WithSessionExpiringBeyondBuffer_ShouldReturnFalse()
        {
            // Arrange
            var session = CreateSessionExpiringIn(TimeSpan.FromHours(2)); // Beyond 30-minute buffer

            // Act
            var result = _tokenRefresher.ShouldRefresh(session);

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region RefreshSessionAsync Tests

        [Fact]
        public async Task RefreshSessionAsync_WithNullSession_ShouldThrowArgumentNullException()
        {
            // Arrange
            var credentials = CreateValidCredentials();

            // Act & Assert
            var action = async () => await _tokenRefresher.RefreshSessionAsync(null, credentials);
            await action.Should().ThrowAsync<ArgumentNullException>()
                .WithParameterName("currentSession");
        }

        [Fact]
        public async Task RefreshSessionAsync_WithNullCredentials_ShouldThrowArgumentNullException()
        {
            // Arrange
            var session = CreateValidSession();

            // Act & Assert
            var action = async () => await _tokenRefresher.RefreshSessionAsync(session, null);
            await action.Should().ThrowAsync<ArgumentNullException>()
                .WithParameterName("originalCredentials");
        }

        [Fact]
        public async Task RefreshSessionAsync_Successful_ShouldReturnNewSession()
        {
            // Arrange
            var oldSession = CreateSessionExpiringIn(TimeSpan.FromMinutes(20));
            var newSession = CreateValidSession();
            var credentials = CreateValidCredentials();
            
            _authService.AuthenticateAsync(credentials)
                .Returns(Task.FromResult<QobuzSession?>(newSession));

            TokenRefreshCompletedEventArgs? capturedArgs = null;
            _tokenRefresher.RefreshCompleted += (sender, args) => capturedArgs = args;

            // Act
            var result = await _tokenRefresher.RefreshSessionAsync(oldSession, credentials);

            // Assert
            result.Should().Be(newSession);
            capturedArgs.Should().NotBeNull();
            capturedArgs.NewSession.Should().Be(newSession);
            capturedArgs.WasForced.Should().BeFalse();
            
            var stats = _tokenRefresher.GetStats();
            stats.SuccessfulRefreshes.Should().Be(1);
            stats.FailedRefreshes.Should().Be(0);
        }

        [Fact]
        public async Task RefreshSessionAsync_Failed_ShouldReturnNull()
        {
            // Arrange
            var session = CreateSessionExpiringIn(TimeSpan.FromMinutes(20));
            var credentials = CreateValidCredentials();
            
            _authService.AuthenticateAsync(credentials)
                .Returns(Task.FromResult<QobuzSession?>(null));

            TokenRefreshFailedEventArgs? capturedArgs = null;
            _tokenRefresher.RefreshFailed += (sender, args) => capturedArgs = args;

            // Act
            var result = await _tokenRefresher.RefreshSessionAsync(session, credentials);

            // Assert
            result.Should().BeNull();
            capturedArgs.Should().NotBeNull();
            capturedArgs.WasForced.Should().BeFalse();
            
            var stats = _tokenRefresher.GetStats();
            stats.SuccessfulRefreshes.Should().Be(0);
            stats.FailedRefreshes.Should().Be(1);
        }

        [Fact]
        public async Task RefreshSessionAsync_WithCooldownActive_ShouldReturnCurrentSession()
        {
            // Arrange
            var session = CreateSessionExpiringIn(TimeSpan.FromMinutes(20));
            var credentials = CreateValidCredentials();
            var newSession = CreateValidSession();
            
            _authService.AuthenticateAsync(credentials)
                .Returns(Task.FromResult<QobuzSession?>(newSession));

            // First refresh
            await _tokenRefresher.RefreshSessionAsync(session, credentials);
            
            // Reset mock
            _authService.ClearReceivedCalls();

            // Act - Second refresh immediately (within cooldown)
            var result = await _tokenRefresher.RefreshSessionAsync(session, credentials);

            // Assert
            result.Should().Be(session); // Returns current session during cooldown
            _authService.DidNotReceive().AuthenticateAsync(Arg.Any<QobuzCredentials>());
        }

        [Fact]
        public async Task RefreshSessionAsync_WithRetryOnFailure_ShouldAttemptMultipleTimes()
        {
            // Arrange
            var session = CreateSessionExpiringIn(TimeSpan.FromMinutes(20));
            var credentials = CreateValidCredentials();
            var newSession = CreateValidSession();
            
            var callCount = 0;
            _authService.AuthenticateAsync(credentials)
                .Returns(_ => 
                {
                    callCount++;
                    if (callCount < 3) // Fail first 2 attempts
                        throw new QobuzAuthenticationException("Auth failed");
                    return Task.FromResult<QobuzSession?>(newSession);
                });

            // Act
            var result = await _tokenRefresher.RefreshSessionAsync(session, credentials);

            // Assert
            result.Should().Be(newSession);
            await _authService.Received(3).AuthenticateAsync(credentials); // 3 attempts
            
            var stats = _tokenRefresher.GetStats();
            stats.SuccessfulRefreshes.Should().Be(1);
        }

        [Fact]
        public async Task RefreshSessionAsync_WithAllRetriesFailed_ShouldReturnNull()
        {
            // Arrange
            var session = CreateSessionExpiringIn(TimeSpan.FromMinutes(20));
            var credentials = CreateValidCredentials();
            
            _authService.AuthenticateAsync(credentials)
                .Returns<QobuzSession?>(_ => throw new QobuzAuthenticationException("Auth failed"));

            // Act
            var result = await _tokenRefresher.RefreshSessionAsync(session, credentials);

            // Assert
            result.Should().BeNull();
            await _authService.Received(3).AuthenticateAsync(credentials); // Max retry attempts
            
            var stats = _tokenRefresher.GetStats();
            stats.FailedRefreshes.Should().Be(1);
            stats.ConsecutiveFailures.Should().Be(1);
        }

        #endregion

        #region Circuit Breaker Tests

        [Fact]
        public async Task RefreshSessionAsync_WithCircuitBreakerOpen_ShouldReturnNullWithoutAttempting()
        {
            // Arrange
            var session = CreateSessionExpiringIn(TimeSpan.FromMinutes(20));
            var credentials = CreateValidCredentials();
            
            _authService.AuthenticateAsync(credentials)
                .Returns<QobuzSession?>(_ => throw new QobuzAuthenticationException("Auth failed"));

            // Trigger circuit breaker by failing multiple times
            for (int i = 0; i < 5; i++)
            {
                await _tokenRefresher.RefreshSessionAsync(session, credentials);
                // Add delay to bypass cooldown
                await Task.Delay(1100);
            }

            _authService.ClearReceivedCalls();
            
            CircuitBreakerTrippedEventArgs? capturedArgs = null;
            _tokenRefresher.CircuitBreakerTripped += (sender, args) => capturedArgs = args;

            // Act - Attempt with circuit breaker open
            var result = await _tokenRefresher.RefreshSessionAsync(session, credentials);

            // Assert
            result.Should().BeNull();
            _authService.DidNotReceive().AuthenticateAsync(Arg.Any<QobuzCredentials>());
            capturedArgs.Should().NotBeNull();
            capturedArgs.FailureCount.Should().BeGreaterThanOrEqualTo(5);
        }

        #endregion

        #region ForceRefreshAsync Tests

        [Fact]
        public async Task ForceRefreshAsync_WithNullCredentials_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var action = async () => await _tokenRefresher.ForceRefreshAsync(null);
            await action.Should().ThrowAsync<ArgumentNullException>()
                .WithParameterName("originalCredentials");
        }

        [Fact]
        public async Task ForceRefreshAsync_Successful_ShouldBypassCooldownAndCircuitBreaker()
        {
            // Arrange
            var credentials = CreateValidCredentials();
            var newSession = CreateValidSession();
            
            // First, trigger cooldown and circuit breaker
            _authService.AuthenticateAsync(credentials)
                .Returns<QobuzSession?>(_ => throw new QobuzAuthenticationException("Auth failed"));
            
            var session = CreateSessionExpiringIn(TimeSpan.FromMinutes(20));
            for (int i = 0; i < 5; i++)
            {
                await _tokenRefresher.RefreshSessionAsync(session, credentials);
                await Task.Delay(1100);
            }
            
            // Now setup successful auth for force refresh
            _authService.AuthenticateAsync(credentials)
                .Returns(Task.FromResult<QobuzSession?>(newSession));

            // Act
            var result = await _tokenRefresher.ForceRefreshAsync(credentials);

            // Assert
            result.Should().Be(newSession);
            var stats = _tokenRefresher.GetStats();
            stats.SuccessfulRefreshes.Should().Be(1);
            stats.ConsecutiveFailures.Should().Be(0); // Reset by successful refresh
        }

        [Fact]
        public async Task ForceRefreshAsync_Failed_ShouldThrowException()
        {
            // Arrange
            var credentials = CreateValidCredentials();
            
            _authService.AuthenticateAsync(credentials)
                .Returns(Task.FromResult<QobuzSession?>(null));

            // Act & Assert
            var action = async () => await _tokenRefresher.ForceRefreshAsync(credentials);
            await action.Should().ThrowAsync<QobuzAuthenticationException>()
                .WithMessage("Force refresh returned invalid session");
        }

        #endregion

        #region Concurrent Access Tests

        [Fact]
        public async Task RefreshSessionAsync_ConcurrentRequests_ShouldOnlyRefreshOnce()
        {
            // Arrange
            var session = CreateSessionExpiringIn(TimeSpan.FromMinutes(20));
            var credentials = CreateValidCredentials();
            var newSession = CreateValidSession();
            
            var refreshCount = 0;
            _authService.AuthenticateAsync(credentials)
                .Returns(async _ => 
                {
                    Interlocked.Increment(ref refreshCount);
                    await Task.Delay(500); // Simulate slow refresh
                    return newSession;
                });
            
            _authService.GetCachedSession().Returns(newSession);

            // Act - Launch multiple concurrent refresh requests
            var tasks = new List<Task<QobuzSession?>>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(_tokenRefresher.RefreshSessionAsync(session, credentials));
            }
            
            var results = await Task.WhenAll(tasks);

            // Assert
            refreshCount.Should().Be(1); // Only one actual refresh should occur
            results.Should().AllBeEquivalentTo(newSession);
        }

        #endregion

        #region Statistics Tests

        [Fact]
        public void GetStats_ShouldReturnAccurateStatistics()
        {
            // Act
            var stats = _tokenRefresher.GetStats();

            // Assert
            stats.Should().NotBeNull();
            stats.ServiceUptime.Should().BePositive();
            stats.TotalAttempts.Should().Be(0);
            stats.SuccessfulRefreshes.Should().Be(0);
            stats.FailedRefreshes.Should().Be(0);
            stats.SuccessRate.Should().Be(0.0);
            stats.IsRefreshing.Should().BeFalse();
            stats.IsCircuitBreakerOpen.Should().BeFalse();
            stats.Configuration.Should().NotBeNull();
            stats.Configuration.RefreshBuffer.Should().Be(TimeSpan.FromMinutes(30));
            stats.Configuration.RefreshCooldown.Should().Be(TimeSpan.FromMinutes(1));
            stats.Configuration.MaxRetryAttempts.Should().Be(3);
            stats.Configuration.CircuitBreakerThreshold.Should().Be(5);
        }

        [Fact]
        public async Task GetStats_AfterOperations_ShouldReflectActivity()
        {
            // Arrange
            var session = CreateSessionExpiringIn(TimeSpan.FromMinutes(20));
            var credentials = CreateValidCredentials();
            var newSession = CreateValidSession();
            
            _authService.AuthenticateAsync(credentials)
                .Returns(Task.FromResult<QobuzSession?>(newSession));

            // Act
            await _tokenRefresher.RefreshSessionAsync(session, credentials);
            var stats = _tokenRefresher.GetStats();

            // Assert
            stats.TotalAttempts.Should().Be(1);
            stats.SuccessfulRefreshes.Should().Be(1);
            stats.FailedRefreshes.Should().Be(0);
            stats.SuccessRate.Should().BeApproximately(1.0, 0.01);
            stats.ConsecutiveFailures.Should().Be(0);
            stats.LastSuccessfulRefresh.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        }

        #endregion

        #region ITokenRefresher Interface Tests

        [Fact]
        public async Task RefreshTokenAsync_WithNullToken_ShouldReturnNull()
        {
            // Act
            var result = await _tokenRefresher.RefreshTokenAsync(null);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task RefreshTokenAsync_WithValidToken_ShouldReturnSameToken()
        {
            // Arrange
            var token = "valid_token";
            var session = CreateValidSession();
            _authService.GetCachedSession().Returns(session);

            // Act
            var result = await _tokenRefresher.RefreshTokenAsync(token);

            // Assert
            result.Should().Be(token); // Returns same token as Qobuz doesn't support refresh
        }

        [Fact]
        public void ShouldRefreshToken_WithNullToken_ShouldReturnFalse()
        {
            // Act
            var result = _tokenRefresher.ShouldRefreshToken(null);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void ShouldRefreshToken_WithValidTokenAndSessionNeedingRefresh_ShouldReturnTrue()
        {
            // Arrange
            var token = "valid_token";
            var session = CreateSessionExpiringIn(TimeSpan.FromMinutes(20));
            _authService.GetCachedSession().Returns(session);

            // Act
            var result = _tokenRefresher.ShouldRefreshToken(token);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task RefreshSessionAsync_InterfaceMethod_WithNullSession_ShouldReturnNull()
        {
            // Act
            var result = await _tokenRefresher.RefreshSessionAsync(null, CancellationToken.None);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void CanRefreshSession_WithValidSession_ShouldReturnTrue()
        {
            // Arrange
            var session = CreateValidSession();

            // Act
            var result = _tokenRefresher.CanRefreshSession(session);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void CanRefreshSession_WithInvalidSession_ShouldReturnFalse()
        {
            // Arrange
            var session = CreateExpiredSession();

            // Act
            var result = _tokenRefresher.CanRefreshSession(session);

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region Event Tests

        [Fact]
        public async Task RefreshStarted_ShouldFireWithCorrectArgs()
        {
            // Arrange
            var session = CreateSessionExpiringIn(TimeSpan.FromMinutes(20));
            var credentials = CreateValidCredentials();
            var newSession = CreateValidSession();
            
            _authService.AuthenticateAsync(credentials)
                .Returns(Task.FromResult<QobuzSession?>(newSession));
            
            TokenRefreshStartedEventArgs? capturedArgs = null;
            _tokenRefresher.RefreshStarted += (sender, args) => capturedArgs = args;

            // Act
            await _tokenRefresher.RefreshSessionAsync(session, credentials);

            // Assert
            capturedArgs.Should().NotBeNull();
            capturedArgs.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            capturedArgs.Reason.Should().Contain(session.ExpiresAt.ToString());
            capturedArgs.AttemptNumber.Should().Be(1);
        }

        [Fact]
        public async Task RefreshCompleted_WhenEventHandlerThrows_ShouldNotPropagateException()
        {
            // Arrange
            var session = CreateSessionExpiringIn(TimeSpan.FromMinutes(20));
            var credentials = CreateValidCredentials();
            var newSession = CreateValidSession();
            
            _authService.AuthenticateAsync(credentials)
                .Returns(Task.FromResult<QobuzSession?>(newSession));
            
            _tokenRefresher.RefreshCompleted += (sender, args) =>
            {
                throw new InvalidOperationException("Event handler error");
            };

            // Act & Assert
            var action = async () => await _tokenRefresher.RefreshSessionAsync(session, credentials);
            await action.Should().NotThrowAsync();
        }

        #endregion

        #region Disposal Tests

        [Fact]
        public void Dispose_ShouldNotThrow()
        {
            // Act & Assert
            var action = () => _tokenRefresher.Dispose();
            action.Should().NotThrow();
        }

        [Fact]
        public void Dispose_MultipleTimes_ShouldNotThrow()
        {
            // Act & Assert
            var action = () =>
            {
                _tokenRefresher.Dispose();
                _tokenRefresher.Dispose();
            };
            action.Should().NotThrow();
        }

        #endregion

        #region Helper Methods

        private QobuzSession CreateValidSession()
        {
            return new QobuzSession
            {
                UserId = "test_user",
                AuthToken = "valid_token",
                ExpiresAt = DateTime.UtcNow.AddHours(24),
                CreatedAt = DateTime.UtcNow
            };
        }

        private QobuzSession CreateExpiredSession()
        {
            return new QobuzSession
            {
                UserId = "test_user",
                AuthToken = "expired_token",
                ExpiresAt = DateTime.UtcNow.AddHours(-1),
                CreatedAt = DateTime.UtcNow.AddHours(-25)
            };
        }

        private QobuzSession CreateSessionExpiringIn(TimeSpan timeToExpiry)
        {
            return new QobuzSession
            {
                UserId = "test_user",
                AuthToken = "expiring_token",
                ExpiresAt = DateTime.UtcNow.Add(timeToExpiry),
                CreatedAt = DateTime.UtcNow
            };
        }

        private QobuzCredentials CreateValidCredentials()
        {
            return new QobuzCredentials
            {
                Username = "test@example.com",
                Password = "password123"
            };
        }

        #endregion
    }
}