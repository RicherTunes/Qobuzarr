using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using FluentAssertions;
using NLog;
using NzbDrone.Core.Download;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Qobuzarr.Tests.Fixtures;

namespace Qobuzarr.Tests.Unit.Download.Services
{
    /// <summary>
    /// Enhanced tests for DownloadQueueService focusing on concurrency, performance, and edge cases.
    /// Complements the existing DownloadQueueServiceTests with advanced scenarios.
    /// </summary>
    public class EnhancedDownloadQueueServiceTests : TestFixtureBase
    {
        private readonly Mock<IDownloadFileService> _mockFileService;
        private readonly DownloadQueueService _sut;

        public EnhancedDownloadQueueServiceTests()
        {
            _mockFileService = new Mock<IDownloadFileService>();
            _sut = new DownloadQueueService(_mockFileService.Object, MockLogger.Object);
        }

        #region Concurrency Tests

        [Fact]
        public async Task ConcurrentAddOperations_WithDifferentItems_AllSucceed()
        {
            // Arrange
            const int concurrentOperations = 50;
            var tasks = new List<Task>();
            var addedIds = new ConcurrentBag<string>();

            // Act
            for (int i = 0; i < concurrentOperations; i++)
            {
                var id = $"concurrent-{i}";
                tasks.Add(Task.Run(() =>
                {
                    var item = CreateTestDownloadItem(id, $"Album {i}");
                    _sut.AddDownload(item);
                    addedIds.Add(id);
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            _sut.ActiveDownloadCount.Should().Be(concurrentOperations);
            addedIds.Should().HaveCount(concurrentOperations);

            // Verify all items are retrievable
            foreach (var id in addedIds)
            {
                _sut.TryGetDownload(id, out var item).Should().BeTrue();
                item.Should().NotBeNull();
            }
        }

        [Fact]
        public async Task ConcurrentAddOperations_WithSameId_OnlyOneSucceeds()
        {
            // Arrange
            const int concurrentOperations = 20;
            const string duplicateId = "duplicate-test";
            var tasks = new List<Task>();
            var exceptions = new ConcurrentBag<Exception>();

            // Act
            for (int i = 0; i < concurrentOperations; i++)
            {
                var index = i; // Capture for closure
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        var item = CreateTestDownloadItem(duplicateId, $"Album {index}");
                        _sut.AddDownload(item);
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            _sut.ActiveDownloadCount.Should().Be(1);
            exceptions.Should().BeEmpty(); // No exceptions should be thrown, just warnings logged
            _sut.TryGetDownload(duplicateId, out var retrievedItem).Should().BeTrue();
            retrievedItem.Should().NotBeNull();
        }

        [Fact]
        public async Task ConcurrentRemoveOperations_WithExistingItems_HandleCorrectly()
        {
            // Arrange
            const int itemCount = 30;
            var itemIds = new List<string>();

            // Add items first
            for (int i = 0; i < itemCount; i++)
            {
                var id = $"remove-test-{i}";
                itemIds.Add(id);
                var item = CreateTestDownloadItem(id, $"Album {i}");
                _sut.AddDownload(item);
            }

            var removeTasks = new List<Task<bool>>();
            var removeResults = new ConcurrentBag<bool>();

            // Act - Remove items concurrently
            foreach (var id in itemIds)
            {
                removeTasks.Add(Task.Run(() =>
                {
                    var result = _sut.RemoveDownload(id);
                    removeResults.Add(result);
                    return result;
                }));
            }

            var results = await Task.WhenAll(removeTasks);

            // Assert
            results.Should().AllSatisfy(result => result.Should().BeTrue());
            _sut.ActiveDownloadCount.Should().Be(0);
            removeResults.Should().HaveCount(itemCount);
            removeResults.Should().AllSatisfy(result => result.Should().BeTrue());
        }

        [Fact]
        public async Task ConcurrentStatusUpdates_MaintainConsistency()
        {
            // Arrange
            const int itemCount = 20;
            var itemIds = new List<string>();

            // Add items
            for (int i = 0; i < itemCount; i++)
            {
                var id = $"status-test-{i}";
                itemIds.Add(id);
                var item = CreateTestDownloadItem(id, $"Album {i}");
                _sut.AddDownload(item);
            }

            var updateTasks = new List<Task>();
            var statuses = new[]
            {
                DownloadItemStatus.Downloading,
                DownloadItemStatus.Completed,
                DownloadItemStatus.Failed
            };

            // Act - Update statuses concurrently
            foreach (var id in itemIds)
            {
                updateTasks.Add(Task.Run(() =>
                {
                    var random = new Random();
                    var randomStatus = statuses[random.Next(statuses.Length)];
                    _sut.UpdateDownloadStatus(id, randomStatus, $"Status: {randomStatus}");
                }));
            }

            await Task.WhenAll(updateTasks);

            // Assert - All items should still be present with updated statuses
            _sut.ActiveDownloadCount.Should().Be(itemCount);

            foreach (var id in itemIds)
            {
                _sut.TryGetDownload(id, out var item).Should().BeTrue();
                item.Status.Should().BeOneOf(statuses);
                item.Message.Should().NotBeNullOrEmpty();
            }
        }

        [Fact]
        public async Task ConcurrentGetActiveDownloads_ReturnsConsistentSnapshots()
        {
            // Arrange
            const int initialItems = 15;
            var itemIds = new List<string>();

            // Add initial items
            for (int i = 0; i < initialItems; i++)
            {
                var id = $"snapshot-test-{i}";
                itemIds.Add(id);
                var item = CreateTestDownloadItem(id, $"Album {i}");
                _sut.AddDownload(item);
            }

            var snapshotTasks = new List<Task<IEnumerable<QobuzDownloadItem>>>();
            var modificationTasks = new List<Task>();

            // Start concurrent snapshot operations
            for (int i = 0; i < 10; i++)
            {
                snapshotTasks.Add(Task.Run(() => _sut.GetActiveDownloads()));
            }

            // Start concurrent modifications
            for (int i = 0; i < 5; i++)
            {
                var index = i;
                modificationTasks.Add(Task.Run(() =>
                {
                    var newId = $"concurrent-add-{index}";
                    var item = CreateTestDownloadItem(newId, $"New Album {index}");
                    _sut.AddDownload(item);
                }));
            }

            // Act
            var snapshots = await Task.WhenAll(snapshotTasks);
            await Task.WhenAll(modificationTasks);

            // Assert
            snapshots.Should().HaveCount(10);
            snapshots.Should().AllSatisfy(snapshot =>
            {
                snapshot.Should().NotBeNull();
                snapshot.Count().Should().BeGreaterOrEqualTo(initialItems);
            });

            // Final state should include all items
            _sut.ActiveDownloadCount.Should().Be(initialItems + 5);
        }

        #endregion

        #region Stress Tests

        [Fact]
        [Trait("Category", "Slow")]
        public async Task HighVolumeOperations_StressTest_MaintainsPerformance()
        {
            // Arrange
            const int operationCount = 1000;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var operationTasks = new List<Task>();

            // Act - Mix of different operations
            for (int i = 0; i < operationCount; i++)
            {
                var operationIndex = i;
                operationTasks.Add(Task.Run(() =>
                {
                    var id = $"stress-test-{operationIndex}";
                    var item = CreateTestDownloadItem(id, $"Stress Album {operationIndex}");

                    // Add item
                    _sut.AddDownload(item);

                    // Update status
                    _sut.UpdateDownloadStatus(id, DownloadItemStatus.Downloading);

                    // Get active downloads
                    var downloads = _sut.GetActiveDownloads();

                    // Update status again
                    _sut.UpdateDownloadStatus(id, DownloadItemStatus.Completed);

                    // Remove item (every 10th item)
                    if (operationIndex % 10 == 0)
                    {
                        _sut.RemoveDownload(id);
                    }
                }));
            }

            await Task.WhenAll(operationTasks);
            stopwatch.Stop();

            // Assert
            var averageTimePerOperation = stopwatch.ElapsedMilliseconds / (double)operationCount;
            averageTimePerOperation.Should().BeLessThan(5.0,
                "Each operation should average less than 5ms under stress");

            // Verify final state consistency
            var finalCount = operationCount - (operationCount / 10); // Minus removed items
            _sut.ActiveDownloadCount.Should().Be(finalCount);
        }

        [Fact]
        public async Task MemoryStressTest_LargeNumberOfItems_HandlesCorrectly()
        {
            // Arrange
            const int largeItemCount = 5000;
            var addTasks = new List<Task>();

            // Act - Add many items
            for (int i = 0; i < largeItemCount; i++)
            {
                var index = i;
                addTasks.Add(Task.Run(() =>
                {
                    var id = $"memory-test-{index}";
                    var item = CreateTestDownloadItem(id, $"Memory Test Album {index}");
                    _sut.AddDownload(item);
                }));
            }

            await Task.WhenAll(addTasks);

            // Assert
            _sut.ActiveDownloadCount.Should().Be(largeItemCount);

            // Test operations on large dataset
            var startTime = DateTime.UtcNow;
            var statistics = _sut.GetQueueStatistics();
            var operationTime = DateTime.UtcNow - startTime;

            statistics.TotalDownloads.Should().Be(largeItemCount);
            operationTime.Should().BeLessThan(TimeSpan.FromMilliseconds(100),
                "Statistics generation should be fast even with many items");
        }

        #endregion

        #region Edge Cases and Error Recovery

        [Fact]
        public void AddDownload_WithNullProperties_HandlesGracefully()
        {
            // Arrange
            var item = CreateTestDownloadItem("null-test", "Test Album");
            item.Artist = null;
            item.Message = null;
            item.OutputPath = null;

            // Act & Assert (should not throw)
            _sut.AddDownload(item);
            _sut.ActiveDownloadCount.Should().Be(1);

            _sut.TryGetDownload("null-test", out var retrievedItem).Should().BeTrue();
            retrievedItem.Should().NotBeNull();
        }

        [Fact]
        public async Task CleanupCompletedDownloads_ConcurrentWithOtherOperations_MaintainsConsistency()
        {
            // Arrange
            const int itemCount = 30;
            var itemIds = new List<string>();

            // Add completed items with various ages
            for (int i = 0; i < itemCount; i++)
            {
                var id = $"cleanup-test-{i}";
                itemIds.Add(id);
                var item = CreateTestDownloadItem(id, $"Album {i}", status: DownloadItemStatus.Completed);

                // Set different ages
                if (i < itemCount / 2)
                {
                    item.StartedAt = DateTime.UtcNow.AddHours(-2); // Old
                }
                else
                {
                    item.StartedAt = DateTime.UtcNow.AddMinutes(-10); // Recent
                }

                _sut.AddDownload(item);
            }

            var cleanupTasks = new List<Task<int>>();
            var addTasks = new List<Task>();

            // Start concurrent cleanup operations
            for (int i = 0; i < 5; i++)
            {
                cleanupTasks.Add(Task.Run(() => _sut.CleanupCompletedDownloads(TimeSpan.FromHours(1))));
            }

            // Start concurrent add operations
            for (int i = 0; i < 10; i++)
            {
                var index = i;
                addTasks.Add(Task.Run(() =>
                {
                    var newId = $"concurrent-cleanup-add-{index}";
                    var item = CreateTestDownloadItem(newId, $"New Album {index}");
                    _sut.AddDownload(item);
                }));
            }

            // Act
            var cleanupResults = await Task.WhenAll(cleanupTasks);
            await Task.WhenAll(addTasks);

            // Assert
            var totalCleaned = cleanupResults.Sum();
            totalCleaned.Should().BeGreaterOrEqualTo(itemCount / 2); // At least half should be cleaned

            // Should have recent items plus new items
            _sut.ActiveDownloadCount.Should().BeGreaterOrEqualTo(itemCount / 2 + 10);
        }

        [Fact]
        public void GetDownloadCountByStatus_WithConcurrentStatusChanges_ReturnsConsistentCounts()
        {
            // Arrange
            const int itemCount = 50;
            var itemIds = new List<string>();

            // Add items with initial status
            for (int i = 0; i < itemCount; i++)
            {
                var id = $"count-test-{i}";
                itemIds.Add(id);
                var item = CreateTestDownloadItem(id, $"Album {i}", status: DownloadItemStatus.Queued);
                _sut.AddDownload(item);
            }

            // Act - Change statuses while counting
            var countTasks = new List<Task<int>>();
            var updateTasks = new List<Task>();

            // Start counting operations
            for (int i = 0; i < 10; i++)
            {
                countTasks.Add(Task.Run(() => _sut.GetDownloadCountByStatus(DownloadItemStatus.Downloading)));
            }

            // Start status update operations
            foreach (var id in itemIds.Take(itemCount / 2))
            {
                updateTasks.Add(Task.Run(() =>
                    _sut.UpdateDownloadStatus(id, DownloadItemStatus.Downloading)));
            }

            // Wait for all operations
            Task.WaitAll(countTasks.ToArray<Task>().Concat(updateTasks).ToArray(), TimeSpan.FromSeconds(10));

            // Assert
            var finalCount = _sut.GetDownloadCountByStatus(DownloadItemStatus.Downloading);
            finalCount.Should().Be(itemCount / 2);

            var queuedCount = _sut.GetDownloadCountByStatus(DownloadItemStatus.Queued);
            queuedCount.Should().Be(itemCount / 2);
        }

        #endregion

        #region Queue Statistics Deep Tests

        [Fact]
        public void GetQueueStatistics_WithVariousStatuses_CalculatesCorrectTotals()
        {
            // Arrange
            var statusDistribution = new Dictionary<DownloadItemStatus, int>
            {
                { DownloadItemStatus.Queued, 10 },
                { DownloadItemStatus.Downloading, 5 },
                { DownloadItemStatus.Completed, 15 },
                { DownloadItemStatus.Failed, 3 }
            };

            long expectedTotalSize = 0;
            var itemId = 0;

            foreach (var kvp in statusDistribution)
            {
                for (int i = 0; i < kvp.Value; i++)
                {
                    var size = (i + 1) * 100; // Vary sizes
                    var item = CreateTestDownloadItem($"stats-{itemId++}", $"Album {itemId}",
                        status: kvp.Key, totalSize: size);
                    _sut.AddDownload(item);
                    expectedTotalSize += size;
                }
            }

            // Act
            var stats = _sut.GetQueueStatistics();

            // Assert
            stats.TotalDownloads.Should().Be(statusDistribution.Values.Sum());
            stats.QueuedDownloads.Should().Be(statusDistribution[DownloadItemStatus.Queued]);
            stats.DownloadingDownloads.Should().Be(statusDistribution[DownloadItemStatus.Downloading]);
            stats.CompletedDownloads.Should().Be(statusDistribution[DownloadItemStatus.Completed]);
            stats.FailedDownloads.Should().Be(statusDistribution[DownloadItemStatus.Failed]);
            stats.TotalBytesDownloaded.Should().Be(expectedTotalSize);
            stats.LastUpdated.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task GetQueueStatistics_ConcurrentAccess_DoesNotCauseDeadlock()
        {
            // Arrange
            const int itemCount = 20;
            for (int i = 0; i < itemCount; i++)
            {
                var item = CreateTestDownloadItem($"deadlock-test-{i}", $"Album {i}");
                _sut.AddDownload(item);
            }

            var statsTasks = new List<Task<DownloadQueueStatistics>>();
            var modificationTasks = new List<Task>();

            // Start many concurrent statistics calls
            for (int i = 0; i < 20; i++)
            {
                statsTasks.Add(Task.Run(() => _sut.GetQueueStatistics()));
            }

            // Start concurrent modifications
            for (int i = 0; i < 10; i++)
            {
                var index = i;
                modificationTasks.Add(Task.Run(() =>
                {
                    _sut.UpdateDownloadStatus($"deadlock-test-{index}", DownloadItemStatus.Completed);
                }));
            }

            // Act & Assert - Should complete without deadlock
            var statsResults = await Task.WhenAll(statsTasks);
            await Task.WhenAll(modificationTasks);

            statsResults.Should().HaveCount(20);
            statsResults.Should().AllSatisfy(stats =>
            {
                stats.Should().NotBeNull();
                stats.TotalDownloads.Should().BeGreaterOrEqualTo(itemCount);
            });
        }

        #endregion

        #region Performance Benchmarks

        [Fact]
        [Trait("Category", "Benchmark")]
        [Trait("Category", "Slow")]
        public void AddDownload_PerformanceBenchmark_MeetsTargets()
        {
            // Arrange
            const int iterations = 10000;
            var items = new List<QobuzDownloadItem>();

            for (int i = 0; i < iterations; i++)
            {
                items.Add(CreateTestDownloadItem($"perf-add-{i}", $"Album {i}"));
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Act
            foreach (var item in items)
            {
                _sut.AddDownload(item);
            }

            stopwatch.Stop();

            // Assert
            var averageTimePerAdd = stopwatch.ElapsedTicks / (double)iterations;
            var averageTimePerAddMs = (averageTimePerAdd * 1000.0) / System.Diagnostics.Stopwatch.Frequency;

            averageTimePerAddMs.Should().BeLessThan(0.1,
                "Each AddDownload operation should average less than 0.1ms");

            _sut.ActiveDownloadCount.Should().Be(iterations);
        }

        [Fact]
        [Trait("Category", "Benchmark")]
        [Trait("Category", "Slow")]
        public void TryGetDownload_PerformanceBenchmark_MeetsTargets()
        {
            // Arrange
            const int itemCount = 5000;
            const int lookupCount = 10000;
            var itemIds = new List<string>();

            // Add items
            for (int i = 0; i < itemCount; i++)
            {
                var id = $"perf-get-{i}";
                itemIds.Add(id);
                var item = CreateTestDownloadItem(id, $"Album {i}");
                _sut.AddDownload(item);
            }

            var random = new Random();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Act - Perform lookups
            for (int i = 0; i < lookupCount; i++)
            {
                var randomId = itemIds[random.Next(itemIds.Count)];
                _sut.TryGetDownload(randomId, out var item);
            }

            stopwatch.Stop();

            // Assert
            var averageTimePerLookup = stopwatch.ElapsedTicks / (double)lookupCount;
            var averageTimePerLookupMs = (averageTimePerLookup * 1000.0) / System.Diagnostics.Stopwatch.Frequency;

            averageTimePerLookupMs.Should().BeLessThan(0.01,
                "Each TryGetDownload operation should average less than 0.01ms");
        }

        #endregion

        #region Helper Methods

        private QobuzDownloadItem CreateTestDownloadItem(
            string downloadId,
            string title,
            string outputPath = @"C:\Test\Output",
            DownloadItemStatus status = DownloadItemStatus.Queued,
            long totalSize = 1000)
        {
            return new QobuzDownloadItem
            {
                DownloadId = downloadId,
                Title = title,
                Artist = "Test Artist",
                Status = status,
                Progress = status == DownloadItemStatus.Completed ? 100 : 0,
                TotalSize = totalSize,
                StartedAt = DateTime.UtcNow,
                OutputPath = outputPath,
                CancellationTokenSource = new CancellationTokenSource(),
                Message = $"Status: {status}"
            };
        }

        #endregion
    }
}
