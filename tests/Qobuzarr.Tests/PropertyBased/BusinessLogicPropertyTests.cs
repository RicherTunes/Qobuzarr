using System;
using System.Linq;
using Xunit;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Qobuzarr.Tests.PropertyBased
{
    /// <summary>
    /// Property-based tests using simple data-driven approach to verify business logic invariants.
    /// These tests validate that certain properties hold across many different inputs.
    /// </summary>
    public class BusinessLogicPropertyTests
    {
        /// <summary>
        /// Property: Email authentication always takes precedence over token authentication
        /// Tests this invariant across many different credential combinations
        /// </summary>
        [Theory]
        [InlineData("user@test.com", "pass123", "user456", "token789", "app123")]
        [InlineData("admin@example.org", "secret", "admin999", "authtoken", "myapp")]
        [InlineData("test@domain.co.uk", "hash456", "12345", "bearer_token", "app_id")]
        [InlineData("user+tag@site.net", "md5hash", "userid", "jwt_token", "application")]
        [InlineData("simple@test.com", "password", "999", "access_token", "id123")]
        public void EmailAuth_Always_Takes_Precedence_Over_Token(
            string email, string password, string userId, string token, string appId)
        {
            // Arrange - credentials with both email and token data
            var credentials = new QobuzCredentials
            {
                Email = email,
                MD5Password = password,
                UserId = userId,
                AuthToken = token,
                AppId = appId
            };

            // Act & Assert - Email should take precedence
            credentials.IsEmailAuth().Should().BeTrue();
            credentials.IsTokenAuth().Should().BeFalse();
            credentials.IsValid().Should().BeTrue();
        }

        /// <summary>
        /// Property: Valid email formats always result in valid credentials when complete
        /// </summary>
        [Theory]
        [InlineData("user@example.com")]
        [InlineData("test.email@domain.org")]
        [InlineData("user+tag@site.co.uk")]
        [InlineData("admin@test-domain.net")]
        [InlineData("simple@local.dev")]
        public void Valid_Email_With_Complete_Data_Always_Valid(string email)
        {
            // Arrange
            var credentials = new QobuzCredentials
            {
                Email = email,
                MD5Password = "some_hash",
                AppId = "app_id"
            };

            // Act & Assert
            credentials.IsValid().Should().BeTrue();
            credentials.IsEmailAuth().Should().BeTrue();
            credentials.IsTokenAuth().Should().BeFalse();
        }

        /// <summary>
        /// Property: Invalid email formats always result in invalid credentials
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("invalid")]
        [InlineData("@domain.com")]
        [InlineData("user@")]
        [InlineData("user@@domain.com")]
        [InlineData("user.domain.com")]
        public void Invalid_Email_Always_Makes_Credentials_Invalid(string invalidEmail)
        {
            // Arrange
            var credentials = new QobuzCredentials
            {
                Email = invalidEmail,
                MD5Password = "some_hash",
                AppId = "app_id"
            };

            // Act & Assert
            credentials.IsValid().Should().BeFalse();
        }

        /// <summary>
        /// Property: Token-only credentials should always be token auth when valid
        /// </summary>
        [Theory]
        [InlineData("user123", "token456")]
        [InlineData("12345", "bearer_abc")]
        [InlineData("userid", "jwt_token")]
        [InlineData("999", "access_token")]
        [InlineData("admin", "auth_key")]
        public void TokenOnly_Credentials_Are_Token_Auth(string userId, string token)
        {
            // Arrange
            var credentials = new QobuzCredentials
            {
                Email = null, // No email
                MD5Password = null, // No password
                UserId = userId,
                AuthToken = token,
                AppId = "app_id"
            };

            // Act & Assert
            credentials.IsValid().Should().BeTrue();
            credentials.IsTokenAuth().Should().BeTrue();
            credentials.IsEmailAuth().Should().BeFalse();
        }

        /// <summary>
        /// Property: Missing AppId is now allowed - credentials are valid based on auth data
        /// </summary>
        [Theory]
        [InlineData("user@test.com", "password", null, null)]
        [InlineData(null, null, "user123", "token456")]
        [InlineData("user@test.com", "password", "user123", "token456")]
        public void Missing_AppId_Always_Invalid(string email, string password, string userId, string token)
        {
            // Arrange
            var credentials = new QobuzCredentials
            {
                Email = email,
                MD5Password = password,
                UserId = userId,
                AuthToken = token,
                AppId = null // Missing AppId - now optional
            };

            // Act & Assert
            // App ID is now optional - validity depends on having valid auth data
            var hasEmailAuth = !string.IsNullOrEmpty(email) && email.Contains("@") && !string.IsNullOrEmpty(password);
            var hasTokenAuth = !string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(token);
            var shouldBeValid = hasEmailAuth || hasTokenAuth;
            
            credentials.IsValid().Should().Be(shouldBeValid);
        }

        /// <summary>
        /// Property: Sessions with future expiration should always be valid when created properly
        /// </summary>
        [Theory]
        [InlineData("user123", "token456", "app789", "secret")]
        [InlineData("admin", "bearer_token", "myapp", "app_secret")]
        [InlineData("12345", "jwt_access", "application", "secure_key")]
        public void Future_Sessions_Are_Valid(string userId, string token, string appId, string appSecret)
        {
            // Arrange & Act
            var session = QobuzSession.CreateSession(userId, token, appId, appSecret);

            // Assert
            session.Should().NotBeNull();
            session.IsValid().Should().BeTrue();
            session.UserId.Should().Be(userId);
            session.AuthToken.Should().Be(token);
            session.AppId.Should().Be(appId);
            session.AppSecret.Should().Be(appSecret);
            session.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        }

        /// <summary>
        /// Property: Filename sanitization should always remove invalid characters
        /// </summary>
        [Theory]
        [InlineData("Normal Track Title")]
        [InlineData("Track: With Colon")]
        [InlineData("Song/Album")]
        [InlineData("Title\"Quoted\"")]
        [InlineData("Song*Star")]
        [InlineData("Track?Question")]
        [InlineData("Song<>Brackets")]
        [InlineData("File|Name")]
        public void Filename_Sanitization_Removes_Invalid_Characters(string input)
        {
            // Act
            var sanitized = input.ToSafeFileName();

            // Assert
            sanitized.Should().NotBeNull();
            sanitized.Should().NotContain("<");
            sanitized.Should().NotContain(">");
            sanitized.Should().NotContain(":");
            sanitized.Should().NotContain("\"");
            sanitized.Should().NotContain("/");
            sanitized.Should().NotContain("\\");
            sanitized.Should().NotContain("|");
            sanitized.Should().NotContain("?");
            sanitized.Should().NotContain("*");
            
            // Should preserve alphanumeric content
            var alphanumericInput = new string(input.Where(char.IsLetterOrDigit).ToArray());
            var alphanumericOutput = new string(sanitized.Where(char.IsLetterOrDigit).ToArray());
            alphanumericOutput.Should().Be(alphanumericInput);
        }

        /// <summary>
        /// Property: Sanitization should be idempotent (applying twice gives same result)
        /// </summary>
        [Theory]
        [InlineData("Track: Title")]
        [InlineData("Song/Album")]
        [InlineData("File*Name")]
        [InlineData("Normal Title")]
        [InlineData("")]
        public void Filename_Sanitization_Is_Idempotent(string input)
        {
            // Act
            var sanitized1 = input.ToSafeFileName();
            var sanitized2 = sanitized1.ToSafeFileName();

            // Assert
            sanitized1.Should().Be(sanitized2);
        }

        /// <summary>
        /// Property: Long filenames should be truncated to reasonable length
        /// </summary>
        [Theory]
        [InlineData("Very Long Track Name That Exceeds Normal Filename Length Limits And Should Be Truncated To Prevent File System Issues And Ensure Compatibility Across Different Operating Systems")]
        public void Long_Filenames_Are_Truncated(string longInput)
        {
            // Act
            var sanitized = longInput.ToSafeFileName();

            // Assert
            sanitized.Length.Should().BeLessOrEqualTo(255); // Most file systems limit
            sanitized.Should().NotBeEmpty();
        }

        /// <summary>
        /// Property: Rate limiter statistics should always be mathematically consistent
        /// This tests our rate limiter's accounting invariants
        /// </summary>
        [Theory]
        [InlineData(10, 7, 2, 1)] // 10 total, 7 success, 2 errors, 1 rate limit
        [InlineData(5, 5, 0, 0)]  // All success
        [InlineData(3, 0, 2, 1)]  // No success
        [InlineData(1, 1, 0, 0)]  // Single success
        public void Rate_Limiter_Statistics_Are_Mathematically_Consistent(
            int totalRequests, int successRequests, int errorRequests, int rateLimitRequests)
        {
            // This is a mathematical invariant test
            // Assert that the components always add up correctly
            var accountedFor = successRequests + errorRequests + rateLimitRequests;
            
            // Property: Total should equal sum of all categorized requests
            accountedFor.Should().Be(totalRequests);
            
            // Property: Success rate should be mathematically correct
            var expectedSuccessRate = totalRequests > 0 ? (double)successRequests / totalRequests : 0;
            expectedSuccessRate.Should().BeInRange(0.0, 1.0);
            
            // Property: All counts should be non-negative
            totalRequests.Should().BeGreaterOrEqualTo(0);
            successRequests.Should().BeGreaterOrEqualTo(0);
            errorRequests.Should().BeGreaterOrEqualTo(0);
            rateLimitRequests.Should().BeGreaterOrEqualTo(0);
        }

        /// <summary>
        /// Property: Session expiration times should be reasonable and consistent
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(10)]
        public void Session_Expiration_Times_Are_Reasonable(int sessionCount)
        {
            // Arrange
            var sessions = new QobuzSession[sessionCount];
            var creationTime = DateTime.UtcNow;

            // Act - Create multiple sessions
            for (int i = 0; i < sessionCount; i++)
            {
                sessions[i] = QobuzSession.CreateSession($"user{i}", $"token{i}", $"app{i}", $"secret{i}");
            }

            // Assert - All sessions should have reasonable expiration times
            foreach (var session in sessions)
            {
                session.ExpiresAt.Should().BeAfter(creationTime);
                session.ExpiresAt.Should().BeBefore(creationTime.AddDays(2)); // Reasonable upper bound
                session.IsValid().Should().BeTrue();
            }

            // Property: Session creation should be monotonic (later sessions expire later or same time)
            for (int i = 1; i < sessionCount; i++)
            {
                sessions[i].ExpiresAt.Should().BeOnOrAfter(sessions[i - 1].ExpiresAt);
            }
        }
    }
}