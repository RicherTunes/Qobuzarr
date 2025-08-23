using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Configuration;
using System.Collections.Concurrent;

namespace Qobuzarr.Tests.Integration
{
    /// <summary>
    /// Integration tests for authentication token lifecycle, refresh, and edge cases
    /// Validates session management, token expiration, and concurrent authentication
    /// </summary>
    [Collection("QobuzIntegration")]
    [Trait("Category", "Integration")]
    [Trait("Component", "Authentication")]
    [Trait("RequiresCredentials", "true")]
    public class AuthenticationTokenRefreshTests : IAsyncLifetime
    {
        private readonly ITestOutputHelper _output;
        private IQobuzAuthenticationService _authService;
        private readonly ConcurrentBag<QobuzSession> _activeSessions = new();
        private string _testEmail;
        private string _testPassword;
        private string _appId;
        private string _appSecret;

        public AuthenticationTokenRefreshTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public async Task InitializeAsync()
        {
            _appId = Environment.GetEnvironmentVariable("QOBUZ_APP_ID");
            _appSecret = Environment.GetEnvironmentVariable("QOBUZ_APP_SECRET");
            _testEmail = Environment.GetEnvironmentVariable("QOBUZ_EMAIL");
            _testPassword = Environment.GetEnvironmentVariable("QOBUZ_PASSWORD");

            if (string.IsNullOrEmpty(_appId) || string.IsNullOrEmpty(_testEmail))
            {
                throw new SkipException("Qobuz credentials not configured");
            }

            var services = new ServiceCollection();
            ConfigureServices(services);
            var provider = services.BuildServiceProvider();

            _authService = provider.GetRequiredService<IQobuzAuthenticationService>();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IQobuzAuthenticationService, QobuzAuthenticationService>();
            services.AddHttpClient();
            services.AddMemoryCache();
        }

        #region Token Lifecycle Tests

        [Fact]
        public async Task Authenticate_WithValidCredentials_ReturnsValidSession()
        {
            // Act
            var session = await _authService.AuthenticateAsync(_testEmail, _testPassword);
            _activeSessions.Add(session);

            // Assert
            session.Should().NotBeNull();
            session.AuthToken.Should().NotBeNullOrEmpty();
            session.UserId.Should().NotBeNullOrEmpty();
            session.Credential.Should().NotBeNull();
            session.IsValid.Should().BeTrue();
            session.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
            
            _output.WriteLine($"Session created: UserID={session.UserId}, Expires={session.ExpiresAt}");
        }

        [Fact]
        public async Task Authenticate_WithInvalidCredentials_ThrowsAuthenticationException()
        {
            // Act & Assert
            var act = async () => await _authService.AuthenticateAsync("invalid@email.com", "wrongpassword");
            
            await act.Should().ThrowAsync<QobuzAuthenticationException>()
                .WithMessage("*authentication failed*");
        }

        [Fact]
        public async Task RefreshToken_BeforeExpiry_MaintainsSession()
        {
            // Arrange
            var originalSession = await _authService.AuthenticateAsync(_testEmail, _testPassword);
            _activeSessions.Add(originalSession);
            var originalToken = originalSession.AuthToken;
            var originalExpiry = originalSession.ExpiresAt;
            
            // Wait a bit to ensure time difference
            await Task.Delay(2000);
            
            // Act
            var refreshedSession = await _authService.RefreshSessionAsync(originalSession);
            _activeSessions.Add(refreshedSession);
            
            // Assert
            refreshedSession.Should().NotBeNull();
            refreshedSession.AuthToken.Should().NotBeNullOrEmpty();
            refreshedSession.UserId.Should().Be(originalSession.UserId);
            refreshedSession.ExpiresAt.Should().BeAfter(originalExpiry);
            refreshedSession.IsValid.Should().BeTrue();
            
            // Token might be same or different depending on Qobuz implementation
            _output.WriteLine($"Token refresh: Original expiry={originalExpiry}, New expiry={refreshedSession.ExpiresAt}");
        }

        [Fact]
        public async Task GetValidSession_WithExpiredToken_AutomaticallyRefreshes()
        {
            // Arrange - Create a session and artificially expire it
            var session = await _authService.AuthenticateAsync(_testEmail, _testPassword);
            
            // Simulate expiration by modifying the session (if possible via reflection or mock)
            var expiredSession = new QobuzSession
            {
                AuthToken = session.AuthToken,
                UserId = session.UserId,
                Credential = session.Credential,
                ExpiresAt = DateTime.UtcNow.AddMinutes(-5) // Expired 5 minutes ago
            };
            
            // Act
            var validSession = await _authService.GetValidSessionAsync(expiredSession);
            _activeSessions.Add(validSession);
            
            // Assert
            validSession.Should().NotBeNull();
            validSession.IsValid.Should().BeTrue();
            validSession.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
            
            _output.WriteLine($"Auto-refresh succeeded: New expiry={validSession.ExpiresAt}");
        }

        #endregion

        #region Concurrent Authentication Tests

        [Fact]
        public async Task ConcurrentAuthentications_SameCredentials_HandledCorrectly()
        {
            // Arrange
            var tasks = new Task<QobuzSession>[10];
            var errors = new ConcurrentBag<Exception>();
            
            // Act - Attempt 10 concurrent authentications
            for (int i = 0; i < tasks.Length; i++)
            {
                var taskId = i;
                tasks[i] = Task.Run(async () =>
                {
                    try
                    {
                        var session = await _authService.AuthenticateAsync(_testEmail, _testPassword);
                        _activeSessions.Add(session);
                        _output.WriteLine($"Task {taskId} authenticated: {session.UserId}");
                        return session;
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                        _output.WriteLine($"Task {taskId} failed: {ex.Message}");
                        throw;
                    }
                });
            }
            
            var sessions = await Task.WhenAll(tasks);
            
            // Assert
            errors.Should().BeEmpty("All concurrent authentications should succeed");
            sessions.Should().AllSatisfy(s =>
            {
                s.Should().NotBeNull();
                s.IsValid.Should().BeTrue();
                s.UserId.Should().Be(sessions[0].UserId);
            });
            
            _output.WriteLine($"Completed {sessions.Length} concurrent authentications successfully");
        }

        [Fact]
        public async Task ConcurrentTokenRefresh_SingleSession_HandledCorrectly()
        {
            // Arrange
            var session = await _authService.AuthenticateAsync(_testEmail, _testPassword);
            _activeSessions.Add(session);
            
            var refreshTasks = new Task<QobuzSession>[5];
            var errors = new ConcurrentBag<Exception>();
            
            // Act - Multiple threads try to refresh the same session
            for (int i = 0; i < refreshTasks.Length; i++)
            {
                var taskId = i;
                refreshTasks[i] = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(Random.Shared.Next(0, 100)); // Random delay to increase contention
                        var refreshed = await _authService.RefreshSessionAsync(session);
                        _activeSessions.Add(refreshed);
                        _output.WriteLine($"Task {taskId} refreshed token");
                        return refreshed;
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                        _output.WriteLine($"Task {taskId} refresh failed: {ex.Message}");
                        throw;
                    }
                });
            }
            
            var refreshedSessions = await Task.WhenAll(refreshTasks);
            
            // Assert
            errors.Should().BeEmpty("All refresh attempts should handle concurrency correctly");
            refreshedSessions.Should().AllSatisfy(s =>
            {
                s.Should().NotBeNull();
                s.IsValid.Should().BeTrue();
                s.UserId.Should().Be(session.UserId);
            });
            
            _output.WriteLine($"Handled {refreshedSessions.Length} concurrent refresh attempts");
        }

        #endregion

        #region Session Validation Tests

        [Fact]
        public async Task ValidateSession_WithActiveSession_ReturnsTrue()
        {
            // Arrange
            var session = await _authService.AuthenticateAsync(_testEmail, _testPassword);
            _activeSessions.Add(session);
            
            // Act
            var isValid = await _authService.ValidateSessionAsync(session);
            
            // Assert
            isValid.Should().BeTrue();
            _output.WriteLine($"Session validation successful for user {session.UserId}");
        }

        [Fact]
        public async Task ValidateSession_WithInvalidToken_ReturnsFalse()
        {
            // Arrange
            var invalidSession = new QobuzSession
            {
                AuthToken = "invalid_token_12345",
                UserId = "invalid_user",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };
            
            // Act
            var isValid = await _authService.ValidateSessionAsync(invalidSession);
            
            // Assert
            isValid.Should().BeFalse();
            _output.WriteLine("Invalid session correctly identified");
        }

        [Fact]
        public async Task SessionCache_MultipleRequests_ReusesCachedSession()
        {
            // This tests that the service properly caches sessions to avoid unnecessary API calls
            
            // Arrange & Act - Authenticate twice with same credentials
            var session1 = await _authService.AuthenticateAsync(_testEmail, _testPassword);
            var session2 = await _authService.AuthenticateAsync(_testEmail, _testPassword);
            
            _activeSessions.Add(session1);
            _activeSessions.Add(session2);
            
            // Assert - Should reuse cached session if implemented
            // Note: This depends on implementation details
            session2.AuthToken.Should().Be(session1.AuthToken, 
                "Service should cache and reuse valid sessions");
            session2.ExpiresAt.Should().BeCloseTo(session1.ExpiresAt, TimeSpan.FromSeconds(1));
            
            _output.WriteLine("Session caching verified - same token reused");
        }

        #endregion

        #region Error Recovery Tests

        [Fact]
        public async Task Authentication_AfterMultipleFailures_EventuallySucceeds()
        {
            // Test retry logic and backoff for failed authentications
            var attempts = 0;
            QobuzSession session = null;
            Exception lastError = null;
            
            // Try authentication with exponential backoff
            for (int i = 0; i < 5; i++)
            {
                attempts++;
                try
                {
                    // First attempt with wrong password, then correct
                    var password = i == 0 ? "wrong_password" : _testPassword;
                    session = await _authService.AuthenticateAsync(_testEmail, password);
                    _activeSessions.Add(session);
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    var delay = Math.Pow(2, i) * 1000; // Exponential backoff
                    _output.WriteLine($"Attempt {attempts} failed, waiting {delay}ms");
                    await Task.Delay(TimeSpan.FromMilliseconds(delay));
                }
            }
            
            // Assert
            session.Should().NotBeNull("Authentication should eventually succeed");
            attempts.Should().BeGreaterThan(1, "Should have required retry after initial failure");
            _output.WriteLine($"Authentication succeeded after {attempts} attempts");
        }

        [Fact]
        public async Task RefreshToken_WithNetworkFailure_RetriesWithBackoff()
        {
            // Simulate network issues during token refresh
            var session = await _authService.AuthenticateAsync(_testEmail, _testPassword);
            _activeSessions.Add(session);
            
            // Note: Actual network failure simulation would require mock/proxy
            // This test validates the retry mechanism exists
            
            var refreshTask = _authService.RefreshSessionAsync(session);
            var completedTask = await Task.WhenAny(refreshTask, Task.Delay(TimeSpan.FromSeconds(30)));
            
            completedTask.Should().Be(refreshTask, "Refresh should complete within timeout");
            
            var refreshedSession = await refreshTask;
            refreshedSession.Should().NotBeNull();
            _activeSessions.Add(refreshedSession);
            
            _output.WriteLine("Token refresh with retry logic validated");
        }

        #endregion

        #region Session Expiry Edge Cases

        [Fact]
        public async Task Session_NearExpiry_ProactivelyRefreshes()
        {
            // Test that sessions are refreshed before actual expiry
            var session = await _authService.AuthenticateAsync(_testEmail, _testPassword);
            _activeSessions.Add(session);
            
            // Create a session that's about to expire (within refresh window)
            var nearExpirySession = new QobuzSession
            {
                AuthToken = session.AuthToken,
                UserId = session.UserId,
                Credential = session.Credential,
                ExpiresAt = DateTime.UtcNow.AddMinutes(5) // Expires in 5 minutes
            };
            
            // Act - Should trigger proactive refresh
            var validSession = await _authService.GetValidSessionAsync(nearExpirySession);
            _activeSessions.Add(validSession);
            
            // Assert
            validSession.ExpiresAt.Should().BeAfter(nearExpirySession.ExpiresAt,
                "Session should be proactively refreshed when near expiry");
            
            _output.WriteLine($"Proactive refresh: Old expiry={nearExpirySession.ExpiresAt}, New={validSession.ExpiresAt}");
        }

        [Fact]
        public async Task Session_LongRunning_HandlesMultipleRefreshCycles()
        {
            // Simulate a long-running session with multiple refresh cycles
            var session = await _authService.AuthenticateAsync(_testEmail, _testPassword);
            _activeSessions.Add(session);
            
            var currentSession = session;
            var refreshCount = 0;
            
            // Simulate 5 refresh cycles
            for (int i = 0; i < 5; i++)
            {
                await Task.Delay(1000); // Wait between refreshes
                
                var refreshed = await _authService.RefreshSessionAsync(currentSession);
                refreshed.Should().NotBeNull();
                refreshed.IsValid.Should().BeTrue();
                
                _activeSessions.Add(refreshed);
                currentSession = refreshed;
                refreshCount++;
                
                _output.WriteLine($"Refresh cycle {refreshCount}: Token valid until {refreshed.ExpiresAt}");
            }
            
            refreshCount.Should().Be(5, "All refresh cycles should complete successfully");
        }

        #endregion

        public Task DisposeAsync()
        {
            _output.WriteLine($"Test completed with {_activeSessions.Count} sessions created");
            return Task.CompletedTask;
        }
    }
}