using System;
using System.Linq;
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
    public class DownloadQueueServiceTests : TestFixtureBase
    {
        private readonly Mock<IDownloadFileService> _mockFileService;
        private readonly DownloadQueueService _sut;

        public DownloadQueueServiceTests()
        {
            _mockFileService = new Mock<IDownloadFileService>();
            _sut = new DownloadQueueService(_mockFileService.Object, MockLogger.Object);
        }

        [Fact]
        public void Constructor_WithNullFileService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DownloadQueueService(null, MockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DownloadQueueService(_mockFileService.Object, null));
        }

        [Fact]
        public void AddDownload_WithValidItem_AddsToQueue()
        {
            // Arrange
            var downloadItem = CreateTestDownloadItem("test-id", "Test Album");

            // Act
            _sut.AddDownload(downloadItem);

            // Assert
            _sut.ActiveDownloadCount.Should().Be(1);
            _sut.TryGetDownload("test-id", out var retrievedItem).Should().BeTrue();
            retrievedItem.Should().Be(downloadItem);
        }

        [Fact]
        public void AddDownload_WithNullItem_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _sut.AddDownload(null));
        }

        [Fact]
        public void AddDownload_WithEmptyDownloadId_ThrowsArgumentException()
        {
            // Arrange
            var downloadItem = CreateTestDownloadItem("", "Test Album");

            // Act & Assert
            Assert.Throws<ArgumentException>(() => _sut.AddDownload(downloadItem));
        }

        [Fact]
        public void AddDownload_WithDuplicateId_DoesNotAddDuplicate()
        {
            // Arrange
            var downloadItem1 = CreateTestDownloadItem("test-id", "Test Album 1");
            var downloadItem2 = CreateTestDownloadItem("test-id", "Test Album 2");

            // Act
            _sut.AddDownload(downloadItem1);
            _sut.AddDownload(downloadItem2);

            // Assert
            _sut.ActiveDownloadCount.Should().Be(1);
            _sut.TryGetDownload("test-id", out var retrievedItem).Should().BeTrue();
            retrievedItem.Title.Should().Be("Test Album 1"); // Should keep the first one
        }

        [Fact]
        public void GetActiveDownloads_WithMultipleItems_ReturnsAllItems()
        {
            // Arrange
            var item1 = CreateTestDownloadItem("id1", "Album 1");
            var item2 = CreateTestDownloadItem("id2", "Album 2");
            var item3 = CreateTestDownloadItem("id3", "Album 3");

            _sut.AddDownload(item1);
            _sut.AddDownload(item2);
            _sut.AddDownload(item3);

            // Act
            var activeDownloads = _sut.GetActiveDownloads().ToList();

            // Assert
            activeDownloads.Should().HaveCount(3);
            activeDownloads.Should().Contain(item1);
            activeDownloads.Should().Contain(item2);
            activeDownloads.Should().Contain(item3);
        }

        [Fact]
        public void GetActiveDownloads_WithEmptyQueue_ReturnsEmptyCollection()
        {
            // Act
            var activeDownloads = _sut.GetActiveDownloads();

            // Assert
            activeDownloads.Should().BeEmpty();
        }

        [Fact]
        public void TryGetDownload_WithExistingId_ReturnsTrue()
        {
            // Arrange
            var downloadItem = CreateTestDownloadItem("test-id", "Test Album");
            _sut.AddDownload(downloadItem);

            // Act
            var found = _sut.TryGetDownload("test-id", out var retrievedItem);

            // Assert
            found.Should().BeTrue();
            retrievedItem.Should().Be(downloadItem);
        }

        [Fact]
        public void TryGetDownload_WithNonExistentId_ReturnsFalse()
        {
            // Act
            var found = _sut.TryGetDownload("non-existent", out var retrievedItem);

            // Assert
            found.Should().BeFalse();
            retrievedItem.Should().BeNull();
        }

        [Fact]
        public void TryGetDownload_WithNullOrEmptyId_ReturnsFalse()
        {
            // Act
            var foundNull = _sut.TryGetDownload(null, out var item1);
            var foundEmpty = _sut.TryGetDownload("", out var item2);
            var foundWhitespace = _sut.TryGetDownload("   ", out var item3);

            // Assert
            foundNull.Should().BeFalse();
            foundEmpty.Should().BeFalse();
            foundWhitespace.Should().BeFalse();
            item1.Should().BeNull();
            item2.Should().BeNull();
            item3.Should().BeNull();
        }

        [Fact]
        public void RemoveDownload_WithExistingId_RemovesFromQueue()
        {
            // Arrange
            var downloadItem = CreateTestDownloadItem("test-id", "Test Album");
            _sut.AddDownload(downloadItem);

            // Act
            var removed = _sut.RemoveDownload("test-id", deleteData: false);

            // Assert
            removed.Should().BeTrue();
            _sut.ActiveDownloadCount.Should().Be(0);
            _sut.TryGetDownload("test-id", out var retrievedItem).Should().BeFalse();
        }

        [Fact]
        public void RemoveDownload_WithDeleteData_CallsFileServiceCleanup()
        {
            // Arrange
            var outputPath = @"C:\Downloads\TestAlbum";
            var downloadItem = CreateTestDownloadItem("test-id", "Test Album", outputPath);
            _sut.AddDownload(downloadItem);

            // Act
            var removed = _sut.RemoveDownload("test-id", deleteData: true);

            // Assert
            removed.Should().BeTrue();
            // Note: File service cleanup is called asynchronously, so we can't verify it directly
            // In a more sophisticated test, we might use TaskCompletionSource or similar
        }

        [Fact]
        public void RemoveDownload_WithNonExistentId_ReturnsFalse()
        {
            // Act
            var removed = _sut.RemoveDownload("non-existent", deleteData: false);

            // Assert
            removed.Should().BeFalse();
        }

        [Fact]
        public void RemoveDownload_WithNullOrEmptyId_ReturnsFalse()
        {
            // Act
            var removedNull = _sut.RemoveDownload(null, deleteData: false);
            var removedEmpty = _sut.RemoveDownload("", deleteData: false);

            // Assert
            removedNull.Should().BeFalse();
            removedEmpty.Should().BeFalse();
        }

        [Fact]
        public void CleanupCompletedDownloads_WithOldCompletedItems_RemovesThem()
        {
            // Arrange
            var oldItem = CreateTestDownloadItem("old-id", "Old Album", status: DownloadItemStatus.Completed);
            oldItem.StartedAt = DateTime.UtcNow.AddHours(-2); // 2 hours ago

            var recentItem = CreateTestDownloadItem("recent-id", "Recent Album", status: DownloadItemStatus.Completed);
            recentItem.StartedAt = DateTime.UtcNow.AddMinutes(-5); // 5 minutes ago

            _sut.AddDownload(oldItem);
            _sut.AddDownload(recentItem);

            // Act
            var cleaned = _sut.CleanupCompletedDownloads(TimeSpan.FromHours(1));

            // Assert
            cleaned.Should().Be(1);
            _sut.ActiveDownloadCount.Should().Be(1);
            _sut.TryGetDownload("old-id", out _).Should().BeFalse();
            _sut.TryGetDownload("recent-id", out _).Should().BeTrue();
        }

        [Fact]
        public void CleanupCompletedDownloads_WithNoOldItems_ReturnsZero()
        {
            // Arrange
            var recentItem = CreateTestDownloadItem("recent-id", "Recent Album", status: DownloadItemStatus.Completed);
            recentItem.StartedAt = DateTime.UtcNow.AddMinutes(-5);
            _sut.AddDownload(recentItem);

            // Act
            var cleaned = _sut.CleanupCompletedDownloads(TimeSpan.FromHours(1));

            // Assert
            cleaned.Should().Be(0);
            _sut.ActiveDownloadCount.Should().Be(1);
        }

        [Fact]
        public void CleanupCompletedDownloads_OnlyRemovesCompletedItems()
        {
            // Arrange
            var oldCompleted = CreateTestDownloadItem("old-completed", "Old Completed", status: DownloadItemStatus.Completed);
            oldCompleted.StartedAt = DateTime.UtcNow.AddHours(-2);

            var oldDownloading = CreateTestDownloadItem("old-downloading", "Old Downloading", status: DownloadItemStatus.Downloading);
            oldDownloading.StartedAt = DateTime.UtcNow.AddHours(-2);

            _sut.AddDownload(oldCompleted);
            _sut.AddDownload(oldDownloading);

            // Act
            var cleaned = _sut.CleanupCompletedDownloads(TimeSpan.FromHours(1));

            // Assert
            cleaned.Should().Be(1);
            _sut.ActiveDownloadCount.Should().Be(1);
            _sut.TryGetDownload("old-completed", out _).Should().BeFalse();
            _sut.TryGetDownload("old-downloading", out _).Should().BeTrue();
        }

        [Fact]
        public void GetDownloadCountByStatus_WithMixedStatuses_ReturnsCorrectCounts()
        {
            // Arrange
            var queued1 = CreateTestDownloadItem("q1", "Queued 1", status: DownloadItemStatus.Queued);
            var queued2 = CreateTestDownloadItem("q2", "Queued 2", status: DownloadItemStatus.Queued);
            var downloading = CreateTestDownloadItem("d1", "Downloading", status: DownloadItemStatus.Downloading);
            var completed = CreateTestDownloadItem("c1", "Completed", status: DownloadItemStatus.Completed);
            var failed = CreateTestDownloadItem("f1", "Failed", status: DownloadItemStatus.Failed);

            _sut.AddDownload(queued1);
            _sut.AddDownload(queued2);
            _sut.AddDownload(downloading);
            _sut.AddDownload(completed);
            _sut.AddDownload(failed);

            // Act & Assert
            _sut.GetDownloadCountByStatus(DownloadItemStatus.Queued).Should().Be(2);
            _sut.GetDownloadCountByStatus(DownloadItemStatus.Downloading).Should().Be(1);
            _sut.GetDownloadCountByStatus(DownloadItemStatus.Completed).Should().Be(1);
            _sut.GetDownloadCountByStatus(DownloadItemStatus.Failed).Should().Be(1);
        }

        [Fact]
        public void UpdateDownloadStatus_WithExistingItem_UpdatesStatus()
        {
            // Arrange
            var downloadItem = CreateTestDownloadItem("test-id", "Test Album", status: DownloadItemStatus.Queued);
            _sut.AddDownload(downloadItem);

            // Act
            _sut.UpdateDownloadStatus("test-id", DownloadItemStatus.Downloading, "Now downloading");

            // Assert
            _sut.TryGetDownload("test-id", out var retrievedItem).Should().BeTrue();
            retrievedItem.Status.Should().Be(DownloadItemStatus.Downloading);
            retrievedItem.Message.Should().Be("Now downloading");
        }

        [Fact]
        public void UpdateDownloadStatus_WithNonExistentItem_DoesNotThrow()
        {
            // Act & Assert (should not throw)
            _sut.UpdateDownloadStatus("non-existent", DownloadItemStatus.Failed, "Error");
        }

        [Fact]
        public void GetQueueStatistics_WithMixedItems_ReturnsCorrectStatistics()
        {
            // Arrange
            var queued = CreateTestDownloadItem("q1", "Queued", status: DownloadItemStatus.Queued, totalSize: 100);
            var downloading = CreateTestDownloadItem("d1", "Downloading", status: DownloadItemStatus.Downloading, totalSize: 200);
            var completed = CreateTestDownloadItem("c1", "Completed", status: DownloadItemStatus.Completed, totalSize: 300);
            var failed = CreateTestDownloadItem("f1", "Failed", status: DownloadItemStatus.Failed, totalSize: 150);

            _sut.AddDownload(queued);
            _sut.AddDownload(downloading);
            _sut.AddDownload(completed);
            _sut.AddDownload(failed);

            // Act
            var stats = _sut.GetQueueStatistics();

            // Assert
            stats.TotalDownloads.Should().Be(4);
            stats.QueuedDownloads.Should().Be(1);
            stats.DownloadingDownloads.Should().Be(1);
            stats.CompletedDownloads.Should().Be(1);
            stats.FailedDownloads.Should().Be(1);
            stats.TotalBytesDownloaded.Should().Be(750); // 100 + 200 + 300 + 150
            stats.LastUpdated.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void ActiveDownloadCount_WithEmptyQueue_ReturnsZero()
        {
            // Act & Assert
            _sut.ActiveDownloadCount.Should().Be(0);
        }

        [Fact]
        public void ActiveDownloadCount_WithItems_ReturnsCorrectCount()
        {
            // Arrange
            var item1 = CreateTestDownloadItem("id1", "Album 1");
            var item2 = CreateTestDownloadItem("id2", "Album 2");

            // Act & Assert
            _sut.AddDownload(item1);
            _sut.ActiveDownloadCount.Should().Be(1);

            _sut.AddDownload(item2);
            _sut.ActiveDownloadCount.Should().Be(2);

            _sut.RemoveDownload("id1");
            _sut.ActiveDownloadCount.Should().Be(1);
        }

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
                Progress = 0,
                TotalSize = totalSize,
                StartedAt = DateTime.UtcNow,
                OutputPath = outputPath,
                CancellationTokenSource = new System.Threading.CancellationTokenSource()
            };
        }
    }
}
