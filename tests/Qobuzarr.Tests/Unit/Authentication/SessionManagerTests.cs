using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using NSubstitute;
using FluentAssertions;
using NzbDrone.Common.Cache;
using NLog;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Exceptions;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Qobuzarr.Tests.Unit.Authentication
{
    public class SessionManagerTests
    {
        private readonly ICacheManager _cacheManager;
        private readonly Logger _logger;
        private readonly SessionManager _sessionManager;
        private readonly ICached<QobuzSession> _sessionCache;

        public SessionManagerTests()
        {
            _cacheManager = Substitute.For<ICacheManager>();
            _logger = Substitute.For<Logger>();
            _sessionCache = Substitute.For<ICached<QobuzSession>>();
            
            _cacheManager.GetCache<QobuzSession>(Arg.Any<Type>(), Arg.Any<string>())
                .Returns(_sessionCache);
                
            _sessionManager = new SessionManager(_cacheManager, _logger);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullCacheManager_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var action = () => new SessionManager(null, _logger);
            action.Should().Throw<ArgumentNullException>()
                .WithParameterName("cacheManager");
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var action = () => new SessionManager(_cacheManager, null);
            action.Should().Throw<ArgumentNullException>()
                .WithParameterName("logger");
        }

        [Fact]
        public void Constructor_WithValidParameters_ShouldInitializeSuccessfully()
        {
            // Act
            var manager = new SessionManager(_cacheManager, _logger);

            // Assert
            manager.Should().NotBeNull();
            _cacheManager.Received(1).GetCache<QobuzSession>(Arg.Any<Type>(), "sessions");
        }

        #endregion

        #region Session Storage Tests

        [Fact]
        public void StoreSession_WithNullSession_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            var action = () => _sessionManager.StoreSession(null);
            action.Should().Throw<ArgumentNullException>()
                .WithParameterName("session");
        }

        [Fact]
        public void StoreSession_WithInvalidSession_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var expiredSession = CreateExpiredSession();

            // Act & Assert
            var action = () => _sessionManager.StoreSession(expiredSession);
            action.Should().Throw<InvalidOperationException>()
                .WithMessage("Cannot store invalid session");
        }

        [Fact]
        public void StoreSession_WithValidSession_ShouldStoreToCacheAndMemory()
        {
            // Arrange
            var session = CreateValidSession();

            // Act
            _sessionManager.StoreSession(session);

            // Assert
            _sessionCache.Received(1).Set(
                "qobuz_current_session",
                session,
                Arg.Any<TimeSpan>());
            
            var retrievedSession = _sessionManager.GetCurrentSession();
            retrievedSession.Should().Be(session);
        }

        [Fact]
        public void StoreSession_WithSessionNeedingRefreshSoon_ShouldTriggerExpiringEvent()
        {
            // Arrange
            var session = CreateSessionExpiringIn(TimeSpan.FromMinutes(15)); // Within 30-minute buffer
            SessionExpiringEventArgs? capturedArgs = null;
            _sessionManager.SessionExpiring += (sender, args) => capturedArgs = args;

            // Act
            _sessionManager.StoreSession(session);

            // Assert
            capturedArgs.Should().NotBeNull();
            capturedArgs.Session.Should().Be(session);
            capturedArgs.RefreshRecommended.Should().BeTrue();
            capturedArgs.TimeRemaining.Should().BeCloseTo(TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void StoreSession_WhenCacheThrowsException_ShouldPropagateException()
        {
            // Arrange
            var session = CreateValidSession();
            _sessionCache.When(x => x.Set(Arg.Any<string>(), Arg.Any<QobuzSession>(), Arg.Any<TimeSpan>()))
                .Do(x => throw new InvalidOperationException("Cache error"));

            // Act & Assert
            var action = () => _sessionManager.StoreSession(session);
            action.Should().Throw<InvalidOperationException>()
                .WithMessage("Cache error");
        }

        #endregion

        #region Session Retrieval Tests

        [Fact]
        public void GetCurrentSession_WhenNoSessionExists_ShouldReturnNull()
        {
            // Arrange
            _sessionCache.Find("qobuz_current_session").Returns((QobuzSession)null);

            // Act
            var result = _sessionManager.GetCurrentSession();

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void GetCurrentSession_WithValidSessionInMemory_ShouldReturnSession()
        {
            // Arrange
            var session = CreateValidSession();
            _sessionManager.StoreSession(session);

            // Act
            var result = _sessionManager.GetCurrentSession();

            // Assert
            result.Should().Be(session);
            // Should not check cache when in-memory session is valid
            _sessionCache.Received(1).Find("qobuz_current_session"); // Only from StoreSession
        }

        [Fact]
        public void GetCurrentSession_WithExpiredSessionInMemory_ShouldCheckCacheAndClear()
        {
            // Arrange
            var expiredSession = CreateExpiredSession();
            _sessionCache.Find("qobuz_current_session").Returns(expiredSession);

            // Act
            var result = _sessionManager.GetCurrentSession(validateExpiration: true);

            // Assert
            result.Should().BeNull();
            _sessionCache.Received(1).Remove("qobuz_current_session");
        }

        [Fact]
        public void GetCurrentSession_WithoutValidation_ShouldReturnExpiredSession()
        {
            // Arrange
            var expiredSession = CreateExpiredSession();
            _sessionCache.Find("qobuz_current_session").Returns(expiredSession);

            // Act
            var result = _sessionManager.GetCurrentSession(validateExpiration: false);

            // Assert
            result.Should().Be(expiredSession);
            _sessionCache.DidNotReceive().Remove("qobuz_current_session");
        }

        [Fact]
        public void GetCurrentSession_WhenCacheThrowsException_ShouldReturnNull()
        {
            // Arrange
            _sessionCache.Find("qobuz_current_session")
                .Returns(x => throw new InvalidOperationException("Cache error"));

            // Act
            var result = _sessionManager.GetCurrentSession();

            // Assert
            result.Should().BeNull();
            _logger.Received(1).Warn(Arg.Any<Exception>(), "Error retrieving session from cache");
        }

        #endregion

        #region Session Clearing Tests

        [Fact]
        public void ClearSession_WithStoredSession_ShouldClearMemoryAndCache()
        {
            // Arrange
            var session = CreateValidSession();
            _sessionManager.StoreSession(session);

            // Act
            _sessionManager.ClearSession();

            // Assert
            _sessionCache.Received(1).Remove("qobuz_current_session");
            var result = _sessionManager.GetCurrentSession();
            result.Should().BeNull();
        }

        [Fact]
        public void ClearSession_WithoutSession_ShouldNotThrow()
        {
            // Act
            var action = () => _sessionManager.ClearSession();

            // Assert
            action.Should().NotThrow();
            _sessionCache.Received(1).Remove("qobuz_current_session");
        }

        [Fact]
        public void ClearSession_WhenCacheThrowsException_ShouldLogError()
        {
            // Arrange
            _sessionCache.When(x => x.Remove(Arg.Any<string>()))
                .Do(x => throw new InvalidOperationException("Cache error"));

            // Act
            _sessionManager.ClearSession();

            // Assert
            _logger.Received(1).Error(Arg.Any<Exception>(), "Error clearing session from cache");
        }

        #endregion

        #region Session Validation Tests

        [Fact]
        public void ValidateSession_WithNoSession_ShouldReturnNoSessionStatus()
        {
            // Arrange
            _sessionCache.Find("qobuz_current_session").Returns((QobuzSession)null);

            // Act
            var result = _sessionManager.ValidateSession();

            // Assert
            result.IsValid.Should().BeFalse();
            result.NeedsRefresh.Should().BeFalse();
            result.Status.Should().Be(SessionStatus.NoSession);
            result.Session.Should().BeNull();
        }

        [Fact]
        public void ValidateSession_WithValidSession_ShouldReturnValidStatus()
        {
            // Arrange
            var session = CreateValidSession();
            _sessionManager.StoreSession(session);

            // Act
            var result = _sessionManager.ValidateSession();

            // Assert
            result.IsValid.Should().BeTrue();
            result.NeedsRefresh.Should().BeFalse();
            result.Status.Should().Be(SessionStatus.Valid);
            result.Session.Should().Be(session);
            result.TimeToExpiry.Should().BePositive();
        }

        [Fact]
        public void ValidateSession_WithExpiredSession_ShouldClearAndReturnExpiredStatus()
        {
            // Arrange
            var expiredSession = CreateExpiredSession();
            _sessionCache.Find("qobuz_current_session").Returns(expiredSession);
            SessionExpiredEventArgs? capturedArgs = null;
            _sessionManager.SessionExpired += (sender, args) => capturedArgs = args;

            // Act
            var result = _sessionManager.ValidateSession();

            // Assert
            result.IsValid.Should().BeFalse();
            result.Status.Should().Be(SessionStatus.Expired);
            _sessionCache.Received(1).Remove("qobuz_current_session");
            capturedArgs.Should().NotBeNull();
            capturedArgs.Session.Should().Be(expiredSession);
        }

        [Fact]
        public void ValidateSession_WithSessionNeedingRefresh_ShouldReturnNeedsRefreshStatus()
        {
            // Arrange
            var session = CreateSessionExpiringIn(TimeSpan.FromMinutes(20));
            _sessionManager.StoreSession(session);

            // Act
            var result = _sessionManager.ValidateSession();

            // Assert
            result.IsValid.Should().BeTrue();
            result.NeedsRefresh.Should().BeTrue();
            result.Status.Should().Be(SessionStatus.NeedsRefresh);
        }

        [Fact]
        public void ValidateSession_WithRecentValidation_ShouldUseCachedResult()
        {
            // Arrange
            var session = CreateValidSession();
            _sessionManager.StoreSession(session);
            
            // First validation
            var firstResult = _sessionManager.ValidateSession();
            
            // Reset cache interactions
            _sessionCache.ClearReceivedCalls();

            // Act - Second validation within cache duration
            var secondResult = _sessionManager.ValidateSession(forceCheck: false);

            // Assert
            secondResult.WasCached.Should().BeTrue();
            secondResult.ValidationTime.Should().Be(firstResult.ValidationTime);
            _sessionCache.DidNotReceive().Find(Arg.Any<string>());
        }

        [Fact]
        public void ValidateSession_WithForceCheck_ShouldBypassCache()
        {
            // Arrange
            var session = CreateValidSession();
            _sessionManager.StoreSession(session);
            _sessionManager.ValidateSession(); // First validation
            
            // Act
            var result = _sessionManager.ValidateSession(forceCheck: true);

            // Assert
            result.WasCached.Should().BeFalse();
        }

        #endregion

        #region Session State Query Tests

        [Fact]
        public void HasValidSession_WithValidSession_ShouldReturnTrue()
        {
            // Arrange
            var session = CreateValidSession();
            _sessionManager.StoreSession(session);

            // Act
            var result = _sessionManager.HasValidSession();

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void HasValidSession_WithExpiredSession_ShouldReturnFalse()
        {
            // Arrange
            var expiredSession = CreateExpiredSession();
            _sessionCache.Find("qobuz_current_session").Returns(expiredSession);

            // Act
            var result = _sessionManager.HasValidSession();

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void SessionNeedsRefresh_WithSessionInRefreshWindow_ShouldReturnTrue()
        {
            // Arrange
            var session = CreateSessionExpiringIn(TimeSpan.FromMinutes(20));
            _sessionManager.StoreSession(session);

            // Act
            var result = _sessionManager.SessionNeedsRefresh();

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void GetTimeToExpiry_WithValidSession_ShouldReturnRemainingTime()
        {
            // Arrange
            var expectedExpiry = TimeSpan.FromHours(20);
            var session = CreateSessionExpiringIn(expectedExpiry);
            _sessionManager.StoreSession(session);

            // Act
            var result = _sessionManager.GetTimeToExpiry();

            // Assert
            result.Should().NotBeNull();
            result.Value.Should().BeCloseTo(expectedExpiry, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void GetTimeToExpiry_WithNoSession_ShouldReturnNull()
        {
            // Arrange
            _sessionCache.Find("qobuz_current_session").Returns((QobuzSession)null);

            // Act
            var result = _sessionManager.GetTimeToExpiry();

            // Assert
            result.Should().BeNull();
        }

        #endregion

        #region Health Monitoring Tests

        [Fact]
        public void GetHealthReport_ShouldReturnComprehensiveReport()
        {
            // Arrange
            var session = CreateValidSession();
            _sessionManager.StoreSession(session);

            // Act
            var report = _sessionManager.GetHealthReport();

            // Assert
            report.Should().NotBeNull();
            report.GeneratedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            report.ServiceUptime.Should().BePositive();
            report.CurrentSession.Should().Be(session);
            report.ValidationResult.Should().NotBeNull();
            report.Metrics.SessionsCreated.Should().Be(1);
            report.Configuration.CacheTtl.Should().Be(TimeSpan.FromHours(24));
            report.Configuration.RefreshBuffer.Should().Be(TimeSpan.FromMinutes(30));
        }

        [Fact]
        public void PerformMaintenance_WithExpiredSession_ShouldCleanup()
        {
            // Arrange
            var expiredSession = CreateExpiredSession();
            _sessionCache.Find("qobuz_current_session").Returns(expiredSession);

            // Act
            _sessionManager.PerformMaintenance();

            // Assert
            _sessionCache.Received(1).Remove("qobuz_current_session");
        }

        [Fact]
        public void PerformMaintenance_WhenExceptionOccurs_ShouldLogError()
        {
            // Arrange
            _sessionCache.Find("qobuz_current_session")
                .Returns(x => throw new InvalidOperationException("Cache error"));

            // Act
            _sessionManager.PerformMaintenance();

            // Assert
            _logger.Received(1).Error(Arg.Any<Exception>(), "Error during session maintenance");
        }

        #endregion

        #region Concurrent Access Tests

        [Fact]
        public async Task ConcurrentSessionAccess_ShouldBeThreadSafe()
        {
            // Arrange
            var sessions = new List<QobuzSession>();
            for (int i = 0; i < 10; i++)
            {
                sessions.Add(CreateValidSession($"user_{i}"));
            }

            var tasks = new List<Task>();
            var exceptions = new ConcurrentBag<Exception>();

            // Act - Concurrent store and retrieve operations
            for (int i = 0; i < 100; i++)
            {
                var sessionIndex = i % sessions.Count;
                var session = sessions[sessionIndex];
                
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        _sessionManager.StoreSession(session);
                        _sessionManager.GetCurrentSession();
                        _sessionManager.ValidateSession();
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            exceptions.Should().BeEmpty();
            var finalSession = _sessionManager.GetCurrentSession();
            finalSession.Should().NotBeNull();
            sessions.Should().Contain(finalSession);
        }

        [Fact]
        public async Task ConcurrentValidation_ShouldUseCachingCorrectly()
        {
            // Arrange
            var session = CreateValidSession();
            _sessionManager.StoreSession(session);
            var validationCount = 0;
            
            // Track actual validation calls
            _sessionManager.SessionValidated += (sender, args) =>
            {
                if (!args.ValidationResult.WasCached)
                    Interlocked.Increment(ref validationCount);
            };

            // Act - Many concurrent validations
            var tasks = new List<Task>();
            for (int i = 0; i < 50; i++)
            {
                tasks.Add(Task.Run(() => _sessionManager.ValidateSession()));
            }

            await Task.WhenAll(tasks);

            // Assert - Most should be cached
            validationCount.Should().BeLessThan(10); // Only a few actual validations
        }

        #endregion

        #region Event Tests

        [Fact]
        public void SessionExpiring_WhenEventHandlerThrows_ShouldLogError()
        {
            // Arrange
            var session = CreateSessionExpiringIn(TimeSpan.FromMinutes(15));
            _sessionManager.SessionExpiring += (sender, args) =>
            {
                throw new InvalidOperationException("Handler error");
            };

            // Act
            var action = () => _sessionManager.StoreSession(session);

            // Assert
            action.Should().NotThrow();
            _logger.Received(1).Error(Arg.Any<Exception>(), "Error in SessionExpiring event handler");
        }

        [Fact]
        public void SessionExpired_ShouldFireWithCorrectArgs()
        {
            // Arrange
            var expiredSession = CreateExpiredSession();
            _sessionCache.Find("qobuz_current_session").Returns(expiredSession);
            SessionExpiredEventArgs? capturedArgs = null;
            _sessionManager.SessionExpired += (sender, args) => capturedArgs = args;

            // Act
            _sessionManager.ValidateSession();

            // Assert
            capturedArgs.Should().NotBeNull();
            capturedArgs.Session.Should().Be(expiredSession);
            capturedArgs.ExpiredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void SessionValidated_ShouldFireOnEachValidation()
        {
            // Arrange
            var session = CreateValidSession();
            _sessionManager.StoreSession(session);
            var validationEvents = new List<SessionValidatedEventArgs>();
            _sessionManager.SessionValidated += (sender, args) => validationEvents.Add(args);

            // Act
            _sessionManager.ValidateSession();
            _sessionManager.ValidateSession(forceCheck: true);

            // Assert
            validationEvents.Should().HaveCountGreaterThanOrEqualTo(2);
            validationEvents.Should().AllSatisfy(e => e.ValidationResult.Should().NotBeNull());
        }

        #endregion

        #region Interface Method Tests

        [Fact]
        public async Task CreateSessionAsync_ShouldStoreAndReturnSession()
        {
            // Arrange
            var credentials = new QobuzCredentials 
            { 
                Username = "test@example.com",
                Password = "password123"
            };

            // Act
            var result = await _sessionManager.CreateSessionAsync(credentials);

            // Assert
            result.Should().NotBeNull();
            result.UserId.Should().Be("dummy_user");
            result.AuthToken.Should().Be("dummy_token");
            var storedSession = _sessionManager.GetCurrentSession();
            storedSession.Should().Be(result);
        }

        [Fact]
        public async Task GetCurrentSessionAsync_ShouldReturnCurrentSession()
        {
            // Arrange
            var session = CreateValidSession();
            _sessionManager.StoreSession(session);

            // Act
            var result = await _sessionManager.GetCurrentSessionAsync();

            // Assert
            result.Should().Be(session);
        }

        [Fact]
        public async Task IsSessionValidAsync_WithValidSession_ShouldReturnTrue()
        {
            // Arrange
            var session = CreateValidSession();

            // Act
            var result = await _sessionManager.IsSessionValidAsync(session);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task InvalidateSessionAsync_ShouldClearSession()
        {
            // Arrange
            var session = CreateValidSession();
            _sessionManager.StoreSession(session);

            // Act
            await _sessionManager.InvalidateSessionAsync();

            // Assert
            var currentSession = _sessionManager.GetCurrentSession();
            currentSession.Should().BeNull();
        }

        [Fact]
        public async Task RefreshSessionAsync_ShouldCreateNewSession()
        {
            // Arrange
            var oldSession = CreateValidSession();
            _sessionManager.StoreSession(oldSession);

            // Act
            var newSession = await _sessionManager.RefreshSessionAsync(oldSession);

            // Assert
            newSession.Should().NotBeNull();
            newSession.UserId.Should().Be(oldSession.UserId);
            newSession.AuthToken.Should().Be($"{oldSession.AuthToken}_refreshed");
            newSession.ExpiresAt.Should().BeAfter(oldSession.ExpiresAt);
        }

        #endregion

        #region Helper Methods

        private QobuzSession CreateValidSession(string userId = "test_user")
        {
            return new QobuzSession
            {
                UserId = userId,
                AuthToken = $"token_{userId}",
                ExpiresAt = DateTime.UtcNow.AddHours(24),
                CreatedAt = DateTime.UtcNow
            };
        }

        private QobuzSession CreateExpiredSession()
        {
            return new QobuzSession
            {
                UserId = "expired_user",
                AuthToken = "expired_token",
                ExpiresAt = DateTime.UtcNow.AddHours(-1),
                CreatedAt = DateTime.UtcNow.AddHours(-25)
            };
        }

        private QobuzSession CreateSessionExpiringIn(TimeSpan timeToExpiry)
        {
            return new QobuzSession
            {
                UserId = "expiring_user",
                AuthToken = "expiring_token",
                ExpiresAt = DateTime.UtcNow.Add(timeToExpiry),
                CreatedAt = DateTime.UtcNow
            };
        }

        #endregion
    }
}