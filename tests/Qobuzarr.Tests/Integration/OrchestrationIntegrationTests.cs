using System;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Moq;
using NLog;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Download.Orchestration;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using NzbDrone.Core.Download;

namespace Qobuzarr.Tests.Integration
{
    /// <summary>
    /// Integration tests for the download orchestration layer.
    /// Tests the interaction between multiple services in the download pipeline.
    /// </summary>
    public class OrchestrationIntegrationTests
    {
        private readonly Mock<Logger> _mockLogger;
        private readonly IDownloadQueueService _queueService;
        private readonly IDownloadFileService _fileService;
        private readonly IConcurrencyManager _concurrencyManager;
        private readonly IDownloadOrchestrator _orchestrator;

        public OrchestrationIntegrationTests()
        {
            _mockLogger = new Mock<Logger>();
            
            // Create real service instances for integration testing
            var mockDiskProvider = new Mock<NzbDrone.Common.Disk.IDiskProvider>();
            var mockRemotePathMappingService = new Mock<NzbDrone.Core.RemotePathMappings.IRemotePathMappingService>();
            
            _fileService = new DownloadFileService(
                mockDiskProvider.Object, 
                mockRemotePathMappingService.Object, 
                _mockLogger.Object);
            
            _queueService = new DownloadQueueService(_fileService, _mockLogger.Object);
            _concurrencyManager = new ConcurrencyManager(_mockLogger.Object, 3);
            _orchestrator = new DownloadOrchestrator(_queueService, _fileService, _concurrencyManager, _mockLogger.Object);
        }

        [Fact]
        public async Task Orchestrator_WithMultipleDownloads_ManagesConcurrencyCorrectly()
        {
            // Arrange
            var download1 = CreateTestDownloadItem("test1");
            var download2 = CreateTestDownloadItem("test2");
            var download3 = CreateTestDownloadItem("test3");
            var download4 = CreateTestDownloadItem("test4");

            // Act
            _queueService.AddDownload(download1);
            _queueService.AddDownload(download2);
            _queueService.AddDownload(download3);
            _queueService.AddDownload(download4);

            // Simulate concurrent processing
            var tasks = new[]
            {
                ProcessDownloadAsync(download1.DownloadId),
                ProcessDownloadAsync(download2.DownloadId),
                ProcessDownloadAsync(download3.DownloadId),
                ProcessDownloadAsync(download4.DownloadId)
            };

            await Task.WhenAll(tasks);

            // Assert
            var stats = _queueService.GetQueueStatistics();
            stats.TotalDownloads.Should().Be(4);
            _concurrencyManager.ActiveCount.Should().Be(0);
        }

        [Fact]
        public void QueueService_WithConcurrentOperations_MaintainsThreadSafety()
        {
            // Arrange
            var downloads = new QobuzDownloadItem[100];
            for (int i = 0; i < 100; i++)
            {
                downloads[i] = CreateTestDownloadItem($"concurrent-{i}");
            }

            // Act - Add downloads concurrently
            Parallel.ForEach(downloads, download =>
            {
                _queueService.AddDownload(download);
            });

            // Assert
            _queueService.ActiveDownloadCount.Should().Be(100);
            
            // Act - Remove downloads concurrently
            Parallel.ForEach(downloads, download =>
            {
                _queueService.RemoveDownload(download.DownloadId);
            });

            // Assert
            _queueService.ActiveDownloadCount.Should().Be(0);
        }

        [Fact]
        public async Task ConcurrencyManager_WithDynamicLimitChanges_AdaptsCorrectly()
        {
            // Arrange
            var initialLimit = _concurrencyManager.CurrentLimit;
            
            // Act - Acquire some slots
            var slot1 = await _concurrencyManager.AcquireSlotAsync();
            var slot2 = await _concurrencyManager.AcquireSlotAsync();
            
            _concurrencyManager.ActiveCount.Should().Be(2);
            
            // Update concurrency limit
            _concurrencyManager.UpdateConcurrencyLimit(5);
            
            // Should still track active slots correctly
            _concurrencyManager.ActiveCount.Should().Be(2);
            _concurrencyManager.CurrentLimit.Should().Be(5);
            
            // Clean up
            slot1.Dispose();
            slot2.Dispose();
            
            _concurrencyManager.ActiveCount.Should().Be(0);
        }

        [Fact]
        public void DownloadQueue_WithStatusUpdates_TracksProgressCorrectly()
        {
            // Arrange
            var download = CreateTestDownloadItem("status-test");
            _queueService.AddDownload(download);

            // Act & Assert - Progress through statuses
            _queueService.UpdateDownloadStatus(download.DownloadId, DownloadItemStatus.Queued);
            _queueService.GetDownloadCountByStatus(DownloadItemStatus.Queued).Should().Be(1);

            _queueService.UpdateDownloadStatus(download.DownloadId, DownloadItemStatus.Downloading, "Starting download");
            _queueService.GetDownloadCountByStatus(DownloadItemStatus.Downloading).Should().Be(1);
            _queueService.GetDownloadCountByStatus(DownloadItemStatus.Queued).Should().Be(0);

            _queueService.UpdateDownloadStatus(download.DownloadId, DownloadItemStatus.Completed, "Download complete");
            _queueService.GetDownloadCountByStatus(DownloadItemStatus.Completed).Should().Be(1);
            _queueService.GetDownloadCountByStatus(DownloadItemStatus.Downloading).Should().Be(0);
        }

        [Fact]
        public async Task Orchestrator_WithFailedDownload_CleansUpProperly()
        {
            // Arrange
            var download = CreateTestDownloadItem("failure-test");
            _queueService.AddDownload(download);

            // Act - Simulate failure
            _queueService.UpdateDownloadStatus(download.DownloadId, DownloadItemStatus.Failed, "Network error");
            
            // Cleanup old downloads
            var cleaned = _queueService.CleanupCompletedDownloads(TimeSpan.FromSeconds(-1)); // Cleanup all
            
            // Assert
            cleaned.Should().Be(0); // Failed downloads aren't cleaned up by CleanupCompletedDownloads
            _queueService.GetDownloadCountByStatus(DownloadItemStatus.Failed).Should().Be(1);
            
            // Manual removal should work
            var removed = _queueService.RemoveDownload(download.DownloadId, deleteData: true);
            removed.Should().BeTrue();
            _queueService.ActiveDownloadCount.Should().Be(0);
        }

        private QobuzDownloadItem CreateTestDownloadItem(string id)
        {
            return new QobuzDownloadItem
            {
                DownloadId = id,
                Title = $"Test Download {id}",
                Status = DownloadItemStatus.Queued,
                TotalSize = 1024 * 1024 * 10, // 10MB
                OutputPath = $"/test/downloads/{id}",
                StartedAt = DateTime.UtcNow
            };
        }

        private async Task ProcessDownloadAsync(string downloadId)
        {
            using (var slot = await _concurrencyManager.AcquireSlotAsync())
            {
                _queueService.UpdateDownloadStatus(downloadId, DownloadItemStatus.Downloading);
                await Task.Delay(100); // Simulate download
                _queueService.UpdateDownloadStatus(downloadId, DownloadItemStatus.Completed);
            }
        }
    }
}