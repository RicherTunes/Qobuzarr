using System;
using System.Collections.Generic;
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

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Additional coverage tests for DownloadOrchestrator.
    /// Tests LastUpdated timestamp verification and edge cases not covered by existing tests.
    /// Source: src/Download/Orchestration/DownloadOrchestrator.cs
    /// </summary>
    public class DownloadOrchestratorMoreCovTests : TestFixtureBase
    {
        private readonly Mock<IDownloadQueueService> _mockQueueService;
        private readonly Mock<IDownloadFileService> _mockFileService;
        private readonly Mock<IConcurrencyManager> _mockConcurrencyManager;
        private readonly Mock<IIndexer> _mockIndexer;

        public DownloadOrchestratorMoreCovTests()
        {
            _mockQueueService = new Mock<IDownloadQueueService>();
            _mockFileService = new Mock<IDownloadFileService>();
            _mockConcurrencyManager = new Mock<IConcurrencyManager>();
            _mockIndexer = new Mock<IIndexer>();
        }

        private DownloadOrchestrator CreateSut()
        {
            return new DownloadOrchestrator(
                _mockQueueService.Object,
                _mockFileService.Object,
                _mockConcurrencyManager.Object,
                MockLogger.Object);
        }

        /// <summary>
        /// Covers line 73: LastUpdated is set to DateTime.UtcNow
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:73
        /// Verifies that LastUpdated is set to current time, not from queue stats.
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_SetsLastUpdatedToCurrentTime()
        {
            // Arrange
            var sut = CreateSut();
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = 1,
                CompletedDownloads = 1,
                LastUpdated = DateTime.UtcNow.AddDays(-1) // Old timestamp from queue
            };

            var concurrencyStats = new ConcurrencyStatistics
            {
                ActiveOperations = 0
            };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            var beforeTime = DateTime.UtcNow;

            // Act
            var status = await sut.GetDownloadStatusAsync();

            var afterTime = DateTime.UtcNow;

            // Assert - Line 73: LastUpdated = DateTime.UtcNow
            // The orchestrator sets its own LastUpdated, not from queue stats
            status.LastUpdated.Should().BeOnOrAfter(beforeTime);
            status.LastUpdated.Should().BeOnOrBefore(afterTime);
        }

        /// <summary>
        /// Covers line 73: LastUpdated is always set, not conditional
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:73
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_AlwaysSetsLastUpdated()
        {
            // Arrange
            var sut = CreateSut();
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = 0,
                LastUpdated = DateTime.MinValue // Invalid timestamp
            };

            var concurrencyStats = new ConcurrencyStatistics
            {
                ActiveOperations = 0
            };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            // Act
            var status = await sut.GetDownloadStatusAsync();

            // Assert - Line 73: LastUpdated is always assigned
            status.LastUpdated.Should().BeAfter(DateTime.MinValue);
            status.LastUpdated.Year.Should().Be(DateTime.UtcNow.Year);
        }

        /// <summary>
        /// Covers line 144: Progress calculation division by TotalDownloads
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:144
        /// Tests with large numbers to verify no overflow.
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_WithLargeNumbers_CalculatesCorrectly()
        {
            // Arrange
            var sut = CreateSut();
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = 10000,
                QueuedDownloads = 5000,
                DownloadingDownloads = 2000,
                CompletedDownloads = 2500,
                FailedDownloads = 500,
                TotalBytesDownloaded = long.MaxValue / 2 // Large value
            };

            var concurrencyStats = new ConcurrencyStatistics
            {
                ActiveOperations = 2000
            };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            // Act
            var status = await sut.GetDownloadStatusAsync();

            // Assert - Line 144: (completedWeight + activeWeight) / stats.TotalDownloads
            // Expected: (2500 * 100 + 7000 * 50) / 10000 = (250000 + 350000) / 10000 = 60.0
            status.TotalProgress.Should().Be(60.0);
            status.TotalBytesDownloaded.Should().Be(long.MaxValue / 2);
        }

        /// <summary>
        /// Covers line 143: Active weight calculation with only queued (no downloading)
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:143
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_WithOnlyQueuedDownloads_ReturnsFiftyPercentProgress()
        {
            // Arrange - only queued, no downloading
            var sut = CreateSut();
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = 3,
                QueuedDownloads = 3,
                DownloadingDownloads = 0,
                CompletedDownloads = 0,
                FailedDownloads = 0
            };

            var concurrencyStats = new ConcurrencyStatistics
            {
                ActiveOperations = 0
            };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            // Act
            var status = await sut.GetDownloadStatusAsync();

            // Assert - Line 143: var activeWeight = (stats.QueuedDownloads + stats.DownloadingDownloads) * 50.0;
            // (0 * 100 + 3 * 50) / 3 = 50.0
            status.TotalProgress.Should().Be(50.0);
        }

        /// <summary>
        /// Covers line 143: Active weight calculation with only downloading (no queued)
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:143
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_WithOnlyDownloadingDownloads_ReturnsFiftyPercentProgress()
        {
            // Arrange - only downloading, no queued
            var sut = CreateSut();
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = 2,
                QueuedDownloads = 0,
                DownloadingDownloads = 2,
                CompletedDownloads = 0,
                FailedDownloads = 0
            };

            var concurrencyStats = new ConcurrencyStatistics
            {
                ActiveOperations = 2
            };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            // Act
            var status = await sut.GetDownloadStatusAsync();

            // Assert - Line 143: var activeWeight = (stats.QueuedDownloads + stats.DownloadingDownloads) * 50.0;
            // (0 * 100 + 2 * 50) / 2 = 50.0
            status.TotalProgress.Should().Be(50.0);
        }

        /// <summary>
        /// Covers line 139: Completed weight with single completed download
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:139
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_WithSingleCompleted_Returns100Progress()
        {
            // Arrange
            var sut = CreateSut();
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = 1,
                QueuedDownloads = 0,
                DownloadingDownloads = 0,
                CompletedDownloads = 1,
                FailedDownloads = 0
            };

            var concurrencyStats = new ConcurrencyStatistics
            {
                ActiveOperations = 0
            };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            // Act
            var status = await sut.GetDownloadStatusAsync();

            // Assert - Line 139: var completedWeight = stats.CompletedDownloads * 100.0;
            // (1 * 100 + 0 * 50) / 1 = 100.0
            status.TotalProgress.Should().Be(100.0);
        }

        /// <summary>
        /// Covers line 140: Failed weight is always 0
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:140
        /// Tests mixed scenario with failed downloads contributing 0.
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_WithMixedIncludingFailed_FailedContributesZero()
        {
            // Arrange - 10 total: 5 completed, 2 active, 3 failed
            var sut = CreateSut();
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = 10,
                QueuedDownloads = 1,
                DownloadingDownloads = 1,
                CompletedDownloads = 5,
                FailedDownloads = 3
            };

            var concurrencyStats = new ConcurrencyStatistics
            {
                ActiveOperations = 1
            };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            // Act
            var status = await sut.GetDownloadStatusAsync();

            // Assert - Line 140: var failedWeight = stats.FailedDownloads * 0.0;
            // (5 * 100 + 2 * 50 + 3 * 0) / 10 = 600 / 10 = 60.0
            status.TotalProgress.Should().Be(60.0);
        }

        /// <summary>
        /// Covers line 95: Cancel() is called on the download item
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:95
        /// Verifies Cancel is called and item status changes to Failed.
        /// </summary>
        [Fact]
        public async Task CancelDownloadAsync_WhenItemCanBeCancelled_CallsCancelAndReturnsTrue()
        {
            // Arrange
            var sut = CreateSut();
            var downloadId = "test-download-id";
            var cts = new System.Threading.CancellationTokenSource();
            var downloadItem = new QobuzDownloadItem
            {
                DownloadId = downloadId,
                Title = "Test Album",
                Status = DownloadItemStatus.Downloading,
                CancellationTokenSource = cts
            };

            _mockQueueService.Setup(x => x.TryGetDownload(downloadId, out downloadItem))
                .Returns(true);

            // Act
            var result = await sut.CancelDownloadAsync(downloadId);

            // Assert - Line 95: downloadItem.Cancel();
            result.Should().BeTrue();
            downloadItem.Status.Should().Be(DownloadItemStatus.Failed);
            downloadItem.Message.Should().Be("Download cancelled by user");
            cts.IsCancellationRequested.Should().BeTrue();
        }

        /// <summary>
        /// Covers line 95: Cancel on already-disposed item still returns true
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:95
        /// QobuzDownloadItem.Cancel() handles disposed state internally.
        /// </summary>
        [Fact]
        public async Task CancelDownloadAsync_WithDisposedItem_StillReturnsTrue()
        {
            // Arrange
            var sut = CreateSut();
            var downloadId = "disposed-item";
            var downloadItem = new QobuzDownloadItem
            {
                DownloadId = downloadId,
                Status = DownloadItemStatus.Downloading,
                CancellationTokenSource = new System.Threading.CancellationTokenSource()
            };

            downloadItem.Dispose();

            _mockQueueService.Setup(x => x.TryGetDownload(downloadId, out downloadItem))
                .Returns(true);

            // Act
            var result = await sut.CancelDownloadAsync(downloadId);

            // Assert - Line 95-97: Cancel is called, then returns true
            result.Should().BeTrue();
            downloadItem.Message.Should().Be("Cannot cancel - download item already disposed");
        }

        /// <summary>
        /// Covers line 87-90: All whitespace variants return false
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:87-90
        /// </summary>
        [Theory]
        [InlineData("\t")]
        [InlineData("\n")]
        [InlineData(" \t \n ")]
        public async Task CancelDownloadAsync_WithVariousWhitespace_ReturnsFalse(string downloadId)
        {
            // Arrange
            var sut = CreateSut();

            // Act
            var result = await sut.CancelDownloadAsync(downloadId);

            // Assert - Line 89: return false;
            result.Should().BeFalse("whitespace-only download ID should return false");
        }

        /// <summary>
        /// Covers line 115-116: Uses 30-minute cutoff for cleanup
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:115-116
        /// Verifies the exact cutoff duration passed to queue service.
        /// </summary>
        [Fact]
        public async Task CleanupCompletedDownloadsAsync_VerifiesExactCutoffDuration()
        {
            // Arrange
            var sut = CreateSut();
            TimeSpan? capturedCutoff = null;

            _mockQueueService.Setup(x => x.CleanupCompletedDownloads(It.IsAny<TimeSpan>()))
                .Callback<TimeSpan>(cutoff => capturedCutoff = cutoff)
                .Returns(0);

            // Act
            await sut.CleanupCompletedDownloadsAsync();

            // Assert - Line 115: var cutoff = TimeSpan.FromMinutes(30);
            capturedCutoff.Should().Be(TimeSpan.FromMinutes(30));
            capturedCutoff.Value.TotalMinutes.Should().Be(30.0);
        }

        /// <summary>
        /// Covers line 123: Returns exact count from queue service
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:123
        /// </summary>
        [Fact]
        public async Task CleanupCompletedDownloadsAsync_ReturnsExactCountFromQueueService()
        {
            // Arrange
            var sut = CreateSut();
            const int expectedCount = 42;
            _mockQueueService.Setup(x => x.CleanupCompletedDownloads(It.IsAny<TimeSpan>()))
                .Returns(expectedCount);

            // Act
            var result = await sut.CleanupCompletedDownloadsAsync();

            // Assert - Line 123: return cleanedUp;
            result.Should().Be(42);
        }

        /// <summary>
        /// Covers line 44: Generated download ID is unique across calls
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:44
        /// </summary>
        [Fact]
        public async Task StartDownloadAsync_GeneratesUniqueIdsAcrossCalls()
        {
            // Arrange
            var sut = CreateSut();
            var remoteAlbum = CreateTestRemoteAlbum("Artist", "Album");

            // Act
            var id1 = await sut.StartDownloadAsync(remoteAlbum, _mockIndexer.Object);
            var id2 = await sut.StartDownloadAsync(remoteAlbum, _mockIndexer.Object);

            // Assert - Line 44: var downloadId = Guid.NewGuid().ToString("N");
            id1.Should().NotBe(id2, "each call should generate a unique GUID");
            id1.Length.Should().Be(32);
            id2.Length.Should().Be(32);
        }

        /// <summary>
        /// Covers line 38-39: Album with null Title uses "Unknown Album"
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:38
        /// </summary>
        [Fact]
        public async Task StartDownloadAsync_WithNullAlbumTitle_StillReturnsDownloadId()
        {
            // Arrange
            var sut = CreateSut();
            var remoteAlbum = new RemoteAlbum
            {
                Artist = new Artist { Name = "Artist" },
                Albums = new List<Album> { new Album { Title = null } }
            };

            // Act
            var result = await sut.StartDownloadAsync(remoteAlbum, _mockIndexer.Object);

            // Assert - Line 38: remoteAlbum.Albums?.FirstOrDefault()?.Title ?? "Unknown Album"
            result.Length.Should().Be(32);
        }

        /// <summary>
        /// Covers line 39: Artist with null Name uses "Unknown Artist"
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:39
        /// </summary>
        [Fact]
        public async Task StartDownloadAsync_WithNullArtistName_StillReturnsDownloadId()
        {
            // Arrange
            var sut = CreateSut();
            var remoteAlbum = new RemoteAlbum
            {
                Artist = new Artist { Name = null },
                Albums = new List<Album> { new Album { Title = "Album" } }
            };

            // Act
            var result = await sut.StartDownloadAsync(remoteAlbum, _mockIndexer.Object);

            // Assert - Line 39: remoteAlbum.Artist?.Name ?? "Unknown Artist"
            result.Length.Should().Be(32);
        }

        /// <summary>
        /// Covers line 62-63: QueueService and ConcurrencyManager are called once each
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:62-63
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_CallsBothServicesExactlyOnce()
        {
            // Arrange
            var sut = CreateSut();
            var queueStats = new DownloadQueueStatistics { TotalDownloads = 1 };
            var concurrencyStats = new ConcurrencyStatistics { ActiveOperations = 1 };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            // Act
            await sut.GetDownloadStatusAsync();

            // Assert - Line 62-63: Both services called exactly once
            _mockQueueService.Verify(x => x.GetQueueStatistics(), Times.Once);
            _mockConcurrencyManager.Verify(x => x.GetStatistics(), Times.Once);
        }

        /// <summary>
        /// Covers line 116: CleanupCompletedDownloads calls queue service exactly once
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:116
        /// </summary>
        [Fact]
        public async Task CleanupCompletedDownloadsAsync_CallsQueueServiceExactlyOnce()
        {
            // Arrange
            var sut = CreateSut();
            _mockQueueService.Setup(x => x.CleanupCompletedDownloads(It.IsAny<TimeSpan>()))
                .Returns(0);

            // Act
            await sut.CleanupCompletedDownloadsAsync();

            // Assert - Line 116: var cleanedUp = _queueService.CleanupCompletedDownloads(cutoff);
            _mockQueueService.Verify(x => x.CleanupCompletedDownloads(It.IsAny<TimeSpan>()), Times.Once);
        }

        /// <summary>
        /// Covers line 93: TryGetDownload called with correct download ID
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:93
        /// </summary>
        [Fact]
        public async Task CancelDownloadAsync_CallsTryGetDownloadWithCorrectId()
        {
            // Arrange
            var sut = CreateSut();
            var downloadId = "exact-id-check";
            QobuzDownloadItem downloadItem = null;

            _mockQueueService.Setup(x => x.TryGetDownload(downloadId, out downloadItem))
                .Returns(false);

            // Act
            await sut.CancelDownloadAsync(downloadId);

            // Assert - Line 93: _queueService.TryGetDownload(downloadId, out var downloadItem)
            _mockQueueService.Verify(x => x.TryGetDownload("exact-id-check", out It.Ref<QobuzDownloadItem>.IsAny), Times.Once);
        }

        /// <summary>
        /// Covers line 134-135: TotalDownloads of 0 returns 0 progress (early return path)
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:134-135
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_ZeroTotal_EarlyReturnsZeroProgress()
        {
            // Arrange
            var sut = CreateSut();
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = 0,
                QueuedDownloads = 0,
                DownloadingDownloads = 0,
                CompletedDownloads = 0,
                FailedDownloads = 0
            };

            var concurrencyStats = new ConcurrencyStatistics { ActiveOperations = 0 };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            // Act
            var status = await sut.GetDownloadStatusAsync();

            // Assert - Line 134-135: if (stats.TotalDownloads == 0) return 0;
            status.TotalProgress.Should().Be(0.0);
            status.ActiveDownloads.Should().Be(0);
            status.QueuedDownloads.Should().Be(0);
            status.CompletedDownloads.Should().Be(0);
            status.FailedDownloads.Should().Be(0);
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
