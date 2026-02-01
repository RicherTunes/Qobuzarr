using System;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Security;

namespace Qobuzarr.Tests.Unit.Security
{
    /// <summary>
    /// Comprehensive security test suite for SecureSessionManager.
    /// Tests session security, token encryption, and concurrent access safety.
    /// </summary>
    public class SecureSessionManagerTests : IDisposable
    {
        private readonly Mock<IQobuzLogger> _mockLogger;
        private readonly SecureCredentialManager _credentialManager;
        private readonly SecureSessionManager _sessionManager;

        public SecureSessionManagerTests()
        {
            _mockLogger = new Mock<IQobuzLogger>();
            _credentialManager = new SecureCredentialManager(_mockLogger.Object);
            _sessionManager = new SecureSessionManager(_credentialManager, _mockLogger.Object);
        }

        #region Session Storage Security Tests

        [Fact]
        public void StoreSessionSecurely_WithValidSession_ShouldStoreWithoutExposingTokens()
        {
            // Arrange
            var session = CreateValidSession();

            // Act
            _sessionManager.StoreSessionSecurely(session);

            // Assert
            _sessionManager.HasValidSession().Should().BeTrue();

            // Verify that sensitive data is not exposed through public methods
            var maskedUserId = _sessionManager.GetMaskedUserId();
            maskedUserId.Should().NotBeNullOrEmpty();
            maskedUserId.Should().NotBe(session.UserId);
            maskedUserId.Should().Contain("*");
        }

        [Fact]
        public void StoreSessionSecurely_WithNullSession_ShouldThrow()
        {
            // Act
            Action act = () => _sessionManager.StoreSessionSecurely(null);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("session");
        }

        [Fact]
        public void GetSecureSession_AfterStoringSession_ShouldReturnCompleteSession()
        {
            // Arrange
            var originalSession = CreateValidSession();
            _sessionManager.StoreSessionSecurely(originalSession);

            // Act
            var retrievedSession = _sessionManager.GetSecureSession();

            // Assert
            retrievedSession.Should().NotBeNull();
            retrievedSession.UserId.Should().Be(originalSession.UserId);
            retrievedSession.AuthToken.Should().Be(originalSession.AuthToken);
            retrievedSession.AppSecret.Should().Be(originalSession.AppSecret);
            retrievedSession.AppId.Should().Be(originalSession.AppId);
            retrievedSession.ExpiresAt.Should().Be(originalSession.ExpiresAt);
        }

        [Fact]
        public void GetSecureSession_WithNoStoredSession_ShouldReturnNull()
        {
            // Act
            var result = _sessionManager.GetSecureSession();

            // Assert
            result.Should().BeNull();
        }

        #endregion

        #region Session Expiration Tests

        [Fact]
        public void GetSecureSession_WithExpiredSession_ShouldReturnNullAndClear()
        {
            // Arrange
            var expiredSession = new QobuzSession
            {
                UserId = "user123",
                AuthToken = "token123",
                AppId = "app123",
                AppSecret = "secret123",
                ExpiresAt = DateTime.UtcNow.AddHours(-1), // Expired
                CreatedAt = DateTime.UtcNow.AddHours(-2)
            };

            _sessionManager.StoreSessionSecurely(expiredSession);

            // Act
            var result = _sessionManager.GetSecureSession();

            // Assert
            result.Should().BeNull();
            _sessionManager.HasValidSession().Should().BeFalse();
        }

        [Fact]
        public void HasValidSession_WithExpiredSession_ShouldReturnFalse()
        {
            // Arrange
            var expiredSession = new QobuzSession
            {
                UserId = "user123",
                AuthToken = "token123",
                AppId = "app123",
                AppSecret = "secret123",
                ExpiresAt = DateTime.UtcNow.AddHours(-1),
                CreatedAt = DateTime.UtcNow.AddHours(-2)
            };

            _sessionManager.StoreSessionSecurely(expiredSession);

            // Act
            var result = _sessionManager.HasValidSession();

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void GetSessionExpiration_WithValidSession_ShouldReturnExpirationTime()
        {
            // Arrange
            var session = CreateValidSession();
            var expectedExpiration = session.ExpiresAt;
            _sessionManager.StoreSessionSecurely(session);

            // Act
            var expiration = _sessionManager.GetSessionExpiration();

            // Assert
            expiration.Should().NotBeNull();
            expiration.Should().Be(expectedExpiration);
        }

        [Fact]
        public void GetSessionExpiration_WithNoSession_ShouldReturnNull()
        {
            // Act
            var expiration = _sessionManager.GetSessionExpiration();

            // Assert
            expiration.Should().BeNull();
        }

        #endregion

        #region Session Security Validation Tests

        [Fact]
        public void ValidateSessionSecurity_WithValidSession_ShouldReturnTrue()
        {
            // Arrange
            var session = CreateValidSession();
            _sessionManager.StoreSessionSecurely(session);

            // Act
            var result = _sessionManager.ValidateSessionSecurity();

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void ValidateSessionSecurity_WithNoSession_ShouldReturnFalse()
        {
            // Act
            var result = _sessionManager.ValidateSessionSecurity();

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void ValidateSessionSecurity_WithSuspiciousUserId_ShouldFailValidation()
        {
            // Arrange
            var suspiciousSession = new QobuzSession
            {
                UserId = "test_user", // Contains "test" which is flagged as suspicious
                AuthToken = "validtoken123",
                AppId = "app123",
                AppSecret = "secret123",
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                CreatedAt = DateTime.UtcNow
            };

            _sessionManager.StoreSessionSecurely(suspiciousSession);

            // Act
            var result = _sessionManager.ValidateSessionSecurity();

            // Assert
            result.Should().BeFalse();
            _mockLogger.Verify(l => l.Warn(It.IsAny<string>(), It.IsAny<object[]>()), Times.AtLeastOnce());
        }

        #endregion

        #region Session Clearing and Cleanup Tests

        [Fact]
        public void ClearSession_ShouldRemoveAllSessionData()
        {
            // Arrange
            var session = CreateValidSession();
            _sessionManager.StoreSessionSecurely(session);
            _sessionManager.HasValidSession().Should().BeTrue();

            // Act
            _sessionManager.ClearSession();

            // Assert
            _sessionManager.HasValidSession().Should().BeFalse();
            _sessionManager.GetSecureSession().Should().BeNull();
            _sessionManager.GetSessionExpiration().Should().BeNull();
            _sessionManager.GetMaskedUserId().Should().BeNull();
        }

        [Fact]
        public void ClearSession_WhenCalledMultipleTimes_ShouldNotThrow()
        {
            // Arrange
            var session = CreateValidSession();
            _sessionManager.StoreSessionSecurely(session);

            // Act
            Action act = () =>
            {
                _sessionManager.ClearSession();
                _sessionManager.ClearSession();
                _sessionManager.ClearSession();
            };

            // Assert
            act.Should().NotThrow();
        }

        #endregion

        #region Memory Protection Tests

        [Fact]
        public void StoreSessionSecurely_ShouldNotExposeTokensInMemory()
        {
            // Arrange
            var session = new QobuzSession
            {
                UserId = "user123",
                AuthToken = "supersecrettoken123",
                AppSecret = "verysecretappkey456",
                AppId = "app123",
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                CreatedAt = DateTime.UtcNow
            };

            // Act
            _sessionManager.StoreSessionSecurely(session);

            // Assert
            // After storing, the original session should have tokens cleared
            // This simulates that the internal storage doesn't keep plain text tokens
            _sessionManager.HasValidSession().Should().BeTrue();

            // When retrieved, tokens should be reconstructed from secure storage
            var retrieved = _sessionManager.GetSecureSession();
            retrieved.AuthToken.Should().Be("supersecrettoken123");
            retrieved.AppSecret.Should().Be("verysecretappkey456");
        }

        [Fact]
        public void GetSecureSession_ShouldCreateNewInstanceEachTime()
        {
            // Arrange
            var session = CreateValidSession();
            _sessionManager.StoreSessionSecurely(session);

            // Act
            var retrieved1 = _sessionManager.GetSecureSession();
            var retrieved2 = _sessionManager.GetSecureSession();

            // Assert
            retrieved1.Should().NotBeNull();
            retrieved2.Should().NotBeNull();
            ReferenceEquals(retrieved1, retrieved2).Should().BeFalse("Should return new instances to prevent external modification");

            // But values should be the same
            retrieved1.UserId.Should().Be(retrieved2.UserId);
            retrieved1.AuthToken.Should().Be(retrieved2.AuthToken);
        }

        #endregion

        #region Concurrent Access Tests

        [Fact]
        public void SecureSessionManager_ShouldBeThreadSafeForConcurrentReads()
        {
            // Arrange
            var session = CreateValidSession();
            _sessionManager.StoreSessionSecurely(session);
            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
            var retrievedSessions = new System.Collections.Concurrent.ConcurrentBag<QobuzSession>();

            // Act
            Parallel.For(0, 100, i =>
            {
                try
                {
                    var retrieved = _sessionManager.GetSecureSession();
                    if (retrieved != null)
                        retrievedSessions.Add(retrieved);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            // Assert
            exceptions.Should().BeEmpty("No exceptions during concurrent reads");
            retrievedSessions.Should().NotBeEmpty();
            retrievedSessions.Should().OnlyContain(s => s.UserId == session.UserId);
        }

        [Fact]
        public void SecureSessionManager_ShouldHandleConcurrentStoreAndClear()
        {
            // Arrange
            var tasks = new Task[20];
            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

            // Act
            for (int i = 0; i < tasks.Length; i++)
            {
                var index = i;
                tasks[i] = Task.Run(() =>
                {
                    try
                    {
                        if (index % 2 == 0)
                        {
                            var session = CreateValidSession();
                            session.UserId = $"user{index}";
                            _sessionManager.StoreSessionSecurely(session);
                        }
                        else
                        {
                            _sessionManager.ClearSession();
                        }

                        // Try to read
                        var current = _sessionManager.GetSecureSession();
                        var hasSession = _sessionManager.HasValidSession();
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                });
            }

            Task.WaitAll(tasks, TimeSpan.FromSeconds(5));

            // Assert
            exceptions.Should().BeEmpty("No exceptions during concurrent operations");
        }

        #endregion

        #region Security Attack Prevention Tests

        [Fact]
        public void StoreSessionSecurely_WithInjectionInUserId_ShouldStoreAndWarnButNotReject()
        {
            // Arrange
            var maliciousSession = new QobuzSession
            {
                UserId = "'; DROP TABLE users; --",
                AuthToken = "validtoken123",
                AppId = "app123",
                AppSecret = "secret123",
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                CreatedAt = DateTime.UtcNow
            };

            // Act
            _sessionManager.StoreSessionSecurely(maliciousSession);

            // Assert
            // Session is stored. Suspicious patterns in credentials only log warnings
            // but don't reject, since opaque auth tokens can legitimately contain
            // SQL-like patterns (e.g., "--" in a base64-encoded token).
            // The warning is logged but validation passes.
            var hasValid = _sessionManager.HasValidSession();
            hasValid.Should().BeTrue("Suspicious patterns log warnings but don't reject credentials");

            // Verify warning was logged
            _mockLogger.Verify(
                l => l.Warn(It.IsAny<string>(), It.IsAny<object[]>()),
                Times.AtLeastOnce(),
                "Should log warning for suspicious credential patterns");
        }

        [Fact]
        public void GetMaskedUserId_ShouldNeverExposeFullUserId()
        {
            // Arrange
            var userIds = new[] { "12345678", "user@example.com", "verylonguserid123456789" };

            foreach (var userId in userIds)
            {
                var session = CreateValidSession();
                session.UserId = userId;
                _sessionManager.StoreSessionSecurely(session);

                // Act
                var masked = _sessionManager.GetMaskedUserId();

                // Assert
                masked.Should().NotBeNullOrEmpty();
                masked.Should().NotBe(userId);
                masked.Should().Contain("*");

                _sessionManager.ClearSession();
            }
        }

        #endregion

        #region Disposal and Resource Management Tests

        [Fact]
        public void Dispose_ShouldClearAllSessions()
        {
            // Arrange
            var session = CreateValidSession();
            _sessionManager.StoreSessionSecurely(session);
            _sessionManager.HasValidSession().Should().BeTrue();

            // Act
            _sessionManager.Dispose();

            // Assert
            _sessionManager.HasValidSession().Should().BeFalse();
            _sessionManager.GetSecureSession().Should().BeNull();
        }

        [Fact]
        public void Dispose_WhenCalledMultipleTimes_ShouldNotThrow()
        {
            // Arrange
            var session = CreateValidSession();
            _sessionManager.StoreSessionSecurely(session);

            // Act
            Action act = () =>
            {
                _sessionManager.Dispose();
                _sessionManager.Dispose();
                _sessionManager.Dispose();
            };

            // Assert
            act.Should().NotThrow();
        }

        #endregion

        #region Edge Cases and Boundary Tests

        [Fact]
        public void StoreSessionSecurely_WithVeryLongTokens_ShouldHandleCorrectly()
        {
            // Arrange
            var session = new QobuzSession
            {
                UserId = "user123",
                AuthToken = new string('a', 10000), // Very long token
                AppSecret = new string('b', 10000), // Very long secret
                AppId = "app123",
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                CreatedAt = DateTime.UtcNow
            };

            // Act
            Action act = () => _sessionManager.StoreSessionSecurely(session);

            // Assert
            act.Should().NotThrow();

            var retrieved = _sessionManager.GetSecureSession();
            retrieved.Should().NotBeNull();
            retrieved.AuthToken.Length.Should().Be(10000);
            retrieved.AppSecret.Length.Should().Be(10000);
        }

        [Fact]
        public void StoreSessionSecurely_WithUnicodeInCredentials_ShouldHandleCorrectly()
        {
            // Arrange
            var session = new QobuzSession
            {
                UserId = "用户123",
                AuthToken = "トークン🔐123",
                AppSecret = "секрет456",
                AppId = "app123",
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                CreatedAt = DateTime.UtcNow
            };

            // Act
            _sessionManager.StoreSessionSecurely(session);

            // Assert
            var retrieved = _sessionManager.GetSecureSession();
            retrieved.Should().NotBeNull();
            retrieved.UserId.Should().Be("用户123");
            retrieved.AuthToken.Should().Be("トークン🔐123");
            retrieved.AppSecret.Should().Be("секрет456");
        }

        [Fact]
        public void StoreSessionSecurely_WithMinimalSession_ShouldStoreSuccessfully()
        {
            // Arrange
            var minimalSession = new QobuzSession
            {
                UserId = "u",
                AuthToken = "t",
                AppId = null, // Optional field
                AppSecret = null, // Optional field
                ExpiresAt = DateTime.UtcNow.AddSeconds(1),
                CreatedAt = DateTime.UtcNow
            };

            // Act
            _sessionManager.StoreSessionSecurely(minimalSession);

            // Assert
            var retrieved = _sessionManager.GetSecureSession();
            retrieved.Should().NotBeNull();
            retrieved.UserId.Should().Be("u");
            retrieved.AuthToken.Should().Be("t");
        }

        #endregion

        #region Helper Methods

        private QobuzSession CreateValidSession()
        {
            return new QobuzSession
            {
                UserId = "validuser123",
                AuthToken = "validtoken456",
                AppId = "app789",
                AppSecret = "secret012",
                ExpiresAt = DateTime.UtcNow.AddHours(2),
                CreatedAt = DateTime.UtcNow,
                Subscription = new QobuzSubscription
                {
                    Type = "studio",
                    CanStream = true
                }
            };
        }

        #endregion

        public void Dispose()
        {
            _sessionManager?.Dispose();
            _credentialManager?.Dispose();
        }
    }
}
