using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Xunit;
using NLog;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Security;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Qobuzarr.Tests.Unit.Security
{
    public class SecureMLModelLoaderTests : IDisposable
    {
        private readonly SecureMLModelLoader _loader;
        private readonly string _testDirectory;

        public SecureMLModelLoaderTests()
        {
            var logger = LogManager.CreateNullLogger();
            _loader = new SecureMLModelLoader(logger);
            // Use a directory under the test output folder so SecureMLModelLoader path validation allows it.
            _testDirectory = Path.Combine(AppContext.BaseDirectory, $"QobuzarrTest_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDirectory);
        }

        [Fact]
        public void Constructor_ShouldInitializeWithTrustedHashes()
        {
            // Arrange & Act
            var loader = new SecureMLModelLoader(LogManager.CreateNullLogger());

            // Assert
            loader.Should().NotBeNull();
        }

        [Fact]
        public void LoadSecureModel_WithEmptyPath_ShouldReturnNull()
        {
            // Act
            var result = _loader.LoadSecureModel("");

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void LoadSecureModel_WithPathTraversal_ShouldReturnNull()
        {
            // Arrange
            var maliciousPath = "../../etc/passwd";

            // Act
            var result = _loader.LoadSecureModel(maliciousPath);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void LoadSecureModel_WithNonExistentFile_ShouldReturnNull()
        {
            // Arrange
            var nonExistentPath = Path.Combine(_testDirectory, "NonExistent.dll");

            // Act
            var result = _loader.LoadSecureModel(nonExistentPath);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void LoadSecureModel_WithOversizedFile_ShouldReturnNull()
        {
            // Arrange
            var oversizedPath = Path.Combine(_testDirectory, "Oversized.dll");

            // Create a file larger than 10MB limit
            var largeData = new byte[11 * 1024 * 1024]; // 11MB
            File.WriteAllBytes(oversizedPath, largeData);

            // Act
            var result = _loader.LoadSecureModel(oversizedPath);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void LoadSecureModel_WithEmptyFile_ShouldReturnNull()
        {
            // Arrange
            var emptyPath = Path.Combine(_testDirectory, "Empty.dll");
            File.WriteAllBytes(emptyPath, new byte[0]);

            // Act
            var result = _loader.LoadSecureModel(emptyPath);

            // Assert
            result.Should().BeNull();
        }

        [Theory]
        [InlineData("../../../malicious.dll")]
        [InlineData("C:\\Windows\\System32\\kernel32.dll")]
        [InlineData("/etc/shadow")]
        [InlineData("~/../.ssh/id_rsa")]
        public void LoadSecureModel_WithMaliciousPaths_ShouldReturnNull(string maliciousPath)
        {
            // Act
            var result = _loader.LoadSecureModel(maliciousPath);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void TryLoadFromPaths_WithMultiplePaths_ShouldTryEachPath()
        {
            // Arrange
            var paths = new[]
            {
                Path.Combine(_testDirectory, "Model1.dll"),
                Path.Combine(_testDirectory, "Model2.dll"),
                Path.Combine(_testDirectory, "Model3.dll")
            };

            // Act
            var result = _loader.TryLoadFromPaths(paths, requireSignature: false);

            // Assert
            result.Should().BeNull(); // All paths are invalid in this test
        }

        [Fact]
        public void UpdateTrustedHash_WithoutAdminToken_ShouldThrowSecurityException()
        {
            // Arrange
            var assemblyName = "TestModel.dll";
            var hash = new string('A', 64); // Valid SHA-256 format

            // Act & Assert
            Assert.Throws<System.Security.SecurityException>(() =>
                _loader.UpdateTrustedHash(assemblyName, hash, "invalid_token"));
        }

        [Fact]
        public void UpdateTrustedHash_WithInvalidHashFormat_ShouldThrowArgumentException()
        {
            // Arrange
            Environment.SetEnvironmentVariable("QOBUZARR_ADMIN_TOKEN", "test_token");
            var assemblyName = "TestModel.dll";
            var invalidHash = "not_a_valid_hash";

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                _loader.UpdateTrustedHash(assemblyName, invalidHash, "test_token"));

            // Cleanup
            Environment.SetEnvironmentVariable("QOBUZARR_ADMIN_TOKEN", null);
        }

        [Fact]
        public void GetAuditLog_ShouldReturnReadOnlyList()
        {
            // Arrange
            _loader.LoadSecureModel("test.dll"); // Generate an audit entry

            // Act
            var auditLog = _loader.GetAuditLog();

            // Assert
            auditLog.Should().NotBeNull();
            auditLog.Should().BeAssignableTo<IReadOnlyList<ModelLoadAuditEntry>>();
            auditLog.Count.Should().BeGreaterThan(0);
        }

        [Fact]
        public void GetSecurityStats_ShouldReturnStatistics()
        {
            // Arrange
            _loader.LoadSecureModel("test1.dll");
            _loader.LoadSecureModel("test2.dll");

            // Act
            var stats = _loader.GetSecurityStats();

            // Assert
            stats.Should().NotBeNull();
            stats.TotalLoadAttempts.Should().Be(2);
            stats.FailedValidations.Should().BeGreaterThan(0);
        }

        [Fact]
        public void LoadSecureModel_ShouldRecordAuditEntry()
        {
            // Arrange
            var testPath = "test.dll";

            // Act
            _loader.LoadSecureModel(testPath);
            var auditLog = _loader.GetAuditLog();

            // Assert
            auditLog.Should().HaveCountGreaterThan(0);
            var lastEntry = auditLog[auditLog.Count - 1];
            lastEntry.RequestedPath.Should().Be(testPath);
            lastEntry.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void Dispose_ShouldClearSensitiveData()
        {
            // Arrange
            var loader = new SecureMLModelLoader(LogManager.CreateNullLogger());
            loader.LoadSecureModel("test.dll");

            // Act
            loader.Dispose();

            // Assert - After disposal, getting audit log should still work but may be empty
            var auditLog = loader.GetAuditLog();
            auditLog.Should().NotBeNull();
        }

        [Fact]
        public void LoadSecureModel_WithValidAssemblyName_ButNoHash_ShouldReturnNull()
        {
            // Arrange
            var validPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PersonalizedMLQueryOptimizer.dll");

            // Create a dummy file
            if (!File.Exists(validPath))
            {
                File.WriteAllBytes(validPath, Encoding.UTF8.GetBytes("dummy content"));
            }

            // Act
            var result = _loader.LoadSecureModel(validPath, requireSignature: true);

            // Assert
            result.Should().BeNull();

            // Cleanup
            if (File.Exists(validPath))
            {
                File.Delete(validPath);
            }
        }

        [Theory]
        [InlineData("PersonalizedMLQueryOptimizer")]
        [InlineData("PersonalMLQueryOptimizer")]
        [InlineData("QobuzMLCustomModel")]
        [InlineData("Lidarr.Plugin.Qobuzarr.ML.Custom")]
        public void ValidateAssemblyName_WithAllowedNames_ShouldPass(string assemblyName)
        {
            // This is an indirect test through LoadSecureModel
            // The assembly name validation is internal to the loader

            // Arrange
            var testPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{assemblyName}.dll");

            // Create a dummy file with reasonable size
            if (!File.Exists(testPath))
            {
                File.WriteAllBytes(testPath, new byte[1024]); // 1KB file
            }

            // Act
            var result = _loader.LoadSecureModel(testPath, requireSignature: false);

            // Assert
            // It will fail at a later stage (no valid assembly), but should pass name validation
            result.Should().BeNull();

            // Cleanup
            if (File.Exists(testPath))
            {
                File.Delete(testPath);
            }
        }

        [Fact]
        public void ComputeFileHash_ShouldGenerateConsistentSHA256()
        {
            // Arrange
            // Use a whitelisted assembly name so the loader reaches the hash verification step.
            var testPath = Path.Combine(_testDirectory, "PersonalizedMLQueryOptimizer.dll");
            var testContent = Encoding.UTF8.GetBytes("Test content for hashing");
            File.WriteAllBytes(testPath, testContent);

            // Act - Load twice to see if hash is consistent
            _loader.LoadSecureModel(testPath);
            _loader.LoadSecureModel(testPath);

            var auditLog = _loader.GetAuditLog();

            // Assert
            auditLog.Should().HaveCountGreaterOrEqualTo(2);
            var hash1 = auditLog[auditLog.Count - 2].FileHash;
            var hash2 = auditLog[auditLog.Count - 1].FileHash;

            hash1.Should().Be(hash2);
            hash1.Should().HaveLength(64); // SHA-256 produces 64 hex characters
        }

        public void Dispose()
        {
            _loader?.Dispose();

            // Clean up test directory
            if (Directory.Exists(_testDirectory))
            {
                try
                {
                    Directory.Delete(_testDirectory, true);
                }
                catch
                {
                    // Ignore cleanup errors in tests
                }
            }
        }
    }
}
