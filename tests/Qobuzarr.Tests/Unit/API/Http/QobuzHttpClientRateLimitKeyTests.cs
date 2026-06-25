using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Services.Performance;
using Lidarr.Plugin.Qobuzarr.API.Http;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using Qobuzarr.Tests.Fixtures;
using Xunit;

namespace Qobuzarr.Tests.Unit.API.Http
{
    /// <summary>
    /// Pins the rate-limiter keying contract (FIX 2): per-album / per-track URLs (distinct ids +
    /// signed query params) MUST collapse into ONE stable per-endpoint bucket. The previous code
    /// keyed the adaptive limiter on the FULL request URL (query string included), so every
    /// album/track became its own bucket, was hit ~once, accumulated no adaptive pressure, and
    /// effectively disabled global rate limiting — risking a 429 storm / ban.
    /// </summary>
    public class QobuzHttpClientRateLimitKeyTests : TestFixtureBase
    {
        /// <summary>
        /// Recording fake: captures every (service, endpoint) key the SUT feeds the limiter via
        /// both WaitIfNeededAsync and RecordResponse so the test can assert on the distinct key set.
        /// </summary>
        private sealed class RecordingRateLimiter : IUniversalAdaptiveRateLimiter
        {
            public ConcurrentBag<string> WaitEndpoints { get; } = new();
            public ConcurrentBag<string> RecordEndpoints { get; } = new();

            public Task<bool> WaitIfNeededAsync(string service, string endpoint, CancellationToken cancellationToken = default)
            {
                WaitEndpoints.Add(endpoint);
                return Task.FromResult(true);
            }

            public void RecordResponse(string service, string endpoint, HttpResponseMessage response)
                => RecordEndpoints.Add(endpoint);

            public int GetCurrentLimit(string service, string endpoint) => 0;
            public ServiceRateLimitStats GetServiceStats(string service) => new ServiceRateLimitStats();
            public GlobalRateLimitStats GetGlobalStats() => new GlobalRateLimitStats();
            public void Dispose() { }
        }

        private static HttpResponse Ok(HttpRequest request)
            => new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, "{}", HttpStatusCode.OK);

        [Fact]
        public async Task ExecuteAsync_DistinctSignedAlbumUrls_ShareOneRateLimitBucket()
        {
            // Arrange
            var limiter = new RecordingRateLimiter();
            var mockHttp = new Mock<IHttpClient>();
            mockHttp.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                    .ReturnsAsync((HttpRequest r) => Ok(r));

            var sut = new QobuzHttpClient(
                mockHttp.Object,
                LogManager.GetLogger("QobuzHttpClientRateLimitKeyTests"),
                performanceMonitor: null,
                adaptiveRateLimiter: limiter,
                healthCache: null);

            const int n = 8;
            // Act: N distinct album URLs — distinct album_id + per-item signed query params.
            for (var i = 1; i <= n; i++)
            {
                var url = $"https://www.qobuz.com/api.json/0.2/album/get?album_id=A{i}&request_ts=170000000{i}&request_sig=sig{i}";
                await sut.ExecuteAsync(new HttpRequest(url));
            }

            // Assert: the limiter must have seen exactly ONE distinct endpoint key, not N.
            var distinctWaitKeys = limiter.WaitEndpoints.Distinct().ToList();
            distinctWaitKeys.Should().HaveCount(1,
                "all per-album URLs must collapse to one host+first-segment bucket so adaptive pressure accumulates");
            distinctWaitKeys.Single().Should().Be("www.qobuz.com:api.json",
                "the coarse key is host + first path segment, independent of album_id/signature");

            limiter.WaitEndpoints.Should().HaveCount(n, "every request still consults the limiter once");

            // RecordResponse must use the SAME coarse key.
            limiter.RecordEndpoints.Distinct().Should().BeEquivalentTo(new[] { "www.qobuz.com:api.json" },
                "RecordResponse must feed back into the same bucket as WaitIfNeededAsync");
        }

        [Fact]
        public async Task ExecuteAsync_SignedQueryParams_DoNotChangeTheKey()
        {
            // Arrange: same endpoint, two different signatures/timestamps -> still one bucket.
            var limiter = new RecordingRateLimiter();
            var mockHttp = new Mock<IHttpClient>();
            mockHttp.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                    .ReturnsAsync((HttpRequest r) => Ok(r));

            var sut = new QobuzHttpClient(
                mockHttp.Object,
                LogManager.GetLogger("QobuzHttpClientRateLimitKeyTests-2"),
                performanceMonitor: null,
                adaptiveRateLimiter: limiter,
                healthCache: null);

            await sut.ExecuteAsync(new HttpRequest("https://www.qobuz.com/api.json/0.2/track/getFileUrl?track_id=1&request_sig=aaa&request_ts=1"));
            await sut.ExecuteAsync(new HttpRequest("https://www.qobuz.com/api.json/0.2/track/getFileUrl?track_id=2&request_sig=bbb&request_ts=2"));

            limiter.WaitEndpoints.Distinct().Should().ContainSingle()
                .Which.Should().Be("www.qobuz.com:api.json");
        }
    }
}
