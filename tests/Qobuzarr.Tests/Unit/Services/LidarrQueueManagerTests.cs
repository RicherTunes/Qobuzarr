using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using FluentAssertions;
using NLog;
using Lidarr.Plugin.Qobuzarr.Services;
using Qobuzarr.Tests.Fixtures;

namespace Qobuzarr.Tests.Unit.Services
{
    public class LidarrQueueManagerTests : TestFixtureBase
    {
        private readonly LidarrQueueManager _sut;

        public LidarrQueueManagerTests()
        {
            _sut = new LidarrQueueManager(MockLogger.Object);
        }

        public override void Dispose()
        {
            _sut?.Dispose();
            base.Dispose();
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new LidarrQueueManager(null));
        }

        [Fact]
        public void Constructor_WithDefaultValues_InitializesCorrectly()
        {
            // Act
            using var manager = new LidarrQueueManager(MockLogger.Object);

            // Assert
            manager.ActiveDownloadCount.Should().Be(0);
            manager.ActiveSearchCount.Should().Be(0);
            manager.MaxConcurrentDownloads.Should().BeGreaterThan(0);
            manager.MaxConcurrentSearches.Should().BeGreaterThan(0);
        }

        [Fact]
        public void Constructor_WithCustomConcurrency_SetsCorrectLimits()
        {
            // Arrange
            const int maxDownloads = 5;
            const int maxSearches = 10;

            // Act
            using var manager = new LidarrQueueManager(MockLogger.Object, maxDownloads, maxSearches);

            // Assert
            manager.MaxConcurrentDownloads.Should().Be(maxDownloads);
            manager.MaxConcurrentSearches.Should().Be(maxSearches);
        }

        [Theory]
        [InlineData(0)] // Should use processor count
        [InlineData(-1)] // Should use processor count
        [InlineData(1)] // Minimum
        [InlineData(20)] // Maximum
        [InlineData(25)] // Above maximum, should be clamped
        public void Constructor_WithVariousConcurrencyValues_HandlesCorrectly(int concurrency)
        {
            // Act
            using var manager = new LidarrQueueManager(MockLogger.Object, concurrency, concurrency);

            // Assert
            manager.MaxConcurrentDownloads.Should().BeInRange(1, 20);
            manager.MaxConcurrentSearches.Should().BeInRange(1, 20);
        }

        #endregion

        #region Download Slot Management Tests

        [Fact]
        public async Task AcquireDownloadSlot_WithAvailableSlots_CompletesImmediately()
        {
            // Act
            var startTime = DateTime.UtcNow;
            await _sut.AcquireDownloadSlotAsync();
            var elapsed = DateTime.UtcNow - startTime;

            // Assert
            elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(100));
            _sut.ActiveDownloadCount.Should().Be(1);
        }

        [Fact]
        public async Task AcquireDownloadSlot_MultipleCalls_IncrementsActiveCount()
        {
            // Act
            await _sut.AcquireDownloadSlotAsync();
            await _sut.AcquireDownloadSlotAsync();

            // Assert
            _sut.ActiveDownloadCount.Should().Be(2);
        }

        [Fact]
        public void ReleaseDownloadSlot_WithActiveSlot_DecrementsActiveCount()
        {
            // Arrange
            _sut.AcquireDownloadSlotAsync().Wait();

            // Act
            _sut.ReleaseDownloadSlot();

            // Assert
            _sut.ActiveDownloadCount.Should().Be(0);
        }

        [Fact]
        public async Task AcquireDownloadSlot_WhenCancelled_ThrowsOperationCancelledException()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => _sut.AcquireDownloadSlotAsync(cts.Token));
        }

        [Fact]
        public async Task DownloadSlots_ConcurrentAcquisition_HandledCorrectly()
        {
            // Arrange
            const int concurrentRequests = 10;
            var tasks = new List<Task>();

            // Act
            for (int i = 0; i < concurrentRequests; i++)
            {
                tasks.Add(_sut.AcquireDownloadSlotAsync());
            }

            await Task.WhenAll(tasks);

            // Assert
            _sut.ActiveDownloadCount.Should().Be(Math.Min(concurrentRequests, _sut.MaxConcurrentDownloads));
        }

        [Fact]
        public async Task DownloadSlots_AcquireAndReleaseCycle_MaintainsConsistency()
        {
            // Act & Assert - Multiple acquire/release cycles
            for (int i = 0; i < 5; i++)
            {
                await _sut.AcquireDownloadSlotAsync();
                _sut.ActiveDownloadCount.Should().Be(1);

                _sut.ReleaseDownloadSlot();
                _sut.ActiveDownloadCount.Should().Be(0);
            }
        }

        #endregion

        #region Search Slot Management Tests

        [Fact]
        public async Task AcquireSearchSlot_WithAvailableSlots_CompletesImmediately()
        {
            // Act
            var startTime = DateTime.UtcNow;
            await _sut.AcquireSearchSlotAsync();
            var elapsed = DateTime.UtcNow - startTime;

            // Assert
            elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(100));
            _sut.ActiveSearchCount.Should().Be(1);
        }

        [Fact]
        public async Task AcquireSearchSlot_MultipleCalls_IncrementsActiveCount()
        {
            // Act
            await _sut.AcquireSearchSlotAsync();
            await _sut.AcquireSearchSlotAsync();

            // Assert
            _sut.ActiveSearchCount.Should().Be(2);
        }

        [Fact]
        public void ReleaseSearchSlot_WithActiveSlot_DecrementsActiveCount()
        {
            // Arrange
            _sut.AcquireSearchSlotAsync().Wait();

            // Act
            _sut.ReleaseSearchSlot();

            // Assert
            _sut.ActiveSearchCount.Should().Be(0);
        }

        [Fact]
        public async Task SearchSlots_ConcurrentAcquisition_HandledCorrectly()
        {
            // Arrange
            const int concurrentRequests = 10;
            var tasks = new List<Task>();

            // Act
            for (int i = 0; i < concurrentRequests; i++)
            {
                tasks.Add(_sut.AcquireSearchSlotAsync());
            }

            await Task.WhenAll(tasks);

            // Assert
            _sut.ActiveSearchCount.Should().Be(Math.Min(concurrentRequests, _sut.MaxConcurrentSearches));
        }

        #endregion

        #region Queue Status Tests

        [Fact]
        public void GetQueueStatus_WithEmptyQueue_ReturnsCorrectStatus()
        {
            // Act
            var status = _sut.GetQueueStatus();

            // Assert
            status.Should().NotBeNull();
            status.ActiveDownloads.Should().Be(0);
            status.ActiveSearches.Should().Be(0);
            status.AvailableDownloadSlots.Should().Be(_sut.MaxConcurrentDownloads);
            status.AvailableSearchSlots.Should().Be(_sut.MaxConcurrentSearches);
            status.IsDownloadQueueFull.Should().BeFalse();
            status.IsSearchQueueFull.Should().BeFalse();
        }

        [Fact]
        public async Task GetQueueStatus_WithActiveOperations_ReturnsCorrectStatus()
        {
            // Arrange
            await _sut.AcquireDownloadSlotAsync();
            await _sut.AcquireDownloadSlotAsync();
            await _sut.AcquireSearchSlotAsync();

            // Act
            var status = _sut.GetQueueStatus();

            // Assert
            status.ActiveDownloads.Should().Be(2);
            status.ActiveSearches.Should().Be(1);
            status.AvailableDownloadSlots.Should().Be(_sut.MaxConcurrentDownloads - 2);
            status.AvailableSearchSlots.Should().Be(_sut.MaxConcurrentSearches - 1);
        }

        [Fact]
        public async Task GetQueueStatus_WhenQueuesFull_ReportsCorrectly()
        {
            // Arrange - Fill all slots
            var downloadTasks = new List<Task>();
            var searchTasks = new List<Task>();

            for (int i = 0; i < _sut.MaxConcurrentDownloads; i++)
            {
                downloadTasks.Add(_sut.AcquireDownloadSlotAsync());
            }

            for (int i = 0; i < _sut.MaxConcurrentSearches; i++)
            {
                searchTasks.Add(_sut.AcquireSearchSlotAsync());
            }

            await Task.WhenAll(downloadTasks);
            await Task.WhenAll(searchTasks);

            // Act
            var status = _sut.GetQueueStatus();

            // Assert
            status.IsDownloadQueueFull.Should().BeTrue();
            status.IsSearchQueueFull.Should().BeTrue();
            status.AvailableDownloadSlots.Should().Be(0);
            status.AvailableSearchSlots.Should().Be(0);
        }

        #endregion

        #region Concurrency Limit Updates Tests

        [Fact]
        public void UpdateConcurrencyLimits_WithValidValues_UpdatesLimits()
        {
            // Arrange
            const int newMaxDownloads = 8;
            const int newMaxSearches = 12;

            // Act
            _sut.UpdateConcurrencyLimits(newMaxDownloads, newMaxSearches);

            // Assert
            _sut.MaxConcurrentDownloads.Should().Be(newMaxDownloads);
            _sut.MaxConcurrentSearches.Should().Be(newMaxSearches);
        }

        [Fact]
        public void UpdateConcurrencyLimits_WithSameValues_DoesNothing()
        {
            // Arrange
            var originalDownloads = _sut.MaxConcurrentDownloads;
            var originalSearches = _sut.MaxConcurrentSearches;

            // Act
            _sut.UpdateConcurrencyLimits(originalDownloads, originalSearches);

            // Assert
            _sut.MaxConcurrentDownloads.Should().Be(originalDownloads);
            _sut.MaxConcurrentSearches.Should().Be(originalSearches);
        }

        [Theory]
        [InlineData(0, 0)] // Should use processor count
        [InlineData(-5, -10)] // Should use processor count
        [InlineData(25, 30)] // Should be clamped to max
        public void UpdateConcurrencyLimits_WithEdgeCaseValues_HandlesCorrectly(int downloads, int searches)
        {
            // Act
            _sut.UpdateConcurrencyLimits(downloads, searches);

            // Assert
            _sut.MaxConcurrentDownloads.Should().BeInRange(1, 20);
            _sut.MaxConcurrentSearches.Should().BeInRange(1, 20);
        }

        #endregion

        #region Wait For Completion Tests

        [Fact]
        public async Task WaitForAllOperationsToComplete_WithNoActiveOperations_CompletesImmediately()
        {
            // Act
            var startTime = DateTime.UtcNow;
            await _sut.WaitForAllOperationsToCompleteAsync();
            var elapsed = DateTime.UtcNow - startTime;

            // Assert
            elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task WaitForAllOperationsToComplete_WithTimeout_CanBeCancelled()
        {
            // Arrange
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            // Fill the queue first
            for (int i = 0; i < _sut.MaxConcurrentDownloads; i++)
            {
                await _sut.AcquireDownloadSlotAsync();
            }

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => _sut.WaitForAllOperationsToCompleteAsync(cts.Token));
        }

        #endregion

        #region Statistics Tests

        [Fact]
        public void GetQueueStatistics_InitialState_ReturnsEmptyStatistics()
        {
            // Act
            var stats = _sut.GetQueueStatistics();

            // Assert
            stats.Should().NotBeNull();
            stats.TotalDownloadSlotAcquisitions.Should().Be(0);
            stats.TotalSearchSlotAcquisitions.Should().Be(0);
            stats.PeakConcurrentDownloads.Should().Be(0);
            stats.PeakConcurrentSearches.Should().Be(0);
            stats.DownloadQueueSaturations.Should().Be(0);
            stats.SearchQueueSaturations.Should().Be(0);
        }

        [Fact]
        public async Task GetQueueStatistics_AfterSlotAcquisitions_UpdatesCounters()
        {
            // Act
            await _sut.AcquireDownloadSlotAsync();
            await _sut.AcquireSearchSlotAsync();
            await _sut.AcquireDownloadSlotAsync();

            var stats = _sut.GetQueueStatistics();

            // Assert
            stats.TotalDownloadSlotAcquisitions.Should().Be(2);
            stats.TotalSearchSlotAcquisitions.Should().Be(1);
            stats.PeakConcurrentDownloads.Should().BeGreaterThan(0);
            stats.PeakConcurrentSearches.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task GetQueueStatistics_WhenQueueSaturated_TracksCorrectly()
        {
            // Arrange - Create manager with small limits
            using var manager = new LidarrQueueManager(MockLogger.Object, 2, 2);

            // Fill queues to capacity
            await manager.AcquireDownloadSlotAsync();
            await manager.AcquireDownloadSlotAsync();
            await manager.AcquireSearchSlotAsync();
            await manager.AcquireSearchSlotAsync();

            // Try to acquire more (should track saturation)
            var downloadTask = Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
                try
                {
                    await manager.AcquireDownloadSlotAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
            });

            await downloadTask;

            // Act
            var stats = manager.GetQueueStatistics();

            // Assert
            stats.PeakConcurrentDownloads.Should().Be(2);
            stats.PeakConcurrentSearches.Should().Be(2);
        }

        #endregion

        #region Concurrency Stress Tests

        [Fact]
        public async Task ConcurrentSlotOperations_StressTest_MaintainsConsistency()
        {
            // Arrange
            const int numberOfOperations = 100;
            var downloadTasks = new List<Task>();
            var searchTasks = new List<Task>();
            var random = new Random();

            // Act - Perform many concurrent acquire/release operations
            for (int i = 0; i < numberOfOperations; i++)
            {
                var delay = random.Next(1, 10); // Random delay to create race conditions

                downloadTasks.Add(Task.Run(async () =>
                {
                    await Task.Delay(delay);
                    await _sut.AcquireDownloadSlotAsync();
                    await Task.Delay(delay);
                    _sut.ReleaseDownloadSlot();
                }));

                searchTasks.Add(Task.Run(async () =>
                {
                    await Task.Delay(delay);
                    await _sut.AcquireSearchSlotAsync();
                    await Task.Delay(delay);
                    _sut.ReleaseSearchSlot();
                }));
            }

            await Task.WhenAll(downloadTasks);
            await Task.WhenAll(searchTasks);

            // Assert
            _sut.ActiveDownloadCount.Should().Be(0);
            _sut.ActiveSearchCount.Should().Be(0);

            var stats = _sut.GetQueueStatistics();
            stats.TotalDownloadSlotAcquisitions.Should().Be(numberOfOperations);
            stats.TotalSearchSlotAcquisitions.Should().Be(numberOfOperations);
        }

        [Fact]
        public async Task ConcurrentQueueStatusChecks_DoNotCauseDeadlocks()
        {
            // Arrange
            var statusCheckTasks = new List<Task<QueueStatus>>();
            var operationTasks = new List<Task>();

            // Start background operations
            for (int i = 0; i < 10; i++)
            {
                operationTasks.Add(Task.Run(async () =>
                {
                    for (int j = 0; j < 5; j++)
                    {
                        await _sut.AcquireDownloadSlotAsync();
                        await Task.Delay(10);
                        _sut.ReleaseDownloadSlot();
                    }
                }));
            }

            // Check status concurrently
            for (int i = 0; i < 20; i++)
            {
                statusCheckTasks.Add(Task.Run(() => _sut.GetQueueStatus()));
            }

            // Act & Assert - Should complete without deadlocks
            var statusResults = await Task.WhenAll(statusCheckTasks);
            await Task.WhenAll(operationTasks);

            statusResults.Should().HaveCount(20);
            statusResults.Should().AllSatisfy(status => status.Should().NotBeNull());
        }

        #endregion

        #region Edge Cases and Error Handling

        [Fact]
        public void AccessAfterDispose_ThrowsObjectDisposedException()
        {
            // Arrange
            var manager = new LidarrQueueManager(MockLogger.Object);
            manager.Dispose();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => manager.GetQueueStatus());
            Assert.ThrowsAsync<ObjectDisposedException>(() => manager.AcquireDownloadSlotAsync());
            Assert.Throws<ObjectDisposedException>(() => manager.ReleaseDownloadSlot());
        }

        [Fact]
        public void ReleaseMoreSlotsThanAcquired_DoesNotCauseNegativeCounts()
        {
            // Act - Release more slots than were acquired
            _sut.ReleaseDownloadSlot(); // Should handle gracefully
            _sut.ReleaseSearchSlot(); // Should handle gracefully

            // Assert - Counts should not go negative
            _sut.ActiveDownloadCount.Should().BeGreaterOrEqualTo(0);
            _sut.ActiveSearchCount.Should().BeGreaterOrEqualTo(0);
        }

        [Fact]
        public async Task MultipleDispose_DoesNotThrow()
        {
            // Arrange
            var manager = new LidarrQueueManager(MockLogger.Object);
            await manager.AcquireDownloadSlotAsync();

            // Act & Assert - Multiple dispose calls should not throw
            manager.Dispose();
            manager.Dispose();
            manager.Dispose();
        }

        #endregion

        #region Performance Tests

        [Fact]
        public async Task SlotAcquisition_PerformanceTest_CompletesWithinReasonableTime()
        {
            // Arrange
            const int iterations = 1000;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Act
            for (int i = 0; i < iterations; i++)
            {
                await _sut.AcquireDownloadSlotAsync();
                _sut.ReleaseDownloadSlot();
            }

            stopwatch.Stop();

            // Assert
            var averageTimePerOperation = stopwatch.ElapsedMilliseconds / (double)iterations;
            averageTimePerOperation.Should().BeLessThan(1.0, 
                "Each slot acquisition/release should average less than 1ms");
        }

        [Fact]
        public void GetQueueStatus_PerformanceTest_CompletesQuickly()
        {
            // Arrange
            const int iterations = 1000;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Act
            for (int i = 0; i < iterations; i++)
            {
                _sut.GetQueueStatus();
            }

            stopwatch.Stop();

            // Assert
            var averageTimePerOperation = stopwatch.ElapsedMilliseconds / (double)iterations;
            averageTimePerOperation.Should().BeLessThan(0.1, 
                "Each status check should average less than 0.1ms");
        }

        #endregion
    }
}