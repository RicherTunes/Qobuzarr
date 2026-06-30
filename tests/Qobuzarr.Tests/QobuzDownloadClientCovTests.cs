using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using FluentAssertions;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Qobuzarr.Tests.TestData;
using Qobuzarr.Tests.Fixtures;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Testable subclass that provides mock settings without relying on base Definition.
    /// Also overrides Tracker with a per-instance store for test isolation.
    /// </summary>
    public class TestableQobuzDownloadClient : QobuzDownloadClient
    {
        private readonly QobuzDownloadSettings _settings;
        private readonly Lidarr.Plugin.Common.HostBridge.HostBridgeDownloadTrackerStore<Lidarr.Plugin.Qobuzarr.Download.Clients.QobuzDownloadItem> _testTracker
            = new Lidarr.Plugin.Common.HostBridge.HostBridgeDownloadTrackerStore<Lidarr.Plugin.Qobuzarr.Download.Clients.QobuzDownloadItem>();

        public TestableQobuzDownloadClient(
            IQobuzAuthenticationService authService,
            IQobuzApiClient apiClient,
            IHttpClient httpClient,
            IDownloadFileService fileService,
            IConcurrencyManager concurrencyManager,
            IDownloadSummary downloadSummary,
            IBatchProcessor batchProcessor,
            Lidarr.Plugin.Qobuzarr.Download.Services.ITrackDownloadService trackDownloadService,
            IConfigService configService,
            IDiskProvider diskProvider,
            IRemotePathMappingService remotePathMappingService,
            ILocalizationService localizationService,
            Logger logger,
            QobuzDownloadSettings settings = null)
            : base(authService, apiClient, httpClient, fileService,
                   concurrencyManager, downloadSummary, batchProcessor,
                   trackDownloadService, configService, diskProvider, remotePathMappingService,
                   localizationService, logger)
        {
            _settings = settings ?? new QobuzDownloadSettings();
        }

        /// <summary>
        /// Test seam: await the most-recently-scheduled deferred cleanup task (the
        /// <c>RemoveItem(deleteData:true)</c> fire-and-forget) so cleanup-race assertions are
        /// deterministic rather than wall-clock polled.
        /// </summary>
        public System.Threading.Tasks.Task? PendingCleanupTask => LastCleanupTask;

        protected override QobuzDownloadSettings GetEffectiveSettings() => _settings;

        internal TimeSpan? GracefulShutdownTimeoutOverride { get; set; }

        protected override TimeSpan GracefulShutdownTimeout
            => GracefulShutdownTimeoutOverride ?? base.GracefulShutdownTimeout;

        internal Func<Task>? StabilizeBeforeCleanupDeleteOverride { get; set; }

        protected override Task StabilizeBeforeCleanupDeleteAsync()
            => StabilizeBeforeCleanupDeleteOverride?.Invoke() ?? base.StabilizeBeforeCleanupDeleteAsync();

        internal Func<Lidarr.Plugin.Qobuzarr.Download.Clients.QobuzDownloadItem, Task>? BeforeCleanupDeleteInsideLifecycleGateOverride { get; set; }

        protected override Task BeforeCleanupDeleteInsideLifecycleGateAsync(Lidarr.Plugin.Qobuzarr.Download.Clients.QobuzDownloadItem removed)
            => BeforeCleanupDeleteInsideLifecycleGateOverride?.Invoke(removed) ?? base.BeforeCleanupDeleteInsideLifecycleGateAsync(removed);

        protected override Lidarr.Plugin.Common.HostBridge.HostBridgeDownloadTrackerStore<Lidarr.Plugin.Qobuzarr.Download.Clients.QobuzDownloadItem> Tracker
            => _testTracker;

        /// <summary>
        /// Test seam: seed the per-instance Tracker so <c>GetItems()</c>'s
        /// <c>Tracker.GetSnapshot()</c> source returns the given item. This keeps host-contract
        /// tests on the same tracker-backed path production uses.
        /// </summary>
        public void SeedTracker(Lidarr.Plugin.Qobuzarr.Download.Clients.QobuzDownloadItem item)
            => _testTracker.AddOrReplace(item);

        /// <summary>
        /// Test seam: resolve a tracked item by id (Wave C — the tracker is the single source of
        /// truth, replacing the old queue-service capture used to await an item's DownloadTask).
        /// </summary>
        public Lidarr.Plugin.Qobuzarr.Download.Clients.QobuzDownloadItem GetTrackedItem(string downloadId)
            => _testTracker.TryGet(downloadId, out var item) ? item : null;

        public void SetLastQueuedItem(Lidarr.Plugin.Qobuzarr.Download.Clients.QobuzDownloadItem? item)
        {
            var field = typeof(QobuzDownloadClient).GetField(
                "_lastQueuedItem",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.Should().NotBeNull();
            field!.SetValue(this, item);
        }
    }

    /// <summary>
    /// Test subclass that lets the re-authentication credential seam be controlled
    /// from the test. Mirrors how the indexer builds fallback credentials from settings,
    /// but lets us assert the download-path re-auth behavior hermetically (download
    /// settings do not themselves carry credentials).
    /// </summary>
    public class ReauthTestableQobuzDownloadClient : TestableQobuzDownloadClient
    {
        private readonly Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzCredentials _credsFromSettings;

        /// <summary>
        /// Overrides the wall clock used by the download-path re-auth failure cooldown so tests can
        /// drive the cooldown window deterministically. Null = use the real clock (existing tests).
        /// </summary>
        public DateTime? ClockOverrideUtc { get; set; }

        protected override DateTime UtcNow => ClockOverrideUtc ?? base.UtcNow;

        public ReauthTestableQobuzDownloadClient(
            IQobuzAuthenticationService authService,
            IQobuzApiClient apiClient,
            IHttpClient httpClient,
            IDownloadFileService fileService,
            IConcurrencyManager concurrencyManager,
            IDownloadSummary downloadSummary,
            IBatchProcessor batchProcessor,
            Lidarr.Plugin.Qobuzarr.Download.Services.ITrackDownloadService trackDownloadService,
            IConfigService configService,
            IDiskProvider diskProvider,
            IRemotePathMappingService remotePathMappingService,
            ILocalizationService localizationService,
            Logger logger,
            Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzCredentials credsFromSettings = null)
            : base(authService, apiClient, httpClient, fileService,
                   concurrencyManager, downloadSummary, batchProcessor,
                   trackDownloadService, configService, diskProvider, remotePathMappingService,
                   localizationService, logger)
        {
            _credsFromSettings = credsFromSettings;
        }

        protected override Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzCredentials BuildReauthCredentialsFromSettings()
            => _credsFromSettings;
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
        private readonly Mock<IDownloadFileService> _mockFileService;
        private readonly Mock<IConcurrencyManager> _mockConcurrencyManager;
        private readonly Mock<IDownloadSummary> _mockDownloadSummary;
        private readonly Mock<IBatchProcessor> _mockBatchProcessor;
        private readonly Mock<Lidarr.Plugin.Qobuzarr.Download.Services.ITrackDownloadService> _mockTrackDownloadService;

        public QobuzDownloadClientCovTests()
        {
            _mockAuthService = new Mock<IQobuzAuthenticationService>();
            _mockApiClient = new Mock<IQobuzApiClient>();
            _mockClient = new Mock<IHttpClient>();
            _mockFileService = new Mock<IDownloadFileService>();
            _mockConcurrencyManager = new Mock<IConcurrencyManager>();
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
                _mockFileService.Object,
                _mockConcurrencyManager.Object,
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
        /// Constructor throws ArgumentNullException for null authService.
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
                    _mockFileService.Object,
                    _mockConcurrencyManager.Object,
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
        /// Constructor throws ArgumentNullException for null apiClient.
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
                    _mockFileService.Object,
                    _mockConcurrencyManager.Object,
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
        /// Constructor throws ArgumentNullException for null httpClient.
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
                    _mockFileService.Object,
                    _mockConcurrencyManager.Object,
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
        /// Constructor throws ArgumentNullException for null fileService.
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
                    null,
                    _mockConcurrencyManager.Object,
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
        /// Constructor throws ArgumentNullException for null concurrencyManager.
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
                    _mockFileService.Object,
                    null,
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
        /// Constructor throws ArgumentNullException for null downloadSummary.
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
                    _mockFileService.Object,
                    _mockConcurrencyManager.Object,
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
        /// Constructor throws ArgumentNullException for null batchProcessor.
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
                    _mockFileService.Object,
                    _mockConcurrencyManager.Object,
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
        /// Constructor throws ArgumentNullException for null trackDownloadService.
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
                    _mockFileService.Object,
                    _mockConcurrencyManager.Object,
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
        /// GetItems returns an empty (non-null) list when the tracker has no items.
        /// </summary>
        [Fact]
        public void GetItems_WithNoTrackedDownloads_ReturnsEmptyList()
        {
            // Arrange
            var sut = CreateSut();

            // Act
            var result = sut.GetItems();

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        /// <summary>
        /// GetItems returns items seeded into the process-wide tracker (single source of truth).
        /// </summary>
        [Fact]
        public void GetItems_WithTrackedDownloads_ReturnsItems()
        {
            // Arrange
            var sut = CreateSut();
            sut.SeedTracker(new QobuzDownloadItem { DownloadId = "x", Artist = "A", Title = "B" });

            // Act
            var result = sut.GetItems().ToList();

            // Assert
            result.Should().NotBeNull();
            result.Should().ContainSingle(r => r.DownloadId == "x");
        }

        /// <summary>
        /// Regression contract: every <see cref="DownloadClientItem"/> returned by
        /// <c>GetItems()</c> MUST carry the registered download-client id/name in its
        /// <c>DownloadClientInfo</c>, NOT a hardcoded 0.
        ///
        /// Root cause: GetItems called <c>item.ToDownloadClientItem(0, Name)</c>, so Lidarr's
        /// CompletedDownloadService → DownloadClientProvider.Get(0) did
        /// <c>.Single(d =&gt; d.Definition.Id == 0)</c> and threw
        /// "Sequence contains no matching element", wedging every completed Qobuz download.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs GetItems().
        /// </summary>
        [Fact]
        public void GetItems_ReturnsItemsCarryingRegisteredClientId_NotZero()
        {
            // Arrange: one in-flight item flowing through the tracker (single source of truth).
            var sut = CreateSut();
            sut.SeedTracker(new QobuzDownloadItem
            {
                DownloadId = "dl-1",
                Artist = "Daft Punk",
                Title = "Random Access Memories"
            });
            // Mirror how Lidarr registers a download client: a non-zero Definition.Id/Name.
            sut.Definition = new DownloadClientDefinition { Id = 42, Name = "Qobuzarr" };

            // Act
            var result = sut.GetItems().ToList();

            // Assert
            result.Should().NotBeEmpty();
            result.First().DownloadClientInfo.Should().NotBeNull();
            result.First().DownloadClientInfo.Id.Should().Be(42,
                "Lidarr resolves the owning client via DownloadClientProvider.Get(DownloadClientInfo.Id); a hardcoded 0 wedges completed downloads");
            result.First().DownloadClientInfo.Name.Should().Be("Qobuzarr");
        }

        /// <summary>
        /// Regression contract: <c>GetItems()</c> MUST NOT report the same download twice.
        /// Emitting two <see cref="DownloadClientItem"/>s with the same <c>DownloadId</c>
        /// gives Lidarr two queue entries for one download,
        /// which wedges CompletedDownloadService at <c>importPending</c> — the completed
        /// download never imports.
        ///
        /// Found 2026-06-26 driving real downloads on the live instance: Muse — The Wow! Signal
        /// and Deep Purple — Guilt Trippin' each appeared twice in the queue with the same
        /// downloadId and never imported (trackFileCount stayed 0) despite a clean manual-import
        /// preview. Source: src/Download/Clients/QobuzDownloadClient.cs GetItems() dedup-by-id.
        /// </summary>
        [Fact]
        public void GetItems_ReportsTrackedDownloadIdExactlyOnce()
        {
            // Wave C: the process-wide tracker is the single source of truth (the bespoke queue
            // service was removed), so a downloadId can no longer appear twice across two sources.
            // GetItems still dedups defensively; one tracked item must surface as exactly one entry.
            // Duplicate downloadIds wedge Lidarr's CompletedDownloadService at importPending.
            const string sharedId = "dup-1";
            var sut = CreateSut();
            sut.Definition = new DownloadClientDefinition { Id = 7, Name = "Qobuzarr" };

            sut.SeedTracker(new QobuzDownloadItem
            {
                DownloadId = sharedId,
                Artist = "Muse",
                Title = "The Wow! Signal"
            });

            // Act
            var result = sut.GetItems().ToList();

            // Assert: exactly one entry for the downloadId — never two.
            result.Count(r => r.DownloadId == sharedId).Should().Be(1,
                "a tracked download must be reported once; duplicate downloadIds wedge Lidarr's CompletedDownloadService at importPending");
        }

        /// <summary>
        /// Pins the converter contract directly: <see cref="QobuzDownloadItem.ToDownloadClientItem"/>
        /// must propagate the supplied download-client id into <c>DownloadClientInfo.Id</c>.
        /// Source: src/Download/Clients/QobuzDownloadItem.cs ToDownloadClientItem().
        /// </summary>
        [Fact]
        public void ToDownloadClientItem_PropagatesSuppliedClientId()
        {
            // Arrange
            var item = new QobuzDownloadItem { DownloadId = "dl-2", Artist = "A", Title = "B" };

            // Act
            var dto = item.ToDownloadClientItem(7, "X");

            // Assert
            dto.DownloadClientInfo.Id.Should().Be(7);
            dto.DownloadClientInfo.Name.Should().Be("X");
        }

        /// <summary>
        /// Covers lines 1001-1004: Dispose calls DisposeAsync.
        /// Source: src/Download/Clients/QobuzDownloadClient.cs:1001-1004
        /// </summary>
        [Fact]
        public void Dispose_DisposesGracefully()
        {
            // Arrange
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
            // Arrange — the in-flight item lives in the process-wide tracker (single source).
            var downloadItem = new QobuzDownloadItem
            {
                DownloadId = "test-id",
                CancellationTokenSource = new System.Threading.CancellationTokenSource()
            };
            downloadItem.SetHostStatus(DownloadItemStatus.Downloading);
            _mockConcurrencyManager.Setup(x => x.Dispose());
            var sut = CreateSut();
            sut.SeedTracker(downloadItem);

            // Act
            await sut.DisposeAsync();

            // Assert
            downloadItem.CancellationTokenSource.IsCancellationRequested.Should().BeTrue();
        }

        [Fact]
        public async Task DisposeAsync_DoesNotCancelRetainedCompletedDownloads()
        {
            // Arrange: Common retains terminal items briefly so Lidarr can import them. Graceful
            // shutdown must only cancel active work, not mutate a retained completed item to Failed.
            using var cts = new System.Threading.CancellationTokenSource();
            var downloadItem = new QobuzDownloadItem
            {
                DownloadId = "completed-id",
                CancellationTokenSource = cts
            };
            downloadItem.SetHostStatus(DownloadItemStatus.Completed);
            _mockConcurrencyManager.Setup(x => x.Dispose());
            var sut = CreateSut();
            sut.SeedTracker(downloadItem);

            // Act
            await sut.DisposeAsync();

            // Assert
            downloadItem.GetHostStatus().Should().Be(DownloadItemStatus.Completed);
            cts.IsCancellationRequested.Should().BeFalse(
                "retained completed tracker entries are not active downloads");
        }

        [Fact]
        public async Task DisposeAsync_StopsWaitingAfterGracefulShutdownTimeout()
        {
            // Arrange: a non-cooperative active download task can outlive cancellation. Dispose must
            // stop waiting at the graceful-shutdown budget instead of hanging Lidarr shutdown.
            var neverCompletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cts = new System.Threading.CancellationTokenSource();
            var downloadItem = new QobuzDownloadItem
            {
                DownloadId = "non-cooperative-id",
                CancellationTokenSource = cts,
                DownloadTask = neverCompletes.Task
            };
            downloadItem.SetHostStatus(DownloadItemStatus.Downloading);
            _mockConcurrencyManager.Setup(x => x.Dispose());
            var sut = CreateSut();
            sut.GracefulShutdownTimeoutOverride = TimeSpan.FromMilliseconds(50);
            sut.SeedTracker(downloadItem);

            // Act
            var disposeTask = sut.DisposeAsync().AsTask();
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(2));

            // Assert
            disposeTask.IsCompletedSuccessfully.Should().BeTrue();
            cts.IsCancellationRequested.Should().BeTrue();
        }

        /// <summary>
        /// RemoveItem cancels a downloading item's CancellationTokenSource (the cancel source of
        /// truth that PerformDownloadAsync observes). Wave C: the item is resolved from the tracker.
        /// </summary>
        [Fact]
        public void RemoveItem_CancelsDownloadingItems()
        {
            // Arrange
            var downloadItem = new QobuzDownloadItem
            {
                DownloadId = "test-id",
                CancellationTokenSource = new System.Threading.CancellationTokenSource()
            };
            downloadItem.SetHostStatus(DownloadItemStatus.Downloading);
            var sut = CreateSut();
            sut.SeedTracker(downloadItem);

            var clientItem = new DownloadClientItem
            {
                DownloadId = "test-id"
            };

            // Act
            sut.RemoveItem(clientItem, false);

            // Assert
            downloadItem.CancellationTokenSource.IsCancellationRequested.Should().BeTrue();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RemoveItem_CancelsQueuedItems(bool deleteData)
        {
            // Arrange: Common inserts the item into the tracker while its status is still Queued.
            // A user remove during that window must cancel the same CTS that the background work
            // will observe once it starts.
            using var cts = new System.Threading.CancellationTokenSource();
            var downloadItem = new QobuzDownloadItem
            {
                DownloadId = "queued-id",
                CancellationTokenSource = cts,
                DownloadTask = Task.CompletedTask
            };
            downloadItem.SetHostStatus(DownloadItemStatus.Queued);
            var sut = CreateSut();
            sut.SeedTracker(downloadItem);

            // Act
            sut.RemoveItem(new DownloadClientItem { DownloadId = "queued-id" }, deleteData);
            if (sut.PendingCleanupTask != null)
            {
                await sut.PendingCleanupTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            // Assert
            cts.IsCancellationRequested.Should().BeTrue(
                "queued downloads are active work and must observe RemoveItem cancellation");
            sut.GetTrackedItem("queued-id").Should().BeNull();
        }

        [Fact]
        public void GetItems_DoesNotReturnLastQueuedItemAfterTrackerRetentionEvictsIt()
        {
            // Arrange: Common evicts old terminal items as a side-effect of GetSnapshot().
            // The legacy _lastQueuedItem fallback must not resurrect an evicted terminal item.
            var sut = CreateSut();
            var evicted = new QobuzDownloadItem
            {
                DownloadId = "evicted-id",
                Artist = "Artist",
                Title = "Old Album",
                CompletedAt = DateTime.UtcNow.AddHours(-2)
            };
            evicted.SetHostStatus(DownloadItemStatus.Completed);
            sut.SeedTracker(evicted);
            sut.SetLastQueuedItem(evicted);

            // Act
            var items = sut.GetItems().ToList();

            // Assert
            items.Should().BeEmpty("the tracker retention sweep is authoritative once it evicts a terminal item");
            sut.GetTrackedItem("evicted-id").Should().BeNull();
        }

        /// <summary>
        /// RemoveItem handles items that are not tracked (no-op, no throw).
        /// </summary>
        [Fact]
        public void RemoveItem_HandlesItemsNotTracked()
        {
            // Arrange
            var sut = CreateSut();

            var clientItem = new DownloadClientItem
            {
                DownloadId = "unknown-id"
            };

            // Act & Assert - should not throw
            sut.RemoveItem(clientItem, false);
        }

        /// <summary>
        /// RemoveItem with deleteData:true (DownloadClientItem overload) completes its deferred
        /// cleanup without throwing.
        /// </summary>
        [Fact]
        public async Task RemoveItem_WithDownloadClientItem_DelegatesToRemoveItem()
        {
            // Arrange
            var downloadItem = new QobuzDownloadItem
            {
                DownloadId = "test-id",
                DownloadTask = Task.CompletedTask,
                CancellationTokenSource = new System.Threading.CancellationTokenSource()
            };
            // Status defaults to Queued
            var sut = CreateSut();
            sut.SeedTracker(downloadItem);

            var clientItem = new DownloadClientItem
            {
                DownloadId = "test-id"
            };

            // Act & Assert - should not throw; await the deferred cleanup for determinism.
            sut.RemoveItem(clientItem, true);
            if (sut.PendingCleanupTask != null)
            {
                await sut.PendingCleanupTask;
            }
        }
    }

    /// <summary>
    /// Pins the download-path re-authentication contract (FIX 1):
    /// <see cref="QobuzDownloadClient.EnsureAuthenticatedAsync"/> must self-heal a stale session
    /// (null OR <c>NeedsRefresh()</c>) by re-authenticating with available credentials — exactly
    /// like the indexer's <c>QobuzPreRequestHandler</c> — instead of throwing. The previous code
    /// threw <c>InvalidOperationException("No valid authentication session available")</c> for any
    /// session within 30 minutes of its synthetic 24h expiry, so every album grabbed in that window
    /// failed and never recovered on the download path.
    /// </summary>
    public class QobuzDownloadClientReauthTests : TestFixtureBase
    {
        private readonly Mock<IQobuzAuthenticationService> _mockAuthService = new();
        private readonly Mock<IQobuzApiClient> _mockApiClient = new();
        private readonly Mock<IHttpClient> _mockClient = new();
        private readonly Mock<IDownloadFileService> _mockFileService = new();
        private readonly Mock<IConcurrencyManager> _mockConcurrencyManager = new();
        private readonly Mock<IDownloadSummary> _mockDownloadSummary = new();
        private readonly Mock<IBatchProcessor> _mockBatchProcessor = new();
        private readonly Mock<Lidarr.Plugin.Qobuzarr.Download.Services.ITrackDownloadService> _mockTrackDownloadService = new();

        private ReauthTestableQobuzDownloadClient CreateSut(
            Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzCredentials credsFromSettings = null)
        {
            return new ReauthTestableQobuzDownloadClient(
                _mockAuthService.Object,
                _mockApiClient.Object,
                _mockClient.Object,
                _mockFileService.Object,
                _mockConcurrencyManager.Object,
                _mockDownloadSummary.Object,
                _mockBatchProcessor.Object,
                _mockTrackDownloadService.Object,
                MockConfigService.Object,
                MockDiskProvider.Object,
                MockRemotePathMappingService.Object,
                MockLocalizationService.Object,
                MockLogger.Object,
                credsFromSettings);
        }

        private static Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzSession SessionExpiringIn(TimeSpan ttl)
            => new Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzSession
            {
                UserId = "user-1",
                AuthToken = "tok-stale",
                AppId = "app-1",
                AppSecret = "secret-1",
                CreatedAt = DateTime.UtcNow.AddHours(-24).Add(ttl),
                ExpiresAt = DateTime.UtcNow.Add(ttl)
            };

        private static Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzSession FreshSession()
            => new Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzSession
            {
                UserId = "user-1",
                AuthToken = "tok-fresh",
                AppId = "app-1",
                AppSecret = "secret-1",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            };

        private static Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzCredentials ValidCreds()
            => new Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzCredentials
            {
                UserId = "user-1",
                AuthToken = "tok-stale"
            };

        [Fact]
        public async Task EnsureAuthenticatedAsync_StaleSession_NeedsRefresh_ReAuthenticatesAndProceeds()
        {
            // Arrange: cached session within the 30-minute NeedsRefresh window (synthetic 24h expiry).
            var stale = SessionExpiringIn(TimeSpan.FromMinutes(20));
            stale.NeedsRefresh().Should().BeTrue("the test fixture must reproduce the bug condition");

            var fresh = FreshSession();
            // Read #1 (initial) and #2 (recheck-under-gate, nothing renewed concurrently) see the
            // stale session; after AuthenticateAsync stores a new session, subsequent reads see fresh.
            _mockAuthService.SetupSequence(a => a.GetCachedSession())
                .Returns(stale)
                .Returns(stale)
                .Returns(fresh)
                .Returns(fresh);
            _mockAuthService.Setup(a => a.AuthenticateAsync(It.IsAny<Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzCredentials>()))
                .ReturnsAsync(fresh);

            var sut = CreateSut(ValidCreds());

            // Act + Assert: must NOT throw, must re-auth, must push the refreshed session.
            await sut.EnsureAuthenticatedAsync();

            _mockAuthService.Verify(a => a.AuthenticateAsync(It.IsAny<Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzCredentials>()), Times.Once);
            _mockApiClient.Verify(c => c.SetSession(fresh), Times.Once);
        }

        [Fact]
        public async Task EnsureAuthenticatedAsync_NullSession_WithCreds_ReAuthenticates()
        {
            var fresh = FreshSession();
            // Reads #1 and #2 (initial + recheck-under-gate) see null; after re-auth, fresh.
            _mockAuthService.SetupSequence(a => a.GetCachedSession())
                .Returns((Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzSession)null)
                .Returns((Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzSession)null)
                .Returns(fresh)
                .Returns(fresh);
            _mockAuthService.Setup(a => a.AuthenticateAsync(It.IsAny<Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzCredentials>()))
                .ReturnsAsync(fresh);

            var sut = CreateSut(ValidCreds());

            await sut.EnsureAuthenticatedAsync();

            _mockAuthService.Verify(a => a.AuthenticateAsync(It.IsAny<Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzCredentials>()), Times.Once);
            _mockApiClient.Verify(c => c.SetSession(fresh), Times.Once);
        }

        [Fact]
        public async Task EnsureAuthenticatedAsync_NullSession_NoCreds_ThrowsActionableError()
        {
            // No cached session at all, and no credentials anywhere to re-auth with.
            _mockAuthService.Setup(a => a.GetCachedSession())
                .Returns((Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzSession)null);

            var sut = CreateSut(credsFromSettings: null);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.EnsureAuthenticatedAsync());
            ex.Message.Should().Contain("authentication", "the error must be actionable about credentials");
            _mockAuthService.Verify(a => a.AuthenticateAsync(It.IsAny<Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzCredentials>()), Times.Never);
        }

        [Fact]
        public async Task EnsureAuthenticatedAsync_ValidSession_DoesNotReAuthenticate()
        {
            var fresh = FreshSession();
            _mockAuthService.Setup(a => a.GetCachedSession()).Returns(fresh);

            var sut = CreateSut(credsFromSettings: null);

            await sut.EnsureAuthenticatedAsync();

            // A valid (not NeedsRefresh) session must never trigger a re-login.
            _mockAuthService.Verify(a => a.AuthenticateAsync(It.IsAny<Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzCredentials>()), Times.Never);
            _mockApiClient.Verify(c => c.SetSession(fresh), Times.Once);
        }

        [Fact]
        public async Task EnsureAuthenticatedAsync_StaleSession_NoSettingsCreds_RebuildsTokenCredsFromSession()
        {
            // Download settings carry no credentials, but the stale session itself holds
            // UserId+AuthToken+AppId+AppSecret — enough to rebuild token-auth credentials and re-auth.
            var stale = SessionExpiringIn(TimeSpan.FromMinutes(10));
            var fresh = FreshSession();
            _mockAuthService.SetupSequence(a => a.GetCachedSession())
                .Returns(stale)
                .Returns(stale)
                .Returns(fresh)
                .Returns(fresh);
            _mockAuthService.Setup(a => a.AuthenticateAsync(It.IsAny<Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzCredentials>()))
                .ReturnsAsync(fresh);

            var sut = CreateSut(credsFromSettings: null); // no settings creds -> must fall back to the session

            await sut.EnsureAuthenticatedAsync();

            _mockAuthService.Verify(a => a.AuthenticateAsync(It.IsAny<Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzCredentials>()), Times.Once);
            _mockApiClient.Verify(c => c.SetSession(fresh), Times.Once);
        }

        [Fact]
        public async Task Download_WhenSessionMissing_UsesSourceIndexerSettingsForReauth()
        {
            var fresh = FreshSession();
            QobuzCredentials capturedCredentials = null;

            _mockAuthService.SetupSequence(a => a.GetCachedSession())
                .Returns((QobuzSession)null)
                .Returns((QobuzSession)null)
                .Returns(fresh)
                .Returns(fresh);
            _mockAuthService
                .Setup(a => a.AuthenticateAsync(It.IsAny<QobuzCredentials>()))
                .Callback<QobuzCredentials>(c => capturedCredentials = c)
                .ReturnsAsync(fresh);

            var album = JsonConvert.DeserializeObject<QobuzAlbum>(SampleQobuzResponses.SampleAlbumResponse);
            _mockApiClient
                .Setup(c => c.GetAsync<QobuzAlbum>("/album/get", It.IsAny<Dictionary<string, string>>()))
                .ReturnsAsync(album);
            _mockTrackDownloadService
                .Setup(s => s.DownloadAlbumAsync(
                    It.IsAny<QobuzDownloadItem>(),
                    It.IsAny<QobuzAlbum>(),
                    It.IsAny<QobuzDownloadSettings>(),
                    It.IsAny<System.Threading.CancellationToken>()))
                .Returns(Task.CompletedTask);

            var indexerSettings = new QobuzIndexerSettings
            {
                AuthMethod = (int)AuthenticationMethod.Email,
                Email = "listener@example.com",
                Password = "plain-password",
                AppId = "123456789",
                AppSecret = "app-secret"
            };
            var indexer = new Mock<IIndexer>();
            indexer.SetupGet(i => i.Definition)
                .Returns(new IndexerDefinition { Settings = indexerSettings });

            var sut = CreateSut(credsFromSettings: null);

            var downloadId = await sut.Download(CreateRemoteAlbum(), indexer.Object);
            var queuedItem = sut.GetTrackedItem(downloadId);
            if (queuedItem?.DownloadTask != null)
            {
                await queuedItem.DownloadTask;
            }

            downloadId.Should().NotBeNullOrWhiteSpace();
            capturedCredentials.Should().NotBeNull("download re-auth must use the source indexer's saved credentials");
            capturedCredentials.Email.Should().Be("listener@example.com");
            capturedCredentials.MD5Password.Should().Be(Lidarr.Plugin.Qobuzarr.Utilities.HashingUtility.ComputePasswordMD5Hash("plain-password"));
            capturedCredentials.AppId.Should().Be("123456789");
            capturedCredentials.AppSecret.Should().Be("app-secret");
            _mockApiClient.Verify(c => c.SetSession(fresh), Times.Once);
        }

        [Fact]
        public async Task Download_WhenCachedSessionBelongsToDifferentSourceCredentials_ReauthenticatesWithSourceIndexer()
        {
            var cachedForOtherIndexer = new QobuzSession
            {
                UserId = "11111111",
                AuthToken = "token-account-a",
                AppId = "123456789",
                AppSecret = "abcdefghijklmnopqrstuvwxyz",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            };
            var freshForSourceIndexer = new QobuzSession
            {
                UserId = "22222222",
                AuthToken = "token-account-b",
                AppId = "123456789",
                AppSecret = "abcdefghijklmnopqrstuvwxyz",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            };
            QobuzCredentials capturedCredentials = null;

            _mockAuthService.SetupSequence(a => a.GetCachedSession())
                .Returns(cachedForOtherIndexer)
                .Returns(cachedForOtherIndexer)
                .Returns(freshForSourceIndexer)
                .Returns(freshForSourceIndexer);
            _mockAuthService
                .Setup(a => a.AuthenticateAsync(It.IsAny<QobuzCredentials>()))
                .Callback<QobuzCredentials>(c => capturedCredentials = c)
                .ReturnsAsync(freshForSourceIndexer);

            var album = JsonConvert.DeserializeObject<QobuzAlbum>(SampleQobuzResponses.SampleAlbumResponse);
            _mockApiClient
                .Setup(c => c.GetAsync<QobuzAlbum>("/album/get", It.IsAny<Dictionary<string, string>>()))
                .ReturnsAsync(album);
            _mockTrackDownloadService
                .Setup(s => s.DownloadAlbumAsync(
                    It.IsAny<QobuzDownloadItem>(),
                    It.IsAny<QobuzAlbum>(),
                    It.IsAny<QobuzDownloadSettings>(),
                    It.IsAny<System.Threading.CancellationToken>()))
                .Returns(Task.CompletedTask);

            var indexerSettings = new QobuzIndexerSettings
            {
                AuthMethod = (int)AuthenticationMethod.Token,
                Email = "stale@example.com",
                Password = "stale-password",
                UserId = "22222222",
                AuthToken = "token-account-b",
                AppId = "123456789",
                AppSecret = "abcdefghijklmnopqrstuvwxyz"
            };
            var indexer = new Mock<IIndexer>();
            indexer.SetupGet(i => i.Definition)
                .Returns(new IndexerDefinition { Settings = indexerSettings });

            var sut = CreateSut(credsFromSettings: null);

            var downloadId = await sut.Download(CreateRemoteAlbum(), indexer.Object);
            var queuedItem = sut.GetTrackedItem(downloadId);
            if (queuedItem?.DownloadTask != null)
            {
                await queuedItem.DownloadTask;
            }

            downloadId.Should().NotBeNullOrWhiteSpace();
            capturedCredentials.Should().NotBeNull("a fresh session for another source indexer must not be reused");
            capturedCredentials.UserId.Should().Be("22222222");
            capturedCredentials.AuthToken.Should().Be("token-account-b");
            capturedCredentials.Email.Should().BeNull("token auth was selected even though stale email fields remain");
            _mockApiClient.Verify(c => c.SetSession(freshForSourceIndexer), Times.Once);
            _mockApiClient.Verify(c => c.SetSession(cachedForOtherIndexer), Times.Never);
        }

        // ----------------------------------------------------------------------------- //
        // Negative-result re-auth cooldown (FIX): on a persistent login failure with a deep
        // download queue, single-flight prevents SIMULTANEOUS re-logins but not REPEATED ones
        // across queued items. Each item independently entered EnsureAuthenticatedAsync, rechecked
        // (still stale) and called AuthenticateAsync again — up to N serialized full re-logins
        // (email auth re-scrapes the web player each time), piling on login-rate-limit / ban
        // pressure. After a failed attempt, subsequent items within a short cooldown window must
        // fail fast with the SAME actionable error instead of re-attempting.
        // ----------------------------------------------------------------------------- //

        private static Lidarr.Plugin.Qobuzarr.Exceptions.QobuzApiException AuthFailure()
            => new Lidarr.Plugin.Qobuzarr.Exceptions.QobuzApiException(
                "HTTP 401: invalid credentials", "/user/login", System.Net.HttpStatusCode.Unauthorized);

        [Fact]
        public async Task EnsureAuthenticatedAsync_PersistentAuthFailure_ReAuthenticatesAtMostOncePerCooldownWindow()
        {
            // Arrange: a stale session that always needs refresh; AuthenticateAsync always fails
            // (bad password / 429) so re-auth never succeeds.
            var stale = SessionExpiringIn(TimeSpan.FromMinutes(10));
            stale.NeedsRefresh().Should().BeTrue();
            _mockAuthService.Setup(a => a.GetCachedSession()).Returns(stale);
            _mockAuthService.Setup(a => a.AuthenticateAsync(It.IsAny<QobuzCredentials>()))
                .ThrowsAsync(AuthFailure());

            var sut = CreateSut(ValidCreds());
            sut.ClockOverrideUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            // Act: simulate a deep queue — N items each call EnsureAuthenticatedAsync sequentially.
            const int n = 8;
            for (var i = 0; i < n; i++)
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.EnsureAuthenticatedAsync());
                ex.Message.Should().Contain("authentication",
                    "every queued item must still surface the existing actionable error");
            }

            // Assert: ONE real re-login for the whole cooldown window, NOT N.
            _mockAuthService.Verify(a => a.AuthenticateAsync(It.IsAny<QobuzCredentials>()), Times.Once);
        }

        [Fact]
        public async Task EnsureAuthenticatedAsync_AfterCooldownExpires_RetriesReauth_AndSucceeds()
        {
            // Arrange: session stays stale until a successful AuthenticateAsync flips it fresh.
            var stale = SessionExpiringIn(TimeSpan.FromMinutes(10));
            var fresh = FreshSession();
            var reauthSucceeded = false;
            var allowSuccess = false;

            _mockAuthService.Setup(a => a.GetCachedSession())
                .Returns(() => reauthSucceeded ? fresh : stale);
            _mockAuthService.Setup(a => a.AuthenticateAsync(It.IsAny<QobuzCredentials>()))
                .Returns(() =>
                {
                    if (!allowSuccess)
                    {
                        throw AuthFailure();
                    }
                    reauthSucceeded = true;
                    return Task.FromResult(fresh);
                });

            var sut = CreateSut(ValidCreds());
            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            sut.ClockOverrideUtc = t0;

            // First attempt fails and arms the cooldown.
            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.EnsureAuthenticatedAsync());
            // Second attempt within the window is blocked — no new re-login.
            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.EnsureAuthenticatedAsync());
            _mockAuthService.Verify(a => a.AuthenticateAsync(It.IsAny<QobuzCredentials>()), Times.Once);

            // Window expires and the user has re-entered valid credentials.
            sut.ClockOverrideUtc = t0.Add(TimeSpan.FromSeconds(61));
            allowSuccess = true;

            await sut.EnsureAuthenticatedAsync(); // must NOT be blocked by the now-stale cooldown

            _mockAuthService.Verify(a => a.AuthenticateAsync(It.IsAny<QobuzCredentials>()), Times.Exactly(2));
            _mockApiClient.Verify(c => c.SetSession(fresh), Times.Once);
        }

        [Fact]
        public async Task EnsureAuthenticatedAsync_PreCancelledToken_ThrowsOperationCanceled_WithoutReauthenticating()
        {
            // A hung re-login must not serialize-stall all downloads: a cancelled token short-circuits
            // the re-auth gate wait promptly instead of queueing behind the in-flight re-login.
            var stale = SessionExpiringIn(TimeSpan.FromMinutes(10));
            _mockAuthService.Setup(a => a.GetCachedSession()).Returns(stale);
            _mockAuthService.Setup(a => a.AuthenticateAsync(It.IsAny<QobuzCredentials>()))
                .ReturnsAsync(FreshSession());

            var sut = CreateSut(ValidCreds());
            using var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => sut.EnsureAuthenticatedAsync(null, cts.Token));

            _mockAuthService.Verify(a => a.GetCachedSession(), Times.Never);
            _mockAuthService.Verify(a => a.AuthenticateAsync(It.IsAny<QobuzCredentials>()), Times.Never);
            _mockApiClient.Verify(c => c.SetSession(It.IsAny<QobuzSession>()), Times.Never);
        }

        private static RemoteAlbum CreateRemoteAlbum()
            => new RemoteAlbum
            {
                Artist = new Artist { Name = "Daft Punk", Id = 1 },
                Albums = new List<Album>
                {
                    new Album
                    {
                        Title = "Random Access Memories",
                        Id = 1,
                        ReleaseDate = new DateTime(2013, 5, 17)
                    }
                },
                Release = new ReleaseInfo
                {
                    Title = "Daft Punk - Random Access Memories",
                    DownloadUrl = "qobuz://album/0060254788359",
                    Guid = "qobuz-0060254788359",
                    Size = 500000000
                }
            };
    }
}
