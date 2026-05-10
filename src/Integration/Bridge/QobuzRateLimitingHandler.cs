using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Services.Performance;
using Microsoft.Extensions.Logging;

namespace Lidarr.Plugin.Qobuzarr.Integration.Bridge
{
    /// <summary>
    /// HttpClient delegating handler that gates every Qobuz egress through
    /// <see cref="IUniversalAdaptiveRateLimiter"/>.
    ///
    /// Background: prior to this handler, the bridge-side <see cref="BridgeQobuzApiClient"/>
    /// used a raw <see cref="HttpClient"/> with no rate limit / no Retry-After honoring. That
    /// reproduces the same 429-storm class of bug recently fixed in tidalarr — Lidarr fans
    /// out searches per artist, the bridge issues unrate-limited HTTP, qobuz.com responds
    /// with 429s. (Note: the Lidarr-native QobuzApiClient path goes through
    /// <see cref="API.Http.QobuzHttpClient"/> which already calls the limiter directly. This
    /// handler closes the gap on the bridge path.)
    ///
    /// Per-host endpoint key is derived from request URI host + first path segment so
    /// distinct Qobuz endpoints (search, album, track) get independent budgets.
    /// </summary>
    public sealed class QobuzRateLimitingHandler : DelegatingHandler
    {
        private const string Service = "Qobuz";
        private readonly IUniversalAdaptiveRateLimiter _rateLimiter;
        private readonly ILogger<QobuzRateLimitingHandler>? _logger;

        public QobuzRateLimitingHandler(IUniversalAdaptiveRateLimiter rateLimiter, ILogger<QobuzRateLimitingHandler>? logger = null)
        {
            _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
            _logger = logger;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string endpointKey = BuildEndpointKey(request);

            try
            {
                await _rateLimiter.WaitIfNeededAsync(Service, endpointKey, cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) { /* limiter shut down — best-effort */ }

            HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            try
            {
                _rateLimiter.RecordResponse(Service, endpointKey, response);
            }
            catch (ObjectDisposedException) { /* limiter shut down */ }

            // Honor Retry-After on 429 so the caller's retry policy doesn't burn the bucket.
            if (response.StatusCode == HttpStatusCode.TooManyRequests && response.Headers.RetryAfter is { } retryAfter)
            {
                TimeSpan delay = ResolveRetryAfter(retryAfter);
                if (delay > TimeSpan.Zero)
                {
                    _logger?.LogWarning("Qobuz returned 429 for {Endpoint}; honoring Retry-After of {Delay}", endpointKey, delay);
                    try
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { /* caller cancelled */ }
                }
            }

            return response;
        }

        private static string BuildEndpointKey(HttpRequestMessage request)
        {
            Uri? uri = request.RequestUri;
            if (uri is null) return "unknown";
            string host = uri.Host;
            string firstSeg = uri.Segments.Length > 1 ? uri.Segments[1].TrimEnd('/') : string.Empty;
            return $"{host}:{firstSeg}";
        }

        private static TimeSpan ResolveRetryAfter(RetryConditionHeaderValue retryAfter)
        {
            if (retryAfter.Delta is { } delta) return delta;
            if (retryAfter.Date is { } date)
            {
                TimeSpan untilDate = date - DateTimeOffset.UtcNow;
                return untilDate > TimeSpan.Zero ? untilDate : TimeSpan.Zero;
            }
            return TimeSpan.Zero;
        }
    }
}
