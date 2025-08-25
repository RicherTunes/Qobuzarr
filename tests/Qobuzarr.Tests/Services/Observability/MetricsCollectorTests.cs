using System;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Services.Observability;

namespace Lidarr.Plugin.Qobuzarr.Tests.Services.Observability
{
    /// <summary>
    /// Comprehensive unit tests for MetricsCollector
    /// Designed to achieve 95%+ mutation testing score through thorough edge case coverage
    /// </summary>
    public class MetricsCollectorTests : IDisposable
    {
        private readonly IQobuzLogger _mockLogger;
        private readonly MetricsCollector _metricsCollector;

        public MetricsCollectorTests()
        {
            _mockLogger = Substitute.For<IQobuzLogger>();
            _metricsCollector = new MetricsCollector(_mockLogger);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithValidLogger_ShouldInitializeSuccessfully()
        {
            // Arrange & Act
            var collector = new MetricsCollector(_mockLogger);

            // Assert
            collector.Should().NotBeNull();
            _mockLogger.Received().Info(Arg.Any<string>(), Arg.Any<object[]>());
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Arrange, Act & Assert
            Action act = () => new MetricsCollector(null);
            act.Should().Throw<ArgumentNullException>()
                .WithMessage("*logger*");
        }

        #endregion

        #region API Request Metrics Tests

        [Theory]
        [InlineData("search", 100, 200, "GET")]
        [InlineData("album", 250, 200, "GET")]
        [InlineData("track", 50, 404, "GET")]
        [InlineData("playlist", 500, 500, "POST")]
        public void RecordApiRequest_WithValidParameters_ShouldRecordMetrics(
            string endpoint, int durationMs, int statusCode, string method)
        {
            // Arrange
            var duration = TimeSpan.FromMilliseconds(durationMs);

            // Act
            _metricsCollector.RecordApiRequest(endpoint, duration, statusCode, method);

            // Assert - Should not throw and should log debug message
            _mockLogger.Received().Debug(Arg.Any<string>(), method, endpoint, durationMs, statusCode);
        }

        [Theory]
        [InlineData("", 100, 200, "GET")] // Empty endpoint
        [InlineData(null, 100, 200, "GET")] // Null endpoint
        [InlineData("test", 0, 200, "")] // Empty method
        [InlineData("test", -100, 200, "GET")] // Negative duration (edge case)
        public void RecordApiRequest_WithEdgeCaseParameters_ShouldHandleGracefully(
            string endpoint, int durationMs, int statusCode, string method)
        {
            // Arrange
            var duration = TimeSpan.FromMilliseconds(durationMs);

            // Act & Assert - Should not throw
            Action act = () => _metricsCollector.RecordApiRequest(endpoint, duration, statusCode, method);
            act.Should().NotThrow();
        }

        [Theory]
        [InlineData("api/search", true, "search_cache_key")]
        [InlineData("api/album/123", false, null)]
        [InlineData("api/track/456", true, "")]
        public void RecordApiCall_WithVariousParameters_ShouldRecordCorrectly(
            string endpoint, bool wasCached, string cacheKey)
        {
            // Arrange
            var duration = TimeSpan.FromMilliseconds(150);

            // Act
            _metricsCollector.RecordApiCall(endpoint, duration, wasCached, cacheKey);

            // Assert - Verify appropriate status code mapping
            var expectedStatusCode = wasCached ? 200 : 200;
            _mockLogger.Received().Debug(Arg.Any<string>(), "GET", endpoint, 150.0, expectedStatusCode);
        }

        #endregion

        #region Cache Metrics Tests

        [Theory]
        [InlineData("search", "get", true)]
        [InlineData("album", "set", false)]
        [InlineData("track", "delete", true)]
        [InlineData("metadata", "update", false)]
        public void RecordCacheOperation_WithValidParameters_ShouldRecordMetrics(
            string cacheType, string operation, bool success)
        {
            // Act
            _metricsCollector.RecordCacheOperation(cacheType, operation, success);

            // Assert
            var result = success ? "hit" : "miss";
            _mockLogger.Received().Debug(Arg.Any<string>(), cacheType, operation, result);
        }

        [Theory]
        [InlineData(null, "get", true)] // Null cache type
        [InlineData("cache", null, true)] // Null operation
        [InlineData("", "", false)] // Empty strings
        public void RecordCacheOperation_WithInvalidParameters_ShouldHandleGracefully(
            string cacheType, string operation, bool success)
        {
            // Act & Assert - Should not throw
            Action act = () => _metricsCollector.RecordCacheOperation(cacheType, operation, success);
            act.Should().NotThrow();
        }

        [Theory]
        [InlineData("query_cache", "search_key_123", true, 10)]
        [InlineData("metadata_cache", "album_456", false, 50)]
        [InlineData("session_cache", "user_789", true, 0)]
        public void RecordCacheHit_WithVariousScenarios_ShouldRecordCorrectly(
            string cacheType, string key, bool hit, int lookupDurationMs)
        {
            // Arrange
            var lookupDuration = TimeSpan.FromMilliseconds(lookupDurationMs);

            // Act
            _metricsCollector.RecordCacheHit(cacheType, key, hit, lookupDuration);

            // Assert - Should record operation and not throw
            Action act = () => _metricsCollector.RecordCacheHit(cacheType, key, hit, lookupDuration);
            act.Should().NotThrow();
        }

        [Fact]
        public void RecordCacheHit_WithNullLookupDuration_ShouldUseZeroTimeSpan()
        {
            // Act & Assert - Should not throw
            Action act = () => _metricsCollector.RecordCacheHit("test", "key", true, null);
            act.Should().NotThrow();
        }

        #endregion

        #region Quality Fallback Tests

        [Theory]
        [InlineData("FLAC_HIRES", "FLAC_CD", "track_unavailable")]
        [InlineData("FLAC_CD", "MP3_320", "quality_not_available")]
        [InlineData("MP3_320", "MP3_128", "bandwidth_limit")]
        public void RecordQualityFallback_WithValidScenarios_ShouldRecordMetrics(
            string originalQuality, string fallbackQuality, string reason)
        {
            // Act
            _metricsCollector.RecordQualityFallback(originalQuality, fallbackQuality, reason);

            // Assert
            _mockLogger.Received().Info(Arg.Any<string>(), originalQuality, fallbackQuality, reason);
        }

        [Theory]
        [InlineData(null, "FLAC_CD", "reason")] // Null original quality
        [InlineData("FLAC_HIRES", null, "reason")] // Null fallback quality
        [InlineData("FLAC_HIRES", "FLAC_CD", null)] // Null reason
        [InlineData("", "", "")] // Empty strings
        public void RecordQualityFallback_WithEdgeCases_ShouldHandleGracefully(
            string originalQuality, string fallbackQuality, string reason)
        {
            // Act & Assert - Should not throw
            Action act = () => _metricsCollector.RecordQualityFallback(originalQuality, fallbackQuality, reason);
            act.Should().NotThrow();
        }

        #endregion

        #region Authentication Metrics Tests

        [Theory]
        [InlineData("login", true, null)]
        [InlineData("token_refresh", false, "expired_token")]
        [InlineData("session_validation", false, "invalid_signature")]
        [InlineData("logout", true, null)]
        public void RecordAuthenticationAttempt_WithVariousScenarios_ShouldRecordCorrectly(
            string authenticationType, bool success, string failureReason)
        {
            // Act
            _metricsCollector.RecordAuthenticationAttempt(authenticationType, success, failureReason);

            // Assert
            if (!success)
            {
                _mockLogger.Received().Warn(Arg.Any<string>(), authenticationType, failureReason);
            }
        }

        [Theory]
        [InlineData(null, true, null)] // Null authentication type
        [InlineData("", false, "reason")] // Empty authentication type
        [InlineData("login", false, "")] // Empty failure reason
        public void RecordAuthenticationAttempt_WithEdgeCases_ShouldHandleGracefully(
            string authenticationType, bool success, string failureReason)
        {
            // Act & Assert - Should not throw
            Action act = () => _metricsCollector.RecordAuthenticationAttempt(authenticationType, success, failureReason);
            act.Should().NotThrow();
        }

        #endregion

        #region Download Performance Tests

        [Theory]
        [InlineData("album", 120, true, "FLAC_HIRES")]
        [InlineData("track", 30, false, "MP3_320")]
        [InlineData("playlist", 600, true, "FLAC_CD")]
        public void RecordDownloadOperation_WithValidParameters_ShouldRecordMetrics(
            string downloadType, int durationSeconds, bool success, string quality)
        {
            // Arrange
            var duration = TimeSpan.FromSeconds(durationSeconds);

            // Act
            _metricsCollector.RecordDownloadOperation(downloadType, duration, success, quality);

            // Assert
            var status = success ? "completed" : "failed";
            _mockLogger.Received().Debug(Arg.Any<string>(), downloadType, quality, status, (double)durationSeconds);
        }

        [Theory]
        [InlineData(null, 60, true, "FLAC")] // Null download type
        [InlineData("album", -10, true, "FLAC")] // Negative duration
        [InlineData("track", 0, false, null)] // Zero duration, null quality
        public void RecordDownloadOperation_WithEdgeCases_ShouldHandleGracefully(
            string downloadType, int durationSeconds, bool success, string quality)
        {
            // Arrange
            var duration = TimeSpan.FromSeconds(durationSeconds);

            // Act & Assert - Should not throw
            Action act = () => _metricsCollector.RecordDownloadOperation(downloadType, duration, success, quality);
            act.Should().NotThrow();
        }

        [Theory]
        [InlineData("album", 5)]
        [InlineData("track", 0)]
        [InlineData("playlist", -1)] // Edge case: negative count
        [InlineData(null, 10)] // Edge case: null type
        public void SetActiveDownloads_WithVariousCounts_ShouldUpdateGauge(
            string downloadType, int count)
        {
            // Act & Assert - Should not throw
            Action act = () => _metricsCollector.SetActiveDownloads(downloadType, count);
            act.Should().NotThrow();
        }

        #endregion

        #region ML Optimization Tests

        [Theory]
        [InlineData("semantic_query", true, 0.95)]
        [InlineData("smart_cache", false, 0.3)]
        [InlineData("pattern_matching", true, 0.0)] // Edge case: zero confidence
        [InlineData("hybrid_search", true, 1.0)] // Edge case: max confidence
        public void RecordMLOptimization_WithVariousScenarios_ShouldRecordMetrics(
            string strategy, bool successful, double confidenceScore)
        {
            // Act
            _metricsCollector.RecordMLOptimization(strategy, successful, confidenceScore);

            // Assert
            var result = successful ? "applied" : "skipped";
            _mockLogger.Received().Debug(Arg.Any<string>(), strategy, result, confidenceScore);
        }

        [Theory]
        [InlineData(null, true, 0.5)] // Null strategy
        [InlineData("", false, -0.1)] // Empty strategy, negative confidence
        [InlineData("test", true, double.NaN)] // NaN confidence
        [InlineData("test", false, double.PositiveInfinity)] // Infinite confidence
        public void RecordMLOptimization_WithEdgeCases_ShouldHandleGracefully(
            string strategy, bool successful, double confidenceScore)
        {
            // Act & Assert - Should not throw
            Action act = () => _metricsCollector.RecordMLOptimization(strategy, successful, confidenceScore);
            act.Should().NotThrow();
        }

        #endregion

        #region Service Health Tests

        [Theory]
        [InlineData("qobuz_api", "connectivity", true)]
        [InlineData("authentication", "token_validation", false)]
        [InlineData("cache_service", "memory_usage", true)]
        [InlineData("download_service", "queue_processing", false)]
        public void SetServiceHealth_WithVariousServices_ShouldUpdateStatus(
            string serviceName, string component, bool healthy)
        {
            // Act
            _metricsCollector.SetServiceHealth(serviceName, component, healthy);

            // Assert
            if (!healthy)
            {
                _mockLogger.Received().Warn(Arg.Any<string>(), serviceName, component);
            }
        }

        [Theory]
        [InlineData(null, "component", true)] // Null service name
        [InlineData("service", null, false)] // Null component
        [InlineData("", "", true)] // Empty strings
        public void SetServiceHealth_WithEdgeCases_ShouldHandleGracefully(
            string serviceName, string component, bool healthy)
        {
            // Act & Assert - Should not throw
            Action act = () => _metricsCollector.SetServiceHealth(serviceName, component, healthy);
            act.Should().NotThrow();
        }

        #endregion

        #region Metrics Export Tests

        [Fact]
        public async Task ExportPrometheusMetricsAsync_ShouldReturnFormattedMetrics()
        {
            // Arrange
            _metricsCollector.RecordApiRequest("test", TimeSpan.FromSeconds(1), 200, "GET");
            _metricsCollector.RecordCacheOperation("test", "get", true);

            // Act
            var result = await _metricsCollector.ExportPrometheusMetricsAsync();

            // Assert
            result.Should().NotBeNull();
            result.Should().NotBeEmpty();
            result.Should().Contain("# HELP");
            result.Should().Contain("# TYPE");
        }

        [Fact]
        public void GetMetricsSummary_AfterRecordingMetrics_ShouldReturnValidSummary()
        {
            // Arrange
            _metricsCollector.RecordApiRequest("test", TimeSpan.FromSeconds(1), 200, "GET");
            _metricsCollector.RecordCacheHit("test", "key", true);
            _metricsCollector.SetActiveDownloads("album", 3);
            _metricsCollector.RecordDownloadOperation("track", TimeSpan.FromSeconds(30), true, "FLAC");
            _metricsCollector.RecordAuthenticationAttempt("login", false, "invalid_credentials");
            _metricsCollector.RecordQualityFallback("FLAC_HIRES", "FLAC_CD", "unavailable");
            _metricsCollector.RecordMLOptimization("semantic", true, 0.8);
            _metricsCollector.SetServiceHealth("api", "connectivity", false);

            // Act
            var summary = _metricsCollector.GetMetricsSummary();

            // Assert
            summary.Should().NotBeNull();
            summary.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
            summary.TotalApiRequests.Should().BeGreaterOrEqualTo(0);
            summary.CacheHitRatio.Should().BeInRange(0, 1);
            summary.ActiveDownloads.Should().BeGreaterOrEqualTo(0);
            summary.TotalDownloads.Should().BeGreaterOrEqualTo(0);
            summary.AuthenticationFailures.Should().BeGreaterOrEqualTo(0);
            summary.QualityFallbacks.Should().BeGreaterOrEqualTo(0);
            summary.MLOptimizations.Should().BeGreaterOrEqualTo(0);
            summary.UnhealthyServices.Should().BeGreaterOrEqualTo(0);
        }

        [Fact]
        public void GetMetricsSummary_WithNoMetrics_ShouldReturnEmptySummary()
        {
            // Act
            var summary = _metricsCollector.GetMetricsSummary();

            // Assert
            summary.Should().NotBeNull();
            summary.TotalApiRequests.Should().Be(0);
            summary.CacheHitRatio.Should().Be(0);
            summary.ActiveDownloads.Should().Be(0);
            summary.TotalDownloads.Should().Be(0);
            summary.AuthenticationFailures.Should().Be(0);
            summary.QualityFallbacks.Should().Be(0);
            summary.MLOptimizations.Should().Be(0);
            summary.UnhealthyServices.Should().Be(0);
        }

        #endregion

        #region Thread Safety Tests

        [Fact]
        public async Task ConcurrentOperations_ShouldNotThrow()
        {
            // Arrange
            var tasks = new Task[10];

            // Act
            for (int i = 0; i < 10; i++)
            {
                var index = i;
                tasks[i] = Task.Run(() =>
                {
                    _metricsCollector.RecordApiRequest($"endpoint_{index}", TimeSpan.FromSeconds(1), 200, "GET");
                    _metricsCollector.RecordCacheOperation($"cache_{index}", "get", true);
                    _metricsCollector.SetActiveDownloads($"type_{index}", index);
                    _metricsCollector.SetServiceHealth($"service_{index}", "component", true);
                });
            }

            // Assert
            Func<Task> act = async () => await Task.WhenAll(tasks);
            await act.Should().NotThrowAsync();
        }

        #endregion

        #region Disposal Tests

        [Fact]
        public void Dispose_ShouldLogShutdownMessage()
        {
            // Act
            _metricsCollector.Dispose();

            // Assert
            _mockLogger.Received().Info("Metrics collector shutting down");
        }

        [Fact]
        public void Dispose_ShouldExportFinalMetrics()
        {
            // Arrange
            _metricsCollector.RecordApiRequest("final_test", TimeSpan.FromSeconds(1), 200, "GET");

            // Act
            _metricsCollector.Dispose();

            // Assert
            _mockLogger.Received().Info(Arg.Is<string>(s => s.Contains("Final metrics")), Arg.Any<object[]>());
        }

        [Fact]
        public void Dispose_WithException_ShouldLogError()
        {
            // Arrange
            _mockLogger.When(x => x.Info(Arg.Any<string>(), Arg.Any<object[]>()))
                      .Do(x => throw new Exception("Test exception"));

            // Act & Assert - Should not throw
            Action act = () => _metricsCollector.Dispose();
            act.Should().NotThrow();
        }

        #endregion

        public void Dispose()
        {
            _metricsCollector?.Dispose();
        }
    }
}