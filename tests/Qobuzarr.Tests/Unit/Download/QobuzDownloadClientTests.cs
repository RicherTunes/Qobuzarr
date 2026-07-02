using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NSubstitute;
using Newtonsoft.Json;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Music;
using Xunit;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Constants;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Exceptions;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Services;
using Qobuzarr.Tests.Fixtures;
using Qobuzarr.Tests.TestData;

namespace Qobuzarr.Tests.Unit.Download
{
    // Tests restored and updated for current API
    public class QobuzDownloadClientTests : TestFixtureBase
    {
        // Test-specific DownloadClient that overrides Settings access for testing.
        // Also overrides Tracker to provide a per-test-instance store so the static
        // process-wide store doesn't contaminate test isolation.
        private class TestableQobuzDownloadClient : QobuzDownloadClient
        {
            private QobuzDownloadSettings _testSettings;
            private readonly Lidarr.Plugin.Common.HostBridge.HostBridgeDownloadTrackerStore<QobuzDownloadItem> _testTracker
                = new Lidarr.Plugin.Common.HostBridge.HostBridgeDownloadTrackerStore<QobuzDownloadItem>();

            public TestableQobuzDownloadClient(
                IQobuzAuthenticationService authService,
                IQobuzApiClient apiClient,
                NzbDrone.Common.Http.IHttpClient httpClient,
                IDownloadFileService fileService,
                IConcurrencyManager concurrencyManager,
                ITrackDownloadService trackDownloadService,
                IDownloadSummary downloadSummary,
                IBatchProcessor batchProcessor,
                NzbDrone.Core.Configuration.IConfigService configService,
                NzbDrone.Common.Disk.IDiskProvider diskProvider,
                NzbDrone.Core.RemotePathMappings.IRemotePathMappingService remotePathMappingService,
                NzbDrone.Core.Localization.ILocalizationService localizationService,
                NLog.Logger logger)
                : base(authService, apiClient, httpClient, fileService, concurrencyManager,
                      downloadSummary, batchProcessor, trackDownloadService,
                      configService, diskProvider, remotePathMappingService, localizationService, logger)
            {
                _testSettings = new QobuzDownloadSettings
                {
                    // Must be ABSOLUTE on whatever OS the test runs on. The Common
                    // DownloadPathValidator (Test() pre-check) uses
                    // Path.IsPathFullyQualified, so a Windows-style "C:\..." string is
                    // NOT absolute on the Linux CI host and would fail Test(). Pick an
                    // OS-appropriate absolute path so Test() validates everywhere.
                    DownloadPath = OperatingSystem.IsWindows() ? @"C:\Downloads\Qobuz" : "/downloads/qobuz",
                    PreferredQuality = 6, // FLAC CD Quality
                    CreateAlbumFolders = true,
                    ConcurrencyMode = (int)DownloadConcurrencyMode.Fixed,
                    FixedConcurrencyLevel = 3
                };
            }

            protected new QobuzDownloadSettings Settings => _testSettings;

            // Override GetEffectiveSettings to return test settings
            protected override QobuzDownloadSettings GetEffectiveSettings() => _testSettings;

            // Override Tracker with a fresh per-instance store for test isolation.
            protected override Lidarr.Plugin.Common.HostBridge.HostBridgeDownloadTrackerStore<QobuzDownloadItem> Tracker
                => _testTracker;

            public IRestrictedReleaseSuppressionStore? ReleaseSuppressionStoreOverride { get; set; }

            protected override IRestrictedReleaseSuppressionStore ReleaseSuppressionStore
                => ReleaseSuppressionStoreOverride ?? base.ReleaseSuppressionStore;

            internal Func<QobuzDownloadItem, CancellationToken, Task>? BeforeDownloadWorkerSideEffectsOverride { get; set; }

            protected override Task BeforeDownloadWorkerSideEffectsAsync(QobuzDownloadItem downloadItem, CancellationToken cancellationToken)
                => BeforeDownloadWorkerSideEffectsOverride?.Invoke(downloadItem, cancellationToken) ?? Task.CompletedTask;

            public QobuzDownloadItem? GetTrackedItem(string downloadId)
                => _testTracker.TryGet(downloadId, out var item) ? item : null;

            public Task? PendingCleanupTask => LastCleanupTask;

            public void SeedTracker(QobuzDownloadItem item)
                => _testTracker.AddOrReplace(item);

            public void SetTestSettings(QobuzDownloadSettings settings)
            {
                _testSettings = settings;
            }
        }
        private readonly IQobuzAuthenticationService _mockAuthService;
        private readonly IQobuzApiClient _mockApiClient;
        private readonly IDownloadFileService _mockFileService;
        private readonly IConcurrencyManager _mockConcurrencyManager;
        private readonly IDownloadSummary _mockDownloadSummary;
        private readonly ITrackDownloadService _mockTrackDownloadService;
        private readonly IBatchProcessor _mockBatchProcessor;
        private readonly TestableQobuzDownloadClient _downloadClient;
        private readonly QobuzSession _testSession;

        private sealed class RecordingSuppressionStore : IRestrictedReleaseSuppressionStore
        {
            public List<(string AlbumId, string TrackId, TrackUnavailableReason Reason)> Records { get; } = new();

            public bool IsSuppressed(string albumId) => false;

            public Task SuppressAsync(
                string albumId,
                string trackId,
                TrackUnavailableReason reason,
                CancellationToken cancellationToken = default)
            {
                Records.Add((albumId, trackId, reason));
                return Task.CompletedTask;
            }

            public Task<bool> ClearAsync(string albumId, CancellationToken cancellationToken = default)
                => Task.FromResult(false);
        }

        public QobuzDownloadClientTests()
        {
            _mockAuthService = Substitute.For<IQobuzAuthenticationService>();
            _mockApiClient = Substitute.For<IQobuzApiClient>();
            _mockFileService = Substitute.For<IDownloadFileService>();
            _mockConcurrencyManager = Substitute.For<IConcurrencyManager>();
            _mockDownloadSummary = Substitute.For<IDownloadSummary>();
            _mockTrackDownloadService = Substitute.For<ITrackDownloadService>();
            _mockBatchProcessor = Substitute.For<IBatchProcessor>();

            _downloadClient = new TestableQobuzDownloadClient(
                _mockAuthService,
                _mockApiClient,
                MockHttpClient.Object,
                _mockFileService,
                _mockConcurrencyManager,
                _mockTrackDownloadService,
                _mockDownloadSummary,
                _mockBatchProcessor,
                MockConfigService.Object,
                MockDiskProvider.Object,
                MockRemotePathMappingService.Object,
                MockLocalizationService.Object,
                MockLogger.Object
            );

            _testSession = new QobuzSession
            {
                UserId = "12345678",
                AuthToken = "sample_auth_token_123456",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };

            SetupMockDefaults();
        }

        // Captured download item for RemoveItem tests
        private void SetupMockDefaults()
        {
            _mockAuthService.GetCachedSession().Returns(_testSession);

            MockDiskProvider.Setup(x => x.FolderExists(It.IsAny<string>())).Returns(true);
            MockDiskProvider.Setup(x => x.CreateFolder(It.IsAny<string>())).Verifiable();

            var album = JsonConvert.DeserializeObject<QobuzAlbum>(SampleQobuzResponses.SampleAlbumResponse);
            _mockApiClient.GetAsync<QobuzAlbum>("/album/get", Arg.Any<Dictionary<string, string>>())
                         .Returns(album);

            // Fix: Make track download succeed by default (prevents null Task cascade)
            _mockTrackDownloadService.DownloadAlbumAsync(
                Arg.Any<QobuzDownloadItem>(),
                Arg.Any<QobuzAlbum>(),
                Arg.Any<QobuzDownloadSettings>(),
                Arg.Any<System.Threading.CancellationToken>()
            ).Returns(Task.CompletedTask);

            // Fix: Make Test() pass path validation
            _mockFileService.ValidateDownloadPath(Arg.Any<string>()).Returns(true);
        }

        private async Task<QobuzDownloadItem> AwaitTrackedDownloadAsync(string downloadId)
        {
            var tracked = _downloadClient.GetTrackedItem(downloadId);
            tracked.Should().NotBeNull("Download() must insert the item into the tracker before returning");
            if (tracked!.DownloadTask != null)
            {
                await tracked.DownloadTask;
            }
            return tracked;
        }

        private async Task<QobuzDownloadItem> AwaitTrackedDownloadIgnoringErrorsAsync(string downloadId)
        {
            var tracked = _downloadClient.GetTrackedItem(downloadId);
            tracked.Should().NotBeNull("Download() must insert the item into the tracker before returning");
            if (tracked!.DownloadTask != null)
            {
                try { await tracked.DownloadTask; } catch { /* expected by failure-path tests */ }
            }
            return tracked;
        }

        [Fact]
        public async Task Download_WithValidRemoteAlbum_ShouldReturnDownloadId()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();

            // Act
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());

            // Assert
            downloadId.Should().NotBeNullOrEmpty();
            Guid.TryParse(downloadId.Replace("-", ""), out _).Should().BeTrue();
        }

        [Fact]
        public async Task Download_WithInvalidAlbumId_ShouldThrowException()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();
            // Fix: Invalidate ALL sources so AlbumIdExtractor returns null
            remoteAlbum.Release.DownloadUrl = "invalid://url";
            remoteAlbum.Release.Guid = "";  // Clear the GUID too

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>()));

            exception.Message.Should().Contain("Could not extract album ID");
        }

        [Fact]
        public async Task Download_ShouldCreateDownloadItemWithCorrectProperties()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();

            // Act
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());

            // Wait for download task to complete so TotalSize gets populated
            await AwaitTrackedDownloadAsync(downloadId);

            // Assert
            var items = _downloadClient.GetItems();
            var downloadItem = items.FirstOrDefault(x => x.DownloadId == downloadId);

            downloadItem.Should().NotBeNull();
            // Fix: ToDownloadClientItem formats title as "{Artist} - {Album}"
            var expectedTitle = $"{remoteAlbum.Artist.Name} - {remoteAlbum.Albums.FirstOrDefault()?.Title ?? "Unknown Album"}";
            downloadItem.Title.Should().Be(expectedTitle);
            // After download completes, status should be Completed (not Queued)
            downloadItem.Status.Should().Be(DownloadItemStatus.Completed);
            downloadItem.TotalSize.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task Download_WithQualityFallbacks_ShouldSurfaceInCompletionMessage()
        {
            // Arrange
            _mockTrackDownloadService.DownloadAlbumAsync(
                Arg.Any<QobuzDownloadItem>(),
                Arg.Any<QobuzAlbum>(),
                Arg.Any<QobuzDownloadSettings>(),
                Arg.Any<CancellationToken>()
            ).Returns(callInfo =>
            {
                var item = callInfo.ArgAt<QobuzDownloadItem>(0);
                item.RecordQualityFallback(requestedFormatId: 7, actualFormatId: 6);
                item.RecordQualityFallback(requestedFormatId: 7, actualFormatId: 6);
                return Task.CompletedTask;
            });

            var remoteAlbum = CreateTestRemoteAlbum();

            // Act
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());
            await AwaitTrackedDownloadAsync(downloadId);

            // Assert
            var downloadItem = _downloadClient.GetItems().FirstOrDefault(x => x.DownloadId == downloadId);
            downloadItem.Should().NotBeNull();
            downloadItem.Status.Should().Be(DownloadItemStatus.Completed);
            downloadItem.Message.Should().Contain("quality fallback used for 2 track(s)");
        }

        [Fact]
        public async Task Download_WithPermanentTrackRestriction_RecordsReleaseSuppressionAndStillFails()
        {
            var suppression = new RecordingSuppressionStore();
            _downloadClient.ReleaseSuppressionStoreOverride = suppression;

            var albumException = new AlbumDownloadException(
                "0060254788359",
                "Random Access Memories",
                totalTracks: 20,
                successfulTracks: 19,
                skippedTracks: 0,
                failedTracks: 1,
                trackResults: new[]
                {
                    new TrackDownloadResult
                    {
                        Success = false,
                        TrackId = "restricted-track",
                        Reason = TrackUnavailableReason.Restricted,
                        Message = "Content restricted (TrackRestrictedByPurchaseCredentials)",
                    },
                });

            _mockTrackDownloadService.DownloadAlbumAsync(
                Arg.Any<QobuzDownloadItem>(),
                Arg.Any<QobuzAlbum>(),
                Arg.Any<QobuzDownloadSettings>(),
                Arg.Any<CancellationToken>())
                .Returns(Task.FromException(albumException));

            var downloadId = await _downloadClient.Download(CreateTestRemoteAlbum(), Substitute.For<IIndexer>());

            var tracked = await AwaitTrackedDownloadIgnoringErrorsAsync(downloadId);

            tracked.GetHostStatus().Should().Be(DownloadItemStatus.Failed);
            suppression.Records.Should().ContainSingle(record =>
                record.AlbumId == "0060254788359" &&
                record.TrackId == "restricted-track" &&
                record.Reason == TrackUnavailableReason.Restricted);
        }

        [Fact]
        public async Task Download_WithOnlyUnclassifiedDeficit_DoesNotRecordReleaseSuppression()
        {
            // An unclassified deficit (Reason == null) is symptomatic of a genuine edition mismatch or an
            // unexpected error, not a proven-permanent restriction. Lidarr still needs the chance to
            // blocklist + fall back to another edition/source, so suppression must NOT fire.
            var suppression = new RecordingSuppressionStore();
            _downloadClient.ReleaseSuppressionStoreOverride = suppression;

            var albumException = new AlbumDownloadException(
                "0060254788359",
                "Random Access Memories",
                totalTracks: 20,
                successfulTracks: 18,
                skippedTracks: 0,
                failedTracks: 2,
                trackResults: new[]
                {
                    new TrackDownloadResult { Success = false, TrackId = "unknown-track", Reason = null },
                });

            _mockTrackDownloadService.DownloadAlbumAsync(
                Arg.Any<QobuzDownloadItem>(),
                Arg.Any<QobuzAlbum>(),
                Arg.Any<QobuzDownloadSettings>(),
                Arg.Any<CancellationToken>())
                .Returns(Task.FromException(albumException));

            var downloadId = await _downloadClient.Download(CreateTestRemoteAlbum(), Substitute.For<IIndexer>());
            var tracked = await AwaitTrackedDownloadIgnoringErrorsAsync(downloadId);

            tracked.GetHostStatus().Should().Be(DownloadItemStatus.Failed);
            suppression.Records.Should().BeEmpty();
        }

        [Fact]
        public async Task Download_WithOnlyRegionalRestrictionDeficit_DoesNotRecordReleaseSuppression()
        {
            // Geo-restriction is deliberately excluded from suppression eligibility
            // (TrackUnavailableReasonExtensions.IsPermanentlyUnavailable) — availability can change (VPN,
            // catalog rollout by region), so permanently hiding the release is a worse failure mode than
            // the bounded re-grab it would otherwise cause.
            var suppression = new RecordingSuppressionStore();
            _downloadClient.ReleaseSuppressionStoreOverride = suppression;

            var albumException = new AlbumDownloadException(
                "geo-album-id",
                "Geo Restricted Album",
                totalTracks: 10,
                successfulTracks: 9,
                skippedTracks: 0,
                failedTracks: 1,
                trackResults: new[]
                {
                    new TrackDownloadResult { Success = false, TrackId = "geo-track", Reason = TrackUnavailableReason.RegionalRestriction },
                });

            _mockTrackDownloadService.DownloadAlbumAsync(
                Arg.Any<QobuzDownloadItem>(),
                Arg.Any<QobuzAlbum>(),
                Arg.Any<QobuzDownloadSettings>(),
                Arg.Any<CancellationToken>())
                .Returns(Task.FromException(albumException));

            var downloadId = await _downloadClient.Download(CreateTestRemoteAlbum(), Substitute.For<IIndexer>());
            var tracked = await AwaitTrackedDownloadIgnoringErrorsAsync(downloadId);

            tracked.GetHostStatus().Should().Be(DownloadItemStatus.Failed);
            suppression.Records.Should().BeEmpty();
        }

        [Fact]
        public async Task Download_SuccessfulAlbum_NeverTouchesReleaseSuppressionStore()
        {
            var suppression = new RecordingSuppressionStore();
            _downloadClient.ReleaseSuppressionStoreOverride = suppression;

            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());
            var tracked = await AwaitTrackedDownloadAsync(downloadId);

            tracked.GetHostStatus().Should().Be(DownloadItemStatus.Completed);
            suppression.Records.Should().BeEmpty();
        }

        [Fact]
        public async Task GetItems_WithActiveDownloads_ShouldReturnDownloadItems()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());

            // Act
            var items = _downloadClient.GetItems();

            // Assert
            items.Should().NotBeEmpty();
            items.Should().HaveCount(1);
            items.First().DownloadId.Should().Be(downloadId);
        }

        [Fact]
        public void GetItems_WithNoActiveDownloads_ShouldReturnEmptyList()
        {
            // Act
            var items = _downloadClient.GetItems();

            // Assert
            items.Should().NotBeNull();
            items.Should().BeEmpty();
        }

        [Fact]
        public async Task RemoveItem_WithValidDownloadId_ShouldRemoveFromTracking()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());

            _downloadClient.GetItems().Should().HaveCount(1);
            var downloadItem = _downloadClient.GetItems().First(x => x.DownloadId == downloadId);

            // Act
            _downloadClient.RemoveItem(downloadItem, false);

            // Assert
            _downloadClient.GetItems().Should().BeEmpty();
        }

        [Fact]
        public async Task RemoveItem_WithDeleteData_DefersTrackerRemovalUntilCleanupTaskRuns()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());

            MockDiskProvider.Setup(x => x.FolderExists(It.IsAny<string>())).Returns(true);
            var downloadItem = _downloadClient.GetItems().First(x => x.DownloadId == downloadId);

            // Act
            _downloadClient.RemoveItem(downloadItem, true);

            // Assert
            _downloadClient.GetTrackedItem(downloadId).Should().NotBeNull(
                "deleteData cleanup is deferred until the download task settles");
            if (_downloadClient.PendingCleanupTask != null)
            {
                await _downloadClient.PendingCleanupTask;
            }
            _downloadClient.GetItems().Should().BeEmpty();
        }

        [Fact]
        public void RemoveItem_WithInvalidDownloadItem_ShouldNotThrow()
        {
            // Arrange
            var invalidDownloadItem = new DownloadClientItem
            {
                DownloadId = "invalid-id",
                Title = "Invalid Item"
            };

            // Act & Assert
            _downloadClient.Invoking(x => x.RemoveItem(invalidDownloadItem, false))
                          .Should().NotThrow();
        }

        [Fact]
        public void Test_ShouldReturnValidationResult()
        {
            // Arrange - ensure session doesn't need refresh and has subscription
            _testSession.ExpiresAt = DateTime.UtcNow.AddHours(24);
            _testSession.Subscription = new QobuzSubscription
            {
                Type = "studio",
                IsHiRes = true,
                MaxSampleRate = 192000,
                MaxBitDepth = 24,
                CanStream = true,
                CanDownload = true
            };

            // Act
            var result = _downloadClient.Test();

            // Assert
            result.Should().NotBeNull();

            // Debug: output validation errors if test fails
            if (!result.IsValid)
            {
                var errors = string.Join(", ", result.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"));
                result.IsValid.Should().BeTrue($"Validation failed with errors: {errors}");
            }
            else
            {
                result.IsValid.Should().BeTrue();
            }
        }

        [Fact]
        public void Protocol_ShouldBeCompatibleWithHost()
        {
            // Act
            object proto = _downloadClient.Protocol;

            // Assert: support both host variants
            if (proto is string s)
            {
                s.Should().Be(nameof(QobuzarrDownloadProtocol));
            }
            else
            {
                proto.ToString().Should().Be("Unknown");
            }
        }

        [Fact]
        public void Name_ShouldReturnQobuzarr()
        {
            // Act & Assert
            _downloadClient.Name.Should().Be("Qobuzarr");
        }

        [Fact]
        public async Task PerformDownload_WithValidAlbum_ShouldCompleteSuccessfully()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());

            // Fix: Wait for the actual download task to complete instead of arbitrary delay
            await AwaitTrackedDownloadAsync(downloadId);

            // Act
            var items = _downloadClient.GetItems();
            var downloadItem = items.FirstOrDefault(x => x.DownloadId == downloadId);

            // Assert
            downloadItem.Should().NotBeNull();
            // Fix: After awaiting the task, status should be Completed (not timing-dependent)
            downloadItem.Status.Should().Be(DownloadItemStatus.Completed);
        }

        [Fact]
        public async Task PerformDownload_WithoutAuthentication_ShouldFail()
        {
            // Arrange
            _mockAuthService.GetCachedSession().Returns((QobuzSession)null);

            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());

            // Fix: Wait for the actual download task to complete instead of arbitrary delay
            await AwaitTrackedDownloadIgnoringErrorsAsync(downloadId);

            // Act
            var items = _downloadClient.GetItems();
            var downloadItem = items.FirstOrDefault(x => x.DownloadId == downloadId);

            // Assert
            downloadItem.Should().NotBeNull();
            downloadItem.Status.Should().Be(DownloadItemStatus.Failed);
            downloadItem.Message.Should().Contain("authentication");
        }

        #region Integration/Wiring Tests (kept for coverage, use reflection)

        /// <summary>
        /// Tests that BuildOutputPath produces a valid path structure.
        /// This is a wiring test - it verifies the method delegates correctly to file service.
        /// </summary>
        [Fact]
        public void BuildOutputPath_WithAlbumFolders_ShouldCreateCorrectPath()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();

            // Use reflection to access private method for testing
            var method = typeof(QobuzDownloadClient).GetMethod("BuildOutputPath",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Act
            var outputPath = (string)method.Invoke(_downloadClient, new object[] { remoteAlbum });

            // Assert
            outputPath.Should().NotBeNullOrEmpty();
            outputPath.Should().Contain(remoteAlbum.Artist.Name);
            outputPath.Should().Contain(remoteAlbum.Albums.FirstOrDefault()?.Title ?? "Unknown Album");
        }

        // NOTE: ExtractAlbumIdFromRelease tests moved to AlbumIdExtractorTests.cs
        // The AlbumIdExtractor is now a public static utility class that can be tested directly.

        /// <summary>
        /// Tests that cleanup doesn't remove recent downloads.
        /// This is a wiring test - it verifies recent terminal items still flow through the tracker.
        /// </summary>
        [Fact]
        public async Task GetItems_RetainsRecentCompletedDownloads()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());

            // Simulate old completed download by manipulating internal state
            var items = _downloadClient.GetItems();
            items.Should().HaveCount(1);

            await AwaitTrackedDownloadAsync(downloadId);

            // Assert: Common's tracker retention sweep must not evict recent terminal items.
            _downloadClient.GetItems().Should().HaveCount(1);
        }

        #endregion

        [Fact]
        public async Task Download_WithApiError_ShouldMarkAsFailed()
        {
            // Arrange
            _mockApiClient.GetAsync<QobuzAlbum>("/album/get", Arg.Any<Dictionary<string, string>>())
                         .Returns<QobuzAlbum>(x => throw new InvalidOperationException("API Error"));

            // Fix: Override the default track download to throw the API error
            _mockTrackDownloadService.DownloadAlbumAsync(
                Arg.Any<QobuzDownloadItem>(),
                Arg.Any<QobuzAlbum>(),
                Arg.Any<QobuzDownloadSettings>(),
                Arg.Any<System.Threading.CancellationToken>()
            ).Returns(Task.FromException(new InvalidOperationException("API Error")));

            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());

            // Fix: Wait for download task to complete instead of arbitrary delay
            await AwaitTrackedDownloadIgnoringErrorsAsync(downloadId);

            // Act
            var items = _downloadClient.GetItems();
            var downloadItem = items.FirstOrDefault(x => x.DownloadId == downloadId);

            // Assert
            downloadItem.Should().NotBeNull();
            downloadItem.Status.Should().Be(DownloadItemStatus.Failed);
            downloadItem.Message.Should().Contain("API Error");
        }

        [Fact]
        public async Task Download_WithMultipleAlbums_ShouldTrackAllDownloads()
        {
            // Arrange
            var remoteAlbum1 = CreateTestRemoteAlbum("Album 1");
            var remoteAlbum2 = CreateTestRemoteAlbum("Album 2");

            // Act
            var downloadId1 = await _downloadClient.Download(remoteAlbum1, Substitute.For<IIndexer>());
            var downloadId2 = await _downloadClient.Download(remoteAlbum2, Substitute.For<IIndexer>());

            // Assert
            var items = _downloadClient.GetItems();
            items.Should().HaveCount(2);
            items.Select(x => x.DownloadId).Should().Contain(downloadId1);
            items.Select(x => x.DownloadId).Should().Contain(downloadId2);
        }

        // ───────────────────────────────────────────────────────────────────────────
        // Wave A: HostBridgeDownloadOrchestrator adoption — behavior-contract guards.
        // These pin the live-proven behavior the orchestrator refactor must preserve:
        //   1. the item is visible in GetItems() the instant Download() returns (the
        //      orchestrator inserts into the tracker BEFORE scheduling the work — no race);
        //   2. Download() registers the item with the tracker and sets _lastQueuedItem;
        //   3. RemoveItem() still cancels the in-flight item's own CancellationTokenSource
        //      (the cancel source-of-truth that PerformDownloadAsync observes).
        // ───────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Download_ItemIsVisibleInGetItems_BeforeWorkCompletes_NoPreInsertRace()
        {
            // Arrange: block the track download so the item is genuinely still in-flight when
            // we poll GetItems() right after Download() returns. If the orchestrator inserted
            // into the tracker only AFTER scheduling Task.Run, this poll could miss the item.
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _mockTrackDownloadService.DownloadAlbumAsync(
                Arg.Any<QobuzDownloadItem>(),
                Arg.Any<QobuzAlbum>(),
                Arg.Any<QobuzDownloadSettings>(),
                Arg.Any<CancellationToken>())
                .Returns(async ci =>
                {
                    entered.TrySetResult();
                    var ct = ci.ArgAt<CancellationToken>(3);
                    await Task.Delay(Timeout.Infinite, ct);
                });

            var remoteAlbum = CreateTestRemoteAlbum();

            // Act
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());
            var itemsImmediately = _downloadClient.GetItems().ToList();

            // Assert: visible immediately, keyed by the generated downloadId.
            downloadId.Should().NotBeNullOrEmpty();
            itemsImmediately.Select(i => i.DownloadId).Should().Contain(downloadId);

            // Cleanup: release the blocked background work.
            var tracked = _downloadClient.GetTrackedItem(downloadId);
            tracked?.CancellationTokenSource?.Cancel();
            if (tracked?.DownloadTask != null)
            {
                try { await tracked.DownloadTask; } catch { /* cancelled */ }
            }
        }

        [Fact]
        public async Task Download_RegistersItemWithTracker_AndSetsLastQueuedItem()
        {
            // Arrange
            var remoteAlbum = CreateTestRemoteAlbum();

            // Act
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());
            var tracked = _downloadClient.GetTrackedItem(downloadId);
            if (tracked?.DownloadTask != null)
            {
                try { await tracked.DownloadTask; } catch { /* not relevant here */ }
            }

            // Assert: the item carrying the generated downloadId was registered with the tracker.
            tracked.Should().NotBeNull();
            tracked!.DownloadId.Should().Be(downloadId);

            // Assert: the private _lastQueuedItem sentinel points at the same item.
            var field = typeof(QobuzDownloadClient).GetField(
                "_lastQueuedItem",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.Should().NotBeNull();
            var lastQueuedItem = field!.GetValue(_downloadClient) as QobuzDownloadItem;
            lastQueuedItem.Should().NotBeNull();
            lastQueuedItem!.DownloadId.Should().Be(downloadId);
        }

        [Fact]
        public async Task RemoveItem_CancelsInFlightDownloadCancellationTokenSource()
        {
            // Arrange: block the track download so the item is genuinely in-flight (Downloading)
            // when RemoveItem fires — that is the only state in which cancellation is signalled.
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _mockTrackDownloadService.DownloadAlbumAsync(
                Arg.Any<QobuzDownloadItem>(),
                Arg.Any<QobuzAlbum>(),
                Arg.Any<QobuzDownloadSettings>(),
                Arg.Any<CancellationToken>())
                .Returns(async ci =>
                {
                    entered.TrySetResult();
                    var ct = ci.ArgAt<CancellationToken>(3);
                    await Task.Delay(Timeout.Infinite, ct);
                });

            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());

            // Ensure the background work reached the in-flight state.
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var inFlight = _downloadClient.GetTrackedItem(downloadId);
            inFlight.Should().NotBeNull();
            inFlight!.CancellationTokenSource.Should().NotBeNull(
                "Download() must build the item with its own CancellationTokenSource (the cancel source of truth)");
            inFlight.CancellationTokenSource!.IsCancellationRequested.Should().BeFalse();

            // Act
            _downloadClient.RemoveItem(new DownloadClientItem { DownloadId = downloadId }, false);

            // Assert: the item's own CancellationTokenSource was cancelled, so PerformDownloadAsync
            // (which observes item.CancellationTokenSource.Token) unwinds the in-flight download.
            inFlight.CancellationTokenSource!.IsCancellationRequested.Should().BeTrue();

            // Cleanup: let the cancelled work unwind.
            if (inFlight.DownloadTask != null)
            {
                try { await inFlight.DownloadTask; } catch { /* cancelled */ }
            }
        }

        [Fact]
        public async Task RemoveItem_WhenStillQueued_CancelsBeforeWorkerDoesAuthApiDirectoryOrTrackWork()
        {
            // Arrange: hold the real Download() worker in the queued window after Common has
            // inserted the item but before Qobuz performs auth/API/filesystem side effects.
            var workerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseWorker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _downloadClient.BeforeDownloadWorkerSideEffectsOverride = (_, _) =>
            {
                workerEntered.TrySetResult();
                return releaseWorker.Task;
            };

            var remoteAlbum = CreateTestRemoteAlbum();
            var downloadId = await _downloadClient.Download(remoteAlbum, Substitute.For<IIndexer>());
            await workerEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var queued = _downloadClient.GetTrackedItem(downloadId);
            queued.Should().NotBeNull();
            queued!.GetStatus().Should().Be(Lidarr.Plugin.Common.HostBridge.HostBridgeDownloadItemStatus.Queued);

            // Act: remove while the item is still queued, then let the worker continue.
            _downloadClient.RemoveItem(new DownloadClientItem { DownloadId = downloadId }, deleteData: false);
            releaseWorker.TrySetResult();
            await queued.DownloadTask!.WaitAsync(TimeSpan.FromSeconds(5));

            // Assert: the queued cancel is not just a token flip; the production worker observes
            // it before auth, album lookup, output directory creation, or track download.
            queued.CancellationTokenSource!.IsCancellationRequested.Should().BeTrue();
            _mockAuthService.DidNotReceive().GetCachedSession();
            _ = _mockApiClient.DidNotReceive().GetAsync<QobuzAlbum>(
                Arg.Any<string>(),
                Arg.Any<Dictionary<string, string>>());
            _mockFileService.DidNotReceive().EnsureOutputDirectory(Arg.Any<string>());
            _ = _mockTrackDownloadService.DidNotReceive().DownloadAlbumAsync(
                Arg.Any<QobuzDownloadItem>(),
                Arg.Any<QobuzAlbum>(),
                Arg.Any<QobuzDownloadSettings>(),
                Arg.Any<CancellationToken>());
        }

        private RemoteAlbum CreateTestRemoteAlbum(string albumTitle = "Random Access Memories")
        {
            return new RemoteAlbum
            {
                Artist = new Artist
                {
                    Name = "Daft Punk",
                    Id = 1
                },
                Albums = new List<Album>
                {
                    new Album
                    {
                        Title = albumTitle,
                        Id = 1,
                        ReleaseDate = new DateTime(2013, 5, 17)
                    }
                },
                Release = new ReleaseInfo
                {
                    Title = $"Daft Punk - {albumTitle}",
                    DownloadUrl = "qobuz://album/0060254788359",
                    Guid = "qobuz-0060254788359",
                    Size = 500000000 // 500MB
                }
            };
        }

        public override void Dispose()
        {
            _downloadClient?.Dispose();
            base.Dispose();
        }
    }
}
