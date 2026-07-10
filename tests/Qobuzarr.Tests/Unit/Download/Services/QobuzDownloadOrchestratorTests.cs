using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Abstractions.Models;
using Lidarr.Plugin.Common.Interfaces;
using Lidarr.Plugin.Common.Services.Download;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Xunit;

namespace Qobuzarr.Tests.Unit.Download.Services
{
    /// <summary>
    /// <see cref="QobuzDownloadOrchestrator"/> reuses Common's shared album loop and per-track URL engine,
    /// customizing two seams: (1) file naming via Qobuz's <see cref="TrackFileNameBuilder"/> (multi-disc
    /// aware) and (2) the post-download audio-payload validation that fails a track.
    /// </summary>
    public sealed class QobuzDownloadOrchestratorTests : IDisposable
    {
        private readonly string _tempDir;

        public QobuzDownloadOrchestratorTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"QobuzOrchTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* best effort */ }
        }

        // ── filename ─────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void BuildTrackOutputPath_SingleDisc_UsesTrackFileNameBuilder()
        {
            var track = new QobuzTrack { Id = "t", Title = "Title", TrackNumber = 5, DiscNumber = 1 };
            var path = QobuzDownloadOrchestrator.BuildTrackOutputPath(@"X:\out", track, mediaCount: 1, namingFormatId: 6);
            Path.GetFileName(path).Should().Be("05 - Title.flac");
        }

        [Fact]
        public void BuildTrackOutputPath_MultiDisc_AddsDiscPrefix()
        {
            var track = new QobuzTrack { Id = "t", Title = "Title", TrackNumber = 5, DiscNumber = 2 };
            var path = QobuzDownloadOrchestrator.BuildTrackOutputPath(@"X:\out", track, mediaCount: 2, namingFormatId: 6);
            Path.GetFileName(path).Should().Be("D02T05 - Title.flac");
        }

        [Fact]
        public void BuildTrackOutputPath_CurlyApostropheTitle_DelegatesToBuilder()
        {
            var track = new QobuzTrack { Id = "t", Title = "Don’t Stop", TrackNumber = 1, DiscNumber = 1 };
            var path = QobuzDownloadOrchestrator.BuildTrackOutputPath(@"X:\out", track, mediaCount: 1, namingFormatId: 6);
            var expected = TrackFileNameBuilder.Build(1, "Don’t Stop", 6, 1, 1);
            Path.GetFileName(path).Should().Be(expected);
            path.Should().EndWith(".flac");
        }

        // ── payload validation ───────────────────────────────────────────────────────────────────

        [Fact]
        public void ValidateDownloadedPayloadOrThrow_AcceptsRealFlac()
        {
            var path = WriteFlac(_tempDir, 2048);
            var act = () => QobuzDownloadOrchestrator.ValidateDownloadedPayloadOrThrow(path);
            act.Should().NotThrow();
        }

        [Fact]
        public void ValidateDownloadedPayloadOrThrow_RejectsTextPayload()
        {
            var path = Path.Combine(_tempDir, "page.flac");
            File.WriteAllText(path, "<!doctype html><html><body>error</body></html>" + new string(' ', 2048));
            var act = () => QobuzDownloadOrchestrator.ValidateDownloadedPayloadOrThrow(path);
            act.Should().Throw<Exception>();
        }

        [Fact]
        public void ValidateDownloadedPayloadOrThrow_RejectsTooSmallFile()
        {
            var path = WriteFlac(_tempDir, 100); // valid magic but below the 1 KiB floor
            var act = () => QobuzDownloadOrchestrator.ValidateDownloadedPayloadOrThrow(path);
            act.Should().Throw<Exception>();
        }

        // ── end-to-end override ──────────────────────────────────────────────────────────────────

        [Fact]
        public async Task DownloadAlbumAsync_HappyPath_DownloadsNamesValidatesAndTags()
        {
            var album = MakeAlbum(("t1", "First", 1, 1), ("t2", "Second", 2, 1));
            var applier = new RecordingApplier();
            var postProcessor = new RecordingPostProcessor();
            var handler = new StubHandler(_ => FlacResponse(2048));
            var orchestrator = MakeOrchestrator(album, handler, applier, postProcessor,
                getStream: (_, _) => Task.FromResult(("https://cdn.qobuz.test/x.flac", ".flac")));

            var outDir = Path.Combine(_tempDir, "album");
            var result = await orchestrator.DownloadAlbumAsync("alb", outDir, null, null, CancellationToken.None);

            result.Success.Should().BeTrue();
            result.TrackResults.Should().HaveCount(2);
            result.TrackResults.Should().OnlyContain(r => r.Success);

            File.Exists(Path.Combine(outDir, "01 - First.flac")).Should().BeTrue("filename comes from TrackFileNameBuilder");
            File.Exists(Path.Combine(outDir, "02 - Second.flac")).Should().BeTrue();

            applier.Calls.Should().Be(2);
            applier.LastTrack.Should().BeOfType<QobuzStreamingTrack>("the orchestrator hands the metadata seam the Qobuz carrier");
            postProcessor.Calls.Should().Be(2);
        }

        [Fact]
        public async Task DownloadAlbumAsync_TextPayload_FailsTrackAndDeletesFile()
        {
            var album = MakeAlbum(("t1", "First", 1, 1));
            var handler = new StubHandler(_ => HtmlResponse());
            var orchestrator = MakeOrchestrator(album, handler, new RecordingApplier(), new RecordingPostProcessor(),
                getStream: (_, _) => Task.FromResult(("https://cdn.qobuz.test/x.flac", ".flac")));

            var outDir = Path.Combine(_tempDir, "album-html");
            var result = await orchestrator.DownloadAlbumAsync("alb", outDir, null, null, CancellationToken.None);

            result.Success.Should().BeFalse();
            result.TrackResults.Should().ContainSingle().Which.Success.Should().BeFalse();
            File.Exists(Path.Combine(outDir, "01 - First.flac")).Should().BeFalse("a non-audio payload must be rejected and cleaned up");
        }

        [Fact]
        public async Task DownloadTrackAsync_DirectCall_TextPayload_FailsTrackAndDeletesFile()
        {
            // Payload validation must guard EVERY download path, not just the album loop — a direct
            // track download of a non-audio payload (HTML soft-404 served as 200) must fail and clean up.
            var album = MakeAlbum(("t1", "First", 1, 1));
            var handler = new StubHandler(_ => HtmlResponse());
            var orchestrator = MakeOrchestrator(album, handler, new RecordingApplier(), new RecordingPostProcessor(),
                getStream: (_, _) => Task.FromResult(("https://cdn.qobuz.test/x.flac", ".flac")));

            var outPath = Path.Combine(_tempDir, "direct", "01 - First.flac");
            var result = await orchestrator.DownloadTrackAsync("t1", outPath, null, CancellationToken.None);

            result.Success.Should().BeFalse("a non-audio payload must fail validation on the direct track path too");
            File.Exists(outPath).Should().BeFalse("a rejected payload must not linger on disk");
        }

        [Fact]
        public async Task DownloadAlbumAsync_EmptyStreamUrl_FailsThatTrack()
        {
            var album = MakeAlbum(("t1", "First", 1, 1), ("t2", "Second", 2, 1));
            var handler = new StubHandler(_ => FlacResponse(2048));
            // First track resolves, second returns an empty URL (e.g. preview/restricted).
            var orchestrator = MakeOrchestrator(album, handler, new RecordingApplier(), new RecordingPostProcessor(),
                getStream: (id, _) => Task.FromResult(id == "t2" ? (string.Empty, string.Empty) : ("https://cdn.qobuz.test/x.flac", ".flac")));

            var outDir = Path.Combine(_tempDir, "album-partial");
            var result = await orchestrator.DownloadAlbumAsync("alb", outDir, null, null, CancellationToken.None);

            result.Success.Should().BeFalse("an incomplete album must not report success");
            result.TrackResults.Count(r => r.Success).Should().Be(1);
            result.TrackResults.Count(r => !r.Success).Should().Be(1);
        }

        [Fact]
        public async Task DownloadAlbumAsync_RedirectToLoopback_IsBlockedBeforeSecondRequest()
        {
            var album = MakeAlbum(("t1", "First", 1, 1));
            var handler = new RedirectToLoopbackHandler();
            var policy = new RemoteMediaUriPolicy
            {
                AllowHttp = true,
                DnsResolver = host => host.Equals("stream.example", StringComparison.OrdinalIgnoreCase)
                    ? new[] { IPAddress.Parse("93.184.216.34") }
                    : new[] { IPAddress.Parse("127.0.0.1") },
            };
            var orchestrator = MakeOrchestrator(
                album,
                handler,
                new RecordingApplier(),
                new RecordingPostProcessor(),
                getStream: (_, _) => Task.FromResult(("https://stream.example/track.flac", ".flac")),
                mediaUriPolicy: policy);

            var outDir = Path.Combine(_tempDir, "album-redirect");
            var result = await orchestrator.DownloadAlbumAsync("alb", outDir, null, null, CancellationToken.None);

            result.Success.Should().BeFalse("a media redirect to loopback is a hard SSRF block");
            result.TrackResults.Should().ContainSingle(r =>
                !r.Success &&
                r.ErrorMessage != null &&
                r.ErrorMessage.Contains("Refusing redirect to an unsafe URL", StringComparison.Ordinal));
            handler.InitialRequests.Should().Be(1);
            handler.UnsafeTargetRequests.Should().Be(0,
                "Common must validate the redirect Location before a request is issued to the unsafe target");
            Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories)
                .Should().BeEmpty("blocked redirects must not write partial media files");
        }

        // ── helpers ──────────────────────────────────────────────────────────────────────────────

        private static QobuzAlbum MakeAlbum(params (string id, string title, int num, int disc)[] tracks)
        {
            return new QobuzAlbum
            {
                Id = "alb",
                Title = "Album",
                Artist = new QobuzArtist { Name = "Artist" },
                MediaCount = tracks.Length > 0 ? tracks.Max(t => t.disc) : 1,
                TracksContainer = new QobuzTracksContainer
                {
                    Items = tracks.Select(t => new QobuzTrack { Id = t.id, Title = t.title, TrackNumber = t.num, DiscNumber = t.disc }).ToList(),
                },
            };
        }

        private static QobuzDownloadOrchestrator MakeOrchestrator(
            QobuzAlbum album,
            HttpMessageHandler handler,
            IAudioMetadataApplier applier,
            IAudioPostProcessor postProcessor,
            Func<string, StreamingQuality?, Task<(string, string)>> getStream,
            RemoteMediaUriPolicy? mediaUriPolicy = null)
        {
            var httpClient = new HttpClient(handler);
            Func<string, Task<StreamingAlbum>> getAlbum = _ => Task.FromResult(new StreamingAlbum { Id = album.Id });
            Func<string, Task<IReadOnlyList<string>>> getTrackIds = _ =>
                Task.FromResult<IReadOnlyList<string>>(album.GetTracks().Select(t => t.Id).ToList());
            Func<string, Task<StreamingTrack>> getTrack = id =>
                Task.FromResult<StreamingTrack>(QobuzStreamingTrack.From(album.GetTracks().First(t => t.Id == id), album));

            return new QobuzDownloadOrchestrator(
                httpClient,
                getAlbum,
                getTrack,
                getTrackIds,
                (id, q) => getStream(id, q),
                maxConcurrentTracks: 2,
                album: album,
                namingFormatId: 6,
                metadataApplier: applier,
                postProcessor: postProcessor,
                // permissive so the fake (non-resolvable) host isn't blocked by the SSRF guard
                mediaUriPolicy: mediaUriPolicy ?? new RemoteMediaUriPolicy { AllowHttp = true, AllowPrivateNetworks = true, ResolveDns = false });
        }

        private static string WriteFlac(string dir, int size)
        {
            var path = Path.Combine(dir, $"{Guid.NewGuid():N}.flac");
            var bytes = new byte[size];
            bytes[0] = (byte)'f'; bytes[1] = (byte)'L'; bytes[2] = (byte)'a'; bytes[3] = (byte)'C';
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static HttpResponseMessage FlacResponse(int size)
        {
            var bytes = new byte[size];
            bytes[0] = (byte)'f'; bytes[1] = (byte)'L'; bytes[2] = (byte)'a'; bytes[3] = (byte)'C';
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }

        private static HttpResponseMessage HtmlResponse()
        {
            var html = "<!doctype html><html><body>403 Forbidden</body></html>" + new string('x', 2048);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(html)) };
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(_responder(request));
        }

        private sealed class RedirectToLoopbackHandler : HttpMessageHandler
        {
            public int InitialRequests;
            public int UnsafeTargetRequests;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri?.Host.Equals("stream.example", StringComparison.OrdinalIgnoreCase) == true)
                {
                    Interlocked.Increment(ref InitialRequests);
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found)
                    {
                        Headers =
                        {
                            Location = new Uri("http://127.0.0.1/internal.flac"),
                        },
                    });
                }

                if (request.RequestUri?.Host == "127.0.0.1")
                {
                    Interlocked.Increment(ref UnsafeTargetRequests);
                    return Task.FromResult(FlacResponse(2048));
                }

                throw new InvalidOperationException($"Unexpected request URI: {request.RequestUri}");
            }
        }

        private sealed class RecordingApplier : IAudioMetadataApplier
        {
            public int Calls;
            public StreamingTrack? LastTrack;
            public Task ApplyAsync(string filePath, StreamingTrack metadata, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref Calls);
                LastTrack = metadata;
                return Task.CompletedTask;
            }
        }

        private sealed class RecordingPostProcessor : IAudioPostProcessor
        {
            public int Calls;
            public Task<string> PostProcessAsync(string filePath, StreamingTrack track, StreamingQuality? quality, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref Calls);
                return Task.FromResult(filePath);
            }
        }
    }
}
