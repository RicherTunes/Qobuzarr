using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using Xunit;
using Lidarr.Plugin.Common.HostBridge;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Qobuzarr.Tests.Fixtures;

namespace Qobuzarr.Tests.Unit.Download.Services
{
    /// <summary>
    /// Regression tests for the download-cleanup race that caused immediate re-grabs to delete
    /// another in-flight attempt's .partial files.
    ///
    /// Wave C removed Qobuz's bespoke DownloadQueueService, so these tests now drive the real
    /// QobuzDownloadClient.RemoveItem -> HostBridgeDownloadTrackerStore.Remove path.
    /// </summary>
    public class DownloadCleanupRaceTests : TestFixtureBase
    {
        private sealed class FixedOutputPathFileService : IDownloadFileService
        {
            private readonly IDownloadFileService _inner;

            public FixedOutputPathFileService(IDownloadFileService inner, string outputPath)
            {
                _inner = inner;
                OutputPath = outputPath;
            }

            public string OutputPath { get; }

            public string BuildOutputPath(RemoteAlbum remoteAlbum, QobuzDownloadSettings settings)
                => OutputPath;

            public void EnsureOutputDirectory(string path)
                => Directory.CreateDirectory(path);

            public Task CleanupFailedDownloadAsync(string path, string downloadRoot)
                => _inner.CleanupFailedDownloadAsync(path, downloadRoot);

            public bool ValidateDownloadPath(string path) => true;

            public long? GetAvailableDiskSpace(string path) => long.MaxValue;

            public string CreateUniqueDownloadDirectory(string basePath, string albumName)
                => Path.Combine(basePath, albumName);
        }

        private Qobuzarr.Tests.TestableQobuzDownloadClient BuildClient(IDownloadFileService? fileService = null)
            => new Qobuzarr.Tests.TestableQobuzDownloadClient(
                new Mock<IQobuzAuthenticationService>().Object,
                new Mock<IQobuzApiClient>().Object,
                MockHttpClient.Object,
                fileService ?? new DownloadFileService(
                    MockDiskProvider.Object,
                    MockRemotePathMappingService.Object,
                    MockLogger.Object),
                new Mock<IConcurrencyManager>().Object,
                new Mock<IDownloadSummary>().Object,
                new Mock<IBatchProcessor>().Object,
                new Mock<ITrackDownloadService>().Object,
                MockConfigService.Object,
                MockDiskProvider.Object,
                MockRemotePathMappingService.Object,
                MockLocalizationService.Object,
                MockLogger.Object,
                new QobuzDownloadSettings
                {
                    DownloadPath = Path.Combine(Path.GetTempPath(), "qobuzarr-cleanup-tests")
                });

        private static QobuzDownloadItem BuildItem(string id, string path, Task? downloadTask = null)
        {
            var item = new QobuzDownloadItem
            {
                DownloadId = id,
                OutputPath = path,
                DownloadRoot = Path.GetDirectoryName(path) ?? string.Empty,
                DownloadTask = downloadTask,
            };
            item.SetHostStatus(DownloadItemStatus.Downloading);
            return item;
        }

        private static string CreateAlbumDirectory()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "qobuzarr-cleanup-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "track.partial"), "partial data");
            return path;
        }

        private static void DeleteIfExists(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        private static RemoteAlbum MakeRemoteAlbum()
        {
            return new RemoteAlbum
            {
                Artist = new Artist { Name = "Artist", Id = 1 },
                Albums =
                [
                    new Album
                    {
                        Title = "Album",
                        Id = 1,
                        ArtistMetadata = new ArtistMetadata { Name = "Artist" }
                    }
                ],
                Release = new ReleaseInfo
                {
                    DownloadUrl = "qobuz://album/replacement-album",
                    Guid = "qobuz://album/replacement-album",
                    Title = "Artist - Album"
                }
            };
        }

        [Fact]
        public async Task RemoveItem_WithInFlightTask_WaitsForTaskBeforeCleanup()
        {
            var outputPath = CreateAlbumDirectory();
            try
            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var sut = BuildClient();
                sut.SeedTracker(BuildItem("id-1", outputPath, tcs.Task));

                sut.RemoveItem(new DownloadClientItem { DownloadId = "id-1" }, deleteData: true);

                await Task.Delay(250);
                Directory.Exists(outputPath).Should().BeTrue(
                    "cleanup must not delete the album directory while the download task is still in-flight");
                sut.GetTrackedItem("id-1").Should().NotBeNull(
                    "deleteData cleanup keeps the tracker entry until the deferred cleanup task runs");

                tcs.SetResult();
                await sut.PendingCleanupTask!.WaitAsync(TimeSpan.FromSeconds(5));

                Directory.Exists(outputPath).Should().BeFalse(
                    "cleanup should run once the in-flight download task has completed");
                sut.GetTrackedItem("id-1").Should().BeNull();
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [Fact]
        public async Task RemoveItem_WhenNewDownloadActiveAtSamePath_SkipsCleanup()
        {
            var outputPath = CreateAlbumDirectory();
            try
            {
                var sut = BuildClient();
                sut.SeedTracker(BuildItem("attempt-A", outputPath, Task.CompletedTask));
                sut.SeedTracker(BuildItem("attempt-B", outputPath, new TaskCompletionSource().Task));

                sut.RemoveItem(new DownloadClientItem { DownloadId = "attempt-A" }, deleteData: true);
                await sut.PendingCleanupTask!.WaitAsync(TimeSpan.FromSeconds(5));

                Directory.Exists(outputPath).Should().BeTrue(
                    "cleanup must not delete a directory owned by another active download at the same output path");
                sut.GetTrackedItem("attempt-A").Should().BeNull();
                sut.GetTrackedItem("attempt-B").Should().NotBeNull();
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [Fact]
        public async Task RemoveItem_WhenSamePathDownloadAppearsDuringCleanupStabilization_SkipsCleanup()
        {
            var outputPath = CreateAlbumDirectory();
            try
            {
                var sut = BuildClient();
                sut.SeedTracker(BuildItem("attempt-A", outputPath, Task.CompletedTask));

                var finalGuardReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                sut.StabilizeBeforeCleanupDeleteOverride = () =>
                {
                    sut.SeedTracker(BuildItem("attempt-B", outputPath, new TaskCompletionSource().Task));
                    finalGuardReached.SetResult();
                    return Task.CompletedTask;
                };

                sut.RemoveItem(new DownloadClientItem { DownloadId = "attempt-A" }, deleteData: true);
                await sut.PendingCleanupTask!.WaitAsync(TimeSpan.FromSeconds(5));

                await finalGuardReached.Task.WaitAsync(TimeSpan.FromSeconds(1));
                Directory.Exists(outputPath).Should().BeTrue(
                    "cleanup must re-check the tracker after the stabilization window because a replacement attempt can appear after the first guard");
                sut.GetTrackedItem("attempt-A").Should().BeNull();
                sut.GetTrackedItem("attempt-B").Should().NotBeNull();
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [Fact]
        public async Task RemoveItem_ReplacementDownloadStartedAfterFinalGuard_WaitsForCleanupLifecycleGate()
        {
            var outputPath = CreateAlbumDirectory();
            try
            {
                var innerFileService = new DownloadFileService(
                    MockDiskProvider.Object,
                    MockRemotePathMappingService.Object,
                    MockLogger.Object);
                var fixedPathFileService = new FixedOutputPathFileService(innerFileService, outputPath);
                var sut = BuildClient(fixedPathFileService);
                sut.SeedTracker(BuildItem("attempt-A", outputPath, Task.CompletedTask));
                sut.StabilizeBeforeCleanupDeleteOverride = () => Task.CompletedTask;

                Task<string>? replacementDownload = null;
                sut.BeforeCleanupDeleteInsideLifecycleGateOverride = async _ =>
                {
                    replacementDownload = sut.Download(MakeRemoteAlbum(), Mock.Of<IIndexer>());
                    await Task.Delay(150);
                    replacementDownload.IsCompleted.Should().BeFalse(
                        "a replacement Download() must not add a same-path tracker item after cleanup's final guard and before delete");
                };

                sut.RemoveItem(new DownloadClientItem { DownloadId = "attempt-A" }, deleteData: true);
                await sut.PendingCleanupTask!.WaitAsync(TimeSpan.FromSeconds(5));

                Directory.Exists(outputPath).Should().BeFalse(
                    "cleanup owns the path lifecycle until the contained delete finishes");
                replacementDownload.Should().NotBeNull();
                var replacementId = await replacementDownload!.WaitAsync(TimeSpan.FromSeconds(5));
                replacementId.Should().NotBeNullOrWhiteSpace();
                var replacementItem = sut.GetTrackedItem(replacementId);
                replacementItem.Should().NotBeNull(
                    "replacement download may enqueue after cleanup releases the lifecycle gate");
                if (replacementItem!.DownloadTask != null)
                {
                    await replacementItem.DownloadTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [Fact]
        public async Task RemoveItem_CleanupLifecycleGate_DoesNotBlockUnrelatedOutputPath()
        {
            var cleanupPath = CreateAlbumDirectory();
            var unrelatedPath = CreateAlbumDirectory();
            try
            {
                var innerFileService = new DownloadFileService(
                    MockDiskProvider.Object,
                    MockRemotePathMappingService.Object,
                    MockLogger.Object);
                var cleanupFileService = new FixedOutputPathFileService(innerFileService, cleanupPath);
                var unrelatedFileService = new FixedOutputPathFileService(innerFileService, unrelatedPath);
                var cleanupClient = BuildClient(cleanupFileService);
                var unrelatedClient = BuildClient(unrelatedFileService);
                cleanupClient.SeedTracker(BuildItem("attempt-A", cleanupPath, Task.CompletedTask));
                cleanupClient.StabilizeBeforeCleanupDeleteOverride = () => Task.CompletedTask;

                Task<string>? unrelatedDownload = null;
                var unrelatedCompletedInsideCleanupGate = false;
                cleanupClient.BeforeCleanupDeleteInsideLifecycleGateOverride = async _ =>
                {
                    unrelatedDownload = unrelatedClient.Download(MakeRemoteAlbum(), Mock.Of<IIndexer>());
                    unrelatedCompletedInsideCleanupGate = await Task.WhenAny(
                        unrelatedDownload,
                        Task.Delay(TimeSpan.FromSeconds(1))) == unrelatedDownload;
                };

                cleanupClient.RemoveItem(new DownloadClientItem { DownloadId = "attempt-A" }, deleteData: true);
                await cleanupClient.PendingCleanupTask!.WaitAsync(TimeSpan.FromSeconds(5));

                unrelatedCompletedInsideCleanupGate.Should().BeTrue(
                    "cleanup for one output path must not serialize enqueue for a different output path");
                Directory.Exists(cleanupPath).Should().BeFalse();
                unrelatedDownload.Should().NotBeNull();
                var unrelatedId = await unrelatedDownload!.WaitAsync(TimeSpan.FromSeconds(5));
                unrelatedId.Should().NotBeNullOrWhiteSpace();
                var unrelatedItem = unrelatedClient.GetTrackedItem(unrelatedId);
                if (unrelatedItem?.DownloadTask != null)
                {
                    await unrelatedItem.DownloadTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            finally
            {
                DeleteIfExists(cleanupPath);
                DeleteIfExists(unrelatedPath);
            }
        }

        [Fact]
        public async Task RemoveItem_WhenNoActiveDownloadAtSamePath_RunsCleanupNormally()
        {
            var outputPath = CreateAlbumDirectory();
            try
            {
                var sut = BuildClient();
                sut.SeedTracker(BuildItem("id-clean", outputPath, Task.CompletedTask));

                sut.RemoveItem(new DownloadClientItem { DownloadId = "id-clean" }, deleteData: true);
                await sut.PendingCleanupTask!.WaitAsync(TimeSpan.FromSeconds(5));

                Directory.Exists(outputPath).Should().BeFalse(
                    "when no active download is competing for the path and the download task is done, cleanup should delete the directory");
                sut.GetTrackedItem("id-clean").Should().BeNull();
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [Fact]
        public async Task RemoveItem_WhileSiblingTracksRunning_DoesNotCallCleanup()
        {
            var outputPath = CreateAlbumDirectory();
            try
            {
                var allTracksSettledTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var sut = BuildClient();
                sut.SeedTracker(BuildItem("id-cascade", outputPath, allTracksSettledTcs.Task));

                sut.RemoveItem(new DownloadClientItem { DownloadId = "id-cascade" }, deleteData: true);

                await Task.Delay(250);
                Directory.Exists(outputPath).Should().BeTrue(
                    "cleanup must not run while the album-wide Task.WhenAll is still waiting on sibling tracks");

                allTracksSettledTcs.SetResult();
                await sut.PendingCleanupTask!.WaitAsync(TimeSpan.FromSeconds(5));

                Directory.Exists(outputPath).Should().BeFalse(
                    "cleanup should run once all sibling tracks have settled");
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [Fact]
        public async Task RemoveItem_OutputPathOutsideDownloadRoot_DoesNotDeleteOutsideDirectory()
        {
            var testRoot = Path.Combine(
                Path.GetTempPath(),
                "qobuzarr-cleanup-tests",
                Guid.NewGuid().ToString("N"));
            var downloadRoot = Path.Combine(testRoot, "download-root");
            var outsidePath = Path.Combine(testRoot, "outside-root");
            Directory.CreateDirectory(downloadRoot);
            Directory.CreateDirectory(outsidePath);
            File.WriteAllText(Path.Combine(outsidePath, "unrelated-file.txt"), "must survive");

            try
            {
                var sut = BuildClient();
                var item = BuildItem("outside-root", outsidePath, Task.CompletedTask);
                item.DownloadRoot = downloadRoot;
                sut.SeedTracker(item);

                sut.RemoveItem(new DownloadClientItem { DownloadId = "outside-root" }, deleteData: true);
                await sut.PendingCleanupTask!.WaitAsync(TimeSpan.FromSeconds(5));

                Directory.Exists(outsidePath).Should().BeTrue(
                    "deleteData cleanup must stay contained to the original download root");
                File.Exists(Path.Combine(outsidePath, "unrelated-file.txt")).Should().BeTrue();
                sut.GetTrackedItem("outside-root").Should().BeNull();
            }
            finally
            {
                DeleteIfExists(testRoot);
            }
        }

        [Fact]
        public async Task RemoveItem_RestoredItemWithoutDownloadRoot_DoesNotDeleteUsingChangedSettingsRoot()
        {
            var outputPath = CreateAlbumDirectory();
            try
            {
                var sut = BuildClient();
                var item = new QobuzDownloadItem
                {
                    DownloadId = "restored-no-root",
                    OutputPath = outputPath,
                    DownloadRoot = null,
                    DownloadTask = Task.CompletedTask,
                    CompletedAt = DateTime.UtcNow
                };
                item.SetHostStatus(DownloadItemStatus.Failed);
                sut.SeedTracker(item);

                sut.RemoveItem(new DownloadClientItem { DownloadId = "restored-no-root" }, deleteData: true);
                await sut.PendingCleanupTask!.WaitAsync(TimeSpan.FromSeconds(5));

                Directory.Exists(outputPath).Should().BeTrue(
                    "a restored tracker item lacks the original root, so cleanup must fail closed instead of re-deriving a possibly changed settings root");
                sut.GetTrackedItem("restored-no-root").Should().BeNull();
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [Fact]
        public void DownloadPolicy_PartialAlbum_RemainsReportedAsFailed()
        {
            var policy = new DownloadPolicy();

            policy.IsAlbumDownloadSuccessful(totalTracks: 14, successfulTracks: 13, skippedTracks: 0)
                .Should().BeFalse(
                    "any incomplete album must be reported Failed so Lidarr blocklists and retries");

            policy.IsAlbumDownloadSuccessful(totalTracks: 14, successfulTracks: 14, skippedTracks: 0)
                .Should().BeTrue("a complete album should succeed");
        }
    }
}
