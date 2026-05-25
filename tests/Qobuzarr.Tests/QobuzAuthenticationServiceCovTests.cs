using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NzbDrone.Common.Http;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Services.Interfaces;
using Qobuzarr.Tests.Fixtures;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Coverage tests for QobuzAuthenticationService focusing on uncovered code paths.
    /// Tests: token provider methods, credential validation, authentication flows, and edge cases.
    /// </summary>
    // Each test instance gets its OWN session file path via the internal constructor
    // (test seam added May 2026 to fix the known-flaky race documented in CLAUDE.md —
    // two test classes used to share QobuzAuthenticationService's default _persistentStore
    // path). [Collection] is retained as belt-and-suspenders, but per-instance file isolation
    // is now the primary defense.
    [Xunit.Collection(Qobuzarr.Tests.Collections.AuthenticationTestCollection.Name)]
    public class QobuzAuthenticationServiceCovTests : TestFixtureBase
    {
        private readonly QobuzAuthenticationService _authService;
        private readonly string _sessionFilePath;

        public QobuzAuthenticationServiceCovTests()
        {
            _sessionFilePath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"qobuzarr-test-{Guid.NewGuid():N}.session.json");
            _authService = CreateAuthService();
        }

        public override void Dispose()
        {
            try { if (System.IO.File.Exists(_sessionFilePath)) System.IO.File.Delete(_sessionFilePath); }
            catch { /* test cleanup is best-effort */ }
            base.Dispose();
        }

        /// <summary>
        /// Creates a QobuzAuthenticationService with a permissive mock ICredentialValidator
        /// that passes all credentials through, allowing tests to focus on downstream behavior.
        /// Uses the internal test-seam constructor with a per-instance session file path.
        /// </summary>
        private QobuzAuthenticationService CreateAuthService()
        {
            var mockValidator = new Mock<ICredentialValidator>();
            var validResult = new CredentialValidationResult(); // IsValid = true (no errors)
            mockValidator.Setup(x => x.ValidateCredentials(It.IsAny<QobuzCredentials>()))
                .Returns(validResult);

            return new QobuzAuthenticationService(
                MockHttpClient.Object,
                MockConfigService.Object,
                MockLocalizationService.Object,
                MockCacheManager,
                MockLogger.Object,
                mockValidator.Object,
                sessionFilePath: _sessionFilePath);
        }

        #region GetAccessTokenAsync Tests (Source lines 170-177)

        [Fact]
        public async Task GetAccessTokenAsync_NoSession_ReturnsNull()
        {
            // Source line 171: if (session == null) return null;
            _authService.ClearSession();

            var result = await _authService.GetAccessTokenAsync();

            result.Should().BeNull();
        }

        [Fact]
        public async Task GetAccessTokenAsync_SessionCachedButValidationFails_ReturnsNull()
        {
            // Source line 173: return valid ? session.AuthToken : null;
            var session = new QobuzSession
            {
                UserId = "123456",
                AuthToken = "expected_token",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };
            _authService.StoreSession(session);

            // ValidateSessionAsync makes HTTP call which fails in test env, returns false
            var result = await _authService.GetAccessTokenAsync();

            result.Should().BeNull();
        }

        #endregion

        #region RefreshTokenAsync Tests (Source line 178)

        [Fact]
        public async Task RefreshTokenAsync_ReturnsNull()
        {
            // Source line 179: return Task.FromResult<string>(null);
            var result = await _authService.RefreshTokenAsync();

            result.Should().BeNull();
        }

        #endregion

        #region ValidateTokenAsync Tests (Source lines 182-189)

        [Fact]
        public async Task ValidateTokenAsync_NullToken_ReturnsFalse()
        {
            // Source line 183: if (string.IsNullOrWhiteSpace(token)) return false;
            var result = await _authService.ValidateTokenAsync(null);
            result.Should().BeFalse();
        }

        [Fact]
        public async Task ValidateTokenAsync_EmptyToken_ReturnsFalse()
        {
            // Source line 183
            var result = await _authService.ValidateTokenAsync(string.Empty);
            result.Should().BeFalse();
        }

        [Fact]
        public async Task ValidateTokenAsync_WhitespaceToken_ReturnsFalse()
        {
            // Source line 183
            var result = await _authService.ValidateTokenAsync("   ");
            result.Should().BeFalse();
        }

        [Fact]
        public async Task ValidateTokenAsync_NoCachedSession_ReturnsFalse()
        {
            // Source line 185: if (session == null) return false;
            _authService.ClearSession();

            var result = await _authService.ValidateTokenAsync("some_token");
            result.Should().BeFalse();
        }

        [Fact]
        public async Task ValidateTokenAsync_TokenMismatch_ReturnsFalse()
        {
            // Source line 186: if (!string.Equals(session.AuthToken, token, Ordinal)) return false;
            var session = new QobuzSession
            {
                UserId = "123456",
                AuthToken = "stored_token",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };
            _authService.StoreSession(session);

            var result = await _authService.ValidateTokenAsync("different_token");
            result.Should().BeFalse();
        }

        [Fact]
        public async Task ValidateTokenAsync_TokenMatchValidationFails_ReturnsFalse()
        {
            // Source line 187: return await ValidateSessionAsync(session);
            var session = new QobuzSession
            {
                UserId = "123456",
                AuthToken = "test_token",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };
            _authService.StoreSession(session);

            var result = await _authService.ValidateTokenAsync("test_token");
            result.Should().BeFalse();
        }

        #endregion

        #region GetTokenExpiration Tests (Source lines 191-197)

        [Fact]
        public void GetTokenExpiration_NoSession_ReturnsNull()
        {
            // Source line 192: if (session == null) return null;
            _authService.ClearSession();

            var result = _authService.GetTokenExpiration("any_token");
            result.Should().BeNull();
        }

        [Fact]
        public void GetTokenExpiration_TokenMismatch_ReturnsNull()
        {
            // Source line 193: if (!string.Equals(session.AuthToken, token, Ordinal)) return null;
            var expiresAt = DateTime.UtcNow.AddHours(2);
            var session = new QobuzSession
            {
                UserId = "123456",
                AuthToken = "stored_token",
                ExpiresAt = expiresAt
            };
            _authService.StoreSession(session);

            var result = _authService.GetTokenExpiration("different_token");
            result.Should().BeNull();
        }

        [Fact]
        public void GetTokenExpiration_TokenMatch_ReturnsExactExpiration()
        {
            // Source line 194: return session.ExpiresAt;
            var expectedExpiration = DateTime.UtcNow.AddHours(3);
            var session = new QobuzSession
            {
                UserId = "123456",
                AuthToken = "test_token",
                ExpiresAt = expectedExpiration
            };
            _authService.StoreSession(session);

            var result = _authService.GetTokenExpiration("test_token");
            result.Should().Be(expectedExpiration);
        }

        #endregion

        #region ClearAuthenticationCache Tests (Source line 202)

        [Fact]
        public void ClearAuthenticationCache_ClearsStoredSession()
        {
            // Source line 202: public void ClearAuthenticationCache() => ClearSession();
            var session = new QobuzSession
            {
                UserId = "123456",
                AuthToken = "test_token",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };
            _authService.StoreSession(session);
            _authService.GetCachedSession().Should().NotBeNull();

            _authService.ClearAuthenticationCache();

            _authService.GetCachedSession().Should().BeNull();
        }

        #endregion

        #region SupportsRefresh and ServiceName Tests (Source lines 203-204)

        [Fact]
        public void SupportsRefresh_ShouldBeFalse()
        {
            // Source line 203: public bool SupportsRefresh => false;
            _authService.SupportsRefresh.Should().BeFalse();
        }

        [Fact]
        public void ServiceName_ShouldBeQobuz()
        {
            // Source line 204: public string ServiceName => "Qobuz";
            _authService.ServiceName.Should().Be("Qobuz");
        }

        #endregion

        #region RefreshSessionAsync(string) Tests (Source line 387)

        [Fact]
        public async Task RefreshSessionAsync_ThrowsNotSupportedException()
        {
            // Source line 387: throw new NotSupportedException("Qobuz does not support session refresh...")
            var exception = await Assert.ThrowsAsync<NotSupportedException>(
                async () => await _authService.RefreshSessionAsync("some_refresh_token"));

            exception.Message.Should().Contain("Qobuz does not support session refresh");
            exception.Message.Should().Contain("Re-authentication is required");
        }

        #endregion

        #region ValidateSessionAsync Tests (Source lines 389-416)

        [Fact]
        public async Task ValidateSessionAsync_NullSession_ReturnsFalse()
        {
            // Source line 393: if (session == null || !session.IsValid()) return false;
            var result = await _authService.ValidateSessionAsync(null);
            result.Should().BeFalse();
        }

        [Fact]
        public async Task ValidateSessionAsync_EmptyUserId_ReturnsFalse()
        {
            // Source line 393: IsValid checks !string.IsNullOrEmpty(UserId)
            var invalidSession = new QobuzSession
            {
                UserId = "",
                AuthToken = "token",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };

            var result = await _authService.ValidateSessionAsync(invalidSession);
            result.Should().BeFalse();
        }

        [Fact]
        public async Task ValidateSessionAsync_ExpiredSession_ReturnsFalse()
        {
            // Source line 393: IsValid checks DateTime.UtcNow < ExpiresAt
            var expiredSession = new QobuzSession
            {
                UserId = "123456",
                AuthToken = "token",
                ExpiresAt = DateTime.UtcNow.AddHours(-1)
            };

            var result = await _authService.ValidateSessionAsync(expiredSession);
            result.Should().BeFalse();
        }

        #endregion

        #region StoreSession Null/Invalid Tests (Source line 348)

        [Fact]
        public void StoreSession_NullSession_RemovesFromCache()
        {
            // Source line 348: if (session == null || !session.IsValid()) { Remove(...); return; }
            var session = new QobuzSession
            {
                UserId = "123456",
                AuthToken = "token",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };
            _authService.StoreSession(session);
            _authService.GetCachedSession().Should().NotBeNull();

            _authService.StoreSession(null);

            _authService.GetCachedSession().Should().BeNull();
        }

        [Fact]
        public void StoreSession_InvalidSession_RemovesFromCache()
        {
            // Source line 348: invalid session triggers Remove instead of Set
            var validSession = new QobuzSession
            {
                UserId = "123456",
                AuthToken = "token",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };
            _authService.StoreSession(validSession);
            _authService.GetCachedSession().Should().NotBeNull();

            var invalidSession = new QobuzSession
            {
                UserId = "",  // Empty makes IsValid() return false
                AuthToken = "token",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };
            _authService.StoreSession(invalidSession);

            _authService.GetCachedSession().Should().BeNull();
        }

        #endregion

        #region AuthenticateAsync Credential Validation Failure (Source line 215)

        [Fact]
        public async Task AuthenticateAsync_InvalidCredentials_ThrowsQobuzAuthenticationException()
        {
            // Source line 215: throw new QobuzAuthenticationException($"Credential validation failed: {errorMessage}");
            var mockValidator = new Mock<ICredentialValidator>();
            var invalidResult = new CredentialValidationResult();
            invalidResult.AddError("Email format is invalid");
            invalidResult.AddError("Password is required");
            mockValidator.Setup(x => x.ValidateCredentials(It.IsAny<QobuzCredentials>()))
                .Returns(invalidResult);

            var service = new QobuzAuthenticationService(
                MockHttpClient.Object, MockConfigService.Object,
                MockLocalizationService.Object, MockCacheManager,
                MockLogger.Object, mockValidator.Object);

            var credentials = new QobuzCredentials { Email = "bad", MD5Password = "" };

            var exception = await Assert.ThrowsAsync<QobuzAuthenticationException>(
                async () => await service.AuthenticateAsync(credentials));

            exception.Message.Should().Contain("Credential validation failed");
            exception.Message.Should().Contain("Email format is invalid");
            exception.Message.Should().Contain("Password is required");
        }

        #endregion

        #region AuthenticateAsync No Valid Auth Method (Source line 256)

        [Fact]
        public async Task AuthenticateAsync_NoAuthMethod_ThrowsInvalidOperationException()
        {
            // Source line 256: throw new InvalidOperationException("No valid authentication method provided");
            var mockValidator = new Mock<ICredentialValidator>();
            mockValidator.Setup(x => x.ValidateCredentials(It.IsAny<QobuzCredentials>()))
                .Returns(new CredentialValidationResult()); // IsValid = true

            var service = new QobuzAuthenticationService(
                MockHttpClient.Object, MockConfigService.Object,
                MockLocalizationService.Object, MockCacheManager,
                MockLogger.Object, mockValidator.Object);

            var credentials = new QobuzCredentials
            {
                Email = "", MD5Password = "", UserId = "", AuthToken = ""
            };

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await service.AuthenticateAsync(credentials));

            // Message now includes remediation hints (Email + Password vs token); the
            // load-bearing contract is the start-of-message + that the exception type
            // is InvalidOperationException for "no valid input shape provided".
            exception.Message.Should().StartWith("No valid authentication method provided");
        }

        #endregion

        #region AuthenticateWithEmailAsync HTTP Error (Source line 316)

        [Fact]
        public async Task AuthenticateWithEmailAsync_HttpError_ThrowsHttpException()
        {
            // Source line 316: throw new HttpException(request, response);
            var credentials = new QobuzCredentials
            {
                Email = "test@example.com",
                MD5Password = "hashed_password",
                AppId = "test_app_id",
                AppSecret = "test_app_secret"
            };

            var errorResponse = new HttpResponse(
                new HttpRequest("https://api.qobuz.com/user/login"),
                new HttpHeader(),
                "{\"error\": \"Unauthorized\"}",
                HttpStatusCode.Unauthorized);

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(errorResponse);

            var exception = await Assert.ThrowsAsync<HttpException>(
                async () => await _authService.AuthenticateAsync(credentials));

            exception.Response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        #endregion

        #region AuthenticateWithEmailAsync Login Failure (Source line 323)

        [Fact]
        public async Task AuthenticateWithEmailAsync_LoginFailed_ThrowsInvalidOperationException()
        {
            // Source line 323: throw new InvalidOperationException($"Authentication failed: {loginResponse.Message}");
            var credentials = new QobuzCredentials
            {
                Email = "test@example.com",
                MD5Password = "wrong_password",
                AppId = "test_app_id",
                AppSecret = "test_app_secret"
            };

            var loginResponse = new QobuzLoginResponse
            {
                User = null,
                UserAuthToken = null,
                Status = "error",
                Code = 401,
                Message = "Invalid credentials"
            };

            var httpResponse = new HttpResponse(
                new HttpRequest("https://api.qobuz.com/user/login"),
                new HttpHeader(),
                JsonConvert.SerializeObject(loginResponse),
                HttpStatusCode.OK);

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(httpResponse);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await _authService.AuthenticateAsync(credentials));

            exception.Message.Should().Be("Authentication failed: Invalid credentials");
        }

        #endregion

        #region AuthenticateWithTokenAsync Invalid Token (Source line 377)

        [Fact]
        public async Task AuthenticateWithTokenAsync_InvalidToken_ThrowsInvalidOperationException()
        {
            // Source line 377: throw new InvalidOperationException("Invalid user ID or auth token");
            var credentials = new QobuzCredentials
            {
                UserId = "123456",
                AuthToken = "invalid_token",
                AppId = "test_app_id",
                AppSecret = "test_app_secret"
            };

            // ValidateSessionAsync will fail (HTTP error caught internally -> returns false)
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await _authService.AuthenticateAsync(credentials));

            // Message now adds a re-authenticate-with-email/password remediation hint;
            // start-of-message is the load-bearing contract.
            exception.Message.Should().StartWith("Invalid user ID or auth token");
        }

        #endregion

        #region AuthenticateAsync Successful Email Auth (Source lines 258-272)

        [Fact]
        public async Task AuthenticateAsync_ValidEmailAuth_ReturnsSessionWithCorrectData()
        {
            // Source line 258: if (credentials.IsEmailAuth()) session = await AuthenticateWithEmailAsync(...)
            var credentials = new QobuzCredentials
            {
                Email = "test@example.com",
                MD5Password = "hashed_password",
                AppId = "test_app_id",
                AppSecret = "test_app_secret"
            };

            var loginResponse = new QobuzLoginResponse
            {
                User = new QobuzUser
                {
                    Id = "654321",
                    Email = "test@example.com",
                    Subscription = new QobuzSubscriptionDetails
                    {
                        Offer = "Sublime",
                        IsActive = true
                    }
                },
                UserAuthToken = "auth_token_xyz",
                Status = "ok"
            };

            var httpResponse = new HttpResponse(
                new HttpRequest("https://api.qobuz.com/user/login"),
                new HttpHeader(),
                JsonConvert.SerializeObject(loginResponse),
                HttpStatusCode.OK);

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(httpResponse);

            var result = await _authService.AuthenticateAsync(credentials);

            result.Should().NotBeNull();
            result.UserId.Should().Be("654321");
            result.AuthToken.Should().Be("auth_token_xyz");
            result.Subscription.Should().NotBeNull();
            result.Subscription.Type.Should().Be("Sublime");
            result.Subscription.IsHiRes.Should().BeTrue();
        }

        [Fact]
        public async Task AuthenticateAsync_ValidEmailAuth_StoresSessionInCache()
        {
            // Source line 265: StoreSession(session);
            var credentials = new QobuzCredentials
            {
                Email = "test@example.com",
                MD5Password = "hashed_password",
                AppId = "test_app_id",
                AppSecret = "test_app_secret"
            };

            var loginResponse = new QobuzLoginResponse
            {
                User = new QobuzUser { Id = "111222", Email = "test@example.com" },
                UserAuthToken = "stored_token",
                Status = "ok"
            };

            var httpResponse = new HttpResponse(
                new HttpRequest("https://api.qobuz.com/user/login"),
                new HttpHeader(),
                JsonConvert.SerializeObject(loginResponse),
                HttpStatusCode.OK);

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(httpResponse);

            await _authService.AuthenticateAsync(credentials);

            var cached = _authService.GetCachedSession();
            cached.Should().NotBeNull();
            cached.UserId.Should().Be("111222");
            cached.AuthToken.Should().Be("stored_token");
        }

        #endregion

        #region CloneSession Tests (Source lines 379-395)

        [Fact]
        public void CloneSession_WithSubscription_ClonesAllProperties()
        {
            // Source lines 379-395: CloneSession creates deep copy including subscription
            var original = new QobuzSession
            {
                UserId = "123456",
                AuthToken = "test_token",
                AppId = "app_id",
                AppSecret = "app_secret",
                ExpiresAt = DateTime.UtcNow.AddHours(2),
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                Subscription = new QobuzSubscription
                {
                    Type = "Sublime",
                    IsHiRes = true,
                    MaxSampleRate = 192000,
                    MaxBitDepth = 24,
                    CanStream = true,
                    CanDownload = true
                }
            };

            var cloneMethod = typeof(QobuzAuthenticationService).GetMethod("CloneSession",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var cloned = cloneMethod?.Invoke(null, new object[] { original }) as QobuzSession;

            cloned.Should().NotBeNull();
            cloned.Should().NotBeSameAs(original);
            cloned.UserId.Should().Be(original.UserId);
            cloned.AuthToken.Should().Be(original.AuthToken);
            cloned.AppId.Should().Be(original.AppId);
            cloned.AppSecret.Should().Be(original.AppSecret);
            cloned.ExpiresAt.Should().Be(original.ExpiresAt);
            cloned.CreatedAt.Should().Be(original.CreatedAt);
            cloned.Subscription.Should().NotBeNull();
            cloned.Subscription.Type.Should().Be(original.Subscription.Type);
            cloned.Subscription.IsHiRes.Should().Be(original.Subscription.IsHiRes);
            cloned.Subscription.MaxSampleRate.Should().Be(original.Subscription.MaxSampleRate);
            cloned.Subscription.MaxBitDepth.Should().Be(original.Subscription.MaxBitDepth);
            cloned.Subscription.CanStream.Should().Be(original.Subscription.CanStream);
            cloned.Subscription.CanDownload.Should().Be(original.Subscription.CanDownload);
        }

        [Fact]
        public void CloneSession_NullSubscription_ClonesWithoutSubscription()
        {
            // Source line 386: Subscription == null ? null : new QobuzSubscription { ... }
            var original = new QobuzSession
            {
                UserId = "123456",
                AuthToken = "test_token",
                AppId = "app_id",
                AppSecret = "app_secret",
                ExpiresAt = DateTime.UtcNow.AddHours(2),
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                Subscription = null
            };

            var cloneMethod = typeof(QobuzAuthenticationService).GetMethod("CloneSession",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var cloned = cloneMethod?.Invoke(null, new object[] { original }) as QobuzSession;

            cloned.Should().NotBeNull();
            cloned.Subscription.Should().BeNull();
        }

        #endregion
    }
}
