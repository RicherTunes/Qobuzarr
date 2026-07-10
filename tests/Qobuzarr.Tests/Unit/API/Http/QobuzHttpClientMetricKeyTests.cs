using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.API.Http;
using Lidarr.Plugin.Qobuzarr.Services;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using Qobuzarr.Tests.Fixtures;
using Xunit;

namespace Qobuzarr.Tests.Unit.API.Http
{
    /// <summary>
    /// Pins the performance-metric keying contract: RecordApiCall MUST receive the same coarse,
    /// scrubbed endpoint key the rate limiter uses (host + first path segment), NOT the raw signed
    /// URL. The previous code passed <c>request.Url.ToString()</c>, so every call minted a unique,
    /// unbounded metric key (request_ts/request_sig vary per call) AND persisted request_sig /
    /// user_auth_token / app_id values verbatim inside metric names.
    /// </summary>
    public class QobuzHttpClientMetricKeyTests : TestFixtureBase
    {
        /// <summary>
        /// Recording fake: captures every endpoint key the SUT feeds RecordApiCall so the test can
        /// assert on stability (same key across calls) and hygiene (no signed-query values).
        /// </summary>
        private sealed class RecordingPerformanceMonitor : IPerformanceMonitoringService
        {
            public ConcurrentBag<string> RecordedEndpoints { get; } = new();

            public void RecordApiCall(string endpoint, TimeSpan duration, bool success)
                => RecordedEndpoints.Add(endpoint);

            public void RecordCacheHit(string key) { }
            public void RecordCacheHit(string key, string source, TimeSpan duration, int size) { }
            public void RecordCacheMiss(string key) { }
            public void LogPerformanceWarning(string message) { }
            public void RecordMLOptimization(string originalQuery, string optimizedQuery, bool assumedCorrect, double confidence) { }
        }

        private static HttpResponse Ok(HttpRequest request)
            => new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, "{}", HttpStatusCode.OK);

        private static QobuzHttpClient CreateSut(RecordingPerformanceMonitor monitor)
        {
            var mockHttp = new Mock<IHttpClient>();
            mockHttp.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                    .ReturnsAsync((HttpRequest r) => Ok(r));

            return new QobuzHttpClient(
                mockHttp.Object,
                LogManager.GetLogger(nameof(QobuzHttpClientMetricKeyTests)),
                performanceMonitor: monitor,
                adaptiveRateLimiter: null,
                healthCache: null);
        }

        [Fact]
        public async Task ExecuteAsync_RecordApiCall_NeverReceivesSignedQueryValues()
        {
            // Arrange
            var monitor = new RecordingPerformanceMonitor();
            var sut = CreateSut(monitor);

            const string secretSig = "SIGSECRETVALUE";
            const string secretToken = "USERAUTHTOKENSECRET";
            const string appId = "APPID12345";
            var url = $"https://www.qobuz.com/api.json/0.2/track/getFileUrl?track_id=1&app_id={appId}&user_auth_token={secretToken}&request_ts=1700000001&request_sig={secretSig}";

            // Act
            await sut.ExecuteAsync(new HttpRequest(url));

            // Assert: the recorded metric key must not embed the signed/secret query values.
            var recorded = monitor.RecordedEndpoints.Should().ContainSingle().Which;
            recorded.Should().NotContain(secretSig, "request_sig must never be stored in metric names");
            recorded.Should().NotContain(secretToken, "user_auth_token must never be stored in metric names");
            recorded.Should().NotContain(appId, "app_id must never be stored in metric names");
        }

        [Fact]
        public async Task ExecuteAsync_RecordApiCall_UsesIdenticalKeyForSameEndpointAcrossSignatures()
        {
            // Arrange: same endpoint, different per-call signatures/timestamps.
            var monitor = new RecordingPerformanceMonitor();
            var sut = CreateSut(monitor);

            await sut.ExecuteAsync(new HttpRequest("https://www.qobuz.com/api.json/0.2/track/getFileUrl?track_id=1&request_sig=aaa&request_ts=1"));
            await sut.ExecuteAsync(new HttpRequest("https://www.qobuz.com/api.json/0.2/track/getFileUrl?track_id=2&request_sig=bbb&request_ts=2"));

            // Assert: ONE stable key, so metric cardinality is bounded per endpoint.
            monitor.RecordedEndpoints.Should().HaveCount(2, "every successful call records one metric");
            monitor.RecordedEndpoints.Distinct().Should().ContainSingle(
                "two calls to the same endpoint must share one metric key regardless of signature")
                .Which.Should().Be("www.qobuz.com:api.json",
                    "the coarse rate-limit key is the bounded, scrubbed per-endpoint bucket");
        }
    }
}
