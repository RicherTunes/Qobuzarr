using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Localization;
using NzbDrone.Core.RemotePathMappings;
using Lidarr.Plugin.Common.HostBridge;
using Lidarr.Plugin.Common.TestKit.Compliance;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Xunit;

namespace Qobuzarr.Tests.Compliance
{
    /// <summary>
    /// Adopts Common's host-contract suite (<see cref="DownloadClientHostContractTestBase"/>) for
    /// Qobuzarr. The four facts it pins (CanMoveFiles+CanBeRemoved on a completed download, a
    /// non-zero client id, Cancelled-is-terminal, and dedup-by-downloadId) are exactly the import
    /// boundary bugs that only ever surfaced live — the per-converter unit tests covering
    /// <see cref="QobuzDownloadItem.ToDownloadClientItem"/> in isolation missed them.
    ///
    /// <para>Every projection here flows through the plugin's REAL boundary code:
    /// <see cref="QobuzDownloadClient.GetItems"/> (which stamps <c>DownloadClientInfo.Id</c> from
    /// <c>Definition.Id</c> and dedups by downloadId) and
    /// <see cref="QobuzDownloadItem.ToDownloadClientItem"/> (which sets CanMoveFiles/CanBeRemoved
    /// and maps the HostBridge status onto the host enum). The host items are then mapped onto the
    /// host-type-free <see cref="HostDownloadItemView"/> so the shared assertions live in Common.</para>
    /// </summary>
    [Trait("Category", "Compliance")]
    public sealed class QobuzDownloadClientHostContractTests : DownloadClientHostContractTestBase
    {
        // Mirror how Lidarr registers a download client: a non-zero Definition.Id.
        // A zero id makes DownloadClientProvider.Get(0) throw and wedges every completed download.
        private const int ClientId = 7;

        protected override HostDownloadItemView Completed()
        {
            var item = new QobuzDownloadItem
            {
                DownloadId = "qobuz-completed",
                Artist = "Daft Punk",
                Title = "Discovery",
                OutputPath = OperatingSystem.IsWindows()
                    ? @"C:\Music\Daft Punk\Discovery"
                    : "/music/daftpunk/discovery",
            };
            item.SetHostStatus(DownloadItemStatus.Completed);
            return ProjectThroughGetItems(item);
        }

        protected override HostDownloadItemView Failed()
        {
            var item = new QobuzDownloadItem
            {
                DownloadId = "qobuz-failed",
                Artist = "Daft Punk",
                Title = "Homework",
            };
            item.SetFailed("track 3 unavailable");
            return ProjectThroughGetItems(item);
        }

        protected override HostDownloadItemView? Cancelled()
        {
            // Qobuz exposes no Cancelled host status. The HostBridge Cancelled state maps through
            // QobuzDownloadItem.GetHostStatus()'s switch default (=> Failed), so a cancelled
            // download surfaces as the terminal Failed status — never Queued/Downloading (which
            // would never resolve and would wedge the download in the queue forever).
            var item = new QobuzDownloadItem
            {
                DownloadId = "qobuz-cancelled",
                Artist = "Daft Punk",
                Title = "Human After All",
            };
            item.SetStatus(HostBridgeDownloadItemStatus.Cancelled);
            return ProjectThroughGetItems(item);
        }

        protected override IReadOnlyList<HostDownloadItemView> DuplicateDownloadId(string downloadId)
        {
            // Wave C: the process-wide tracker is the single source of truth (the bespoke queue
            // service was removed), so a download can no longer appear twice across two sources.
            // GetItems still dedups by downloadId defensively; seeding the tracker once must yield
            // exactly one entry — two entries with the same id wedge Lidarr's
            // CompletedDownloadService at importPending (the completed download never imports).
            var sut = BuildClient();

            sut.SeedTracker(new QobuzDownloadItem
            {
                DownloadId = downloadId,
                Artist = "Muse",
                Title = "The Wow! Signal",
            });

            return sut.GetItems().Select(ToView).ToList();
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Seed a single item into the per-instance tracker and project it through the real
        /// <see cref="QobuzDownloadClient.GetItems"/> path (client-id stamping + status mapping).
        /// </summary>
        private static HostDownloadItemView ProjectThroughGetItems(QobuzDownloadItem seed)
        {
            var sut = BuildClient();
            sut.SeedTracker(seed);
            var dto = sut.GetItems().Single(i => i.DownloadId == seed.DownloadId);
            return ToView(dto);
        }

        private static HostDownloadItemView ToView(DownloadClientItem dto) => new(
            dto.DownloadId,
            dto.DownloadClientInfo?.Id ?? 0,
            dto.Status.ToString(),
            dto.CanMoveFiles,
            dto.CanBeRemoved);

        private static TestableQobuzDownloadClient BuildClient()
        {
            var sut = new TestableQobuzDownloadClient(
                new Mock<IQobuzAuthenticationService>().Object,
                new Mock<IQobuzApiClient>().Object,
                new Mock<IHttpClient>().Object,
                new Mock<IDownloadFileService>().Object,
                new Mock<IConcurrencyManager>().Object,
                new Mock<IDownloadSummary>().Object,
                new Mock<IBatchProcessor>().Object,
                new Mock<ITrackDownloadService>().Object,
                new Mock<IConfigService>().Object,
                new Mock<IDiskProvider>().Object,
                new Mock<IRemotePathMappingService>().Object,
                new Mock<ILocalizationService>().Object,
                LogManager.GetCurrentClassLogger());

            sut.Definition = new DownloadClientDefinition { Id = ClientId, Name = "Qobuzarr" };
            return sut;
        }
    }
}
