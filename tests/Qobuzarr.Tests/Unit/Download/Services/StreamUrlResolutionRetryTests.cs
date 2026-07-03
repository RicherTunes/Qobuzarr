using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Lidarr.Plugin.Qobuzarr.Models;
using Xunit;

namespace Qobuzarr.Tests.Unit.Download.Services
{
    /// <summary>
    /// Q-D: stream-URL resolution (<see cref="IQobuzApiClient.GetStreamingInfoAsync"/>, the
    /// "track/getFileUrl" call) previously had NO retry of its own. The byte-download retry
    /// (<see cref="TrackDownloadRetryTests"/>) only covers the CDN body download that happens
    /// AFTER a stream URL has already been resolved; a transient 5xx/timeout/network blip while
    /// resolving the URL itself failed the whole track (and therefore the album, per the
    /// completion contract) with zero retry.
    ///
    /// <see cref="TrackDownloadService.ResolveStreamAsync"/> now retries the
    /// <c>GetStreamingInfoAsync</c> call for transient failures only (mirrors the
    /// attempt-count/backoff-seam pattern used by <see cref="TrackDownloadRetryTests"/> so tests
    /// can run with zero delay).
    /// </summary>
    public sealed class StreamUrlResolutionRetryTests
    {
        private sealed class FakeStreamResolveService : TrackDownloadService
        {
            internal override int MaxStreamResolveAttempts { get; }

            public FakeStreamResolveService(IQobuzApiClient api, int maxAttempts)
                : base(api, Mock.Of<IConcurrencyManager>(), Mock.Of<IDownloadSummary>(), LogManager.GetLogger("StreamUrlResolutionRetryTests"))
            {
                MaxStreamResolveAttempts = maxAttempts;
            }

            internal override TimeSpan GetStreamResolveRetryDelay(int attempt) => TimeSpan.Zero;
        }

        private static QobuzDownloadItem MakeItem() =>
            new QobuzDownloadItem { AlbumId = "alb", Title = "Album", Artist = "Artist", OutputPath = "out", TotalSize = 0 };

        // ── RED→GREEN: transient failure retries then succeeds ────────────────────────────────

        [Theory]
        [InlineData(503)]
        [InlineData(500)]
        [InlineData(429)]
        [InlineData(408)]
        public async Task ResolveStreamAsync_ApiStatusFailure_IsNotRetriedAgainAtTrackLayer(int statusCode)
        {
            var api = new Mock<IQobuzApiClient>();
            var attempts = 0;
            api.Setup(a => a.GetStreamingInfoAsync("t1", 7, It.IsAny<CancellationToken>()))
               .Returns(() =>
               {
                   attempts++;
                   throw new Lidarr.Plugin.Qobuzarr.API.QobuzApiException("transient", statusCode, "ServerError");
               });

            var sut = new FakeStreamResolveService(api.Object, maxAttempts: 3);

            var (url, ext) = await sut.ResolveStreamAsync("t1", new QobuzDownloadSettings { PreferredQuality = 7 }, MakeItem(), new QobuzTrackClassifier(), CancellationToken.None);

            url.Should().BeEmpty();
            ext.Should().BeEmpty();
            attempts.Should().Be(1, "QobuzHttpClient already owns retry/backoff for classified API HTTP status failures");
        }

        [Fact]
        public async Task ResolveStreamAsync_TransientNetworkException_RetriesThenSucceeds()
        {
            var api = new Mock<IQobuzApiClient>();
            var attempts = 0;
            api.Setup(a => a.GetStreamingInfoAsync("t1", 7, It.IsAny<CancellationToken>()))
               .Returns(() =>
               {
                   attempts++;
                   if (attempts == 1)
                   {
                       throw new SocketException();
                   }
                   return Task.FromResult(new QobuzStreamResponse { Url = "https://cdn.qobuz/x", FormatId = 7 });
               });

            var sut = new FakeStreamResolveService(api.Object, maxAttempts: 3);

            var (url, _) = await sut.ResolveStreamAsync("t1", new QobuzDownloadSettings { PreferredQuality = 7 }, MakeItem(), new QobuzTrackClassifier(), CancellationToken.None);

            url.Should().Be("https://cdn.qobuz/x");
            attempts.Should().Be(2);
        }

        [Fact]
        public async Task ResolveStreamAsync_TransientTimeout_RetriesThenSucceeds()
        {
            var api = new Mock<IQobuzApiClient>();
            var attempts = 0;
            api.Setup(a => a.GetStreamingInfoAsync("t1", 7, It.IsAny<CancellationToken>()))
               .Returns(() =>
               {
                   attempts++;
                   if (attempts == 1)
                   {
                       // Mirrors the SendAsync-timeout shape (TaskCanceledException without the
                       // caller's own CancellationToken having fired).
                       throw new TaskCanceledException("The request timed out.");
                   }
                   return Task.FromResult(new QobuzStreamResponse { Url = "https://cdn.qobuz/x", FormatId = 7 });
               });

            var sut = new FakeStreamResolveService(api.Object, maxAttempts: 3);

            var (url, _) = await sut.ResolveStreamAsync("t1", new QobuzDownloadSettings { PreferredQuality = 7 }, MakeItem(), new QobuzTrackClassifier(), CancellationToken.None);

            url.Should().Be("https://cdn.qobuz/x");
            attempts.Should().Be(2);
        }

        [Fact]
        public async Task ResolveStreamAsync_TransientNetworkFailure_ExhaustsRetriesThenReturnsEmpty()
        {
            var api = new Mock<IQobuzApiClient>();
            var attempts = 0;
            api.Setup(a => a.GetStreamingInfoAsync("t1", 7, It.IsAny<CancellationToken>()))
               .Returns(() =>
               {
                   attempts++;
                   throw new HttpRequestException("still down");
               });

            var sut = new FakeStreamResolveService(api.Object, maxAttempts: 3);

            var (url, ext) = await sut.ResolveStreamAsync("t1", new QobuzDownloadSettings { PreferredQuality = 7 }, MakeItem(), new QobuzTrackClassifier(), CancellationToken.None);

            url.Should().BeEmpty("all retries were exhausted; the track fails, not the whole process");
            ext.Should().BeEmpty();
            attempts.Should().Be(3, "all configured attempts must be used before giving up");
        }

        // ── guard: non-transient (auth/4xx/classified) failures are NOT retried ───────────────

        [Theory]
        [InlineData(401)]
        [InlineData(403)]
        [InlineData(404)]
        public async Task ResolveStreamAsync_AuthOrClientError_IsNotRetried(int statusCode)
        {
            var api = new Mock<IQobuzApiClient>();
            var attempts = 0;
            api.Setup(a => a.GetStreamingInfoAsync("t1", 7, It.IsAny<CancellationToken>()))
               .Returns(() =>
               {
                   attempts++;
                   throw new Lidarr.Plugin.Qobuzarr.API.QobuzApiException("denied", statusCode, "AuthenticationFailed");
               });

            var sut = new FakeStreamResolveService(api.Object, maxAttempts: 3);

            var (url, _) = await sut.ResolveStreamAsync("t1", new QobuzDownloadSettings { PreferredQuality = 7 }, MakeItem(), new QobuzTrackClassifier(), CancellationToken.None);

            url.Should().BeEmpty();
            attempts.Should().Be(1, "auth/4xx failures are not transient and must fail fast without retrying");
        }

        [Fact]
        public async Task ResolveStreamAsync_ClassifiedRestriction_IsNotRetried()
        {
            // A classified TrackUnavailableException (preview/restricted/etc.) is a business-rule
            // rejection, not a transport failure — retrying it would just re-confirm the same
            // permanent answer while burning time/log noise.
            var api = new Mock<IQobuzApiClient>();
            var attempts = 0;
            api.Setup(a => a.GetStreamingInfoAsync("t1", 7, It.IsAny<CancellationToken>()))
               .Returns(() =>
               {
                   attempts++;
                   throw new TrackUnavailableException("t1", "preview only", TrackUnavailableReason.PreviewOnly);
               });

            var sut = new FakeStreamResolveService(api.Object, maxAttempts: 3);
            var classifier = new QobuzTrackClassifier();

            var (url, _) = await sut.ResolveStreamAsync("t1", new QobuzDownloadSettings { PreferredQuality = 7 }, MakeItem(), classifier, CancellationToken.None);

            url.Should().BeEmpty();
            attempts.Should().Be(1);
            classifier.IsSkipped("t1").Should().BeTrue();
        }

        [Fact]
        public async Task ResolveStreamAsync_OperationCancelled_IsNotRetried()
        {
            var api = new Mock<IQobuzApiClient>();
            var attempts = 0;
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            api.Setup(a => a.GetStreamingInfoAsync("t1", 7, It.IsAny<CancellationToken>()))
               .Returns(() =>
               {
                   attempts++;
                   throw new OperationCanceledException();
               });

            var sut = new FakeStreamResolveService(api.Object, maxAttempts: 3);

            Func<Task> act = () => sut.ResolveStreamAsync("t1", new QobuzDownloadSettings { PreferredQuality = 7 }, MakeItem(), new QobuzTrackClassifier(), cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
            attempts.Should().Be(1, "an honored cancellation must not be retried");
        }
    }
}
