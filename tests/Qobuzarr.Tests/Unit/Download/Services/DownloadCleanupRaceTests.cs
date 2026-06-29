using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Core.Download;
using Xunit;
using Lidarr.Plugin.Common.HostBridge;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Qobuzarr.Tests.Fixtures;

namespace Qobuzarr.Tests.Unit.Download.Services
{
    /// <summary>
    /// Regression tests for the download-cleanup race that caused an infinite re-grab loop on
    /// "The Pretty Reckless – Dear God" (and any multi-track album where Lidarr re-grabs quickly).
    ///
    /// Root cause: DownloadQueueService.RemoveDownload(deleteData:true) fired a fire-and-forget
    /// Task.Run with only a 100ms delay before deleting the album output directory. When Lidarr
    /// re-grabbed immediately after a failure, the new attempt's in-flight .partial files lived in
    /// the SAME directory. The cleanup fired 100ms later and deleted the whole tree, causing
    /// File.Move(partialPath, filePath) → FileNotFoundException for every in-flight track →
    /// AlbumDownloadException → Lidarr re-grabbed again → infinite loop.
    ///
    /// Fix: await the item's DownloadTask before cleanup (same-attempt guard), and skip cleanup
    /// entirely when a new download is already active at the same output path (cross-attempt guard).
    /// </summary>
    public class DownloadCleanupRaceTests : TestFixtureBase
    {
        // ──────────────────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────────────────

        private DownloadQueueService BuildService(IDownloadFileService fileService)
            => new DownloadQueueService(fileService, MockLogger.Object);

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

        // ──────────────────────────────────────────────────────────────────────────────────
        // TEST 1 — same-attempt guard
        // ──────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// When RemoveDownload is called with deleteData=true while the item's DownloadTask is
        /// still running (tracks are in-flight), cleanup MUST NOT fire until the task completes.
        ///
        /// Failure mode (current code): the fire-and-forget fires after only 100ms; the task
        /// can still be running at that point, so cleanup deletes in-flight .partial files.
        /// </summary>
        [Fact]
        public async Task RemoveDownload_WithInFlightTask_WaitsForTaskBeforeCleanup()
        {
            // Arrange
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleanupFired = new SemaphoreSlim(0, 1);

            var mockFileService = new Mock<IDownloadFileService>();
            mockFileService
                .Setup(x => x.CleanupFailedDownloadAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback(() => cleanupFired.Release())
                .Returns(Task.CompletedTask);

            var sut = BuildService(mockFileService.Object);

            var item = BuildItem("id-1", "/downloads/qobuz/Artist/Album", tcs.Task);
            sut.AddDownload(item);

            // Act — remove with deleteData while the DownloadTask is still running
            sut.RemoveDownload("id-1", deleteData: true);

            // Assert — cleanup must NOT fire before the task completes
            // (give the current fire-and-forget time to run its 100ms delay + some buffer)
            bool firedBeforeTaskCompleted = await cleanupFired.WaitAsync(TimeSpan.FromMilliseconds(600));
            firedBeforeTaskCompleted.Should().BeFalse(
                "cleanup must not delete the album directory while the download task is still in-flight; " +
                "doing so causes File.Move(partialPath, filePath) → FileNotFoundException cascades " +
                "for all concurrently-downloading tracks");

            // Now complete the task — cleanup should follow
            tcs.SetResult();

            bool firedAfterTaskCompleted = await cleanupFired.WaitAsync(TimeSpan.FromSeconds(5));
            firedAfterTaskCompleted.Should().BeTrue(
                "cleanup should run once the in-flight download task has completed");
        }

        // ──────────────────────────────────────────────────────────────────────────────────
        // TEST 2 — cross-attempt (re-grab) guard
        // ──────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// When Lidarr re-grabs an album immediately after a failure, a NEW QobuzDownloadItem
        /// is queued with the SAME output path as the failed attempt. The cleanup for the OLD
        /// attempt must be skipped (not delete the directory) so the new attempt's in-flight
        /// .partial files are not nuked.
        ///
        /// Failure mode (current code): the cleanup fires 100ms after removal of the old item;
        /// by then the new item may already be downloading into the same path. Cleanup deletes
        /// the directory → FileNotFoundException cascade → new attempt fails → loop forever.
        /// </summary>
        [Fact]
        public async Task RemoveDownload_WhenNewDownloadActiveAtSamePath_SkipsCleanup()
        {
            // Arrange
            const string sharedOutputPath = "/downloads/qobuz/The Pretty Reckless/Dear God";

            var cleanupFired = new SemaphoreSlim(0, 1);
            var mockFileService = new Mock<IDownloadFileService>();
            mockFileService
                .Setup(x => x.CleanupFailedDownloadAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback(() => cleanupFired.Release())
                .Returns(Task.CompletedTask);

            var sut = BuildService(mockFileService.Object);

            // Attempt A already completed (task done) — this is the failed attempt
            var attemptATask = Task.CompletedTask;
            var attemptA = BuildItem("attempt-A", sharedOutputPath, attemptATask);
            sut.AddDownload(attemptA);

            // Attempt B is the re-grab — already in-flight at the SAME output path
            var attemptB = BuildItem("attempt-B", sharedOutputPath, new TaskCompletionSource().Task);
            sut.AddDownload(attemptB);

            // Act — remove attempt A (the failed one), deleteData:true
            sut.RemoveDownload("attempt-A", deleteData: true);

            // Assert — cleanup must be SKIPPED because attempt B is actively downloading at the same path
            bool firedWhileNewDownloadActive = await cleanupFired.WaitAsync(TimeSpan.FromSeconds(3));
            firedWhileNewDownloadActive.Should().BeFalse(
                "cleanup of a failed attempt must not delete the output directory when a new " +
                "re-grabbed download is already writing into the same path; this would nuke the new " +
                "attempt's in-flight .partial files and trigger the infinite re-grab loop");
        }

        // ──────────────────────────────────────────────────────────────────────────────────
        // TEST 3 — cleanup proceeds normally when no active download at same path
        // ──────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Sanity check: when no other active download is at the same path AND the removed item's
        /// task is already completed, cleanup runs normally. This verifies the guard doesn't
        /// over-suppress legitimate cleanups.
        /// </summary>
        [Fact]
        public async Task RemoveDownload_WhenNoActiveDownloadAtSamePath_RunsCleanupNormally()
        {
            // Arrange
            const string outputPath = "/downloads/qobuz/Artist/OtherAlbum";
            var cleanupFired = new SemaphoreSlim(0, 1);

            var mockFileService = new Mock<IDownloadFileService>();
            mockFileService
                .Setup(x => x.CleanupFailedDownloadAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback(() => cleanupFired.Release())
                .Returns(Task.CompletedTask);

            var sut = BuildService(mockFileService.Object);

            // Item with an already-completed task (failed/finished download)
            var item = BuildItem("id-clean", outputPath, Task.CompletedTask);
            sut.AddDownload(item);

            // Act
            sut.RemoveDownload("id-clean", deleteData: true);

            // Assert — cleanup should proceed normally within a reasonable time
            bool firedNormally = await cleanupFired.WaitAsync(TimeSpan.FromSeconds(5));
            firedNormally.Should().BeTrue(
                "when no active download is competing for the path and the download task is " +
                "already done, cleanup should proceed normally");
        }

        // ──────────────────────────────────────────────────────────────────────────────────
        // TEST 4 — cascade guard: a single track failure must not destroy sibling tracks
        // ──────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The FileNotFoundException cascade log shows that when track 12 fails, track "Dark Days"
        /// also fails with FileNotFoundException — caused by the cleanup deleting both tracks'
        /// .partial files. After the fix: track 12 fails for its own reason (not FileNotFound),
        /// but sibling tracks must NOT see FileNotFoundException from an external cleanup.
        ///
        /// This test verifies the cascade: DownloadQueueService MUST NOT allow cleanup to run
        /// while sibling tracks are still in-flight (tracked via the same DownloadTask that
        /// covers all tracks via Task.WhenAll in TrackDownloadService.DownloadAlbumAsync).
        /// </summary>
        [Fact]
        public async Task RemoveDownload_WhileSiblingTracksRunning_DoesNotCallCleanup()
        {
            // Arrange — simulate a Task.WhenAll covering N tracks; one track fails but the
            // overall task (Task.WhenAll) is still awaiting the other tracks.
            var allTracksSettledTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleanupFired = new SemaphoreSlim(0, 1);

            var mockFileService = new Mock<IDownloadFileService>();
            mockFileService
                .Setup(x => x.CleanupFailedDownloadAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Callback(() => cleanupFired.Release())
                .Returns(Task.CompletedTask);

            var sut = BuildService(mockFileService.Object);

            // The download task represents the whole Task.WhenAll — still running
            var item = BuildItem("id-cascade", "/downloads/qobuz/Pretty Reckless/Dear God",
                allTracksSettledTcs.Task);
            sut.AddDownload(item);

            // Act — simulate Lidarr calling RemoveItem while tracks are still running
            sut.RemoveDownload("id-cascade", deleteData: true);

            // Assert — cleanup must NOT fire while the task is in-flight
            bool firedEarly = await cleanupFired.WaitAsync(TimeSpan.FromMilliseconds(700));
            firedEarly.Should().BeFalse(
                "RemoveDownload must not allow cleanup while the album download task (Task.WhenAll " +
                "over all concurrent tracks) is still in progress; this is what causes the " +
                "FileNotFoundException cascade observed in the Devil In Disguise / Dark Days log");

            // Simulate tracks finishing
            allTracksSettledTcs.SetResult();
            bool firedAfterSettle = await cleanupFired.WaitAsync(TimeSpan.FromSeconds(5));
            firedAfterSettle.Should().BeTrue(
                "cleanup should run once all tracks have finished");
        }

        // ──────────────────────────────────────────────────────────────────────────────────
        // TEST 5 — regression: DownloadPolicy incomplete ⇒ Failed contract preserved
        // ──────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Guard against regressing the "incomplete ⇒ Failed" CLAUDE.md contract.
        /// A partial album must still be reported Failed so Lidarr can blocklist + re-search.
        /// The fix to the cleanup race must not accidentally suppress Failed reporting.
        /// </summary>
        [Fact]
        public void DownloadPolicy_PartialAlbum_RemainsReportedAsFailed()
        {
            // Arrange — 14 tracks (The Pretty Reckless – Dear God), one fails (e.g. track 12)
            var policy = new Lidarr.Plugin.Qobuzarr.Download.DownloadPolicy();

            // 13 tracks downloaded successfully, 1 failed
            policy.IsAlbumDownloadSuccessful(totalTracks: 14, successfulTracks: 13, skippedTracks: 0)
                .Should().BeFalse(
                    "any incomplete album must be reported Failed so Lidarr blocklists and retries; " +
                    "Lidarr's NoMissingOrUnmatchedTracksSpecification permanently rejects partial imports");

            // Even with 0 failures — a full success must still report true (sanity check)
            policy.IsAlbumDownloadSuccessful(totalTracks: 14, successfulTracks: 14, skippedTracks: 0)
                .Should().BeTrue("a complete album should succeed");
        }
    }
}
