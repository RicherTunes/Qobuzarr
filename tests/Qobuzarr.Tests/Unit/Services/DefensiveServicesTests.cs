using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using NLog;
using NSubstitute;
// DISABLED: Many defensive services have been removed/consolidated
// using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Qobuzarr.Tests.Unit.Services
{
    /// <summary>
    /// DISABLED: Comprehensive unit tests for defensive services
    /// Many defensive services have been removed - functionality consolidated into other services
    /// </summary>
    /*
    public class DefensiveServicesTests
    {
        private readonly Logger _mockLogger = Substitute.For<Logger>();

        #region SimpleRetryService Tests

        [Fact]
        public async Task RetryUtilities_Should_ReturnResult_OnFirstSuccess()
        {
            // Arrange
            var expectedResult = "success";
            
            // Act
            var result = await RetryUtilities.ExecuteWithRetryAsync(
                async () => await Task.FromResult(expectedResult),
                maxRetries: 3,
                initialDelayMs: 1,
                operationName: "TestOperation"
            );

            // Assert
            result.Should().Be(expectedResult);
        }

        [Fact]
        public async Task RetryUtilities_Should_RetryOnTransientFailure()
        {
            // Arrange
            var attempts = 0;
            
            // Act
            var result = await RetryUtilities.ExecuteWithRetryAsync(async () =>
            {
                attempts++;
                if (attempts < 3)
                    throw new TimeoutException("Transient failure");
                return "success";
            }, maxRetries: 3, initialDelayMs: 1, operationName: "TestOperation");

            // Assert
            result.Should().Be("success");
            attempts.Should().Be(3);
        }

        [Fact]
        public async Task RetryUtilities_Should_NotRetryOnNonTransientFailure()
        {
            // Arrange
            var attempts = 0;
            
            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await RetryUtilities.ExecuteWithRetryAsync(async () =>
                {
                    attempts++;
                    throw new InvalidOperationException("Non-transient failure");
                    return (object)null;
                }, maxRetries: 3, initialDelayMs: 1, operationName: "TestOperation");
            });

            attempts.Should().Be(1); // Should not retry
        }

        [Fact]
        public async Task RetryUtilities_Should_HandleTimeout()
        {
            // Arrange
            var attempts = 0;

            // Act & Assert
            await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await RetryUtilities.ExecuteWithRetryAsync(async () =>
                {
                    attempts++;
                    throw new TimeoutException("Simulated timeout");
                    return (object)null;
                }, maxRetries: 2, initialDelayMs: 1, operationName: "TimeoutTest");
            });
            
            // Should retry up to maxRetries
            attempts.Should().Be(2);
        }

        #endregion

        #region DataValidationService Tests

        [Fact]
        public void DataValidationService_Should_SanitizeInvalidFileNames()
        {
            // Arrange
            var service = new DataValidationService(_mockLogger);
            var invalidFileName = "test<>:\"/\\|?*.txt";

            // Act
            var sanitized = service.SanitizeFileName(invalidFileName);

            // Assert
            sanitized.Should().NotContain("<");
            sanitized.Should().NotContain(">");
            sanitized.Should().NotContain(":");
            sanitized.Should().NotContain("\"");
            sanitized.Should().NotContain("/");
            sanitized.Should().NotContain("\\");
            sanitized.Should().NotContain("|");
            sanitized.Should().NotContain("?");
            sanitized.Should().NotContain("*");
        }

        [Fact]
        public void DataValidationService_Should_PreventPathTraversal()
        {
            // Arrange
            var service = new DataValidationService(_mockLogger);
            var maliciousFileName = "../../etc/passwd";

            // Act
            var sanitized = service.SanitizeFileName(maliciousFileName);

            // Assert
            sanitized.Should().NotContain("..");
            sanitized.Should().NotContain("/");
            sanitized.Should().NotContain("\\");
        }

        [Fact]
        public void DataValidationService_Should_HandleNullFileName()
        {
            // Arrange
            var service = new DataValidationService(_mockLogger);

            // Act
            var sanitized = service.SanitizeFileName(null);

            // Assert
            sanitized.Should().Be("Unknown");
        }

        [Fact]
        public void DataValidationService_Should_TruncateLongFileNames()
        {
            // Arrange
            var service = new DataValidationService(_mockLogger);
            var longFileName = new string('a', 300) + ".mp3";

            // Act
            var sanitized = service.SanitizeFileName(longFileName);

            // Assert
            sanitized.Length.Should().BeLessOrEqualTo(255);
            sanitized.Should().EndWith("...");
        }

        [Fact]
        public void DataValidationService_Should_ValidateTrackData()
        {
            // Arrange
            var service = new DataValidationService(_mockLogger);
            var track = new TestTrack { Title = "Test Title", Artist = "Test Artist" };

            // Act
            var result = service.ValidateTrackData(
                track,
                t => t.Title,
                t => t.Artist
            );

            // Assert
            result.IsValid.Should().BeTrue();
            result.Data.Should().Be(track);
        }

        [Fact]
        public void DataValidationService_Should_RejectNullTrack()
        {
            // Arrange
            var service = new DataValidationService(_mockLogger);

            // Act
            var result = service.ValidateTrackData<TestTrack>(
                null,
                t => t?.Title,
                t => t?.Artist
            );

            // Assert
            result.IsValid.Should().BeFalse();
            result.ErrorMessage.Should().Contain("null");
        }

        [Fact]
        public void DataValidationService_Should_DetectDuplicates()
        {
            // Arrange
            var service = new DataValidationService(_mockLogger);
            var tracks = new[]
            {
                new TestTrack { Title = "Same Song", Artist = "Artist", Duration = TimeSpan.FromMinutes(3) },
                new TestTrack { Title = "Same Song", Artist = "Artist", Duration = TimeSpan.FromMinutes(3.5) },
                new TestTrack { Title = "Different Song", Artist = "Artist", Duration = TimeSpan.FromMinutes(4) }
            };

            // Act
            var result = service.DetectDuplicates(
                tracks,
                t => t.Title,
                t => t.Artist,
                t => t.Duration
            );

            // Assert
            result.HasDuplicates.Should().BeTrue();
            result.DuplicateCount.Should().Be(1);
            result.RecommendedTracks.Count.Should().Be(2); // One duplicate removed
        }

        #endregion

        #region DefensiveServiceWrapper Tests

        [Fact]
        public void DefensiveWrapper_Should_ReturnFallbackOnException()
        {
            // Arrange
            var failingService = Substitute.For<ITestService>();
            failingService.GetValue().Returns(x => throw new Exception("Service failed"));
            var wrapper = new DefensiveServiceWrapper<ITestService>(failingService, _mockLogger);

            // Act
            var result = wrapper.ExecuteSafely(
                s => s.GetValue(),
                fallbackValue: "fallback"
            );

            // Assert
            result.Should().Be("fallback");
            wrapper.ConsecutiveFailures.Should().Be(1);
        }

        [Fact]
        public void DefensiveWrapper_Should_OpenCircuitAfterMaxFailures()
        {
            // Arrange
            var failingService = Substitute.For<ITestService>();
            failingService.GetValue().Returns(x => throw new Exception("Service failed"));
            var wrapper = new DefensiveServiceWrapper<ITestService>(failingService, _mockLogger);

            // Act - Fail 5 times to open circuit
            for (int i = 0; i < 5; i++)
            {
                wrapper.ExecuteSafely(s => s.GetValue(), "fallback");
            }

            // Assert
            wrapper.IsHealthy.Should().BeFalse();
            
            // Subsequent calls should return fallback immediately without calling service
            failingService.ClearReceivedCalls();
            var result = wrapper.ExecuteSafely(s => s.GetValue(), "immediate fallback");
            
            result.Should().Be("immediate fallback");
            failingService.DidNotReceive().GetValue();
        }

        [Fact]
        public void DefensiveWrapper_Should_ResetOnSuccess()
        {
            // Arrange
            var service = Substitute.For<ITestService>();
            var callCount = 0;
            service.GetValue().Returns(x =>
            {
                callCount++;
                if (callCount <= 2) throw new Exception("Failed");
                return "success";
            });
            var wrapper = new DefensiveServiceWrapper<ITestService>(service, _mockLogger);

            // Act - Fail twice, then succeed
            wrapper.ExecuteSafely(s => s.GetValue(), "fallback");
            wrapper.ExecuteSafely(s => s.GetValue(), "fallback");
            var result = wrapper.ExecuteSafely(s => s.GetValue(), "fallback");

            // Assert
            result.Should().Be("success");
            wrapper.ConsecutiveFailures.Should().Be(0);
            wrapper.IsHealthy.Should().BeTrue();
        }

        #endregion

        #region CacheValidationService Tests

        [Fact]
        public void CacheValidation_Should_HandleNullKey()
        {
            // Arrange
            var tempDir = Path.GetTempPath();
            var service = new CacheValidationService(tempDir, logger: _mockLogger);

            // Act
            var result = service.ValidateCacheEntry(null);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Reason.Should().Contain("null");
        }

        [Fact]
        public void CacheValidation_Should_DetectExpiredEntries()
        {
            // Arrange
            var tempDir = Path.GetTempPath();
            var service = new CacheValidationService(tempDir, defaultExpiry: TimeSpan.FromMilliseconds(1), logger: _mockLogger);
            var key = "test_key";
            var fileName = "test_file.cache";
            
            // Create a temporary file
            var filePath = Path.Combine(tempDir, fileName);
            File.WriteAllText(filePath, "test content");
            
            service.AddCacheEntry(key, fileName, TimeSpan.FromMilliseconds(1));
            Thread.Sleep(10); // Wait for expiry

            // Act
            var result = service.ValidateCacheEntry(key);

            // Assert
            result.IsValid.Should().BeFalse();
            result.Reason.Should().Contain("expired");
            
            // Cleanup
            File.Delete(filePath);
        }

        [Fact]
        public void CacheValidation_Should_HandleMissingFile()
        {
            // Arrange
            var tempDir = Path.GetTempPath();
            var service = new CacheValidationService(tempDir, logger: _mockLogger);
            
            // Add entry without creating file
            service.AddCacheEntry("missing_key", "nonexistent.cache");

            // Act
            var result = service.ValidateCacheEntry("missing_key");

            // Assert
            result.IsValid.Should().BeFalse();
        }

        #endregion

        #region ApiHealthMonitor Tests

        [Fact]
        public void ApiHealth_Should_TrackSuccessRate()
        {
            // Arrange
            var monitor = new ApiHealthMonitor(_mockLogger);
            var endpoint = "/api/test";

            // Act
            monitor.RecordSuccess(endpoint, TimeSpan.FromMilliseconds(100));
            monitor.RecordSuccess(endpoint, TimeSpan.FromMilliseconds(150));
            monitor.RecordFailure(endpoint, "Timeout");

            // Assert
            var isHealthy = monitor.IsEndpointHealthy(endpoint);
            isHealthy.Should().BeTrue(); // 66% success rate > 50% threshold
        }

        [Fact]
        public void ApiHealth_Should_RecommendDelayAfterFailures()
        {
            // Arrange
            var monitor = new ApiHealthMonitor(_mockLogger);
            var endpoint = "/api/test";

            // Act
            monitor.RecordFailure(endpoint, "RateLimit");
            monitor.RecordFailure(endpoint, "RateLimit");
            var delay = monitor.GetRecommendedDelay(endpoint);

            // Assert
            delay.Should().BeGreaterThan(TimeSpan.Zero);
        }

        #endregion

        #region SafeOperationExecutor Tests

        [Fact]
        public void SafeOperator_Should_ValidateStrings()
        {
            // Arrange & Act
            var validated = SafeOperationExecutor.ValidateString(null, "fallback");
            var empty = SafeOperationExecutor.ValidateString("", "fallback");
            var tooLong = SafeOperationExecutor.ValidateString(new string('a', 20000), "fallback");

            // Assert
            validated.Should().Be("fallback");
            empty.Should().Be("fallback");
            tooLong.Should().EndWith("...");
            tooLong.Length.Should().BeLessThan(2000);
        }

        [Fact]
        public void SafeOperator_Should_ValidateNumericRanges()
        {
            // Arrange & Act
            var inRange = SafeOperationExecutor.ValidateNumeric(50, 0, 100, -1);
            var tooLow = SafeOperationExecutor.ValidateNumeric(-10, 0, 100, 50);
            var tooHigh = SafeOperationExecutor.ValidateNumeric(200, 0, 100, 50);

            // Assert
            inRange.Should().Be(50);
            tooLow.Should().Be(50);
            tooHigh.Should().Be(50);
        }

        #endregion

        // Test helper classes
        public interface ITestService
        {
            string GetValue();
        }

        private class TestTrack
        {
            public string Title { get; set; }
            public string Artist { get; set; }
            public TimeSpan? Duration { get; set; }
        }
    }
    */
}
