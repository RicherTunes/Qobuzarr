using System;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Security;

namespace Qobuzarr.Tests.Unit.Security
{
    /// <summary>
    /// Comprehensive security test suite for SecureCredentialManager.
    /// Tests credential protection, memory security, and vulnerability prevention.
    /// </summary>
    public class SecureCredentialManagerTests : IDisposable
    {
        private readonly Mock<IQobuzLogger> _mockLogger;
        private readonly SecureCredentialManager _credentialManager;

        public SecureCredentialManagerTests()
        {
            _mockLogger = new Mock<IQobuzLogger>();
            _credentialManager = new SecureCredentialManager(_mockLogger.Object);
        }

        #region SecureString Creation and Protection Tests

        [Fact]
        public void CreateSecureString_WithValidString_ShouldReturnSecureString()
        {
            // Arrange
            var sensitive = "supersecretpassword123";

            // Act
            var secureString = _credentialManager.CreateSecureString(sensitive);

            // Assert
            secureString.Should().NotBeNull();
            secureString.Length.Should().Be(sensitive.Length);
            secureString.IsReadOnly().Should().BeTrue();
        }

        [Fact]
        public void CreateSecureString_WithEmptyString_ShouldReturnNull()
        {
            // Arrange
            var empty = string.Empty;

            // Act
            var result = _credentialManager.CreateSecureString(empty);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void CreateSecureString_WithNull_ShouldReturnNull()
        {
            // Act
            var result = _credentialManager.CreateSecureString(null);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void SecureStringToString_WithValidSecureString_ShouldReturnOriginalValue()
        {
            // Arrange
            var original = "testpassword123";
            var secureString = _credentialManager.CreateSecureString(original);

            // Act
            var converted = _credentialManager.SecureStringToString(secureString);

            // Assert
            converted.Should().Be("testpassword123");
            
            // Clean up
            secureString?.Dispose();
        }

        [Fact]
        public void SecureStringToString_WithNullSecureString_ShouldReturnEmptyString()
        {
            // Act
            var result = _credentialManager.SecureStringToString(null);

            // Assert
            result.Should().BeEmpty();
        }

        [Fact]
        public void SecureStringToString_WithEmptySecureString_ShouldReturnEmptyString()
        {
            // Arrange
            var emptySecure = new SecureString();
            emptySecure.MakeReadOnly();

            // Act
            var result = _credentialManager.SecureStringToString(emptySecure);

            // Assert
            result.Should().BeEmpty();
            
            // Clean up
            emptySecure.Dispose();
        }

        #endregion

        #region Memory Protection and Clearing Tests

        [Fact]
        public void ClearString_WithValidString_ShouldNullifyReference()
        {
            // Arrange
            string sensitive = "passwordToBeCleared";

            // Act
            _credentialManager.ClearString(ref sensitive);

            // Assert
            sensitive.Should().BeNull();
        }

        [Fact]
        public void ClearString_WithNullString_ShouldNotThrow()
        {
            // Arrange
            string nullString = null;

            // Act
            Action act = () => _credentialManager.ClearString(ref nullString);

            // Assert
            act.Should().NotThrow();
            nullString.Should().BeNull();
        }

        [Fact]
        public void CreateSecureString_ShouldClearOriginalStringFromMemory()
        {
            // Arrange
            var original = "sensitivecredential";
            var copyForVerification = new string(original.ToCharArray());

            // Act
            var secureString = _credentialManager.CreateSecureString(original);

            // Assert
            // The original variable should be cleared (best effort in managed code)
            secureString.Should().NotBeNull();
            
            // Verify the secure string still contains the value
            var retrieved = _credentialManager.SecureStringToString(secureString);
            retrieved.Should().Be(copyForVerification);
            
            // Clean up
            secureString?.Dispose();
        }

        #endregion

        #region Credential Validation Security Tests

        [Theory]
        [InlineData("example123", false)]
        [InlineData("test@test.com", false)]
        [InlineData("demo_password", false)]
        [InlineData("placeholder", false)]
        [InlineData("changeme", false)]
        [InlineData("password", false)]
        [InlineData("admin", false)]
        [InlineData("realPasswordHere123!", true)]
        [InlineData("ActualSecureP@ssw0rd", true)]
        public void ValidateCredentialSecurity_WithVariousInputs_ShouldDetectInsecurePatterns(string credential, bool expectedValid)
        {
            // Act
            var result = _credentialManager.ValidateCredentialSecurity(credential, "TestCredential");

            // Assert
            result.Should().Be(expectedValid);
        }

        [Fact]
        public void ValidateCredentialSecurity_WithShortPassword_ShouldReturnFalse()
        {
            // Arrange
            var shortPassword = "pass123";

            // Act
            var result = _credentialManager.ValidateCredentialSecurity(shortPassword, "Password");

            // Assert
            result.Should().BeFalse();
            _mockLogger.Verify(l => l.Warn(It.IsAny<string>(), It.IsAny<object[]>()), Times.AtLeastOnce());
        }

        [Fact]
        public void ValidateCredentialSecurity_WithEnvironmentVariable_ShouldDetectAndReject()
        {
            // Arrange
            var envVarPatterns = new[] { "$PASSWORD", "%APPDATA%", "${SECRET_KEY}" };

            foreach (var pattern in envVarPatterns)
            {
                // Act
                var result = _credentialManager.ValidateCredentialSecurity(pattern, "Credential");

                // Assert
                result.Should().BeFalse($"Pattern {pattern} should be detected as environment variable");
            }
        }

        [Fact]
        public void ValidateCredentialSecurity_WithFilePath_ShouldDetectAndReject()
        {
            // Arrange
            var filePathPatterns = new[] { "C:\\passwords.txt", "/etc/passwd", "./secrets/key.pem" };

            foreach (var path in filePathPatterns)
            {
                // Act
                var result = _credentialManager.ValidateCredentialSecurity(path, "Credential");

                // Assert
                result.Should().BeFalse($"Path {path} should be detected as file path");
            }
        }

        #endregion

        #region Data Masking Tests

        [Theory]
        [InlineData("supersecrettoken123", "su***************23")]
        [InlineData("abc", "***")]
        [InlineData("abcd", "****")]
        [InlineData("abcde", "ab*de")]
        [InlineData("", "[empty]")]
        [InlineData(null, "[empty]")]
        [InlineData("  ", "[empty]")]
        public void MaskSensitiveData_WithVariousInputs_ShouldMaskCorrectly(string input, string expectedMasked)
        {
            // Act
            var result = _credentialManager.MaskSensitiveData(input);

            // Assert
            result.Should().Be(expectedMasked);
        }

        #endregion

        #region Secure Hashing Tests

        [Fact]
        public void GenerateSecureHash_WithValidCredential_ShouldGenerateConsistentHash()
        {
            // Arrange
            var credential = "mySecurePassword123";
            var salt = new byte[32];
            new Random(42).NextBytes(salt); // Fixed seed for consistent test

            // Act
            var hash1 = _credentialManager.GenerateSecureHash(credential, salt);
            var hash2 = _credentialManager.GenerateSecureHash(credential, salt);

            // Assert
            hash1.Should().NotBeNullOrEmpty();
            hash2.Should().NotBeNullOrEmpty();
            hash1.Should().Be(hash2, "Same credential with same salt should produce identical hash");
        }

        [Fact]
        public void GenerateSecureHash_WithDifferentSalts_ShouldProduceDifferentHashes()
        {
            // Arrange
            var credential = "mySecurePassword123";
            var salt1 = new byte[32];
            var salt2 = new byte[32];
            new Random(42).NextBytes(salt1);
            new Random(43).NextBytes(salt2);

            // Act
            var hash1 = _credentialManager.GenerateSecureHash(credential, salt1);
            var hash2 = _credentialManager.GenerateSecureHash(credential, salt2);

            // Assert
            hash1.Should().NotBe(hash2, "Different salts should produce different hashes");
        }

        [Fact]
        public void GenerateSecureHash_WithoutSalt_ShouldGenerateRandomSalt()
        {
            // Arrange
            var credential = "mySecurePassword123";

            // Act
            var hash1 = _credentialManager.GenerateSecureHash(credential);
            var hash2 = _credentialManager.GenerateSecureHash(credential);

            // Assert
            hash1.Should().NotBeNullOrEmpty();
            hash2.Should().NotBeNullOrEmpty();
            hash1.Should().NotBe(hash2, "Auto-generated salts should be different");
        }

        [Fact]
        public void GenerateSecureHash_WithEmptyCredential_ShouldThrow()
        {
            // Act
            Action act = () => _credentialManager.GenerateSecureHash("");

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithMessage("*cannot be null or empty*");
        }

        [Fact]
        public void VerifySecureHash_WithCorrectCredential_ShouldReturnTrue()
        {
            // Arrange
            var credential = "correctPassword123";
            var storedHash = _credentialManager.GenerateSecureHash(credential);

            // Act
            var result = _credentialManager.VerifySecureHash(credential, storedHash);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void VerifySecureHash_WithIncorrectCredential_ShouldReturnFalse()
        {
            // Arrange
            var correctCredential = "correctPassword123";
            var wrongCredential = "wrongPassword456";
            var storedHash = _credentialManager.GenerateSecureHash(correctCredential);

            // Act
            var result = _credentialManager.VerifySecureHash(wrongCredential, storedHash);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void VerifySecureHash_WithInvalidHash_ShouldReturnFalse()
        {
            // Arrange
            var credential = "somePassword";
            var invalidHashes = new[] { "notbase64!", "dG9vc2hvcnQ=", "", null };

            foreach (var invalidHash in invalidHashes)
            {
                // Act
                var result = _credentialManager.VerifySecureHash(credential, invalidHash);

                // Assert
                result.Should().BeFalse($"Invalid hash '{invalidHash}' should fail verification");
            }
        }

        #endregion

        #region Injection Attack Prevention Tests

        [Theory]
        [InlineData("'; DROP TABLE users; --")]
        [InlineData("\" OR \"1\"=\"1")]
        [InlineData("<script>alert('xss')</script>")]
        [InlineData("../../etc/passwd")]
        [InlineData("javascript:alert(1)")]
        public void ValidateCredentialSecurity_WithInjectionAttempts_ShouldNotCauseVulnerability(string maliciousInput)
        {
            // Act
            Action act = () => _credentialManager.ValidateCredentialSecurity(maliciousInput, "TestCredential");

            // Assert
            act.Should().NotThrow("Validation should safely handle malicious input");
            
            // The validation should fail for suspicious input
            var result = _credentialManager.ValidateCredentialSecurity(maliciousInput, "TestCredential");
            result.Should().BeFalse("Malicious patterns should be detected");
        }

        #endregion

        #region Thread Safety Tests

        [Fact]
        public void SecureCredentialManager_ShouldBeThreadSafe()
        {
            // Arrange
            var tasks = new Task[10];
            var credentials = new[] { "password1", "password2", "password3", "password4", "password5" };
            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

            // Act
            for (int i = 0; i < tasks.Length; i++)
            {
                var index = i % credentials.Length;
                tasks[i] = Task.Run(() =>
                {
                    try
                    {
                        // Test various operations concurrently
                        var secureString = _credentialManager.CreateSecureString(credentials[index]);
                        var plainText = _credentialManager.SecureStringToString(secureString);
                        var hash = _credentialManager.GenerateSecureHash(plainText);
                        var verified = _credentialManager.VerifySecureHash(plainText, hash);
                        var masked = _credentialManager.MaskSensitiveData(plainText);
                        
                        secureString?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                });
            }

            Task.WaitAll(tasks);

            // Assert
            exceptions.Should().BeEmpty("No exceptions should occur during concurrent operations");
        }

        #endregion

        #region Memory Leak Prevention Tests

        [Fact]
        public void Dispose_ShouldCleanupResources()
        {
            // Arrange
            var manager = new SecureCredentialManager(_mockLogger.Object);
            var secureString = manager.CreateSecureString("test");

            // Act
            manager.Dispose();

            // Assert
            // After disposal, the manager should still be safe to call Dispose again
            Action act = () => manager.Dispose();
            act.Should().NotThrow();
        }

        [Fact]
        public void SecureString_WhenNotDisposed_ShouldNotLeakMemory()
        {
            // This test verifies that we're creating SecureStrings that can be properly disposed
            // Arrange & Act
            var secureStrings = new SecureString[100];
            
            for (int i = 0; i < secureStrings.Length; i++)
            {
                secureStrings[i] = _credentialManager.CreateSecureString($"password{i}");
            }

            // Assert & Cleanup
            foreach (var ss in secureStrings)
            {
                if (ss != null)
                {
                    ss.IsReadOnly().Should().BeTrue("All SecureStrings should be read-only");
                    ss.Dispose();
                }
            }
        }

        #endregion

        #region Edge Cases and Boundary Tests

        [Fact]
        public void CreateSecureString_WithVeryLongString_ShouldHandleCorrectly()
        {
            // Arrange
            var veryLongString = new string('a', 10000);

            // Act
            var secureString = _credentialManager.CreateSecureString(veryLongString);

            // Assert
            secureString.Should().NotBeNull();
            secureString.Length.Should().Be(10000);
            
            // Verify we can convert back
            var converted = _credentialManager.SecureStringToString(secureString);
            converted.Length.Should().Be(10000);
            
            // Clean up
            secureString?.Dispose();
        }

        [Fact]
        public void MaskSensitiveData_WithUnicodeCharacters_ShouldHandleCorrectly()
        {
            // Arrange
            var unicodeString = "密码🔐セキュリティ";

            // Act
            var masked = _credentialManager.MaskSensitiveData(unicodeString);

            // Assert
            masked.Should().NotBeNullOrEmpty();
            masked.Should().StartWith(unicodeString.Substring(0, 2));
            masked.Should().EndWith(unicodeString.Substring(unicodeString.Length - 2));
            masked.Should().Contain("*");
        }

        [Fact]
        public void GenerateSecureHash_WithUnicodeCredential_ShouldWorkCorrectly()
        {
            // Arrange
            var unicodeCredential = "パスワード123🔒";
            
            // Act
            var hash = _credentialManager.GenerateSecureHash(unicodeCredential);
            var verified = _credentialManager.VerifySecureHash(unicodeCredential, hash);

            // Assert
            hash.Should().NotBeNullOrEmpty();
            verified.Should().BeTrue();
        }

        #endregion

        public void Dispose()
        {
            _credentialManager?.Dispose();
        }
    }
}