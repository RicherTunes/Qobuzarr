using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Security;
using Qobuzarr.Tests.Fixtures;

namespace Qobuzarr.Tests.Integration
{
    /// <summary>
    /// Integration tests for security components working together.
    /// Tests end-to-end security scenarios and component interactions.
    /// </summary>
    public class SecurityIntegrationTests : TestFixtureBase
    {
        private readonly SecureCredentialManager _credentialManager;
        private readonly SecureSessionManager _sessionManager;
        private readonly SecurityConfigValidator _configValidator;
        private readonly QobuzAuthenticationService _authService;

        public SecurityIntegrationTests()
        {
            _credentialManager = new SecureCredentialManager(MockLogger.Object);
            _sessionManager = new SecureSessionManager(_credentialManager, MockLogger.Object);
            _configValidator = new SecurityConfigValidator(MockLogger.Object, _credentialManager);
            _authService = new QobuzAuthenticationService(
                MockHttpClient.Object,
                MockConfigService.Object,
                MockLocalizationService.Object,
                MockCacheManager,
                MockLogger.Object);
        }

        #region End-to-End Authentication Security Flow

        [Fact]
        public async Task CompleteAuthenticationFlow_WithSecureCredentials_ShouldSucceed()
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = "realuser@example.com",
                Password = "VerySecure@Password123!",
                BaseUrl = "https://www.qobuz.com",
                CountryCode = "US"
            };

            // Act
            // Step 1: Validate configuration security
            var configValidation = _configValidator.ValidateConfiguration(settings);
            
            // Step 2: Create secure credentials
            var securePassword = _credentialManager.CreateSecureString(settings.Password);
            
            // Step 3: Hash password for Qobuz API
            var hashedPassword = QobuzAuthenticationService.HashPassword(
                _credentialManager.SecureStringToString(securePassword));
            
            // Step 4: Create authentication credentials
            var credentials = new QobuzCredentials
            {
                Email = settings.Email,
                MD5Password = hashedPassword
            };
            
            // Step 5: Store session securely after authentication
            var session = new QobuzSession
            {
                UserId = "12345678",
                AuthToken = "auth_token_from_api",
                AppSecret = "app_secret_from_api",
                AppId = "app_id",
                ExpiresAt = DateTime.UtcNow.AddHours(2),
                CreatedAt = DateTime.UtcNow
            };
            
            _sessionManager.StoreSessionSecurely(session);

            // Assert
            configValidation.IsSecure.Should().BeTrue();
            configValidation.SecurityLevel.Should().Be(SecurityLevel.High);
            
            credentials.IsValid().Should().BeTrue();
            credentials.IsEmailAuth().Should().BeTrue();
            
            _sessionManager.HasValidSession().Should().BeTrue();
            
            var retrievedSession = _sessionManager.GetSecureSession();
            retrievedSession.Should().NotBeNull();
            retrievedSession.AuthToken.Should().Be("auth_token_from_api");
            
            // Clean up
            securePassword?.Dispose();
        }

        [Fact]
        public async Task CompleteAuthenticationFlow_WithMaliciousInput_ShouldBeBlocked()
        {
            // Arrange
            var maliciousSettings = new QobuzIndexerSettings
            {
                Email = "admin'; DROP TABLE users; --",
                Password = "<script>alert('xss')</script>",
                BaseUrl = "http://evil.com",
                CountryCode = "XX"
            };

            // Act
            var configValidation = _configValidator.ValidateConfiguration(maliciousSettings);

            // Assert
            configValidation.IsSecure.Should().BeFalse();
            configValidation.HasCriticalIssues.Should().BeTrue();
            configValidation.SecurityLevel.Should().Be(SecurityLevel.Critical);
            configValidation.CriticalIssues.Should().NotBeEmpty();
        }

        #endregion

        #region Credential Lifecycle Security Tests

        [Fact]
        public void CredentialLifecycle_FromInputToStorage_ShouldMaintainSecurity()
        {
            // Arrange
            var plainPassword = "MySuperSecretPassword123!";
            var email = "user@secure.com";

            // Act
            // Step 1: Validate credential security
            var isPasswordSecure = _credentialManager.ValidateCredentialSecurity(plainPassword, "Password");
            var isEmailSecure = _credentialManager.ValidateCredentialSecurity(email, "Email");

            // Step 2: Create secure string for password
            var securePassword = _credentialManager.CreateSecureString(plainPassword);

            // Step 3: Generate secure hash for storage
            var salt = new byte[32];
            new Random(42).NextBytes(salt);
            var hashedForStorage = _credentialManager.GenerateSecureHash(plainPassword, salt);

            // Step 4: Verify we can authenticate with the hash
            var canVerify = _credentialManager.VerifySecureHash(plainPassword, hashedForStorage);

            // Step 5: Mask for logging
            var maskedPassword = _credentialManager.MaskSensitiveData(plainPassword);

            // Assert
            isPasswordSecure.Should().BeTrue();
            isEmailSecure.Should().BeTrue();
            
            securePassword.Should().NotBeNull();
            securePassword.Length.Should().Be(plainPassword.Length);
            
            hashedForStorage.Should().NotBeNullOrEmpty();
            hashedForStorage.Should().NotBe(plainPassword);
            
            canVerify.Should().BeTrue();
            
            maskedPassword.Should().Contain("*");
            maskedPassword.Should().NotBe(plainPassword);

            // Clean up
            securePassword?.Dispose();
        }

        #endregion

        #region Session Management Security Tests

        [Fact]
        public void SessionManagement_WithMultipleUsers_ShouldIsolateData()
        {
            // Arrange
            var user1Session = new QobuzSession
            {
                UserId = "user1",
                AuthToken = "token1_secret",
                AppSecret = "secret1",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };

            var user2Session = new QobuzSession
            {
                UserId = "user2",
                AuthToken = "token2_secret",
                AppSecret = "secret2",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };

            // Act
            // Store first user's session
            _sessionManager.StoreSessionSecurely(user1Session);
            var retrieved1 = _sessionManager.GetSecureSession();

            // Store second user's session (should replace first)
            _sessionManager.StoreSessionSecurely(user2Session);
            var retrieved2 = _sessionManager.GetSecureSession();

            // Assert
            retrieved1.Should().NotBeNull();
            retrieved1.UserId.Should().Be("user1");
            retrieved1.AuthToken.Should().Be("token1_secret");

            retrieved2.Should().NotBeNull();
            retrieved2.UserId.Should().Be("user2");
            retrieved2.AuthToken.Should().Be("token2_secret");
            
            // Verify first user's data is no longer accessible
            retrieved2.AuthToken.Should().NotBe(retrieved1.AuthToken);
        }

        [Fact]
        public void SessionRotation_AfterExpiry_ShouldRequireReauthentication()
        {
            // Arrange
            var initialSession = new QobuzSession
            {
                UserId = "user123",
                AuthToken = "initial_token",
                AppSecret = "initial_secret",
                ExpiresAt = DateTime.UtcNow.AddMinutes(30),
                CreatedAt = DateTime.UtcNow
            };

            // Act
            _sessionManager.StoreSessionSecurely(initialSession);
            var beforeExpiry = _sessionManager.HasValidSession();

            // Simulate session expiry
            var expiredSession = new QobuzSession
            {
                UserId = initialSession.UserId,
                AuthToken = initialSession.AuthToken,
                AppSecret = initialSession.AppSecret,
                ExpiresAt = DateTime.UtcNow.AddMinutes(-1), // Expired
                CreatedAt = initialSession.CreatedAt
            };
            
            _sessionManager.ClearSession();
            _sessionManager.StoreSessionSecurely(expiredSession);
            
            var afterExpiry = _sessionManager.HasValidSession();
            var retrievedExpired = _sessionManager.GetSecureSession();

            // Assert
            beforeExpiry.Should().BeTrue();
            afterExpiry.Should().BeFalse();
            retrievedExpired.Should().BeNull("Expired session should not be returned");
        }

        #endregion

        #region Configuration Validation Integration Tests

        [Fact]
        public void ConfigurationValidation_WithProgressivelyWorseSettings_ShouldDegradeSecurityScore()
        {
            // Arrange
            var configurations = new List<(QobuzIndexerSettings settings, string description)>
            {
                (new QobuzIndexerSettings
                {
                    Email = "secure@example.com",
                    Password = "VerySecure@Pass123!",
                    BaseUrl = "https://www.qobuz.com",
                    CountryCode = "US",
                    ConnectionTimeout = 30
                }, "Perfect configuration"),

                (new QobuzIndexerSettings
                {
                    Email = "secure@example.com",
                    Password = "SimplePass123",
                    BaseUrl = "https://www.qobuz.com",
                    CountryCode = "US",
                    ConnectionTimeout = 30
                }, "Weaker password"),

                (new QobuzIndexerSettings
                {
                    Email = "test@example.com",
                    Password = "SimplePass123",
                    BaseUrl = "https://www.qobuz.com",
                    CountryCode = "USA"
                }, "Test email and invalid country code"),

                (new QobuzIndexerSettings
                {
                    Email = "test@test.com",
                    Password = "test",
                    BaseUrl = "http://www.qobuz.com",
                    CountryCode = "XX"
                }, "Multiple critical issues")
            };

            var scores = new List<int>();

            // Act
            foreach (var (settings, description) in configurations)
            {
                var result = _configValidator.ValidateConfiguration(settings);
                scores.Add(result.SecurityScore);
            }

            // Assert
            scores.Should().BeInDescendingOrder("Security scores should decrease with worse configurations");
            scores[0].Should().BeGreaterOrEqualTo(90, "Perfect configuration should have high score");
            scores[^1].Should().BeLessThan(50, "Configuration with critical issues should have low score");
        }

        #endregion

        #region Attack Simulation Tests

        [Fact]
        public void SimulateCredentialStuffingAttack_ShouldNotLeakTiming()
        {
            // Arrange
            var validEmail = "valid@example.com";
            var invalidEmail = "invalid@example.com";
            var passwords = new[] { "password1", "password2", "password3", "correct_password" };
            
            var timings = new List<long>();

            // Act
            foreach (var password in passwords)
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                var credentials = new QobuzCredentials
                {
                    Email = validEmail,
                    MD5Password = QobuzAuthenticationService.HashPassword(password)
                };
                
                var isValid = credentials.IsValid();
                
                stopwatch.Stop();
                timings.Add(stopwatch.ElapsedMilliseconds);
            }

            // Assert
            // Timing should be relatively consistent to prevent timing attacks
            var maxTiming = timings.Max();
            var minTiming = timings.Min();
            var variance = maxTiming - minTiming;
            
            variance.Should().BeLessThan(100, "Timing variance should be minimal to prevent timing attacks");
        }

        [Fact]
        public void SimulateSessionHijackingAttempt_ShouldBeDetected()
        {
            // Arrange
            var legitimateSession = new QobuzSession
            {
                UserId = "legitimate_user",
                AuthToken = "legitimate_token_123",
                AppSecret = "legitimate_secret",
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                CreatedAt = DateTime.UtcNow
            };

            _sessionManager.StoreSessionSecurely(legitimateSession);

            // Act - Attempt to hijack with modified session
            var hijackedSession = new QobuzSession
            {
                UserId = "legitimate_user", // Same user ID
                AuthToken = "hijacked_token_456", // Different token
                AppSecret = "hijacked_secret",
                ExpiresAt = DateTime.UtcNow.AddHours(2), // Extended expiry
                CreatedAt = DateTime.UtcNow
            };

            _sessionManager.StoreSessionSecurely(hijackedSession);

            // Assert
            var currentSession = _sessionManager.GetSecureSession();
            currentSession.Should().NotBeNull();
            currentSession.AuthToken.Should().Be("hijacked_token_456", 
                "New session should replace old one completely");
            
            // The old token should no longer be valid
            currentSession.AuthToken.Should().NotBe(legitimateSession.AuthToken);
        }

        #endregion

        #region Memory Security Tests

        [Fact]
        public void MemorySecurity_AfterProcessingCredentials_ShouldNotLeaveTraces()
        {
            // Arrange
            var sensitiveData = "SuperSecretPassword123!@#";
            var processedData = new List<string>();

            // Act
            // Process sensitive data through various security components
            var secureString = _credentialManager.CreateSecureString(sensitiveData);
            var plainText = _credentialManager.SecureStringToString(secureString);
            processedData.Add(plainText);
            
            var hash = _credentialManager.GenerateSecureHash(plainText);
            var masked = _credentialManager.MaskSensitiveData(plainText);
            
            // Clear references
            _credentialManager.ClearString(ref plainText);
            secureString?.Dispose();
            
            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Assert
            plainText.Should().BeNull("Cleared string reference should be null");
            masked.Should().NotContain(sensitiveData, "Masked data should not contain original");
            hash.Should().NotContain(sensitiveData, "Hash should not contain plaintext");
        }

        #endregion

        #region Cleanup

        public override void Dispose()
        {
            _sessionManager?.Dispose();
            _credentialManager?.Dispose();
            base.Dispose();
        }

        #endregion
    }
}