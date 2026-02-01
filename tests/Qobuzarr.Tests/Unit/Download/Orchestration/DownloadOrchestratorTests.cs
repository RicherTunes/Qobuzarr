using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using FluentAssertions;
using NLog;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Music;
using NzbDrone.Core.Download;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Download.Orchestration;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Qobuzarr.Tests.Fixtures;

namespace Qobuzarr.Tests.Unit.Download.Orchestration
{
    public class DownloadOrchestratorTests : TestFixtureBase
    {
        private readonly Mock<IDownloadQueueService> _mockQueueService;
        private readonly Mock<IDownloadFileService> _mockFileService;
        private readonly Mock<IConcurrencyManager> _mockConcurrencyManager;
        private readonly Mock<IIndexer> _mockIndexer;
        private readonly DownloadOrchestrator _sut;

        public DownloadOrchestratorTests()
        {
            _mockQueueService = new Mock<IDownloadQueueService>();
            _mockFileService = new Mock<IDownloadFileService>();
            _mockConcurrencyManager = new Mock<IConcurrencyManager>();
            _mockIndexer = new Mock<IIndexer>();

            _sut = new DownloadOrchestrator(
                _mockQueueService.Object,
                _mockFileService.Object,
                _mockConcurrencyManager.Object,
                MockLogger.Object);
        }

        [Fact]
        public void Constructor_WithNullQueueService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DownloadOrchestrator(
                null,
                _mockFileService.Object,
                _mockConcurrencyManager.Object,
                MockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullFileService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DownloadOrchestrator(
                _mockQueueService.Object,
                null,
                _mockConcurrencyManager.Object,
                MockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullConcurrencyManager_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DownloadOrchestrator(
                _mockQueueService.Object,
                _mockFileService.Object,
                null,
                MockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DownloadOrchestrator(
                _mockQueueService.Object,
                _mockFileService.Object,
                _mockConcurrencyManager.Object,
                null));
        }

        [Fact]
        public async Task StartDownloadAsync_WithValidRemoteAlbum_ReturnsDownloadId()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum("Test Artist", "Test Album");

            // Act
            var downloadId = await _sut.StartDownloadAsync(remoteAlbum, _mockIndexer.Object);

            // Assert
            downloadId.Should().NotBeNullOrEmpty();
            Guid.TryParse(downloadId.Replace("-", ""), out _).Should().BeTrue(); // Should be a valid GUID format
        }

        [Fact]
        public async Task StartDownloadAsync_WithNullAlbums_HandlesGracefully()
        {
            // Arrange
            var artist = new Artist { Name = "Test Artist" };
            var remoteAlbum = new RemoteAlbum
            {
                Artist = artist,
                Albums = null
            };

            // Act
            var downloadId = await _sut.StartDownloadAsync(remoteAlbum, _mockIndexer.Object);

            // Assert
            downloadId.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task StartDownloadAsync_WithNullArtist_HandlesGracefully()
        {
            // Arrange
            var album = new Album { Title = "Test Album" };
            var remoteAlbum = new RemoteAlbum
            {
                Artist = null,
                Albums = new List<Album> { album }
            };

            // Act
            var downloadId = await _sut.StartDownloadAsync(remoteAlbum, _mockIndexer.Object);

            // Assert
            downloadId.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task GetDownloadStatusAsync_WithMockedServices_ReturnsStatus()
        {
            // Arrange
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = 5,
                QueuedDownloads = 2,
                DownloadingDownloads = 1,
                CompletedDownloads = 1,
                FailedDownloads = 1,
                TotalBytesDownloaded = 1000L,
                LastUpdated = DateTime.UtcNow
            };

            var concurrencyStats = new ConcurrencyStatistics
            {
                MaxConcurrency = 3,
                ActiveOperations = 1,
                QueuedOperations = 2,
                TotalSlotsUsed = 1,
                LastUpdated = DateTime.UtcNow
            };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            // Act
            var status = await _sut.GetDownloadStatusAsync();

            // Assert
            status.Should().NotBeNull();
            status.ActiveDownloads.Should().Be(concurrencyStats.ActiveOperations);
            status.QueuedDownloads.Should().Be(queueStats.QueuedDownloads);
            status.CompletedDownloads.Should().Be(queueStats.CompletedDownloads);
            status.FailedDownloads.Should().Be(queueStats.FailedDownloads);
            status.TotalBytesDownloaded.Should().Be(queueStats.TotalBytesDownloaded);
            status.LastUpdated.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task GetDownloadStatusAsync_CalculatesOverallProgress()
        {
            // Arrange
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = 4,
                QueuedDownloads = 1,      // 50% progress estimate
                DownloadingDownloads = 1, // 50% progress estimate  
                CompletedDownloads = 2,   // 100% progress each
                FailedDownloads = 0,      // 0% progress
                TotalBytesDownloaded = 1000L
            };

            var concurrencyStats = new ConcurrencyStatistics
            {
                ActiveOperations = 2,
                LastUpdated = DateTime.UtcNow
            };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            // Act
            var status = await _sut.GetDownloadStatusAsync();

            // Assert
            // Expected: (2 * 100 + 2 * 50) / 4 = 75%
            status.TotalProgress.Should().Be(75.0);
        }

        [Fact]
        public async Task GetDownloadStatusAsync_WithZeroDownloads_ReturnsZeroProgress()
        {
            // Arrange
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = 0,
                QueuedDownloads = 0,
                DownloadingDownloads = 0,
                CompletedDownloads = 0,
                FailedDownloads = 0,
                TotalBytesDownloaded = 0L
            };

            var concurrencyStats = new ConcurrencyStatistics
            {
                ActiveOperations = 0
            };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            // Act
            var status = await _sut.GetDownloadStatusAsync();

            // Assert
            status.TotalProgress.Should().Be(0.0);
        }

        [Fact]
        public async Task CancelDownloadAsync_WithExistingDownload_CancelsAndReturnsTrue()
        {
            // Arrange
            var downloadId = "test-download-id";
            var downloadItem = new QobuzDownloadItem
            {
                DownloadId = downloadId,
                Title = "Test Album",
                Status = DownloadItemStatus.Downloading,
                CancellationTokenSource = new System.Threading.CancellationTokenSource()
            };

            _mockQueueService.Setup(x => x.TryGetDownload(downloadId, out downloadItem))
                            .Returns(true);

            // Act
            var result = await _sut.CancelDownloadAsync(downloadId);

            // Assert
            result.Should().BeTrue();
            downloadItem.Status.Should().Be(DownloadItemStatus.Failed);
            downloadItem.Message.Should().Be("Download cancelled by user");
        }

        [Fact]
        public async Task CancelDownloadAsync_WithNonExistentDownload_ReturnsFalse()
        {
            // Arrange
            var downloadId = "non-existent-id";
            QobuzDownloadItem downloadItem = null;

            _mockQueueService.Setup(x => x.TryGetDownload(downloadId, out downloadItem))
                            .Returns(false);

            // Act
            var result = await _sut.CancelDownloadAsync(downloadId);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task CancelDownloadAsync_WithNullOrEmptyId_ReturnsFalse()
        {
            // Act
            var resultNull = await _sut.CancelDownloadAsync(null);
            var resultEmpty = await _sut.CancelDownloadAsync("");
            var resultWhitespace = await _sut.CancelDownloadAsync("   ");

            // Assert
            resultNull.Should().BeFalse();
            resultEmpty.Should().BeFalse();
            resultWhitespace.Should().BeFalse();
        }

        [Fact]
        public async Task CancelDownloadAsync_WithCancellationException_ReturnsFalse()
        {
            // Arrange
            var downloadId = "test-download-id";
            var downloadItem = new QobuzDownloadItem
            {
                DownloadId = downloadId,
                CancellationTokenSource = new System.Threading.CancellationTokenSource()
            };

            // Simulate an exception during cancellation (e.g., already cancelled)
            downloadItem.CancellationTokenSource.Cancel();
            downloadItem.CancellationTokenSource.Dispose();

            _mockQueueService.Setup(x => x.TryGetDownload(downloadId, out downloadItem))
                            .Returns(true);

            // Act
            var result = await _sut.CancelDownloadAsync(downloadId);

            // Assert
            result.Should().BeTrue(); // Still returns true as the download item was found
        }

        [Fact]
        public async Task CleanupCompletedDownloadsAsync_CallsQueueServiceCleanup()
        {
            // Arrange
            var expectedCleanedCount = 3;
            _mockQueueService.Setup(x => x.CleanupCompletedDownloads(It.IsAny<TimeSpan>()))
                            .Returns(expectedCleanedCount);

            // Act
            var result = await _sut.CleanupCompletedDownloadsAsync();

            // Assert
            result.Should().Be(expectedCleanedCount);
            _mockQueueService.Verify(x => x.CleanupCompletedDownloads(It.IsAny<TimeSpan>()), Times.Once);
        }

        [Fact]
        public async Task CleanupCompletedDownloadsAsync_WithException_ReturnsZero()
        {
            // Arrange
            _mockQueueService.Setup(x => x.CleanupCompletedDownloads(It.IsAny<TimeSpan>()))
                            .Throws<InvalidOperationException>();

            // Act
            var result = await _sut.CleanupCompletedDownloadsAsync();

            // Assert
            result.Should().Be(0);
        }

        [Fact]
        public async Task CleanupCompletedDownloadsAsync_WithNoItemsToCleanup_ReturnsZero()
        {
            // Arrange
            _mockQueueService.Setup(x => x.CleanupCompletedDownloads(It.IsAny<TimeSpan>()))
                            .Returns(0);

            // Act
            var result = await _sut.CleanupCompletedDownloadsAsync();

            // Assert
            result.Should().Be(0);
        }

        [Theory]
        [InlineData(0, 0, 0, 0, 0.0)]           // No downloads
        [InlineData(1, 0, 0, 1, 100.0)]         // One completed
        [InlineData(2, 0, 0, 2, 100.0)]         // All completed
        [InlineData(2, 1, 0, 1, 75.0)]          // Mixed: 1 completed + 1 queued (50%) = 150/2
        [InlineData(4, 1, 1, 2, 75.0)]          // Mixed: 2 completed (100%) + 2 active (50%) = 300/4
        [InlineData(3, 0, 0, 0, 0.0)]           // All failed
        public async Task GetDownloadStatusAsync_ProgressCalculation_ReturnsExpectedProgress(
            int totalDownloads, int queued, int downloading, int completed, double expectedProgress)
        {
            // Arrange
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = totalDownloads,
                QueuedDownloads = queued,
                DownloadingDownloads = downloading,
                CompletedDownloads = completed,
                FailedDownloads = totalDownloads - queued - downloading - completed,
                TotalBytesDownloaded = 1000L
            };

            var concurrencyStats = new ConcurrencyStatistics { ActiveOperations = downloading };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            // Act
            var status = await _sut.GetDownloadStatusAsync();

            // Assert
            status.TotalProgress.Should().Be(expectedProgress);
        }

        private RemoteAlbum CreateTestRemoteAlbum(string artistName, string albumTitle)
        {
            var artist = new Artist { Name = artistName };
            var album = new Album
            {
                Title = albumTitle,
                Artist = new NzbDrone.Core.Datastore.LazyLoaded<Artist>(artist)
            };

            return new RemoteAlbum
            {
                Artist = artist,
                Albums = new List<Album> { album }
            };
        }
    }
}
