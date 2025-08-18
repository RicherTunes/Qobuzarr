using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using NLog;
using NSubstitute;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Qobuzarr.Tests.Integration
{
    /// <summary>
    /// Integration tests that verify services work together correctly
    /// Tests the full defensive service stack in realistic scenarios
    /// </summary>
    public class ServiceIntegrationTests : IDisposable
    {
        private readonly string _testCacheDir;
        private readonly string _testConfigFile;
        private readonly ServiceIntegrationLayer _serviceLayer;
        private readonly Logger _mockLogger = Substitute.For<Logger>();

        public ServiceIntegrationTests()
        {
            // Setup test environment
            _testCacheDir = Path.Combine(Path.GetTempPath(), $"qobuz_test_{Guid.NewGuid()}");
            _testConfigFile = Path.Combine(_testCacheDir, "config.json");
            
            Directory.CreateDirectory(_testCacheDir);
            File.WriteAllText(_testConfigFile, "{}"); // Empty config
            
            // Initialize service layer
            _serviceLayer = new ServiceIntegrationLayer(_mockLogger);
            _serviceLayer.InitializeCacheService(_testCacheDir, 10); // 10MB cache limit for tests
            _serviceLayer.InitializeConfigurationMonitor(_testConfigFile);
        }

        [Fact]
        public void ServiceLayer_Should_InitializeSuccessfully()
        {
            // Assert
            _serviceLayer.Should().NotBeNull();
            _serviceLayer.SafeValidator.Should().NotBeNull();
            _serviceLayer.ApiHealth.Should().NotBeNull();
        }

        [Fact]
        public async Task ServiceLayer_Should_HandleFailuresCascadingGracefully()
        {
            // Arrange
            var track = new { Title = "Test Song", Artist = "Test Artist" };

            // Act - Force some services to fail
            for (int i = 0; i < 10; i++)
            {
                // This should trigger defensive measures
                _serviceLayer.SafeValidator.ExecuteSafely<object>(
                    v => throw new Exception("Simulated failure"),
                    fallbackValue: null,
                    operationName: "SimulatedFailure"
                );
            }

            // Even after failures, other operations should work with fallbacks
            var sanitized = _serviceLayer.SanitizePathSafely(_testCacheDir, "test<>file.mp3");
            var cacheValid = _serviceLayer.IsCacheValid("nonexistent_key");
            var config = _serviceLayer.GetCurrentConfiguration();

            // Assert
            sanitized.Should().NotBeNullOrEmpty();
            sanitized.Should().NotContain("<>");
            cacheValid.Should().BeFalse();
            config.Should().NotBeNull();
        }

        [Fact]
        public async Task FullWorkflow_Should_ProcessTrackWithAllDefensiveMeasures()
        {
            // Arrange
            var trackTitle = "Test Song with <invalid> characters";
            var artistName = "../../../malicious/path/injection";
            var expectedSanitized = "Test Song with (invalid) characters";

            // Act - Full defensive workflow
            
            // 1. Sanitize inputs
            var sanitizedPath = _serviceLayer.SanitizePathSafely(_testCacheDir, $"{artistName} - {trackTitle}.mp3");
            
            // 2. Check cache (should miss)
            var cacheHit = _serviceLayer.IsCacheValid($"{artistName}_{trackTitle}");
            
            // 3. Record API interaction
            _serviceLayer.RecordApiSuccess("/api/search", TimeSpan.FromMilliseconds(250));
            
            // 4. Get recommended delay
            var delay = _serviceLayer.GetApiDelay("/api/search");
            
            // 5. Get health report
            var health = _serviceLayer.GetHealthReport();

            // Assert
            sanitizedPath.Should().NotContain("..");
            sanitizedPath.Should().NotContain("<");
            sanitizedPath.Should().NotContain(">");
            cacheHit.Should().BeFalse();
            delay.Should().Be(TimeSpan.Zero); // No delay for healthy endpoint
            health.Should().NotBeNull();
            health.ValidatorHealthy.Should().BeTrue();
        }

        [Fact]
        public async Task ApiHealth_Should_AdaptToFailurePatterns()
        {
            // Arrange
            var endpoint = "/api/test";

            // Act - Simulate rate limiting scenario
            for (int i = 0; i < 5; i++)
            {
                _serviceLayer.RecordApiFailure(endpoint, new Exception("429 Too Many Requests"));
            }

            var delayAfterFailures = _serviceLayer.GetApiDelay(endpoint);
            
            // Now simulate recovery
            for (int i = 0; i < 10; i++)
            {
                _serviceLayer.RecordApiSuccess(endpoint, TimeSpan.FromMilliseconds(100));
            }

            var delayAfterRecovery = _serviceLayer.GetApiDelay(endpoint);

            // Assert
            delayAfterFailures.Should().BeGreaterThan(TimeSpan.Zero);
            delayAfterRecovery.Should().BeLessThan(delayAfterFailures);
        }

        [Fact]
        public void PathSanitization_Should_HandleComplexScenarios()
        {
            // Arrange
            var testCases = new[]
            {
                ("normal_file.mp3", true),
                ("file<with>illegal:chars?.mp3", true),
                ("../../etc/passwd", true),
                ("", true), // Empty should get fallback
                (null, true), // Null should get fallback
                (new string('a', 500) + ".mp3", true), // Too long
                ("CON.mp3", true), // Windows reserved name
                ("file\nwith\nnewlines.mp3", true)
            };

            // Act & Assert
            foreach (var (input, shouldSucceed) in testCases)
            {
                var result = _serviceLayer.SanitizePathSafely(_testCacheDir, input);
                
                if (shouldSucceed)
                {
                    result.Should().NotBeNullOrEmpty();
                    result.Should().StartWith(_testCacheDir);
                    result.Should().NotContain("..");
                    result.Should().NotContain("\n");
                    
                    // Should be valid path
                    Path.GetFileName(result).Should().NotBeNullOrEmpty();
                }
            }
        }

        [Fact]
        public void ServiceHealth_Should_ReflectActualState()
        {
            // Arrange & Act
            var initialHealth = _serviceLayer.GetHealthReport();

            // Force validator to become unhealthy
            for (int i = 0; i < 6; i++)
            {
                _serviceLayer.SafeValidator.ExecuteSafely<object>(
                    v => throw new Exception("Force unhealthy"),
                    fallbackValue: null
                );
            }

            var unhealthyReport = _serviceLayer.GetHealthReport();

            // Assert
            initialHealth.AllHealthy.Should().BeTrue();
            unhealthyReport.ValidatorHealthy.Should().BeFalse();
            unhealthyReport.AllHealthy.Should().BeFalse();
        }

        [Fact]
        public async Task RetryUtilities_Integration_Should_WorkWithOtherServices()
        {
            // Arrange
            var attemptCount = 0;
            var expectedResult = "success";

            // Act - Use retry utilities with simulated API call
            var result = await RetryUtilities.ExecuteWithRetryAsync(async () =>
            {
                attemptCount++;
                
                // Record attempt in API health
                if (attemptCount < 2)
                {
                    _serviceLayer.RecordApiFailure("/api/retry", new TimeoutException());
                    throw new TimeoutException("Simulated timeout");
                }
                
                _serviceLayer.RecordApiSuccess("/api/retry", TimeSpan.FromMilliseconds(100));
                return expectedResult;
            }, maxRetries: 3, initialDelayMs: 1, operationName: "RetryServiceIntegrationTest");

            // Assert
            result.Should().Be(expectedResult);
            attemptCount.Should().Be(2);
            
            // API health should reflect the pattern
            var delay = _serviceLayer.GetApiDelay("/api/retry");
            delay.Should().BeGreaterOrEqualTo(TimeSpan.Zero);
        }

        [Fact]
        public void ConfigChanges_Should_NotBreakOperations()
        {
            // Arrange
            var config1 = _serviceLayer.GetCurrentConfiguration();

            // Act - Simulate config file change
            File.WriteAllText(_testConfigFile, "{ \"updated\": true }");
            System.Threading.Thread.Sleep(200); // Wait for file watcher

            var config2 = _serviceLayer.GetCurrentConfiguration();

            // Operations should continue working regardless
            var sanitized = _serviceLayer.SanitizePathSafely(_testCacheDir, "test.mp3");

            // Assert
            config1.Should().NotBeNull();
            config2.Should().NotBeNull();
            sanitized.Should().NotBeNullOrEmpty();
        }

        public void Dispose()
        {
            try
            {
                _serviceLayer?.Dispose();
                
                if (Directory.Exists(_testCacheDir))
                {
                    Directory.Delete(_testCacheDir, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}