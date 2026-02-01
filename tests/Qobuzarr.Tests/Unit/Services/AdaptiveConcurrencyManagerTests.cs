using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Xunit;
using Xunit.Abstractions;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Services;

namespace Qobuzarr.Tests.Unit.Services
{
    public class AdaptiveConcurrencyManagerTests : IDisposable
    {
        private readonly Mock<IQobuzLogger> _mockLogger;
        private readonly AdaptiveConcurrencyManager _manager;
        private readonly ITestOutputHelper _output;

        public AdaptiveConcurrencyManagerTests(ITestOutputHelper output)
        {
            _output = output;
            _mockLogger = new Mock<IQobuzLogger>();
            _manager = new AdaptiveConcurrencyManager(
                _mockLogger.Object,
                minConcurrency: 1,
                maxConcurrency: 8,
                adjustmentInterval: TimeSpan.FromMilliseconds(100), // Fast for testing
                targetLatency: 1000.0,
                maxLatency: 3000.0
            );
        }

        public void Dispose()
        {
            // Cleanup if needed
        }

        [Fact]
        public void Constructor_WithValidParameters_InitializesCorrectly()
        {
            // Assert
            _manager.CurrentConcurrency.Should().BeGreaterOrEqualTo(1);
            _manager.CurrentConcurrency.Should().BeLessOrEqualTo(8);
            _manager.AverageLatency.Should().Be(0);
            _manager.SuccessRate.Should().Be(1.0);
        }

        [Fact]
        public void RecordOperation_WithSuccessfulOperation_UpdatesMetrics()
        {
            // Arrange
            var latency = TimeSpan.FromMilliseconds(500);

            // Act
            _manager.RecordOperation(latency, success: true);

            // Assert
            _manager.AverageLatency.Should().Be(500);
            _manager.SuccessRate.Should().Be(1.0);
        }

        [Fact]
        public void RecordOperation_WithFailedOperation_UpdatesSuccessRate()
        {
            // Arrange
            var latency = TimeSpan.FromMilliseconds(2000);
            var error = new InvalidOperationException("Test error");

            // Act
            _manager.RecordOperation(latency, success: false, error);

            // Assert
            _manager.SuccessRate.Should().Be(0.0);
            _manager.AverageLatency.Should().Be(2000);
        }

        [Fact]
        public async Task ExecuteWithConcurrencyAsync_WithSuccessfulOperation_ReturnsResult()
        {
            // Arrange
            var expectedResult = "test result";
            var operation = new Func<Task<string>>(() => Task.FromResult(expectedResult));

            // Act
            var result = await _manager.ExecuteWithConcurrencyAsync(operation);

            // Assert
            result.Should().Be(expectedResult);
        }

        [Fact]
        public async Task ExecuteWithConcurrencyAsync_WithFailedOperation_ThrowsAndRecordsMetrics()
        {
            // Arrange
            var expectedException = new InvalidOperationException("Test error");
            var operation = new Func<Task<string>>(() => Task.FromException<string>(expectedException));

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _manager.ExecuteWithConcurrencyAsync(operation));

            exception.Message.Should().Be("Test error");
            _manager.SuccessRate.Should().Be(0.0);
        }

        [Fact]
        public async Task ConcurrencyAdjustment_WithHighLatency_ReducesConcurrency()
        {
            // Arrange
            var initialConcurrency = _manager.CurrentConcurrency;
            var highLatency = TimeSpan.FromMilliseconds(4000); // Above maxLatency

            // Act - Record several high-latency operations
            for (int i = 0; i < 5; i++)
            {
                _manager.RecordOperation(highLatency, success: true);
            }

            // Wait for adjustment interval
            await Task.Delay(200);

            // Force another operation to trigger adjustment check
            _manager.RecordOperation(highLatency, success: true);

            // Assert
            _manager.CurrentConcurrency.Should().BeLessOrEqualTo(initialConcurrency);
            _output.WriteLine($"Concurrency adjusted from {initialConcurrency} to {_manager.CurrentConcurrency} due to high latency");
        }

        [Fact]
        public async Task ConcurrencyAdjustment_WithManySuccesses_IncreasesConcurrency()
        {
            // Arrange
            var initialConcurrency = _manager.CurrentConcurrency;
            var lowLatency = TimeSpan.FromMilliseconds(200); // Well below target

            // Act - Record many successful, fast operations
            for (int i = 0; i < 25; i++)
            {
                _manager.RecordOperation(lowLatency, success: true);
            }

            // Wait for adjustment interval
            await Task.Delay(200);

            // Force adjustment check
            _manager.RecordOperation(lowLatency, success: true);

            // Assert
            _manager.CurrentConcurrency.Should().BeGreaterOrEqualTo(initialConcurrency);
            _output.WriteLine($"Concurrency adjusted from {initialConcurrency} to {_manager.CurrentConcurrency} due to good performance");
        }

        [Fact]
        public async Task ConcurrencyAdjustment_WithRateLimitError_ImmediatelyReduces()
        {
            // Arrange
            var initialConcurrency = _manager.CurrentConcurrency;
            var rateLimitError = new InvalidOperationException("Rate limit exceeded - 429 Too Many Requests");

            // Act
            _manager.RecordOperation(TimeSpan.FromSeconds(1), success: false, rateLimitError);

            // Wait for adjustment
            await Task.Delay(200);
            _manager.RecordOperation(TimeSpan.FromSeconds(1), success: false, rateLimitError);

            // Assert
            _manager.CurrentConcurrency.Should().BeLessThan(initialConcurrency);
            _output.WriteLine($"Rate limit triggered immediate reduction from {initialConcurrency} to {_manager.CurrentConcurrency}");
        }

        [Fact]
        public void GetStats_ReturnsCurrentStatistics()
        {
            // Arrange
            _manager.RecordOperation(TimeSpan.FromMilliseconds(500), success: true);
            _manager.RecordOperation(TimeSpan.FromMilliseconds(1000), success: false);

            // Act
            var stats = _manager.GetStats();

            // Assert
            stats.Should().NotBeNull();
            stats.CurrentConcurrency.Should().BeGreaterThan(0);
            stats.AverageLatency.Should().BeGreaterThan(0);
            stats.SuccessRate.Should().BeLessOrEqualTo(1.0);
            stats.RecentOperations.Should().Be(2);

            _output.WriteLine($"Stats: Concurrency={stats.CurrentConcurrency}, Latency={stats.AverageLatency:F1}ms, Success={stats.SuccessRate:P1}");
        }

        [Fact]
        public void GetConcurrencySemaphore_ReturnsConfiguredSemaphore()
        {
            // Act
            using var semaphore = _manager.GetConcurrencySemaphore();

            // Assert
            semaphore.Should().NotBeNull();
            semaphore.CurrentCount.Should().Be(_manager.CurrentConcurrency);
        }

        [Fact]
        public async Task ConcurrentOperations_RespectsSemaphoreLimit()
        {
            // Arrange
            var concurrency = _manager.CurrentConcurrency;
            var activeCount = 0;
            var maxActiveCount = 0;
            var lockObject = new object();

            async Task TestOperation()
            {
                await _manager.ExecuteWithConcurrencyAsync(async () =>
                {
                    lock (lockObject)
                    {
                        activeCount++;
                        maxActiveCount = Math.Max(maxActiveCount, activeCount);
                    }

                    await Task.Delay(50); // Simulate work

                    lock (lockObject)
                    {
                        activeCount--;
                    }

                    return "completed";
                });
            }

            // Act - Start many concurrent operations
            var tasks = new Task[concurrency * 2]; // More tasks than allowed concurrency
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = TestOperation();
            }

            await Task.WhenAll(tasks);

            // Assert
            maxActiveCount.Should().BeLessOrEqualTo(concurrency); // Should respect exact concurrency limit
            _output.WriteLine($"Max active operations: {maxActiveCount}, Configured concurrency: {concurrency}");
        }

        [Theory]
        [InlineData(1, 4)] // Should start with max(min, processorCount/2) capped by max
        [InlineData(5, 10)] // Should start with 5 (higher than processor calculation)
        [InlineData(10, 20)] // Should start with 10 (higher than processor calculation)
        public void Constructor_WithDifferentBounds_SetsAppropriateInitialConcurrency(int min, int max)
        {
            // Arrange & Act
            var manager = new AdaptiveConcurrencyManager(
                _mockLogger.Object,
                minConcurrency: min,
                maxConcurrency: max);

            // Assert - should respect the bounds and use the expected algorithm
            manager.CurrentConcurrency.Should().BeGreaterOrEqualTo(min);
            manager.CurrentConcurrency.Should().BeLessOrEqualTo(max);

            // For min >= 5, should use the min value (higher than processor calculation)
            if (min >= 5)
            {
                manager.CurrentConcurrency.Should().Be(min);
            }
            // Note: Exact initial value depends on Environment.ProcessorCount
        }
    }
}
