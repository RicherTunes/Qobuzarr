using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NLog.Config;
using NLog.Targets;
using NSubstitute;
using Xunit;
// DISABLED: AdaptiveRateLimiter has been removed - functionality consolidated into other services
// using Lidarr.Plugin.Qobuzarr.Services;

namespace Qobuzarr.Tests.Unit.Services
{
    // DISABLED: AdaptiveRateLimiter has been removed - functionality consolidated into other services
    /*
    public class AdaptiveRateLimiterTests : IDisposable
    {
        private AdaptiveRateLimiter _rateLimiter;
        private Logger _logger;

        public AdaptiveRateLimiterTests()
        {
            // Setup test logger
            var config = new LoggingConfiguration();
            var target = new MemoryTarget { Layout = "${message}" };
            config.AddTarget("memory", target);
            config.AddRule(LogLevel.Trace, LogLevel.Fatal, target);
            LogManager.Configuration = config;
            
            _logger = LogManager.GetLogger("Test");
            _rateLimiter = new AdaptiveRateLimiter(_logger);
        }

        public void Dispose()
        {
            LogManager.Configuration = null;
        }

        [Fact]
        public async Task WaitIfNeededAsync_FirstRequest_ShouldNotWait()
        {
            // Arrange
            var endpoint = "test/endpoint";
            
            // Act
            var start = DateTime.UtcNow;
            var result = await _rateLimiter.WaitIfNeededAsync(endpoint);
            var elapsed = DateTime.UtcNow - start;
            
            // Assert
            result.Should().BeTrue();
            elapsed.TotalMilliseconds.Should().BeLessThan(100);
        }

        [Fact]
        public async Task WaitIfNeededAsync_RapidRequests_ShouldEnforceRateLimit()
        {
            // Arrange
            var endpoint = "test/endpoint";
            var requestCount = 5;
            
            // Act - Make rapid requests
            var start = DateTime.UtcNow;
            for (int i = 0; i < requestCount; i++)
            {
                await _rateLimiter.WaitIfNeededAsync(endpoint);
            }
            var elapsed = DateTime.UtcNow - start;
            
            // Assert - Should take at least (requestCount-1) * (60/60) seconds
            // Default is 60 requests per minute = 1 per second
            var expectedMinTime = TimeSpan.FromSeconds((requestCount - 1) / 60.0 * 60.0);
            elapsed.Should().BeGreaterThanOrEqualTo(expectedMinTime.Subtract(TimeSpan.FromMilliseconds(50)));
        }

        [Fact]
        public void RecordResponse_Success_ShouldMaintainLimit()
        {
            // Arrange
            var endpoint = "test/endpoint";
            var successResponse = new HttpResponseMessage(HttpStatusCode.OK);
            
            // Act
            var initialLimit = _rateLimiter.GetCurrentLimit(endpoint);
            _rateLimiter.RecordResponse(endpoint, successResponse);
            var afterLimit = _rateLimiter.GetCurrentLimit(endpoint);
            
            // Assert
            initialLimit.Should().Be(60); // Default
            afterLimit.Should().Be(60); // Should not change immediately
        }

        [Fact]
        public void RecordResponse_RateLimit_ShouldReduceLimit()
        {
            // Arrange
            var endpoint = "test/endpoint";
            var rateLimitResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            
            // Act
            var initialLimit = _rateLimiter.GetCurrentLimit(endpoint);
            _rateLimiter.RecordResponse(endpoint, rateLimitResponse);
            var afterLimit = _rateLimiter.GetCurrentLimit(endpoint);
            
            // Assert
            initialLimit.Should().Be(60); // Default
            afterLimit.Should().Be(45); // 75% of 60
        }

        [Fact]
        public void RecordResponse_MultipleSuccesses_ShouldIncreaseLimit()
        {
            // Arrange
            var endpoint = "test/endpoint";
            var successResponse = new HttpResponseMessage(HttpStatusCode.OK);
            
            // Act - First trigger a rate limit to get below default
            var rateLimitResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            _rateLimiter.RecordResponse(endpoint, rateLimitResponse);
            var reducedLimit = _rateLimiter.GetCurrentLimit(endpoint);
            
            // Then record many successes
            for (int i = 0; i < 60; i++)
            {
                _rateLimiter.RecordResponse(endpoint, successResponse);
            }
            var increasedLimit = _rateLimiter.GetCurrentLimit(endpoint);
            
            // Assert
            reducedLimit.Should().Be(45); // 75% of 60
            increasedLimit.Should().BeGreaterThan(reducedLimit);
        }

        [Fact]
        public void RecordResponse_ConsecutiveErrors_ShouldReduceLimit()
        {
            // Arrange
            var endpoint = "test/endpoint";
            var errorResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);
            
            // Act
            var initialLimit = _rateLimiter.GetCurrentLimit(endpoint);
            
            // Record enough errors to trigger reduction
            for (int i = 0; i < 6; i++)
            {
                _rateLimiter.RecordResponse(endpoint, errorResponse);
            }
            var afterLimit = _rateLimiter.GetCurrentLimit(endpoint);
            
            // Assert
            initialLimit.Should().Be(60);
            afterLimit.Should().BeLessThan(initialLimit);
        }

        [Fact]
        public void GetStats_ShouldReturnAccurateStatistics()
        {
            // Arrange
            var endpoint1 = "endpoint1";
            var endpoint2 = "endpoint2";
            
            // Act
            _rateLimiter.RecordResponse(endpoint1, new HttpResponseMessage(HttpStatusCode.OK));
            _rateLimiter.RecordResponse(endpoint1, new HttpResponseMessage(HttpStatusCode.OK));
            _rateLimiter.RecordResponse(endpoint1, new HttpResponseMessage(HttpStatusCode.TooManyRequests));
            
            _rateLimiter.RecordResponse(endpoint2, new HttpResponseMessage(HttpStatusCode.OK));
            _rateLimiter.RecordResponse(endpoint2, new HttpResponseMessage(HttpStatusCode.InternalServerError));
            
            var stats = _rateLimiter.GetStats();
            
            // Assert
            stats.EndpointStats.Should().ContainKey(endpoint1);
            stats.EndpointStats.Should().ContainKey(endpoint2);
            
            var stats1 = stats.EndpointStats[endpoint1];
            stats1.TotalRequests.Should().Be(3);
            stats1.SuccessfulRequests.Should().Be(2);
            stats1.RateLimitHits.Should().Be(1);
            stats1.SuccessRate.Should().BeApproximately(0.667, 0.01);
            
            var stats2 = stats.EndpointStats[endpoint2];
            stats2.TotalRequests.Should().Be(2);
            stats2.SuccessfulRequests.Should().Be(1);
            stats2.TotalErrors.Should().Be(1);
        }

        [Fact]
        public async Task WaitIfNeededAsync_WithCancellation_ShouldRespectToken()
        {
            // Arrange
            var endpoint = "test/endpoint";
            var cts = new CancellationTokenSource();
            
            // Make one request to initialize the endpoint
            await _rateLimiter.WaitIfNeededAsync(endpoint);
            
            // Act & Assert
            cts.Cancel();
            Func<Task> act = async () => await _rateLimiter.WaitIfNeededAsync(endpoint, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public void RecordResponse_SoftRateLimit_ShouldReduceLessAggressively()
        {
            // Arrange
            var endpoint = "test/endpoint";
            
            // First, record a few errors to set up the condition
            for (int i = 0; i < 3; i++)
            {
                _rateLimiter.RecordResponse(endpoint, new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }
            
            // Act - Now the 401 should be treated as a soft rate limit
            var beforeLimit = _rateLimiter.GetCurrentLimit(endpoint);
            _rateLimiter.RecordResponse(endpoint, new HttpResponseMessage(HttpStatusCode.Unauthorized));
            var afterLimit = _rateLimiter.GetCurrentLimit(endpoint);
            
            // Assert
            beforeLimit.Should().Be(60);
            afterLimit.Should().Be(51); // 85% of 60
        }
    }
    */
}
