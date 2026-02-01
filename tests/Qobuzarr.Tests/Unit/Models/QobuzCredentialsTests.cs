using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace Qobuzarr.Tests.Unit.Models
{
    /// <summary>
    /// Tests for QobuzCredentials using the current API
    /// </summary>
    public class QobuzCredentialsTests
    {
        [Fact]
        public void IsValid_WithValidEmailPassword_ShouldReturnTrue()
        {
            // Arrange
            var credentials = new QobuzCredentials
            {
                Email = "test@example.com",
                MD5Password = "5d41402abc4b2a76b9719d911017c592", // MD5 hash of "hello"
                AppId = "test_app_id"
            };

            // Act & Assert
            credentials.IsValid().Should().BeTrue();
        }

        [Fact]
        public void IsValid_WithValidTokenCredentials_ShouldReturnTrue()
        {
            // Arrange
            var credentials = new QobuzCredentials
            {
                UserId = "12345678",
                AuthToken = "sample_token_123",
                AppId = "test_app_id"
            };

            // Act & Assert
            credentials.IsValid().Should().BeTrue();
        }

        [Fact]
        public void IsValid_WithEmptyEmail_ShouldReturnFalse()
        {
            // Arrange
            var credentials = new QobuzCredentials
            {
                Email = "",
                MD5Password = "password123",
                AppId = "test_app_id"
            };

            // Act & Assert
            credentials.IsValid().Should().BeFalse();
        }

        [Fact]
        public void IsValid_WithEmptyPassword_ShouldReturnFalse()
        {
            // Arrange
            var credentials = new QobuzCredentials
            {
                Email = "test@example.com",
                MD5Password = "",
                AppId = "test_app_id"
            };

            // Act & Assert
            credentials.IsValid().Should().BeFalse();
        }

        [Fact]
        public void IsValid_WithInvalidEmailFormat_ShouldReturnFalse()
        {
            // Arrange
            var credentials = new QobuzCredentials
            {
                Email = "invalid-email",
                MD5Password = "password123",
                AppId = "test_app_id"
            };

            // Act & Assert
            credentials.IsValid().Should().BeFalse();
        }

        [Fact]
        public void IsValid_WithEmptyUserId_ShouldReturnFalse()
        {
            // Arrange
            var credentials = new QobuzCredentials
            {
                UserId = "",
                AuthToken = "token123",
                AppId = "test_app_id"
            };

            // Act & Assert
            credentials.IsValid().Should().BeFalse();
        }

        [Fact]
        public void IsValid_WithEmptyAuthToken_ShouldReturnFalse()
        {
            // Arrange
            var credentials = new QobuzCredentials
            {
                UserId = "12345",
                AuthToken = "",
                AppId = "test_app_id"
            };

            // Act & Assert
            credentials.IsValid().Should().BeFalse();
        }

        [Fact]
        public void IsValid_WithMissingAppId_ShouldReturnTrue()
        {
            // Arrange
            var credentials = new QobuzCredentials
            {
                Email = "test@example.com",
                MD5Password = "password123"
                // Missing AppId - now optional
            };

            // Act & Assert
            // App ID is now optional - empty values will use built-in defaults
            credentials.IsValid().Should().BeTrue();
        }

        [Fact]
        public void IsValid_WithNoCredentials_ShouldReturnFalse()
        {
            // Arrange
            var credentials = new QobuzCredentials();

            // Act & Assert
            credentials.IsValid().Should().BeFalse();
        }

        [Fact]
        public void IsEmailAuth_WithValidEmailCredentials_ShouldReturnTrue()
        {
            // Arrange
            var credentials = new QobuzCredentials
            {
                Email = "test@example.com",
                MD5Password = "password123",
                AppId = "test_app_id"
            };

            // Act & Assert
            credentials.IsEmailAuth().Should().BeTrue();
        }

        [Fact]
        public void IsEmailAuth_WithTokenCredentials_ShouldReturnFalse()
        {
            // Arrange
            var credentials = new QobuzCredentials
            {
                UserId = "12345",
                AuthToken = "token123",
                AppId = "test_app_id"
            };

            // Act & Assert
            credentials.IsEmailAuth().Should().BeFalse();
        }

        [Fact]
        public void IsTokenAuth_WithValidTokenCredentials_ShouldReturnTrue()
        {
            // Arrange
            var credentials = new QobuzCredentials
            {
                UserId = "12345",
                AuthToken = "token123",
                AppId = "test_app_id"
            };

            // Act & Assert
            credentials.IsTokenAuth().Should().BeTrue();
        }

        [Fact]
        public void IsTokenAuth_WithEmailCredentials_ShouldReturnFalse()
        {
            // Arrange
            var credentials = new QobuzCredentials
            {
                Email = "test@example.com",
                MD5Password = "password123",
                AppId = "test_app_id"
            };

            // Act & Assert
            credentials.IsTokenAuth().Should().BeFalse();
        }

        [Theory]
        [InlineData("test@example.com", true)]
        [InlineData("user+tag@domain.co.uk", true)]
        [InlineData("invalid.email", false)]
        [InlineData("@domain.com", false)]
        [InlineData("user@", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void EmailValidation_ShouldValidateEmailFormat(string email, bool expectedValid)
        {
            // Arrange
            var credentials = new QobuzCredentials
            {
                Email = email,
                MD5Password = "password123",
                AppId = "test_app_id"
            };

            // Act & Assert
            if (expectedValid)
            {
                credentials.IsValid().Should().BeTrue();
            }
            else
            {
                credentials.IsValid().Should().BeFalse();
            }
        }

        [Fact]
        public void MixedCredentials_ShouldPreferEmailAuth()
        {
            // Arrange - credentials with both email and token data
            var credentials = new QobuzCredentials
            {
                Email = "test@example.com",
                MD5Password = "password123",
                UserId = "12345",
                AuthToken = "token123",
                AppId = "test_app_id"
            };

            // Act & Assert
            credentials.IsValid().Should().BeTrue();
            credentials.IsEmailAuth().Should().BeTrue();
            credentials.IsTokenAuth().Should().BeFalse(); // Email takes precedence
        }

        [Fact]
        public void Properties_ShouldAcceptValidValues()
        {
            // Arrange & Act
            var credentials = new QobuzCredentials
            {
                Email = "test@example.com",
                MD5Password = "hashedpassword",
                UserId = "12345678",
                AuthToken = "auth_token_123",
                AppId = "app_123",
                AppSecret = "app_secret_456"
            };

            // Assert
            credentials.Email.Should().Be("test@example.com");
            credentials.MD5Password.Should().Be("hashedpassword");
            credentials.UserId.Should().Be("12345678");
            credentials.AuthToken.Should().Be("auth_token_123");
            credentials.AppId.Should().Be("app_123");
            credentials.AppSecret.Should().Be("app_secret_456");
        }
    }
}
