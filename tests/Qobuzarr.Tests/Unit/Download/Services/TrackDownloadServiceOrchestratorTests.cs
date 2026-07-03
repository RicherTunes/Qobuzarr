using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Interfaces;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Exceptions;
using Lidarr.Plugin.Qobuzarr.Models;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using Xunit;
using CommonDownloadProgress = Lidarr.Plugin.Common.Interfaces.DownloadProgress;
using CommonDownloadResult = Lidarr.Plugin.Common.Interfaces.DownloadResult;
using CommonTrackDownloadResult = Lidarr.Plugin.Common.Interfaces.TrackDownloadResult;

namespace Qobuzarr.Tests.Unit.Download.Services
{
    /// <summary>
    /// Wave B: pins the Qobuz-specific glue that survives the orchestrator migration —
    /// the album-completion policy (incomplete ⇒ <see cref="AlbumDownloadException"/> ⇒ host reports
    /// Failed), skipped-vs-failed accounting, the stream-resolution / re-auth call path, and
    /// quality-fallback recording.
    /// </summary>
    public sealed class TrackDownloadServiceOrchestratorTests
    {
        private static readonly Logger Log = LogManager.GetLogger("TrackDownloadServiceOrchestratorTests");

        // ── completion policy (the most important contract) ───────────────────────────────────────

        /// <summary>
        /// Test subclass that bypasses the real orchestrator/HTTP and returns a synthetic result, so the
        /// DownloadAlbumAsync completion mapping (policy + AlbumDownloadException) is exercised in isolation.
        /// </summary>
        private sealed class SyntheticTrackDownloadService : TrackDownloadService
        {
            private readonly CommonDownloadResult _result;
            private readonly Action<QobuzTrackClassifier>? _seedClassifier;
            private readonly Action<IProgress<CommonDownloadProgress>>? _reportProgress;

            public SyntheticTrackDownloadService(
                CommonDownloadResult result,
                Action<QobuzTrackClassifier>? seedClassifier = null,
                Action<IProgress<CommonDownloadProgress>>? reportProgress = null,
                IDownloadSummary? downloadSummary = null,
                Logger? logger = null)
                : base(Mock.Of<IQobuzApiClient>(), Mock.Of<IConcurrencyManager>(), downloadSummary ?? Mock.Of<IDownloadSummary>(), logger ?? Log)
            {
                _result = result;
                _seedClassifier = seedClassifier;
                _reportProgress = reportProgress;
            }

            protected override Task<CommonDownloadResult> RunOrchestratorAsync(
                QobuzAlbum album,
                QobuzDownloadSettings settings,
                QobuzDownloadItem downloadItem,
                QobuzTrackClassifier classifier,
                IProgress<CommonDownloadProgress> progress,
                CancellationToken cancellationToken)
            {
                _seedClassifier?.Invoke(classifier);
                _reportProgress?.Invoke(progress);
                return Task.FromResult(_result);
            }
        }

        [Fact]
        public async Task DownloadAlbumAsync_IncompleteAlbum_13of14_ThrowsAlbumDownloadException()
        {
            var album = MakeAlbum(14);
            var result = SyntheticResult(successful: 13, total: 14);
            var sut = new SyntheticTrackDownloadService(result);

            var act = async () => await sut.DownloadAlbumAsync(MakeItem(), album, new QobuzDownloadSettings(), CancellationToken.None);

            var ex = (await act.Should().ThrowAsync<AlbumDownloadException>()).Which;
            ex.TotalTracks.Should().Be(14);
            ex.SuccessfulTracks.Should().Be(13);
            ex.FailedTracks.Should().Be(1);
        }

        [Fact]
        public async Task DownloadAlbumAsync_CompleteAlbum_14of14_DoesNotThrow()
        {
            var album = MakeAlbum(14);
            var result = SyntheticResult(successful: 14, total: 14);
            var sut = new SyntheticTrackDownloadService(result);

            var act = async () => await sut.DownloadAlbumAsync(MakeItem(), album, new QobuzDownloadSettings(), CancellationToken.None);

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task DownloadAlbumAsync_OrchestratorProgress_UpdatesDownloadItemBeforeReturn()
        {
            var album = MakeAlbum(2);
            var item = MakeItem();
            var result = SyntheticResult(successful: 2, total: 2);
            var sut = new SyntheticTrackDownloadService(
                result,
                reportProgress: p => p.Report(new CommonDownloadProgress
                {
                    CompletedTracks = 1,
                    TotalTracks = 2,
                    PercentComplete = 50,
                }));

            var priorContext = SynchronizationContext.Current;
            Task downloadTask;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new QueuingSynchronizationContext());
                downloadTask = sut.DownloadAlbumAsync(item, album, new QobuzDownloadSettings(), CancellationToken.None);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(priorContext);
            }

            await downloadTask;

            item.GetProgress().Should().Be(50,
                "the previous bespoke loop updated progress inline, so callers should not race queued Progress<T> callbacks");
        }

        [Fact]
        public async Task DownloadAlbumAsync_SkippedAndFailedTracks_AreClassifiedSeparatelyInException()
        {
            // 12 success, 2 deficit; one of the deficit was a preview-only skip, the other a hard failure.
            var album = MakeAlbum(14);
            var result = SyntheticResult(successful: 12, total: 14);
            var sut = new SyntheticTrackDownloadService(
                result,
                seedClassifier: c => c.RecordSkipped("skip-track", TrackUnavailableReason.PreviewOnly));

            var act = async () => await sut.DownloadAlbumAsync(MakeItem(), album, new QobuzDownloadSettings(), CancellationToken.None);

            var ex = (await act.Should().ThrowAsync<AlbumDownloadException>()).Which;
            ex.SuccessfulTracks.Should().Be(12);
            ex.SkippedTracks.Should().Be(1, "the preview-only track is accounted as skipped, not failed");
            ex.FailedTracks.Should().Be(1);
        }

        // ── permanently-restricted tracks STILL throw (Option C: completion policy is unchanged) ──
        //
        // Live-confirmed bug: a permanently-restricted track (purchase-only / subscription gate) makes
        // DownloadAlbumAsync throw AlbumDownloadException exactly like any other deficit, and Lidarr's
        // blocklist provably never fires for it on the live instance (55+ failures, 0 blocklist entries) —
        // so depending on blocklist-driven fallback, or changing this method to report Completed instead,
        // would NOT reliably stop the loop. DownloadAlbumAsync's completion decision is therefore
        // deliberately left untouched by this fix; the loop is broken further upstream instead (see
        // QobuzDownloadClient.PerformDownloadAsync's AlbumDownloadException catch, which records the
        // album id in RestrictedReleaseSuppressionStore so the indexer stops offering it — see
        // QobuzParserSuppressionTests). This pins that DownloadAlbumAsync keeps throwing regardless of
        // whether the deficit is permanent, so a future change doesn't accidentally reintroduce the
        // Completed-for-incomplete anti-pattern (the Aphex-Twin regression).

        [Fact]
        public async Task DownloadAlbumAsync_PermanentlyRestrictedDeficit_StillThrowsAlbumDownloadException()
        {
            var album = MakeAlbum(20);
            var result = SyntheticResult(successful: 19, total: 20);
            var sut = new SyntheticTrackDownloadService(
                result,
                seedClassifier: c => c.RecordSkipped("t20", TrackUnavailableReason.Restricted));

            var act = async () => await sut.DownloadAlbumAsync(MakeItem(), album, new QobuzDownloadSettings(), CancellationToken.None);

            var ex = (await act.Should().ThrowAsync<AlbumDownloadException>(
                "the completion policy is unchanged by the suppression fix — Failed is still reported so " +
                "Lidarr can blocklist + fall back when a different edition/source genuinely could help")).Which;
            ex.TrackResults.Should().ContainSingle(r => r.TrackId == "t20" && r.Reason == TrackUnavailableReason.Restricted,
                "the classified reason must reach AlbumDownloadException.TrackResults so the download client " +
                "can decide whether to suppress the release");
        }

        // ── stream resolution / re-auth path ──────────────────────────────────────────────────────

        [Fact]
        public async Task ResolveStreamAsync_CallsGetStreamingInfoWithPreferredQuality()
        {
            var api = new Mock<IQobuzApiClient>();
            api.Setup(a => a.GetStreamingInfoAsync("t1", 7, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new QobuzStreamResponse { Url = "https://cdn.qobuz/x", FormatId = 7 });
            var sut = MakeService(api.Object);
            var settings = new QobuzDownloadSettings { PreferredQuality = 7 };

            var (url, ext) = await sut.ResolveStreamAsync("t1", settings, MakeItem(), new QobuzTrackClassifier(), CancellationToken.None);

            url.Should().Be("https://cdn.qobuz/x");
            ext.Should().Be(".flac");
            // Same call the api client renews a stale session on — the download-path re-auth seam.
            api.Verify(a => a.GetStreamingInfoAsync("t1", 7, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ResolveStreamAsync_QualityFallback_IsRecordedOnDownloadItem()
        {
            var api = new Mock<IQobuzApiClient>();
            api.Setup(a => a.GetStreamingInfoAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new QobuzStreamResponse
               {
                   Url = "https://cdn.qobuz/x",
                   FormatId = 6, // requested 7, served 6 → fallback
                   Restrictions = new List<QobuzStreamRestriction>
                   {
                       new QobuzStreamRestriction { Code = "FormatRestrictedByFormatAvailability" },
                   },
               });
            var sut = MakeService(api.Object);
            var item = MakeItem();
            var settings = new QobuzDownloadSettings { PreferredQuality = 7 };

            await sut.ResolveStreamAsync("t1", settings, item, new QobuzTrackClassifier(), CancellationToken.None);

            item.QualityFallbackCount.Should().Be(1);
        }

        [Fact]
        public async Task ResolveStreamAsync_PreviewOnly_ClassifiedAsSkip()
        {
            var api = new Mock<IQobuzApiClient>();
            api.Setup(a => a.GetStreamingInfoAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new TrackUnavailableException("t1", "preview only", TrackUnavailableReason.PreviewOnly));
            var sut = MakeService(api.Object);
            var classifier = new QobuzTrackClassifier();

            var (url, _) = await sut.ResolveStreamAsync("t1", new QobuzDownloadSettings(), MakeItem(), classifier, CancellationToken.None);

            url.Should().BeEmpty("a skip yields no URL so only that track fails, not the album");
            classifier.IsSkipped("t1").Should().BeTrue();
            classifier.SkippedCount.Should().Be(1);
        }

        [Fact]
        public async Task ResolveStreamAsync_HardFailure_IsNotClassifiedAsSkip()
        {
            var api = new Mock<IQobuzApiClient>();
            api.Setup(a => a.GetStreamingInfoAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("Qobuz returned a sample stream"));
            var sut = MakeService(api.Object);
            var classifier = new QobuzTrackClassifier();

            var (url, _) = await sut.ResolveStreamAsync("t1", new QobuzDownloadSettings(), MakeItem(), classifier, CancellationToken.None);

            url.Should().BeEmpty();
            classifier.SkippedCount.Should().Be(0, "a non-preview error is a failure, not a skip");
        }

        [Fact]
        public async Task ResolveStreamAsync_RestrictedTrackUnavailableException_IsRecordedWithReason()
        {
            // Bug: only PreviewOnly/NoQualityAvailable were ever recorded on the classifier; a Restricted
            // TrackUnavailableException (purchase/subscription/geo gate — see QobuzApiClient.GetStreamingInfoAsync)
            // fell into the same "no reason recorded" bucket as a genuinely-unknown hard failure. That erases the
            // distinction the download client needs after DownloadAlbumAsync throws: only purchase/subscription
            // restrictions are terminal enough to suppress; geo/transient/unknown still stay on the normal path.
            var api = new Mock<IQobuzApiClient>();
            api.Setup(a => a.GetStreamingInfoAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new TrackUnavailableException("t1", "Content restricted (TrackRestrictedByPurchaseCredentials)", TrackUnavailableReason.Restricted));
            var sut = MakeService(api.Object);
            var classifier = new QobuzTrackClassifier();

            var (url, _) = await sut.ResolveStreamAsync("t1", new QobuzDownloadSettings(), MakeItem(), classifier, CancellationToken.None);

            url.Should().BeEmpty();
            classifier.GetReason("t1").Should().Be(TrackUnavailableReason.Restricted,
                "the album-level permanent-only decision needs the reason recorded for every classified unavailability, not just previews");
        }

        [Fact]
        public async Task DownloadAlbumAsync_LogsBriefSummaryInsteadOfRegeneratingFullReportPerAlbum()
        {
            var album = MakeAlbum(2);
            var item = MakeItem();
            var result = SyntheticResult(successful: 2, total: 2);
            var summary = new Mock<IDownloadSummary>(MockBehavior.Strict);
            summary.Setup(s => s.RecordAlbumResult(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<long>()));
            summary.Setup(s => s.GetBriefSummary()).Returns("Downloaded 1/1 albums");
            var sut = new SyntheticTrackDownloadService(result, downloadSummary: summary.Object);

            await sut.DownloadAlbumAsync(item, album, new QobuzDownloadSettings(), CancellationToken.None);

            summary.Verify(s => s.GetBriefSummary(), Times.Once);
            summary.Verify(s => s.GenerateReport(), Times.Never,
                "full cumulative reports are too noisy and O(n) per album when several downloads complete concurrently");
        }

        [Fact]
        public async Task DownloadAlbumAsync_FullAlbumQualityFallback_LogsWarning()
        {
            var (logger, memory, factory) = CreateMemoryLogger();
            try
            {
                var album = MakeAlbum(2);
                var item = MakeItem();
                item.RecordQualityFallback(requestedFormatId: 7, actualFormatId: 6);
                item.RecordQualityFallback(requestedFormatId: 7, actualFormatId: 6);
                var sut = new SyntheticTrackDownloadService(SyntheticResult(successful: 2, total: 2), logger: logger);

                await sut.DownloadAlbumAsync(item, album, new QobuzDownloadSettings(), CancellationToken.None);

                memory.Logs.Should().Contain(l => l.Contains("Warn|Quality fallback used for 2/2 tracks", StringComparison.Ordinal));
                memory.Logs.Should().NotContain(l => l.Contains("Info|Quality fallback used for 2/2 tracks", StringComparison.Ordinal));
            }
            finally
            {
                factory.Shutdown();
            }
        }

        [Fact]
        public async Task DownloadAlbumAsync_PartialAlbumQualityFallback_LogsInfo()
        {
            var (logger, memory, factory) = CreateMemoryLogger();
            try
            {
                var album = MakeAlbum(2);
                var item = MakeItem();
                item.RecordQualityFallback(requestedFormatId: 7, actualFormatId: 6);
                var sut = new SyntheticTrackDownloadService(SyntheticResult(successful: 2, total: 2), logger: logger);

                await sut.DownloadAlbumAsync(item, album, new QobuzDownloadSettings(), CancellationToken.None);

                memory.Logs.Should().Contain(l => l.Contains("Info|Quality fallback used for 1/2 tracks", StringComparison.Ordinal));
                memory.Logs.Should().NotContain(l => l.Contains("Warn|Quality fallback used for 1/2 tracks", StringComparison.Ordinal));
            }
            finally
            {
                factory.Shutdown();
            }
        }

        [Fact]
        public async Task DownloadAlbumAsync_HttpLoopbackStreamUrl_IsBlockedByProductionUriPolicy()
        {
            var api = new Mock<IQobuzApiClient>();
            api.Setup(a => a.GetStreamingInfoAsync("t1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new QobuzStreamResponse { Url = "http://127.0.0.1:9/internal.flac", FormatId = 6 });
            var sut = MakeService(api.Object);
            var outputPath = Path.Combine(Path.GetTempPath(), "qobuzarr-ssrf-" + Guid.NewGuid().ToString("N"));
            var item = MakeItem(outputPath);

            try
            {
                var act = async () => await sut.DownloadAlbumAsync(item, MakeAlbum(1), new QobuzDownloadSettings { PreferredQuality = 6 }, CancellationToken.None);

                var ex = await act.Should().ThrowAsync<AlbumDownloadException>();
                ex.Which.FailedTracks.Should().Be(1);
                ex.Which.TrackResults.Should().ContainSingle(r =>
                    !r.Success &&
                    r.Message != null &&
                    r.Message.Contains("Unsafe stream URL", StringComparison.Ordinal));
                Directory.EnumerateFiles(outputPath, "*", SearchOption.AllDirectories)
                    .Should().BeEmpty("blocked stream URLs must fail before any media file is created");
            }
            finally
            {
                if (Directory.Exists(outputPath))
                {
                    Directory.Delete(outputPath, recursive: true);
                }
            }
        }

        // ── helpers ──────────────────────────────────────────────────────────────────────────────

        private static TrackDownloadService MakeService(IQobuzApiClient api)
            => new TrackDownloadService(api, Mock.Of<IConcurrencyManager>(), Mock.Of<IDownloadSummary>(), Log);

        private static (Logger Logger, MemoryTarget Memory, LogFactory Factory) CreateMemoryLogger()
        {
            var factory = new LogFactory();
            var config = new LoggingConfiguration(factory);
            var memory = new MemoryTarget("quality-fallback-memory") { Layout = "${level}|${message}" };
            config.AddTarget(memory);
            config.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, memory));
            factory.Configuration = config;
            return (factory.GetLogger("QualityFallbackLogger"), memory, factory);
        }

        private static QobuzAlbum MakeAlbum(int trackCount)
        {
            return new QobuzAlbum
            {
                Id = "alb",
                Title = "Album",
                Artist = new QobuzArtist { Name = "Artist" },
                MediaCount = 1,
                TracksContainer = new QobuzTracksContainer
                {
                    Items = Enumerable.Range(1, trackCount)
                        .Select(i => new QobuzTrack { Id = $"t{i}", Title = $"Track {i}", TrackNumber = i, DiscNumber = 1 })
                        .ToList(),
                },
            };
        }

        private static QobuzDownloadItem MakeItem(string? outputPath = null)
            => new QobuzDownloadItem { AlbumId = "alb", Title = "Album", Artist = "Artist", OutputPath = outputPath ?? "out", TotalSize = 1000 };

        private sealed class QueuingSynchronizationContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state)
            {
                // Intentionally do not execute posted callbacks. This makes the test fail if
                // TrackDownloadService uses Progress<T>, which captures the current context and posts.
            }
        }

        private static CommonDownloadResult SyntheticResult(int successful, int total)
        {
            var result = new CommonDownloadResult();
            for (var i = 1; i <= total; i++)
            {
                result.TrackResults.Add(new CommonTrackDownloadResult
                {
                    TrackId = $"t{i}",
                    Success = i <= successful,
                    ErrorMessage = i <= successful ? null : "failed",
                });
            }
            result.Success = successful == total;
            return result;
        }
    }
}
