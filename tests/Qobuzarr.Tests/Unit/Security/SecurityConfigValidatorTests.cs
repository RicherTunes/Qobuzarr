using System;
using System.Linq;
using FluentAssertions;
using Moq;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Security;

namespace Qobuzarr.Tests.Unit.Security
{
    /// <summary>
    /// Comprehensive security test suite for SecurityConfigValidator.
    /// Tests configuration validation, injection prevention, and security scoring.
    /// </summary>
    public class SecurityConfigValidatorTests
    {
        private readonly Mock<IQobuzLogger> _mockLogger;
        private readonly SecurityConfigValidator _validator;

        public SecurityConfigValidatorTests()
        {
            _mockLogger = new Mock<IQobuzLogger>();
            _validator = new SecurityConfigValidator(_mockLogger.Object);
        }

        #region Basic Validation Tests

        [Fact]
        public void ValidateConfiguration_WithNullSettings_ShouldThrow()
        {
            // Act
            Action act = () => _validator.ValidateConfiguration(null);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("settings");
        }

        [Fact]
        public void ValidateConfiguration_WithValidEmailAuth_ShouldReturnHighSecurity()
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = "realuser@example.com",
                Password = "SecureP@ssw0rd123!",
                BaseUrl = "https://www.qobuz.com",
                CountryCode = "US",
                ConnectionTimeout = 30,
                ApiRateLimit = 60,
                SearchCacheDuration = 30
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.Should().NotBeNull();
            result.IsSecure.Should().BeTrue();
            result.SecurityLevel.Should().Be(SecurityLevel.High);
            result.HasCriticalIssues.Should().BeFalse();
            result.HasSecurityIssues.Should().BeFalse();
        }

        [Fact]
        public void ValidateConfiguration_WithValidTokenAuth_ShouldReturnHighSecurity()
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                UserId = "12345678",
                AuthToken = "validtoken123456789abcdef",
                BaseUrl = "https://www.qobuz.com",
                CountryCode = "FR",
                ConnectionTimeout = 60,
                ApiRateLimit = 100,
                SearchCacheDuration = 45
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.Should().NotBeNull();
            result.IsSecure.Should().BeTrue();
            result.SecurityLevel.Should().Be(SecurityLevel.High);
            result.HasCriticalIssues.Should().BeFalse();
        }

        #endregion

        #region Authentication Security Tests

        [Fact]
        public void ValidateConfiguration_WithNoAuthentication_ShouldReturnCriticalIssue()
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                BaseUrl = "https://www.qobuz.com",
                CountryCode = "US"
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.HasCriticalIssues.Should().BeTrue();
            result.CriticalIssues.Should().Contain(i => i.Title.Contains("No authentication method"));
        }

        [Theory]
        [InlineData("test@example.com", "password123")]
        [InlineData("demo@test.com", "demo123")]
        [InlineData("placeholder@email.com", "changeme")]
        [InlineData("admin@admin.com", "admin")]
        public void ValidateConfiguration_WithTestCredentials_ShouldDetectAndFlag(string email, string password)
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = email,
                Password = password,
                BaseUrl = "https://www.qobuz.com"
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.HasSecurityIssues.Should().BeTrue();
            result.SecurityLevel.Should().BeLessThan(SecurityLevel.High);
        }

        [Fact]
        public void ValidateConfiguration_WithWeakPassword_ShouldReturnCriticalIssue()
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = "user@example.com",
                Password = "pass", // Too short
                BaseUrl = "https://www.qobuz.com"
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.CriticalIssues.Should().Contain(i => i.Title.Contains("Password security issue"));
        }

        [Theory]
        [InlineData("notanemail")]
        [InlineData("@example.com")]
        [InlineData("user@")]
        [InlineData("user.example.com")]
        public void ValidateConfiguration_WithInvalidEmailFormat_ShouldReturnMajorIssue(string invalidEmail)
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = invalidEmail,
                Password = "ValidPassword123",
                BaseUrl = "https://www.qobuz.com"
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.MajorIssues.Should().Contain(i => i.Title.Contains("Invalid email format"));
        }

        #endregion

        #region Injection Attack Prevention Tests

        [Theory]
        [InlineData("'; DROP TABLE users; --")]
        [InlineData("\" OR \"1\"=\"1")]
        [InlineData("<script>alert('xss')</script>")]
        [InlineData("javascript:alert(1)")]
        [InlineData("../../../etc/passwd")]
        [InlineData("..\\..\\windows\\system32")]
        [InlineData("union select * from passwords")]
        public void ValidateConfiguration_WithSQLInjectionAttempts_ShouldDetectAndFlag(string maliciousInput)
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = maliciousInput,
                Password = "password123",
                BaseUrl = "https://www.qobuz.com"
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.CriticalIssues.Should().Contain(i => 
                i.Title.Contains("Suspicious") || i.Title.Contains("Injection"));
        }

        [Fact]
        public void ValidateConfiguration_WithXSSInMultipleFields_ShouldDetectAll()
        {
            // Arrange
            var xssPattern = "<img src=x onerror=alert(1)>";
            var settings = new QobuzIndexerSettings
            {
                Email = xssPattern,
                Password = xssPattern,
                UserId = xssPattern,
                AuthToken = xssPattern,
                BaseUrl = "https://www.qobuz.com"
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.CriticalIssues.Count.Should().BeGreaterThan(1);
            result.CriticalIssues.Should().OnlyContain(i => 
                i.Title.Contains("Suspicious") || i.Title.Contains("malicious"));
        }

        [Theory]
        [InlineData("$PASSWORD")]
        [InlineData("%APPDATA%")]
        [InlineData("${SECRET_KEY}")]
        [InlineData("%(password)s")]
        public void ValidateConfiguration_WithEnvironmentVariables_ShouldDetectAsPlaceholder(string envVar)
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = "user@example.com",
                Password = envVar,
                BaseUrl = "https://www.qobuz.com"
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.HasSecurityIssues.Should().BeTrue();
            result.CriticalIssues.Should().Contain(i => 
                i.Title.Contains("Password") && i.Description.Contains("placeholder"));
        }

        #endregion

        #region App Credentials Validation Tests

        [Fact]
        public void ValidateConfiguration_WithOnlyAppId_ShouldReturnMajorIssue()
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = "user@example.com",
                Password = "SecurePassword123",
                AppId = "123456",
                AppSecret = "", // Missing
                BaseUrl = "https://www.qobuz.com"
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.MajorIssues.Should().Contain(i => i.Title.Contains("Incomplete app credentials"));
        }

        [Fact]
        public void ValidateConfiguration_WithOnlyAppSecret_ShouldReturnMajorIssue()
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = "user@example.com",
                Password = "SecurePassword123",
                AppId = "", // Missing
                AppSecret = "secretkey123",
                BaseUrl = "https://www.qobuz.com"
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.MajorIssues.Should().Contain(i => i.Title.Contains("Incomplete app credentials"));
        }

        [Fact]
        public void ValidateConfiguration_WithNonNumericAppId_ShouldReturnMajorIssue()
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = "user@example.com",
                Password = "SecurePassword123",
                AppId = "abc123def",
                AppSecret = "validsecret123",
                BaseUrl = "https://www.qobuz.com"
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.MajorIssues.Should().Contain(i => i.Title.Contains("Invalid App ID format"));
        }

        [Fact]
        public void ValidateConfiguration_WithShortAppSecret_ShouldReturnMajorIssue()
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = "user@example.com",
                Password = "SecurePassword123",
                AppId = "123456",
                AppSecret = "short",
                BaseUrl = "https://www.qobuz.com"
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.MajorIssues.Should().Contain(i => i.Title.Contains("App Secret too short"));
        }

        #endregion

        #region Network Security Tests

        [Fact]
        public void ValidateConfiguration_WithHttpUrl_ShouldReturnCriticalIssue()
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = "user@example.com",
                Password = "SecurePassword123",
                BaseUrl = "http://www.qobuz.com", // Not HTTPS
                CountryCode = "US"
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.CriticalIssues.Should().Contain(i => i.Title.Contains("Insecure connection"));
        }

        [Fact]
        public void ValidateConfiguration_WithInvalidUrl_ShouldReturnMajorIssue()
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = "user@example.com",
                Password = "SecurePassword123",
                BaseUrl = "not-a-valid-url",
                CountryCode = "US"
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.MajorIssues.Should().Contain(i => i.Title.Contains("Invalid base URL"));
        }

        [Fact]
        public void ValidateConfiguration_WithNonQobuzUrl_ShouldReturnMajorIssue()
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = "user@example.com",
                Password = "SecurePassword123",
                BaseUrl = "https://www.evil-site.com",
                CountryCode = "US"
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.MajorIssues.Should().Contain(i => i.Title.Contains("Non-standard API endpoint"));
        }

        [Theory]
        [InlineData(2, "Connection timeout outside recommended range")]
        [InlineData(400, "Connection timeout outside recommended range")]
        public void ValidateConfiguration_WithInvalidTimeout_ShouldReturnMinorIssue(int timeout, string expectedMessage)
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = "user@example.com",
                Password = "SecurePassword123",
                BaseUrl = "https://www.qobuz.com",
                ConnectionTimeout = timeout
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.MinorIssues.Should().Contain(i => i.Description.Contains(expectedMessage));
        }

        [Fact]
        public void ValidateConfiguration_WithHighRateLimit_ShouldReturnMinorIssue()
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = "user@example.com",
                Password = "SecurePassword123",
                BaseUrl = "https://www.qobuz.com",
                ApiRateLimit = 500
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.MinorIssues.Should().Contain(i => i.Title.Contains("High API rate limit"));
        }

        #endregion

        #region Privacy Settings Tests

        [Fact]
        public void ValidateConfiguration_WithLongCacheDuration_ShouldReturnMinorIssue()
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = "user@example.com",
                Password = "SecurePassword123",
                BaseUrl = "https://www.qobuz.com",
                SearchCacheDuration = 120
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.MinorIssues.Should().Contain(i => i.Title.Contains("Long cache duration"));
        }

        [Theory]
        [InlineData("U", "Invalid country code")]
        [InlineData("USA", "Invalid country code")]
        [InlineData("1", "Invalid country code")]
        public void ValidateConfiguration_WithInvalidCountryCode_ShouldReturnMajorIssue(string countryCode, string expectedMessage)
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = "user@example.com",
                Password = "SecurePassword123",
                BaseUrl = "https://www.qobuz.com",
                CountryCode = countryCode
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.MajorIssues.Should().Contain(i => i.Title.Contains(expectedMessage));
        }

        #endregion

        #region Security Scoring Tests

        [Fact]
        public void CalculateSecurityScore_WithNoCriticalIssues_ShouldHaveHighScore()
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = "realuser@example.com",
                Password = "VerySecureP@ssw0rd123!",
                BaseUrl = "https://www.qobuz.com",
                CountryCode = "US",
                ConnectionTimeout = 30
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.SecurityScore.Should().BeGreaterOrEqualTo(90);
            result.SecurityLevel.Should().Be(SecurityLevel.High);
        }

        [Fact]
        public void CalculateSecurityScore_WithOneCriticalIssue_ShouldReduceScoreSignificantly()
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = "user@example.com",
                Password = "test", // Critical: weak password
                BaseUrl = "https://www.qobuz.com"
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.SecurityScore.Should().BeLessThan(75);
            result.HasCriticalIssues.Should().BeTrue();
        }

        [Fact]
        public void CalculateSecurityScore_WithMultipleMajorIssues_ShouldHaveMediumScore()
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = "invalidemail", // Major: invalid format
                Password = "Password123", // OK
                BaseUrl = "https://evil.com", // Major: non-standard endpoint
                CountryCode = "USA" // Major: invalid country code
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.SecurityScore.Should().BeLessThan(90);
            result.SecurityScore.Should().BeGreaterThan(50);
            result.SecurityLevel.Should().Be(SecurityLevel.Medium);
        }

        [Fact]
        public void CalculateSecurityScore_WithManyCriticalIssues_ShouldHaveZeroScore()
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = "'; DROP TABLE users; --",
                Password = "test",
                BaseUrl = "http://malicious.com",
                UserId = "<script>alert(1)</script>"
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.SecurityScore.Should().Be(0);
            result.SecurityLevel.Should().Be(SecurityLevel.Critical);
        }

        #endregion

        #region Logging Tests

        [Fact]
        public void ValidateConfiguration_ShouldLogSecurityFindings()
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = "user@example.com",
                Password = "SecurePassword123",
                BaseUrl = "https://www.qobuz.com"
            };

            // Act
            _validator.ValidateConfiguration(settings);

            // Assert
            _mockLogger.Verify(l => l.Info(
                It.IsAny<string>(), 
                It.IsAny<object[]>()), 
                Times.AtLeastOnce());
        }

        [Fact]
        public void ValidateConfiguration_WithCriticalIssues_ShouldLogWarning()
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = "user@example.com",
                Password = "test", // Critical issue
                BaseUrl = "https://www.qobuz.com"
            };

            // Act
            _validator.ValidateConfiguration(settings);

            // Assert
            _mockLogger.Verify(l => l.Warn(
                It.IsAny<string>(), 
                It.IsAny<object[]>()), 
                Times.AtLeastOnce());
        }

        [Fact]
        public void ValidateConfiguration_WithException_ShouldLogErrorAndReturnCriticalIssue()
        {
            // Arrange
            var mockCredentialManager = new Mock<SecureCredentialManager>(_mockLogger.Object);
            mockCredentialManager.Setup(m => m.ValidateCredentialSecurity(It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new Exception("Test exception"));
            
            var validatorWithMock = new SecurityConfigValidator(_mockLogger.Object, mockCredentialManager.Object);
            
            var settings = new QobuzIndexerSettings
            {
                Email = "user@example.com",
                Password = "password123",
                BaseUrl = "https://www.qobuz.com"
            };

            // Act
            var result = validatorWithMock.ValidateConfiguration(settings);

            // Assert
            result.CriticalIssues.Should().Contain(i => i.Title.Contains("Security validation process failed"));
            _mockLogger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>(), It.IsAny<object[]>()), Times.Once());
        }

        #endregion

        #region Complex Scenario Tests

        [Fact]
        public void ValidateConfiguration_WithMixedSecurityIssues_ShouldCategorizeCorrectly()
        {
            // Arrange
            var settings = new QobuzIndexerSettings
            {
                Email = "test@test.com", // Major: test email
                Password = "test", // Critical: weak and test password
                BaseUrl = "http://www.qobuz.com", // Critical: HTTP not HTTPS
                CountryCode = "USA", // Major: invalid format
                ConnectionTimeout = 1, // Minor: too short
                ApiRateLimit = 1000, // Minor: too high
                SearchCacheDuration = 200 // Minor: too long
            };

            // Act
            var result = _validator.ValidateConfiguration(settings);

            // Assert
            result.CriticalIssues.Count.Should().BeGreaterThan(0);
            result.MajorIssues.Count.Should().BeGreaterThan(0);
            result.MinorIssues.Count.Should().BeGreaterThan(0);
            result.SecurityLevel.Should().Be(SecurityLevel.Critical);
            result.IsSecure.Should().BeFalse();
        }

        #endregion
    }
}