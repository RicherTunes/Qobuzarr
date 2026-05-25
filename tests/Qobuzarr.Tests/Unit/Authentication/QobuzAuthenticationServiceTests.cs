using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NzbDrone.Common.Http;
using Xunit;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Qobuzarr.Tests.Fixtures;
using Qobuzarr.Tests.TestData;

namespace Qobuzarr.Tests.Unit.Authentication
{
    // Each test instance gets its OWN session file path via the internal constructor
    // (test seam added May 2026 to fix the known-flaky race documented in CLAUDE.md —
    // two test classes used to share QobuzAuthenticationService's default _persistentStore
    // path). [Collection] is retained as belt-and-suspenders, but per-instance file isolation
    // is now the primary defense.
    [Xunit.Collection(Qobuzarr.Tests.Collections.AuthenticationTestCollection.Name)]
    public class QobuzAuthenticationServiceTests : TestFixtureBase
    {
        private readonly QobuzAuthenticationService _authService;
        private readonly string _sessionFilePath;

        public QobuzAuthenticationServiceTests()
        {
            _sessionFilePath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"qobuzarr-test-{Guid.NewGuid():N}.session.json");
            _authService = new QobuzAuthenticationService(
                MockHttpClient.Object,
                MockConfigService.Object,
                MockLocalizationService.Object,
                MockCacheManager,
                MockLogger.Object,
                credentialValidator: null,
                sessionFilePath: _sessionFilePath);
        }

        public override void Dispose()
        {
            try { if (System.IO.File.Exists(_sessionFilePath)) System.IO.File.Delete(_sessionFilePath); }
            catch { /* test cleanup is best-effort */ }
            base.Dispose();
        }

        [Fact]
        public void AuthenticateAsync_WithValidCredentials_ShouldRequireRealApiIntegration()
        {
            // This test would require real API integration
            // For unit testing, we focus on validation and caching logic
            var credentials = new QobuzCredentials
            {
                Email = "test@example.com",
                MD5Password = "testpassword"
                // AppId is optional - will use defaults if not provided
            };

            credentials.IsValid().Should().BeTrue();
            credentials.IsEmailAuth().Should().BeTrue();
        }

        [Fact]
        public void AuthenticateAsync_WithTokenCredentials_ShouldValidateCorrectly()
        {
            // Arrange - test token authentication validation
            var credentials = new QobuzCredentials
            {
                UserId = "12345678",
                AuthToken = "sample_auth_token_123456"
                // AppId is optional - will use defaults if not provided
            };

            // Act & Assert
            credentials.IsValid().Should().BeTrue();
            credentials.IsTokenAuth().Should().BeTrue();
            credentials.IsEmailAuth().Should().BeFalse();
        }

        [Theory]
        [InlineData("", "password", false)] // Empty email
        [InlineData("invalid-email", "password", false)] // Invalid email format
        [InlineData("test@example.com", "", false)] // Empty password
        [InlineData("", "", false)] // No credentials
        public void ValidateCredentials_WithInvalidData_ShouldReturnFalse(string email, string password, bool expectedValid)
        {
            // Arrange
            var credentials = new QobuzCredentials
            {
                Email = email,
                MD5Password = password
                // AppId is optional
            };

            // Act & Assert
            var result = credentials.IsValid();
            result.Should().Be(expectedValid);
        }

        [Fact]
        public void ValidateCredentials_WithValidEmailPassword_ShouldNotThrow()
        {
            // Arrange
            var credentials = new QobuzCredentials
            {
                Email = "test@example.com",
                MD5Password = "password123"
                // AppId is optional
            };

            // Act & Assert
            credentials.IsValid().Should().BeTrue();
        }

        [Fact]
        public void ValidateCredentials_WithValidTokenData_ShouldNotThrow()
        {
            // Arrange
            var credentials = new QobuzCredentials
            {
                UserId = "12345678",
                AuthToken = "valid_token_123"
                // AppId is optional
            };

            // Act & Assert
            credentials.IsValid().Should().BeTrue();
        }

        [Fact]
        public void GetCachedSession_WithValidSession_ShouldReturnSession()
        {
            // Arrange
            var session = new QobuzSession
            {
                UserId = "12345678",
                AuthToken = "token123",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };

            _authService.StoreSession(session);

            // Act
            var result = _authService.GetCachedSession();

            // Assert
            result.Should().NotBeNull();
            result.UserId.Should().Be("12345678");
            result.AuthToken.Should().Be("token123");
        }

        [Fact]
        public void GetCachedSession_WithExpiredSession_ShouldReturnNull()
        {
            // Arrange
            var expiredSession = new QobuzSession
            {
                UserId = "12345678",
                AuthToken = "token123",
                ExpiresAt = DateTime.UtcNow.AddHours(-1) // Expired
            };

            _authService.StoreSession(expiredSession);

            // Act
            var result = _authService.GetCachedSession();

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void ClearCachedSession_ShouldRemoveSession()
        {
            // Arrange
            var session = new QobuzSession
            {
                UserId = "12345678",
                AuthToken = "token123",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };

            _authService.StoreSession(session);
            _authService.GetCachedSession().Should().NotBeNull();

            // Act
            _authService.ClearSession();

            // Assert
            _authService.GetCachedSession().Should().BeNull();
        }

        [Fact]
        public void HashPassword_WithValidPassword_ShouldReturnMD5Hash()
        {
            // Arrange
            var password = "testpassword";
            var expectedHash = "e16b2ab8d12314bf4efbd6203906ea6c"; // MD5 of "testpassword"

            // Act
            var result = QobuzAuthenticationService.HashPassword(password);

            // Assert
            result.Should().Be(expectedHash);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void HashPassword_WithInvalidPassword_ShouldReturnEmptyString(string password)
        {
            // Act
            var result = QobuzAuthenticationService.HashPassword(password);

            // Assert
            result.Should().BeEmpty();
        }
    }
}
