using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;
using NLog;
using Xunit;
using Lidarr.Plugin.Common.HostBridge;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Qobuzarr.Tests.Fixtures;

namespace Qobuzarr.Tests.Unit.Download
{
    /// <summary>
    /// Wave 10B: Verifies that QobuzDownloadClient correctly adopts
    /// HostBridgeDownloadTrackerStore via the QobuzDownloadItem subclass (Option A).
    ///
    /// These tests operate directly on the tracker store and the QobuzDownloadItem
    /// subclass — they do NOT require a running Lidarr instance.
    /// </summary>
    public class HostBridgeTrackerAdoptionTests
    {
        [Fact]
        public void StaticTracker_IsPersistentForPluginConfigRoot()
        {
            var field = typeof(QobuzDownloadClient)
                .GetField("_staticTracker", BindingFlags.NonPublic | BindingFlags.Static);

            field.Should().NotBeNull();
            field!.FieldType.Should().Be(typeof(HostBridgeDownloadTrackerStore<QobuzDownloadItem>));

            var tracker = field.GetValue(null);
            var persistencePathField = field.FieldType.GetField("_persistencePath", BindingFlags.NonPublic | BindingFlags.Instance);
            persistencePathField.Should().NotBeNull();
            var persistencePath = persistencePathField!.GetValue(tracker).Should().BeOfType<string>().Subject;
            persistencePath.Replace('\\', '/').Should().EndWith(
                "/Qobuzarr/download-tracker.json",
                "the real plugin tracker must survive Lidarr restarts, not just tests that construct a temporary store");
        }

        // ────────────────────────────────────────────────────────────────────────────
        // 1. Download_AddsItemToBridgeTracker
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Download_AddsItemToBridgeTracker()
        {
            // Arrange: a fresh store (not the static one on QobuzDownloadClient — we test
            // the store API directly to avoid static-state cross-test pollution).
            var store = new HostBridgeDownloadTrackerStore<QobuzDownloadItem>();
            var item = new QobuzDownloadItem
            {
                DownloadId = Guid.NewGuid().ToString("N"),
                Title = "Achtung Baby",
                Artist = "U2",
                OutputPath = @"C:\Music\U2\AchtungBaby"
            };

            // Act
            store.AddOrReplace(item);

            // Assert
            store.TryGet(item.DownloadId, out var retrieved).Should().BeTrue();
            retrieved.Should().BeSameAs(item);
        }

        // ────────────────────────────────────────────────────────────────────────────
        // 2. GetItems_ReadsFromBridgeTracker
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void GetItems_ReadsFromBridgeTracker()
        {
            // Arrange: two items in the store
            var store = new HostBridgeDownloadTrackerStore<QobuzDownloadItem>();
            var id1 = "tracker-get-1";
            var id2 = "tracker-get-2";
            var item1 = new QobuzDownloadItem { DownloadId = id1, Title = "OK Computer", Artist = "Radiohead", OutputPath = "/music/ok" };
            var item2 = new QobuzDownloadItem { DownloadId = id2, Title = "The Bends", Artist = "Radiohead", OutputPath = "/music/bends" };

            store.AddOrReplace(item1);
            store.AddOrReplace(item2);

            // Act
            var snapshot = store.GetSnapshot().ToList();

            // Assert: both items are present (no retention eviction — they're still Queued)
            snapshot.Should().HaveCount(2);
            snapshot.Select(x => x.DownloadId).Should().Contain(id1);
            snapshot.Select(x => x.DownloadId).Should().Contain(id2);
        }

        // ────────────────────────────────────────────────────────────────────────────
        // 3. RemoveItem_DelegatesToBridgeTracker
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void RemoveItem_DelegatesToBridgeTracker()
        {
            // Arrange
            var store = new HostBridgeDownloadTrackerStore<QobuzDownloadItem>();
            var id = "tracker-remove-1";
            var item = new QobuzDownloadItem { DownloadId = id, Title = "In Rainbows", Artist = "Radiohead", OutputPath = "/music/ir" };
            store.AddOrReplace(item);

            // Act
            var removed = store.Remove(id, deleteData: false, out var removedItem);

            // Assert
            removed.Should().BeTrue();
            removedItem.Should().BeSameAs(item);
            store.TryGet(id, out _).Should().BeFalse();
        }

        // ────────────────────────────────────────────────────────────────────────────
        // 4. CompletedDownload_RetentionSweep_Works
        //    (advance CompletedAt, verify 30+ min old completed entries get evicted)
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void CompletedDownload_RetentionSweep_Works()
        {
            // Arrange: use a very short retention window so we can test eviction without sleeping.
            var retention = TimeSpan.FromMilliseconds(1);
            var store = new HostBridgeDownloadTrackerStore<QobuzDownloadItem>(retention);

            var staleId = "stale-completed";
            var liveId  = "live-downloading";

            var staleItem = new QobuzDownloadItem
            {
                DownloadId = staleId,
                Title = "Stale Album",
                Artist = "Old Band",
                OutputPath = "/music/stale"
            };
            staleItem.SetStatus(HostBridgeDownloadItemStatus.Completed);
            // Back-date CompletedAt beyond the retention window.
            staleItem.CompletedAt = DateTime.UtcNow.AddMinutes(-31);

            var liveItem = new QobuzDownloadItem
            {
                DownloadId = liveId,
                Title = "Live Album",
                Artist = "New Band",
                OutputPath = "/music/live"
            };
            // liveItem stays Queued — should never be evicted.

            store.AddOrReplace(staleItem);
            store.AddOrReplace(liveItem);

            // Wait just past the retention window (1ms) so the sweep can fire.
            Thread.Sleep(5);

            // Act: GetSnapshot() runs the retention sweep as a side-effect.
            var snapshot = store.GetSnapshot().ToList();

            // Assert: stale completed item is gone; live queued item remains.
            snapshot.Select(x => x.DownloadId).Should().NotContain(staleId,
                "completed item older than retention window should be evicted");
            snapshot.Select(x => x.DownloadId).Should().Contain(liveId,
                "non-terminal item should never be evicted by the retention sweep");
        }

        // ────────────────────────────────────────────────────────────────────────────
        // 5. Extras_PersistedAcrossLifecycle
        //    (CancellationTokenSource / Album survive a get-after-set)
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Extras_PersistedAcrossLifecycle()
        {
            // Arrange
            var store = new HostBridgeDownloadTrackerStore<QobuzDownloadItem>();
            var id  = "extras-lifecycle";
            var cts = new CancellationTokenSource();

            var item = new QobuzDownloadItem
            {
                DownloadId = id,
                Title = "Dark Side of the Moon",
                Artist = "Pink Floyd",
                OutputPath = "/music/pf",
                CancellationTokenSource = cts
            };
            item.SetProgress(42.5);
            item.SetHostStatus(DownloadItemStatus.Downloading);

            // Act
            store.AddOrReplace(item);
            store.TryGet(id, out var retrieved).Should().BeTrue();

            // Assert — Qobuz-only extras survive the round-trip through the store.
            retrieved!.CancellationTokenSource.Should().BeSameAs(cts,
                "CancellationTokenSource must be preserved for in-flight cancel paths");
            retrieved.GetProgress().Should().BeApproximately(42.5, 0.001,
                "progress written via SetProgress must survive the round-trip");
            retrieved.GetHostStatus().Should().Be(DownloadItemStatus.Downloading);

            // Cancellation must still be reachable and functional.
            cts.Cancel();
            retrieved.CancellationTokenSource!.IsCancellationRequested.Should().BeTrue();
        }

        // ────────────────────────────────────────────────────────────────────────────
        // 6. HostBridgeDownloadItem base fields are provided by the subclass
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void HostBridgeBaseFields_AreProvidedBySubclass()
        {
            // This test documents exactly which fields come from the base vs. the subclass.
            var item = new QobuzDownloadItem
            {
                DownloadId  = "field-check-1",
                AlbumId     = "qobuz-album-99",
                Title       = "Abbey Road",
                Artist      = "The Beatles",
                OutputPath  = "/music/beatles/abbey",
                TotalSize   = 1234567L,
                StartedAt   = new DateTime(2026, 5, 24, 10, 0, 0, DateTimeKind.Utc),
            };

            // Base class fields
            item.DownloadId.Should().Be("field-check-1");
            item.AlbumId.Should().Be("qobuz-album-99");
            item.Title.Should().Be("Abbey Road");
            item.Artist.Should().Be("The Beatles");
            item.OutputPath.Should().Be("/music/beatles/abbey");
            item.TotalSize.Should().Be(1234567L);
            item.StartedAt.Should().Be(new DateTime(2026, 5, 24, 10, 0, 0, DateTimeKind.Utc));

            // Thread-safe status / progress from base class
            item.GetStatus().Should().Be(HostBridgeDownloadItemStatus.Queued);
            item.GetProgress().Should().Be(0);
            item.CompletedAt.Should().BeNull();

            // Qobuz-only extras default to null
            item.CancellationTokenSource.Should().BeNull();
            item.DownloadTask.Should().BeNull();
            item.Album.Should().BeNull();
            item.DownloadedSize.Should().Be(0);
        }

        [Fact]
        public void FromHostBridgeDto_RestoresSubclassWithPersistedBaseFields()
        {
            var startedAt = new DateTime(2026, 6, 29, 18, 0, 0, DateTimeKind.Utc);
            var completedAt = startedAt.AddMinutes(4);
            var dto = new HostBridgeDownloadItemDto
            {
                DownloadId = "qobuz-persisted-id",
                AlbumId = "qobuz-album-42",
                Title = "Persisted Album",
                Artist = "Persisted Artist",
                OutputPath = "/downloads/qobuz/Persisted Artist/Persisted Album",
                StartedAt = startedAt,
                CompletedAt = completedAt,
                TotalSize = 123456789L,
                Status = HostBridgeDownloadItemStatus.Completed,
                Progress = 100,
            };

            var item = QobuzDownloadItem.FromHostBridgeDto(dto);

            item.Should().BeOfType<QobuzDownloadItem>();
            item.DownloadId.Should().Be(dto.DownloadId);
            item.AlbumId.Should().Be(dto.AlbumId);
            item.Title.Should().Be(dto.Title);
            item.Artist.Should().Be(dto.Artist);
            item.OutputPath.Should().Be(dto.OutputPath);
            item.StartedAt.Should().Be(startedAt);
            item.CompletedAt.Should().Be(completedAt);
            item.TotalSize.Should().Be(dto.TotalSize);
            item.GetStatus().Should().Be(HostBridgeDownloadItemStatus.Completed);
            item.GetProgress().Should().Be(100);
            item.DownloadTask.Should().BeNull("task handles cannot be reconstructed after a process restart");
            item.CancellationTokenSource.Should().BeNull("cancellation handles cannot be reconstructed after a process restart");
            item.Album.Should().BeNull("provider DTOs are not part of Common's persisted tracker snapshot");
        }

        // ────────────────────────────────────────────────────────────────────────────
        // 7. SetHostStatus_MirrorsCompletedAt_OnTerminalTransition
        // ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void SetHostStatus_MirrorsCompletedAt_OnTerminalTransition()
        {
            var item = new QobuzDownloadItem { DownloadId = "completed-at-test", Title = "T", Artist = "A", OutputPath = "/" };

            item.CompletedAt.Should().BeNull();

            item.SetHostStatus(DownloadItemStatus.Downloading);
            item.CompletedAt.Should().BeNull("Downloading is not a terminal state");

            var before = DateTime.UtcNow;
            item.SetHostStatus(DownloadItemStatus.Completed);
            var after = DateTime.UtcNow;

            item.CompletedAt.Should().NotBeNull("Completed is a terminal state");
            item.CompletedAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);

            // Calling again must NOT overwrite (retention sweep uses the first CompletedAt).
            var firstCompletedAt = item.CompletedAt;
            item.SetHostStatus(DownloadItemStatus.Completed);
            item.CompletedAt.Should().Be(firstCompletedAt, "CompletedAt must not be reset on a second Completed transition");
        }
    }
}
