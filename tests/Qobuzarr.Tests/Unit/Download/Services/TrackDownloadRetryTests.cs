using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Xunit;

namespace Qobuzarr.Tests.Unit.Download.Services
{
    /// <summary>
    /// Regression tests for the live-found infinite re-grab loop on multi-track albums
    /// (e.g. The Pretty Reckless – Dear God). A single track's HTTP body stream truncated mid-download
    /// (System.Net.Http.HttpIOException: "The response ended prematurely … (ResponseEnded)") and was
    /// NOT retried, so the whole album failed (13/14) → Lidarr re-grabbed → the same truncation recurred
    /// → perpetual loop. The download path already supports resume (Range from the preserved .partial via
    /// ResumeHttpDownloader); the fix is to retry the attempt on transient stream failures so a resume
    /// completes the track instead of failing the album.
    /// </summary>
    public sealed class TrackDownloadRetryTests
    {
        private sealed record DepsForTest(
            IQobuzApiClient Api,
            IConcurrencyManager Concurrency,
            IDownloadSummary Summary,
            Logger Logger);

        private static DepsForTest MakeDeps() => new(
            Mock.Of<IQobuzApiClient>(),
            Mock.Of<IConcurrencyManager>(),
            Mock.Of<IDownloadSummary>(),
            LogManager.GetLogger("TrackDownloadRetryTests"));

        private sealed class FakeTrackDownloadService : TrackDownloadService
        {
            private readonly int _succeedOnAttempt;
            private readonly Func<Exception> _transientFactory;
            private readonly Exception? _nonTransient;
            private int _attempts;

            public int Attempts => _attempts;
            internal override int MaxDownloadAttempts { get; }

            public FakeTrackDownloadService(
                DepsForTest deps,
                int succeedOnAttempt,
                int maxAttempts,
                Func<Exception>? transientFactory = null,
                Exception? nonTransient = null)
                : base(deps.Api, deps.Concurrency, deps.Summary, deps.Logger)
            {
                _succeedOnAttempt = succeedOnAttempt;
                MaxDownloadAttempts = maxAttempts;
                _transientFactory = transientFactory ?? (() => new HttpIOException(HttpRequestError.ResponseEnded, "truncated"));
                _nonTransient = nonTransient;
            }

            internal override async Task<long> DownloadAttemptAsync(string url, string filePath, string partialPath, CancellationToken cancellationToken)
            {
                await Task.Yield();
                var n = Interlocked.Increment(ref _attempts);
                if (_nonTransient != null)
                {
                    throw _nonTransient;
                }
                if (n < _succeedOnAttempt)
                {
                    throw _transientFactory();
                }
                File.WriteAllBytes(filePath, new byte[4096]);
                return 4096L;
            }

            internal override TimeSpan GetRetryDelay(int attempt) => TimeSpan.Zero;

            public Task<long> InvokeDownloadAsync(string url, string filePath, CancellationToken ct) =>
                DownloadToFileAsync(url, filePath, ct);
        }

        [Fact]
        public async Task DownloadToFileAsync_retries_on_transient_truncation_then_succeeds()
        {
            var dir = Path.Combine(Path.GetTempPath(), "qz-retry-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var filePath = Path.Combine(dir, "track.flac");
            try
            {
                var svc = new FakeTrackDownloadService(MakeDeps(), succeedOnAttempt: 2, maxAttempts: 4);

                var bytes = await svc.InvokeDownloadAsync("https://cdn.example/track", filePath, CancellationToken.None);

                bytes.Should().Be(4096);
                svc.Attempts.Should().Be(2, "the first attempt truncated and the second resumed to completion");
                File.Exists(filePath).Should().BeTrue();
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* best effort */ }
            }
        }

        [Fact]
        public async Task DownloadToFileAsync_exhausts_retries_then_throws()
        {
            var dir = Path.Combine(Path.GetTempPath(), "qz-retry-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var filePath = Path.Combine(dir, "track.flac");
            try
            {
                var svc = new FakeTrackDownloadService(MakeDeps(), succeedOnAttempt: int.MaxValue, maxAttempts: 3);

                Func<Task> act = () => svc.InvokeDownloadAsync("https://cdn.example/track", filePath, CancellationToken.None);

                await act.Should().ThrowAsync<HttpIOException>();
                svc.Attempts.Should().Be(3, "all attempts are exhausted before giving up");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* best effort */ }
            }
        }

        [Fact]
        public async Task DownloadToFileAsync_does_not_retry_on_non_transient_error()
        {
            var dir = Path.Combine(Path.GetTempPath(), "qz-retry-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var filePath = Path.Combine(dir, "track.flac");
            try
            {
                var svc = new FakeTrackDownloadService(
                    MakeDeps(), succeedOnAttempt: int.MaxValue, maxAttempts: 4,
                    nonTransient: new InvalidOperationException("Download returned no content"));

                Func<Task> act = () => svc.InvokeDownloadAsync("https://cdn.example/track", filePath, CancellationToken.None);

                await act.Should().ThrowAsync<InvalidOperationException>();
                svc.Attempts.Should().Be(1, "a non-transient failure must fail fast without retrying");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* best effort */ }
            }
        }

        [Fact]
        public async Task DownloadToFileAsync_does_not_retry_when_cancelled()
        {
            var dir = Path.Combine(Path.GetTempPath(), "qz-retry-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var filePath = Path.Combine(dir, "track.flac");
            try
            {
                using var cts = new CancellationTokenSource();
                cts.Cancel();
                var svc = new FakeTrackDownloadService(
                    MakeDeps(), succeedOnAttempt: int.MaxValue, maxAttempts: 4,
                    transientFactory: () => new OperationCanceledException());

                Func<Task> act = () => svc.InvokeDownloadAsync("https://cdn.example/track", filePath, cts.Token);

                await act.Should().ThrowAsync<OperationCanceledException>();
                svc.Attempts.Should().Be(1, "an honored cancellation must not be retried");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* best effort */ }
            }
        }
    }
}
