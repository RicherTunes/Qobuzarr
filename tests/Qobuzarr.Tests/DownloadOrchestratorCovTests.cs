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
using Lidarr.Plugin.Qobuzarr.Download.Orchestration;
using Qobuzarr.Tests.Fixtures;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Coverage tests for DownloadOrchestrator exception rethrow paths.
    /// Tests lines 54 and 79 in DownloadOrchestrator.cs (throw statements in catch blocks).
    /// </summary>
    public class DownloadOrchestratorCovTests : TestFixtureBase
    {
        private readonly Mock<IDownloadQueueService> _mockQueueService;
        private readonly Mock<IDownloadFileService> _mockFileService;
        private readonly Mock<IConcurrencyManager> _mockConcurrencyManager;
        private readonly Mock<IIndexer> _mockIndexer;
        private readonly DownloadOrchestrator _sut;

        public DownloadOrchestratorCovTests()
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

        /// <summary>
        /// Covers line 79: throw in GetDownloadStatusAsync catch block.
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:79
        /// When _queueService.GetQueueStatistics() throws, the exception should be rethrown.
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_WhenQueueServiceThrows_RethrowsException()
        {
            // Arrange
            var expectedException = new InvalidOperationException("Queue service failure");
            _mockQueueService.Setup(x => x.GetQueueStatistics()).Throws(expectedException);

            // Act & Assert
            var actualException = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.GetDownloadStatusAsync());

            actualException.Message.Should().Be("Queue service failure");
            actualException.Should().BeSameAs(expectedException);
        }

        /// <summary>
        /// Covers line 79: throw in GetDownloadStatusAsync catch block.
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:79
        /// When _concurrencyManager.GetStatistics() throws, the exception should be rethrown.
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_WhenConcurrencyManagerThrows_RethrowsException()
        {
            // Arrange
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = 1,
                CompletedDownloads = 1
            };
            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);

            var expectedException = new InvalidOperationException("Concurrency manager failure");
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Throws(expectedException);

            // Act & Assert
            var actualException = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.GetDownloadStatusAsync());

            actualException.Message.Should().Be("Concurrency manager failure");
            actualException.Should().BeSameAs(expectedException);
        }

        /// <summary>
        /// Covers line 132: CalculateOverallProgress with active downloads contributing 50% progress.
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:132
        /// Tests the progress calculation where queued + downloading contribute 50% average.
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_WithOnlyActiveDownloads_CalculatesFiftyPercentProgress()
        {
            // Arrange - 2 active downloads (queued + downloading), 0 completed
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = 2,
                QueuedDownloads = 1,
                DownloadingDownloads = 1,
                CompletedDownloads = 0,
                FailedDownloads = 0,
                TotalBytesDownloaded = 500L
            };

            var concurrencyStats = new ConcurrencyStatistics
            {
                ActiveOperations = 1,
                LastUpdated = DateTime.UtcNow
            };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            // Act
            var status = await _sut.GetDownloadStatusAsync();

            // Assert - (0 * 100 + 2 * 50) / 2 = 50%
            status.TotalProgress.Should().Be(50.0);
        }

        /// <summary>
        /// Covers line 134: Failed downloads contribute 0% to progress.
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:134
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_WithOnlyFailedDownloads_ReturnsZeroProgress()
        {
            // Arrange
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = 3,
                QueuedDownloads = 0,
                DownloadingDownloads = 0,
                CompletedDownloads = 0,
                FailedDownloads = 3,
                TotalBytesDownloaded = 0L
            };

            var concurrencyStats = new ConcurrencyStatistics
            {
                ActiveOperations = 0,
                LastUpdated = DateTime.UtcNow
            };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            // Act
            var status = await _sut.GetDownloadStatusAsync();

            // Assert - Failed downloads contribute 0%
            status.TotalProgress.Should().Be(0.0);
        }

        /// <summary>
        /// Covers line 137: Progress calculation with mixed completed and active.
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:137
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_WithMixedStates_CalculatesCorrectProgress()
        {
            // Arrange - 6 total: 3 completed (100%), 2 active (50%), 1 failed (0%)
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = 6,
                QueuedDownloads = 1,
                DownloadingDownloads = 1,
                CompletedDownloads = 3,
                FailedDownloads = 1,
                TotalBytesDownloaded = 3000L
            };

            var concurrencyStats = new ConcurrencyStatistics
            {
                ActiveOperations = 1,
                LastUpdated = DateTime.UtcNow
            };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            // Act
            var status = await _sut.GetDownloadStatusAsync();

            // Assert - (3 * 100 + 2 * 50 + 1 * 0) / 6 = 400 / 6 = 66.67%
            status.TotalProgress.Should().BeApproximately(66.67, 0.01);
        }

        /// <summary>
        /// Covers line 65-73: DownloadOrchestrationStatus property mapping.
        /// Source: src/Download/Orchestration/DownloadOrchestrator.cs:65-73
        /// </summary>
        [Fact]
        public async Task GetDownloadStatusAsync_MapsAllPropertiesCorrectly()
        {
            // Arrange
            var expectedTime = DateTime.UtcNow;
            var queueStats = new DownloadQueueStatistics
            {
                TotalDownloads = 10,
                QueuedDownloads = 3,
                DownloadingDownloads = 2,
                CompletedDownloads = 4,
                FailedDownloads = 1,
                TotalBytesDownloaded = 1234567L,
                LastUpdated = expectedTime
            };

            var concurrencyStats = new ConcurrencyStatistics
            {
                MaxConcurrency = 5,
                ActiveOperations = 2,
                QueuedOperations = 3,
                TotalSlotsUsed = 2,
                LastUpdated = expectedTime
            };

            _mockQueueService.Setup(x => x.GetQueueStatistics()).Returns(queueStats);
            _mockConcurrencyManager.Setup(x => x.GetStatistics()).Returns(concurrencyStats);

            // Act
            var status = await _sut.GetDownloadStatusAsync();

            // Assert - Verify all properties are mapped correctly
            status.ActiveDownloads.Should().Be(2);       // From concurrencyStats.ActiveOperations
            status.QueuedDownloads.Should().Be(3);      // From queueStats.QueuedDownloads
            status.CompletedDownloads.Should().Be(4);   // From queueStats.CompletedDownloads
            status.FailedDownloads.Should().Be(1);      // From queueStats.FailedDownloads
            status.TotalBytesDownloaded.Should().Be(1234567L);
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
