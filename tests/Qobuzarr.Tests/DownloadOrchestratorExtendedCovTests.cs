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
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Download.Orchestration;
using Qobuzarr.Tests.Fixtures;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Extended coverage tests for DownloadOrchestrator.
    /// Covers constructor validation, StartDownloadAsync, CancelDownloadAsync, CleanupCompletedDownloadsAsync,
    /// and additional GetDownloadStatusAsync edge cases not covered by DownloadOrchestratorCovTests.
    /// Source: src/Download/Orchestration/DownloadOrchestrator.cs
    /// </summary>
    public class DownloadOrchestratorExtendedCovTests : TestFixtureBase
    {
        private readonly Mock<IDownloadQueueService> _mockQueueService;
        private readonly Mock<IDownloadFileService> _mockFileService;
        private readonly Mock<IConcurrencyManager> _mockConcurrencyManager;
        private readonly Mock<IIndexer> _mockIndexer;

        public DownloadOrchestratorExtendedCovTests()
        {
            _mockQueueService = new Mock<IDownloadQueueService>();
            _mockFileService = new Mock<IDownloadFileService>();
            _mockConcurrencyManager = new Mock<IConcurrencyManager>();
            _mockIndexer = new Mock<IIndexer>();
        }

        private DownloadOrchestrator CreateSut(
            IDownloadQueueService queueService = null,
            IDownloadFileService fileService = null,
            IConcurrencyManager concurrencyManager = null,
            Logger logger = null)
        {
            return new DownloadOrchestrator(
                queueService ?? _mockQueueService.Object,
                fileService ?? _mockFileService.Object,
                concurrencyManager ?? _mockConcurrencyManager.Object,
                logger ?? MockLogger.Object);
        }

        #region Constructor ArgumentNullException Tests (Lines 28-31)

        /// <summary>
        /// Covers line 28: throw new ArgumentNullException(nameof(queueService))
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:28
        /// </summary>
        [Fact]
        public void Constructor_WithNullQueueService_ThrowsArgumentNullException()
        {
            // Source line 28: _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new DownloadOrchestrator(
                    null,
                    _mockFileService.Object,
                    _mockConcurrencyManager.Object,
                    MockLogger.Object));

            ex.ParamName.Should().Be("queueService");
        }

        /// <summary>
        /// Covers line 29: throw new ArgumentNullException(nameof(fileService))
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:29
        /// </summary>
        [Fact]
        public void Constructor_WithNullFileService_ThrowsArgumentNullException()
        {
            // Source line 29: _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new DownloadOrchestrator(
                    _mockQueueService.Object,
                    null,
                    _mockConcurrencyManager.Object,
                    MockLogger.Object));

            ex.ParamName.Should().Be("fileService");
        }

        /// <summary>
        /// Covers line 30: throw new ArgumentNullException(nameof(concurrencyManager))
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:30
        /// </summary>
        [Fact]
        public void Constructor_WithNullConcurrencyManager_ThrowsArgumentNullException()
        {
            // Source line 30: _concurrencyManager = concurrencyManager ?? throw new ArgumentNullException(nameof(concurrencyManager));
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new DownloadOrchestrator(
                    _mockQueueService.Object,
                    _mockFileService.Object,
                    null,
                    MockLogger.Object));

            ex.ParamName.Should().Be("concurrencyManager");
        }

        /// <summary>
        /// Covers line 31: throw new ArgumentNullException(nameof(logger))
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:31
        /// </summary>
        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Source line 31: _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new DownloadOrchestrator(
                    _mockQueueService.Object,
                    _mockFileService.Object,
                    _mockConcurrencyManager.Object,
                    null));

            ex.ParamName.Should().Be("logger");
        }

        #endregion

        #region StartDownloadAsync Tests (Lines 34-56)

        /// <summary>
        /// Covers line 44: Generates unique download ID as GUID in N format
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:44
        /// </summary>
        [Fact]
        public async Task StartDownloadAsync_WithValidAlbum_ReturnsGuidFormatId()
        {
            // Arrange
            var sut = CreateSut();
            var remoteAlbum = CreateTestRemoteAlbum("Test Artist", "Test Album");

            // Act
            var result = await sut.StartDownloadAsync(remoteAlbum, _mockIndexer.Object);

            // Assert - Line 44: var downloadId = Guid.NewGuid().ToString("N");
            result.Length.Should().Be(32, "GUID without hyphens has 32 characters");
            Guid.TryParse(result, out var parsedGuid).Should().BeTrue("result should be a valid GUID");
            parsedGuid.ToString("N").Should().Be(result, "GUID should be in N format (no hyphens)");
        }

        /// <summary>
        /// Covers line 38: Uses "Unknown Album" when Albums is null via null-conditional
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:38
        /// </summary>
        [Fact]
        public async Task StartDownloadAsync_WithNullAlbums_ReturnsDownloadId()
        {
            // Arrange
            var sut = CreateSut();
            var remoteAlbum = new RemoteAlbum
            {
                Artist = new Artist { Name = "Test Artist" },
                Albums = null
            };

            // Act
            var result = await sut.StartDownloadAsync(remoteAlbum, _mockIndexer.Object);

            // Assert - Line 38: var albumTitle = remoteAlbum.Albums?.FirstOrDefault()?.Title ?? "Unknown Album";
            result.Length.Should().Be(32, "should still generate a valid download ID");
        }

        /// <summary>
        /// Covers line 39: Uses "Unknown Artist" when Artist is null via null-conditional
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:39
        /// </summary>
        [Fact]
        public async Task StartDownloadAsync_WithNullArtist_ReturnsDownloadId()
        {
            // Arrange
            var sut = CreateSut();
            var remoteAlbum = new RemoteAlbum
            {
                Artist = null,
                Albums = new List<Album> { new Album { Title = "Test Album" } }
            };

            // Act
            var result = await sut.StartDownloadAsync(remoteAlbum, _mockIndexer.Object);

            // Assert - Line 39: var artistName = remoteAlbum.Artist?.Name ?? "Unknown Artist";
            result.Length.Should().Be(32, "should still generate a valid download ID");
        }

        /// <summary>
        /// Covers line 38: FirstOrDefault() returns null for empty Albums list
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:38
        /// </summary>
        [Fact]
        public async Task StartDownloadAsync_WithEmptyAlbumsList_ReturnsDownloadId()
        {
            // Arrange
            var sut = CreateSut();
            var remoteAlbum = new RemoteAlbum
            {
                Artist = new Artist { Name = "Test Artist" },
                Albums = new List<Album>()
            };

            // Act
            var result = await sut.StartDownloadAsync(remoteAlbum, _mockIndexer.Object);

            // Assert - Line 38: FirstOrDefault() on empty list returns null
            result.Length.Should().Be(32, "should still generate a valid download ID");
        }

        /// <summary>
        /// Covers lines 38-39: Both Artist and Albums are null
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:38-39
        /// </summary>
        [Fact]
        public async Task StartDownloadAsync_WithNullArtistAndNullAlbums_ReturnsDownloadId()
        {
            // Arrange
            var sut = CreateSut();
            var remoteAlbum = new RemoteAlbum
            {
                Artist = null,
                Albums = null
            };

            // Act
            var result = await sut.StartDownloadAsync(remoteAlbum, _mockIndexer.Object);

            // Assert - Both null-conditional operators yield null, defaults used
            result.Length.Should().Be(32, "should still generate a valid download ID");
        }

        /// <summary>
        /// Covers line 49: Returns the generated download ID
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:49
        /// </summary>
        [Fact]
        public async Task StartDownloadAsync_ReturnsNonEmptyString()
        {
            // Arrange
            var sut = CreateSut();
            var remoteAlbum = CreateTestRemoteAlbum("Artist Name", "Album Title");

            // Act
            var result = await sut.StartDownloadAsync(remoteAlbum, _mockIndexer.Object);

            // Assert - Line 49: return downloadId;
            result.Should().NotBeNullOrEmpty("download ID should be generated");
        }

        #endregion

        #region GetDownloadStatusAsync Tests (Lines 58-81)

        /// <summary>
        /// Covers line 79: throw in GetDownloadStatusAsync catch block
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:79
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_WhenQueueServiceThrows_RethrowsException()
        {
            // Arrange
            var sut = CreateSut();
            var expectedException = new InvalidOperationException("Queue service failure");
            _mockQueueService.Setup(x => x.GetQueueStatistics()).Throws(expectedException);

            // Act & Assert - Source line 79: throw;
            var actualException = await Assert.ThrowsAsync<InvalidOperationException>(
                () => sut.GetDownloadStatusAsync());

            actualException.Message.Should().Be("Queue service failure");
            actualException.Should().BeSameAs(expectedException);
        }

        /// <summary>
        /// Covers line 79: throw when concurrency manager throws
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:79
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_WhenConcurrencyManagerThrows_RethrowsException()
        {
            // Arrange
            var sut = CreateSut();
            var queueStats = new DownloadQueueStatistics { TotalDownloads = 1 };
            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);

            var expectedException = new InvalidOperationException("Concurrency failure");
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Throws(expectedException);

            // Act & Assert - Source line 79: throw;
            var actualException = await Assert.ThrowsAsync<InvalidOperationException>(
                () => sut.GetDownloadStatusAsync());

            actualException.Message.Should().Be("Concurrency failure");
            actualException.Should().BeSameAs(expectedException);
        }

        /// <summary>
        /// Covers lines 65-73: DownloadOrchestrationStatus property mapping
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:65-73
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_MapsAllPropertiesCorrectly()
        {
            // Arrange
            var sut = CreateSut();
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = 10,
                QueuedDownloads = 3,
                DownloadingDownloads = 2,
                CompletedDownloads = 4,
                FailedDownloads = 1,
                TotalBytesDownloaded = 1234567L
            };

            var concurrencyStats = new ConcurrencyStatistics
            {
                ActiveOperations = 2
            };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            // Act
            var status = await sut.GetDownloadStatusAsync();

            // Assert - Verify all property mappings from lines 65-73
            status.ActiveDownloads.Should().Be(2, "line 67: ActiveDownloads = concurrencyStats.ActiveOperations");
            status.QueuedDownloads.Should().Be(3, "line 68: QueuedDownloads = queueStats.QueuedDownloads");
            status.CompletedDownloads.Should().Be(4, "line 69: CompletedDownloads = queueStats.CompletedDownloads");
            status.FailedDownloads.Should().Be(1, "line 70: FailedDownloads = queueStats.FailedDownloads");
            status.TotalBytesDownloaded.Should().Be(1234567L, "line 72: TotalBytesDownloaded = queueStats.TotalBytesDownloaded");
        }

        /// <summary>
        /// Covers line 134-135: TotalDownloads == 0 returns 0 progress
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:134-135
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_WithZeroTotalDownloads_ReturnsZeroProgress()
        {
            // Arrange
            var sut = CreateSut();
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = 0,
                QueuedDownloads = 0,
                CompletedDownloads = 0,
                FailedDownloads = 0
            };

            var concurrencyStats = new ConcurrencyStatistics { ActiveOperations = 0 };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            // Act
            var status = await sut.GetDownloadStatusAsync();

            // Assert - Line 134-135: if (stats.TotalDownloads == 0) return 0;
            status.TotalProgress.Should().Be(0.0, "zero total downloads should yield zero progress");
        }

        /// <summary>
        /// Covers line 139: Completed downloads contribute 100% weight
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:139
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_WithOnlyCompletedDownloads_Returns100Progress()
        {
            // Arrange
            var sut = CreateSut();
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = 5,
                QueuedDownloads = 0,
                DownloadingDownloads = 0,
                CompletedDownloads = 5,
                FailedDownloads = 0
            };

            var concurrencyStats = new ConcurrencyStatistics { ActiveOperations = 0 };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            // Act
            var status = await sut.GetDownloadStatusAsync();

            // Assert - Line 139: (5 * 100 + 0) / 5 = 100
            status.TotalProgress.Should().Be(100.0, "all completed downloads should yield 100% progress");
        }

        /// <summary>
        /// Covers line 140: Failed downloads contribute 0% weight
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:140
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_WithOnlyFailedDownloads_ReturnsZeroProgress()
        {
            // Arrange
            var sut = CreateSut();
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = 3,
                QueuedDownloads = 0,
                DownloadingDownloads = 0,
                CompletedDownloads = 0,
                FailedDownloads = 3
            };

            var concurrencyStats = new ConcurrencyStatistics { ActiveOperations = 0 };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            // Act
            var status = await sut.GetDownloadStatusAsync();

            // Assert - Line 140: var failedWeight = stats.FailedDownloads * 0.0;
            status.TotalProgress.Should().Be(0.0, "all failed downloads should yield zero progress");
        }

        /// <summary>
        /// Covers line 143: Active downloads contribute 50% average weight
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:143
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_WithOnlyActiveDownloads_ReturnsFiftyPercentProgress()
        {
            // Arrange - 2 active (queued + downloading), 0 completed
            var sut = CreateSut();
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = 2,
                QueuedDownloads = 1,
                DownloadingDownloads = 1,
                CompletedDownloads = 0,
                FailedDownloads = 0
            };

            var concurrencyStats = new ConcurrencyStatistics { ActiveOperations = 1 };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            // Act
            var status = await sut.GetDownloadStatusAsync();

            // Assert - Line 143: (0 * 100 + 2 * 50) / 2 = 50
            status.TotalProgress.Should().Be(50.0, "only active downloads should yield 50% progress");
        }

        /// <summary>
        /// Covers line 145: Combined progress calculation with mixed states
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:145
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_WithMixedStates_CalculatesCorrectProgress()
        {
            // Arrange - 6 total: 3 completed, 2 active, 1 failed
            var sut = CreateSut();
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = 6,
                QueuedDownloads = 1,
                DownloadingDownloads = 1,
                CompletedDownloads = 3,
                FailedDownloads = 1
            };

            var concurrencyStats = new ConcurrencyStatistics { ActiveOperations = 1 };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            // Act
            var status = await sut.GetDownloadStatusAsync();

            // Assert - Line 145: (3 * 100 + 2 * 50) / 6 = 400 / 6 = 66.67%
            status.TotalProgress.Should().BeApproximately(66.67, 0.01);
        }

        #endregion

        #region CancelDownloadAsync Tests (Lines 83-108)

        /// <summary>
        /// Covers lines 87-90: Returns false when downloadId is null/empty/whitespace
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:87-90
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task CancelDownloadAsync_WithInvalidDownloadId_ReturnsFalse(string downloadId)
        {
            // Arrange
            var sut = CreateSut();

            // Act
            var result = await sut.CancelDownloadAsync(downloadId);

            // Assert - Line 89: return false;
            result.Should().BeFalse("invalid download ID should return false");
        }

        /// <summary>
        /// Covers lines 93-98: Cancels download when found in queue
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:93-98
        /// </summary>
        [Fact]
        public async Task CancelDownloadAsync_WhenDownloadFound_CancelsAndReturnsTrue()
        {
            // Arrange
            var sut = CreateSut();
            var downloadId = "test-download-id-123";
            var mockItem = new QobuzDownloadItem
            {
                DownloadId = downloadId,
                Title = "Test Album",
                Status = NzbDrone.Core.Download.DownloadItemStatus.Downloading,
                CancellationTokenSource = new System.Threading.CancellationTokenSource()
            };

            _mockQueueService.Setup(x => x.TryGetDownload(downloadId, out mockItem))
                .Returns(true);

            // Act
            var result = await sut.CancelDownloadAsync(downloadId);

            // Assert - Line 97: return true;
            result.Should().BeTrue("download was found and cancelled");
            mockItem.Status.Should().Be(NzbDrone.Core.Download.DownloadItemStatus.Failed,
                "cancelled download should have Failed status");
        }

        /// <summary>
        /// Covers lines 100-101: Returns false when download not found
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:100-101
        /// </summary>
        [Fact]
        public async Task CancelDownloadAsync_WhenDownloadNotFound_ReturnsFalse()
        {
            // Arrange
            var sut = CreateSut();
            var downloadId = "non-existent-id";
            QobuzDownloadItem mockItem = null;

            _mockQueueService.Setup(x => x.TryGetDownload(downloadId, out mockItem))
                .Returns(false);

            // Act
            var result = await sut.CancelDownloadAsync(downloadId);

            // Assert - Line 101: return false;
            result.Should().BeFalse("download not found should return false");
        }

        /// <summary>
        /// Covers lines 103-107: Returns false when exception occurs during cancellation
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:103-107
        /// </summary>
        [Fact]
        public async Task CancelDownloadAsync_WhenExceptionOccurs_ReturnsFalse()
        {
            // Arrange
            var sut = CreateSut();
            var downloadId = "test-id";
            var mockItem = new QobuzDownloadItem
            {
                DownloadId = downloadId,
                Status = NzbDrone.Core.Download.DownloadItemStatus.Downloading
            };

            _mockQueueService.Setup(x => x.TryGetDownload(downloadId, out mockItem))
                .Throws(new InvalidOperationException("Database error"));

            // Act
            var result = await sut.CancelDownloadAsync(downloadId);

            // Assert - Line 106: return false;
            result.Should().BeFalse("exception during cancellation should return false");
        }

        #endregion

        #region CleanupCompletedDownloadsAsync Tests (Lines 110-130)

        /// <summary>
        /// Covers lines 116-123: Returns count of cleaned up downloads
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:116-123
        /// </summary>
        [Fact]
        public async Task CleanupCompletedDownloadsAsync_WithCompletedDownloads_ReturnsCleanupCount()
        {
            // Arrange
            var sut = CreateSut();
            _mockQueueService.Setup(x => x.CleanupCompletedDownloads(It.IsAny<TimeSpan>()))
                .Returns(5);

            // Act
            var result = await sut.CleanupCompletedDownloadsAsync();

            // Assert - Line 123: return cleanedUp;
            result.Should().Be(5, "should return the number of cleaned up downloads");
        }

        /// <summary>
        /// Covers line 115: Uses 30 minute cutoff
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:115
        /// </summary>
        [Fact]
        public async Task CleanupCompletedDownloadsAsync_UsesThirtyMinuteCutoff()
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
            capturedCutoff.Should().Be(TimeSpan.FromMinutes(30), "cutoff should be 30 minutes");
        }

        /// <summary>
        /// Covers line 123: Returns 0 when no downloads to clean up
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:123
        /// </summary>
        [Fact]
        public async Task CleanupCompletedDownloadsAsync_WithNoCompletedDownloads_ReturnsZero()
        {
            // Arrange
            var sut = CreateSut();
            _mockQueueService.Setup(x => x.CleanupCompletedDownloads(It.IsAny<TimeSpan>()))
                .Returns(0);

            // Act
            var result = await sut.CleanupCompletedDownloadsAsync();

            // Assert - Line 123: return cleanedUp;
            result.Should().Be(0, "no downloads to clean up should return 0");
        }

        /// <summary>
        /// Covers lines 126-129: Returns 0 when exception occurs
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:126-129
        /// </summary>
        [Fact]
        public async Task CleanupCompletedDownloadsAsync_WhenExceptionOccurs_ReturnsZero()
        {
            // Arrange
            var sut = CreateSut();
            _mockQueueService.Setup(x => x.CleanupCompletedDownloads(It.IsAny<TimeSpan>()))
                .Throws(new InvalidOperationException("Cleanup failed"));

            // Act
            var result = await sut.CleanupCompletedDownloadsAsync();

            // Assert - Line 128: return 0;
            result.Should().Be(0, "exception during cleanup should return 0");
        }

        #endregion

        #region Helper Methods

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

        #endregion
    }
}
