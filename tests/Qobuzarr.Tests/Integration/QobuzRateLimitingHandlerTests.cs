using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Services.Performance;
using Lidarr.Plugin.Qobuzarr.Integration.Bridge;
using Xunit;

namespace Qobuzarr.Tests.Integration;

/// <summary>
/// Characterisation tests for QobuzRateLimitingHandler — captures the
/// observable behaviour before a refactor that swaps the inline
/// ResolveRetryAfter / BuildEndpointKey helpers for common's
/// RateLimitHeaderUtilities.
///
/// The handler:
///   - Gates each request via IUniversalAdaptiveRateLimiter.WaitIfNeededAsync,
///     keyed by "host:firstPathSegment" so distinct Qobuz endpoints get
///     independent budgets.
///   - Feeds each response to RecordResponse so the limiter can adapt.
///   - Honours Retry-After on 429s before returning to the caller.
/// </summary>
public sealed class QobuzRateLimitingHandlerTests
{
    [Fact]
    public async Task SendAsync_RecordsEndpointKey_AsHostColonFirstSegment()
    {
        var limiter = new SpyRateLimiter();
        var inner = new StubInner(HttpStatusCode.OK);
        using var handler = new QobuzRateLimitingHandler(limiter) { InnerHandler = inner };
        using var client = new HttpClient(handler);

        using var resp = await client.GetAsync("https://www.qobuz.com/api.json/0.2/album/get");

        Assert.Equal("Qobuz", limiter.LastWaitService);
        Assert.Equal("www.qobuz.com:api.json", limiter.LastWaitEndpoint);
        Assert.Equal("Qobuz", limiter.LastRecordService);
        Assert.Equal("www.qobuz.com:api.json", limiter.LastRecordEndpoint);
    }

    [Fact]
    public async Task SendAsync_429WithRetryAfter_HonoursDeltaBeforeReturning()
    {
        var limiter = new SpyRateLimiter();
        var inner = new StubInner(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromMilliseconds(50));
        using var handler = new QobuzRateLimitingHandler(limiter) { InnerHandler = inner };
        using var client = new HttpClient(handler);

        var start = DateTimeOffset.UtcNow;
        using var resp = await client.GetAsync("https://www.qobuz.com/api.json/0.2/track/getFileUrl");
        var elapsed = DateTimeOffset.UtcNow - start;

        Assert.Equal(HttpStatusCode.TooManyRequests, resp.StatusCode);
        // Honour at least ~40ms (allow scheduler jitter); the assertion is "did we wait at all".
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(40),
            $"expected handler to honour Retry-After ≥ 40ms, got {elapsed.TotalMilliseconds:0} ms");
    }

    [Fact]
    public async Task SendAsync_UriWithNoPath_KeysAsHostColonEmpty()
    {
        // Defensive: degenerate URI (just a host) should still produce a stable
        // key, not throw.
        var limiter = new SpyRateLimiter();
        var inner = new StubInner(HttpStatusCode.OK);
        using var handler = new QobuzRateLimitingHandler(limiter) { InnerHandler = inner };
        using var client = new HttpClient(handler);

        using var resp = await client.GetAsync("https://streaming.qobuz.com/");

        Assert.Equal("streaming.qobuz.com:", limiter.LastWaitEndpoint);
    }

    private sealed class SpyRateLimiter : IUniversalAdaptiveRateLimiter
    {
        public string? LastWaitService;
        public string? LastWaitEndpoint;
        public string? LastRecordService;
        public string? LastRecordEndpoint;

        public Task<bool> WaitIfNeededAsync(string service, string endpoint, CancellationToken cancellationToken = default)
        {
            LastWaitService = service;
            LastWaitEndpoint = endpoint;
            return Task.FromResult(true);
        }

        public void RecordResponse(string service, string endpoint, HttpResponseMessage response)
        {
            LastRecordService = service;
            LastRecordEndpoint = endpoint;
        }

        public int GetCurrentLimit(string service, string endpoint) => 1000;
        public ServiceRateLimitStats GetServiceStats(string service) => new() { ServiceName = service };
        public GlobalRateLimitStats GetGlobalStats() => new();
        public void Dispose() { }
    }

    private sealed class StubInner : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly TimeSpan? _retryAfter;

        public StubInner(HttpStatusCode status, TimeSpan? retryAfter = null)
        {
            _status = status;
            _retryAfter = retryAfter;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var resp = new HttpResponseMessage(_status);
            if (_retryAfter is { } delta)
            {
                resp.Headers.RetryAfter = new RetryConditionHeaderValue(delta);
            }
            return Task.FromResult(resp);
        }
    }
}
