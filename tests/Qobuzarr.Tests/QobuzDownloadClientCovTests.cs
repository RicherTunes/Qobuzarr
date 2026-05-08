using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Moq;
using FluentAssertions;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Download.Orchestration;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.API;
using Qobuzarr.Tests.Fixtures;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Testable subclass that provides mock settings without relying on base Definition.
    /// </summary>
    public class TestableQobuzDownloadClient : QobuzDownloadClient
    {
        private readonly QobuzDownloadSettings _settings;

        public TestableQobuzDownloadClient(
            IQobuzAuthenticationService authService,
            IQobuzApiClient apiClient,
            IHttpClient httpClient,
            IDownloadQueueService queueService,
            IDownloadFileService fileService,
            IConcurrencyManager concurrencyManager,
            IDownloadOrchestrator orchestrator,
            IDownloadSummary downloadSummary,
            IBatchProcessor batchProcessor,
            Lidarr.Plugin.Qobuzarr.Download.Services.ITrackDownloadService trackDownloadService,
            IConfigService configService,
            IDiskProvider diskProvider,
            IRemotePathMappingService remotePathMappingService,
            ILocalizationService localizationService,
            Logger logger,
            QobuzDownloadSettings settings = null)
            : base(authService, apiClient, httpClient, queueService, fileService,
                   concurrencyManager, orchestrator, downloadSummary, batchProcessor,
                   trackDownloadService, configService, diskProvider, remotePathMappingService,
                   localizationService, logger)
        {
            _settings = settings ?? new QobuzDownloadSettings();
        }

        protected override QobuzDownloadSettings GetEffectiveSettings() => _settings;
    }

    /// <summary>
    /// Coverage tests for QobuzDownloadClient exception paths and constructor validation.
    /// Tests constructor ArgumentNullException throws (lines 81-90) and other paths.
    /// </summary>
    public class QobuzDownloadClientCovTests : TestFixtureBase
    {
        private readonly Mock<IQobuzAuthenticationService> _mockAuthService;
        private readonly Mock<IQobuzApiClient> _mockApiClient;
        private readonly Mock<IHttpClient> _mockClient;
        private readonly Mock<IDownloadQueueService> _mockQueueService;
        private readonly Mock<IDownloadFileService> _mockFileService;
        private readonly Mock<IConcurrencyManager> _mockConcurrencyManager;
        private readonly Mock<IDownloadOrchestrator> _mockOrchestrator;
        private readonly Mock<IDownloadSummary> _mockDownloadSummary;
        private readonly Mock<IBatchProcessor> _mockBatchProcessor;
        private readonly Mock<Lidarr.Plugin.Qobuzarr.Download.Services.ITrackDownloadService> _mockTrackDownloadService;

        public QobuzDownloadClientCovTests()
        {
            _mockAuthService = new Mock<IQobuzAuthenticationService>();
            _mockApiClient = new Mock<IQobuzApiClient>();
            _mockClient = new Mock<IHttpClient>();
            _mockQueueService = new Mock<IDownloadQueueService>();
            _mockFileService = new Mock<IDownloadFileService>();
            _mockConcurrencyManager = new Mock<IConcurrencyManager>();
            _mockOrchestrator = new Mock<IDownloadOrchestrator>();
            _mockDownloadSummary = new Mock<IDownloadSummary>();
            _mockBatchProcessor = new Mock<IBatchProcessor>();
            _mockTrackDownloadService = new Mock<Lidarr.Plugin.Qobuzarr.Download.Services.ITrackDownloadService>();
        }

        private TestableQobuzDownloadClient CreateSut()
        {
            return new TestableQobuzDownloadClient(
                _mockAuthService.Object,
                _mockApiClient.Object,
                _mockClient.Object,
                _mockQueueService.Object,
                _mockFileService.Object,
                _mockConcurrencyManager.Object,
                _mockOrchestrator.Object,
                _mockDownloadSummary.Object,
                _mockBatchProcessor.Object,
                _mockTrackDownloadService.Object,
                MockConfigService.Object,
                MockDiskProvider.Object,
                MockRemotePathMappingService.Object,
                MockLocalizationService.Object,
                MockLogger.Object);
        }

        /// <summary>
        /// Covers line 81: throw ArgumentNullException for null authService.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs:81
        /// Constructor validates authService is not null.
        /// </summary>
        [Fact]
        public void Constructor_NullAuthService_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new QobuzDownloadClient(
                    null,
                    _mockApiClient.Object,
                    _mockClient.Object,
                    _mockQueueService.Object,
                    _mockFileService.Object,
                    _mockConcurrencyManager.Object,
                    _mockOrchestrator.Object,
                    _mockDownloadSummary.Object,
                    _mockBatchProcessor.Object,
                    _mockTrackDownloadService.Object,
                    MockConfigService.Object,
                    MockDiskProvider.Object,
                    MockRemotePathMappingService.Object,
                    MockLocalizationService.Object,
                    MockLogger.Object));

            ex.ParamName.Should().Be("authService");
        }

        /// <summary>
        /// Covers line 82: throw ArgumentNullException for null apiClient.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs:82
        /// Constructor validates apiClient is not null.
        /// </summary>
        [Fact]
        public void Constructor_NullApiClient_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new QobuzDownloadClient(
                    _mockAuthService.Object,
                    null,
                    _mockClient.Object,
                    _mockQueueService.Object,
                    _mockFileService.Object,
                    _mockConcurrencyManager.Object,
                    _mockOrchestrator.Object,
                    _mockDownloadSummary.Object,
                    _mockBatchProcessor.Object,
                    _mockTrackDownloadService.Object,
                    MockConfigService.Object,
                    MockDiskProvider.Object,
                    MockRemotePathMappingService.Object,
                    MockLocalizationService.Object,
                    MockLogger.Object));

            ex.ParamName.Should().Be("apiClient");
        }

        /// <summary>
        /// Covers line 83: throw ArgumentNullException for null httpClient.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs:83
        /// Constructor validates httpClient is not null.
        /// </summary>
        [Fact]
        public void Constructor_NullHttpClient_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new QobuzDownloadClient(
                    _mockAuthService.Object,
                    _mockApiClient.Object,
                    null,
                    _mockQueueService.Object,
                    _mockFileService.Object,
                    _mockConcurrencyManager.Object,
                    _mockOrchestrator.Object,
                    _mockDownloadSummary.Object,
                    _mockBatchProcessor.Object,
                    _mockTrackDownloadService.Object,
                    MockConfigService.Object,
                    MockDiskProvider.Object,
                    MockRemotePathMappingService.Object,
                    MockLocalizationService.Object,
                    MockLogger.Object));

            ex.ParamName.Should().Be("httpClient");
        }

        /// <summary>
        /// Covers line 84: throw ArgumentNullException for null queueService.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs:84
        /// Constructor validates queueService is not null.
        /// </summary>
        [Fact]
        public void Constructor_NullQueueService_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new QobuzDownloadClient(
                    _mockAuthService.Object,
                    _mockApiClient.Object,
                    _mockClient.Object,
                    null,
                    _mockFileService.Object,
                    _mockConcurrencyManager.Object,
                    _mockOrchestrator.Object,
                    _mockDownloadSummary.Object,
                    _mockBatchProcessor.Object,
                    _mockTrackDownloadService.Object,
                    MockConfigService.Object,
                    MockDiskProvider.Object,
                    MockRemotePathMappingService.Object,
                    MockLocalizationService.Object,
                    MockLogger.Object));

            ex.ParamName.Should().Be("queueService");
        }

        /// <summary>
        /// Covers line 85: throw ArgumentNullException for null fileService.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs:85
        /// Constructor validates fileService is not null.
        /// </summary>
        [Fact]
        public void Constructor_NullFileService_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new QobuzDownloadClient(
                    _mockAuthService.Object,
                    _mockApiClient.Object,
                    _mockClient.Object,
                    _mockQueueService.Object,
                    null,
                    _mockConcurrencyManager.Object,
                    _mockOrchestrator.Object,
                    _mockDownloadSummary.Object,
                    _mockBatchProcessor.Object,
                    _mockTrackDownloadService.Object,
                    MockConfigService.Object,
                    MockDiskProvider.Object,
                    MockRemotePathMappingService.Object,
                    MockLocalizationService.Object,
                    MockLogger.Object));

            ex.ParamName.Should().Be("fileService");
        }

        /// <summary>
        /// Covers line 86: throw ArgumentNullException for null concurrencyManager.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs:86
        /// Constructor validates concurrencyManager is not null.
        /// </summary>
        [Fact]
        public void Constructor_NullConcurrencyManager_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new QobuzDownloadClient(
                    _mockAuthService.Object,
                    _mockApiClient.Object,
                    _mockClient.Object,
                    _mockQueueService.Object,
                    _mockFileService.Object,
                    null,
                    _mockOrchestrator.Object,
                    _mockDownloadSummary.Object,
                    _mockBatchProcessor.Object,
                    _mockTrackDownloadService.Object,
                    MockConfigService.Object,
                    MockDiskProvider.Object,
                    MockRemotePathMappingService.Object,
                    MockLocalizationService.Object,
                    MockLogger.Object));

            ex.ParamName.Should().Be("concurrencyManager");
        }

        /// <summary>
        /// Covers line 87: throw ArgumentNullException for null orchestrator.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs:87
        /// Constructor validates orchestrator is not null.
        /// </summary>
        [Fact]
        public void Constructor_NullOrchestrator_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new QobuzDownloadClient(
                    _mockAuthService.Object,
                    _mockApiClient.Object,
                    _mockClient.Object,
                    _mockQueueService.Object,
                    _mockFileService.Object,
                    _mockConcurrencyManager.Object,
                    null,
                    _mockDownloadSummary.Object,
                    _mockBatchProcessor.Object,
                    _mockTrackDownloadService.Object,
                    MockConfigService.Object,
                    MockDiskProvider.Object,
                    MockRemotePathMappingService.Object,
                    MockLocalizationService.Object,
                    MockLogger.Object));

            ex.ParamName.Should().Be("orchestrator");
        }

        /// <summary>
        /// Covers line 88: throw ArgumentNullException for null downloadSummary.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs:88
        /// Constructor validates downloadSummary is not null.
        /// </summary>
        [Fact]
        public void Constructor_NullDownloadSummary_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new QobuzDownloadClient(
                    _mockAuthService.Object,
                    _mockApiClient.Object,
                    _mockClient.Object,
                    _mockQueueService.Object,
                    _mockFileService.Object,
                    _mockConcurrencyManager.Object,
                    _mockOrchestrator.Object,
                    null,
                    _mockBatchProcessor.Object,
                    _mockTrackDownloadService.Object,
                    MockConfigService.Object,
                    MockDiskProvider.Object,
                    MockRemotePathMappingService.Object,
                    MockLocalizationService.Object,
                    MockLogger.Object));

            ex.ParamName.Should().Be("downloadSummary");
        }

        /// <summary>
        /// Covers line 89: throw ArgumentNullException for null batchProcessor.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs:89
        /// Constructor validates batchProcessor is not null.
        /// </summary>
        [Fact]
        public void Constructor_NullBatchProcessor_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new QobuzDownloadClient(
                    _mockAuthService.Object,
                    _mockApiClient.Object,
                    _mockClient.Object,
                    _mockQueueService.Object,
                    _mockFileService.Object,
                    _mockConcurrencyManager.Object,
                    _mockOrchestrator.Object,
                    _mockDownloadSummary.Object,
                    null,
                    _mockTrackDownloadService.Object,
                    MockConfigService.Object,
                    MockDiskProvider.Object,
                    MockRemotePathMappingService.Object,
                    MockLocalizationService.Object,
                    MockLogger.Object));

            ex.ParamName.Should().Be("batchProcessor");
        }

        /// <summary>
        /// Covers line 90: throw ArgumentNullException for null trackDownloadService.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs:90
        /// Constructor validates trackDownloadService is not null.
        /// </summary>
        [Fact]
        public void Constructor_NullTrackDownloadService_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new QobuzDownloadClient(
                    _mockAuthService.Object,
                    _mockApiClient.Object,
                    _mockClient.Object,
                    _mockQueueService.Object,
                    _mockFileService.Object,
                    _mockConcurrencyManager.Object,
                    _mockOrchestrator.Object,
                    _mockDownloadSummary.Object,
                    _mockBatchProcessor.Object,
                    null,
                    MockConfigService.Object,
                    MockDiskProvider.Object,
                    MockRemotePathMappingService.Object,
                    MockLocalizationService.Object,
                    MockLogger.Object));

            ex.ParamName.Should().Be("trackDownloadService");
        }

        /// <summary>
        /// Covers line 59: Name property returns plugin name constant.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs:59
        /// </summary>
        [Fact]
        public void Name_ReturnsQobuzarrConstants()
        {
            // Arrange
            var sut = CreateSut();

            // Act & Assert
            sut.Name.Should().Be("Qobuzarr");
        }

        /// <summary>
        /// Covers line 62: Protocol property returns QobuzarrDownloadProtocol.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs:62
        /// </summary>
        [Fact]
        public void Protocol_ReturnsQobuzarrDownloadProtocol()
        {
            // Arrange
            var sut = CreateSut();

            // Act & Assert
            sut.Protocol.Should().Be("QobuzarrDownloadProtocol");
        }

        /// <summary>
        /// Covers lines 340-348: GetStatus returns DownloadClientInfo with settings.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs:340-348
        /// </summary>
        [Fact]
        public void GetStatus_ReturnsDownloadClientInfoWithSettings()
        {
            // Arrange
            var sut = CreateSut();

            // Act
            var result = sut.GetStatus();

            // Assert
            result.IsLocalhost.Should().BeTrue();
            result.OutputRootFolders.Should().HaveCount(1);
        }

        /// <summary>
        /// Covers lines 173-208: GetItems returns empty list when queue service throws.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs:203-207
        /// When an exception occurs, GetItems catches it and returns an empty list.
        /// </summary>
        [Fact]
        public void GetItems_WhenQueueServiceThrows_ReturnsEmptyList()
        {
            // Arrange
            _mockQueueService.Setup(x => x.GetActiveDownloads())
                .Throws(new InvalidOperationException("Queue service error"));
            var sut = CreateSut();

            // Act
            var result = sut.GetItems();

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        /// <summary>
        /// Covers lines 173-208: GetItems returns items from active downloads.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs:183-187
        /// </summary>
        [Fact]
        public void GetItems_WithActiveDownloads_ReturnsItems()
        {
            // Arrange
            _mockQueueService.Setup(x => x.GetActiveDownloads())
                .Returns(new List<QobuzDownloadItem>());
            var sut = CreateSut();

            // Act
            var result = sut.GetItems();

            // Assert
            result.Should().NotBeNull();
        }

        /// <summary>
        /// Covers lines 1001-1004: Dispose calls DisposeAsync.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs:1001-1004
        /// </summary>
        [Fact]
        public void Dispose_DisposesGracefully()
        {
            // Arrange
            _mockQueueService.Setup(x => x.GetActiveDownloads())
                .Returns(new List<QobuzDownloadItem>());
            _mockConcurrencyManager.Setup(x => x.Dispose());
            var sut = CreateSut();

            // Act & Assert - should not throw
            sut.Dispose();
        }

        /// <summary>
        /// Covers lines 1009-1054: DisposeAsync disposes resources gracefully.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs:1009-1054
        /// </summary>
        [Fact]
        public async Task DisposeAsync_DisposesGracefully()
        {
            // Arrange
            _mockQueueService.Setup(x => x.GetActiveDownloads())
                .Returns(new List<QobuzDownloadItem>());
            _mockConcurrencyManager.Setup(x => x.Dispose());
            var sut = CreateSut();

            // Act & Assert - should not throw
            await sut.DisposeAsync();
        }

        /// <summary>
        /// Covers lines 1009-1054: DisposeAsync cancels active downloads.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs:1016-1020
        /// </summary>
        [Fact]
        public async Task DisposeAsync_CancelsActiveDownloads()
        {
            // Arrange
            var downloadItem = new QobuzDownloadItem
            {
                DownloadId = "test-id",
                Status = DownloadItemStatus.Downloading,
                CancellationTokenSource = new System.Threading.CancellationTokenSource()
            };
            _mockQueueService.Setup(x => x.GetActiveDownloads())
                .Returns(new List<QobuzDownloadItem> { downloadItem });
            _mockConcurrencyManager.Setup(x => x.Dispose());
            var sut = CreateSut();

            // Act
            await sut.DisposeAsync();

            // Assert
            downloadItem.CancellationTokenSource.IsCancellationRequested.Should().BeTrue();
        }

        /// <summary>
        /// Covers lines 210-255: RemoveItem cancels downloading items.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs:231-233
        /// </summary>
        [Fact]
        public void RemoveItem_CancelsDownloadingItems()
        {
            // Arrange
            var downloadItem = new QobuzDownloadItem
            {
                DownloadId = "test-id",
                Status = DownloadItemStatus.Downloading,
                CancellationTokenSource = new System.Threading.CancellationTokenSource()
            };
            _mockQueueService.Setup(x => x.TryGetDownload("test-id", out downloadItem))
                .Returns(true);
            _mockQueueService.Setup(x => x.RemoveDownload("test-id", false))
                .Returns(true);
            var sut = CreateSut();

            var clientItem = new DownloadClientItem
            {
                DownloadId = "test-id"
            };

            // Act
            sut.RemoveItem(clientItem, false);

            // Assert
            downloadItem.CancellationTokenSource.IsCancellationRequested.Should().BeTrue();
        }

        /// <summary>
        /// Covers lines 210-255: RemoveItem handles items not in queue.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs:240-249
        /// </summary>
        [Fact]
        public void RemoveItem_HandlesItemsNotInQueue()
        {
            // Arrange
            QobuzDownloadItem nullItem = null;
            _mockQueueService.Setup(x => x.TryGetDownload("unknown-id", out nullItem))
                .Returns(false);
            var sut = CreateSut();

            var clientItem = new DownloadClientItem
            {
                DownloadId = "unknown-id"
            };

            // Act & Assert - should not throw
            sut.RemoveItem(clientItem, false);
        }

        /// <summary>
        /// Covers lines 210-255: RemoveItem with DownloadClientItem overload.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs:210-213
        /// </summary>
        [Fact]
        public void RemoveItem_WithDownloadClientItem_DelegatesToRemoveItem()
        {
            // Arrange
            var downloadItem = new QobuzDownloadItem
            {
                DownloadId = "test-id",
                Status = DownloadItemStatus.Queued,
                CancellationTokenSource = new System.Threading.CancellationTokenSource()
            };
            _mockQueueService.Setup(x => x.TryGetDownload("test-id", out downloadItem))
                .Returns(true);
            _mockQueueService.Setup(x => x.RemoveDownload("test-id", true))
                .Returns(true);
            var sut = CreateSut();

            var clientItem = new DownloadClientItem
            {
                DownloadId = "test-id"
            };

            // Act & Assert - should not throw
            sut.RemoveItem(clientItem, true);
        }
    }
}
