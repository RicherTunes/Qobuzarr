using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Lidarr.Plugin.Common.Services.Http;
using Lidarr.Plugin.Common.Services.Performance;
using Lidarr.Plugin.Common.Observability;
using Lidarr.Plugin.Qobuzarr.Constants;
using Lidarr.Plugin.Common.Resilience;
using Lidarr.Plugin.Qobuzarr.API;
using SharedRetryUtilities = Lidarr.Plugin.Common.Utilities.RetryUtilities;

namespace Lidarr.Plugin.Qobuzarr.API.Http
{
    /// <summary>
    /// Implementation of pure HTTP communication with the Qobuz API.
    /// Handles rate limiting, retries, and basic HTTP operations without business logic.
    /// </summary>
    public class QobuzHttpClient : IQobuzHttpClient
    {
        private readonly IHttpClient _httpClient;
        private readonly IUniversalAdaptiveRateLimiter? _adaptiveRateLimiter;
        private readonly Logger _logger;
        private readonly IPerformanceMonitoringService? _performanceMonitor;
        private readonly BackendHealthCache _healthCache;
        private readonly object _fallbackLock = new object();
        private readonly System.Collections.Generic.Queue<DateTime> _fallbackRequestTimes = new System.Collections.Generic.Queue<DateTime>();
        private readonly TimeSpan _fallbackWindow = TimeSpan.FromMinutes(1);

        // Stable provider key for all Qobuz API calls into the BackendHealthCache.
        private const string BackendProvider = "qobuz:api";

        // Per-host concurrency gates — Lazy ensures the factory runs exactly once per key
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<SemaphoreSlim>> _hostGates = new();
        private static SemaphoreSlim GetHostGate(string? host, int maxConcurrencyPerHost)
        {
            host ??= "__unknown__";
            return _hostGates.GetOrAdd(host, _ => new Lazy<SemaphoreSlim>(
                () => new SemaphoreSlim(maxConcurrencyPerHost, maxConcurrencyPerHost))).Value;
        }

        public QobuzHttpClient(
            IHttpClient httpClient,
            Logger logger,
            IPerformanceMonitoringService? performanceMonitor = null,
            IUniversalAdaptiveRateLimiter? adaptiveRateLimiter = null,
            BackendHealthCache? healthCache = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _performanceMonitor = performanceMonitor;
            _adaptiveRateLimiter = adaptiveRateLimiter; // Optional: relies on host DI; falls back gracefully when null
            _healthCache = healthCache ?? BackendHealthCache.Shared;
        }

        public QobuzHttpClient(IHttpClient httpClient, Logger logger)
            : this(httpClient, logger, performanceMonitor: null, adaptiveRateLimiter: null, healthCache: null)
        {
        }

        /// <inheritdoc/>
        public async Task<HttpResponse> ExecuteAsync(HttpRequest request, CancellationToken cancellationToken = default)
        {
            // Apply adaptive rate limiting when available.
            // Key on a COARSE host+first-path-segment bucket (e.g. "www.qobuz.com:api.json"), NOT the
            // full URL. Every album/track URL carries distinct ids + signed query params, so keying on
            // the full URL gave each item its own bucket — each hit ~once, the adaptive limiter never
            // accumulated pressure, and global rate limiting was effectively disabled (429 storm / ban
            // risk). The coarse key must be IDENTICAL for WaitIfNeededAsync and every RecordResponse.
            var endpoint = BuildRateLimitKey(request);
            if (_adaptiveRateLimiter != null)
            {
                await _adaptiveRateLimiter.WaitIfNeededAsync(QobuzarrConstants.ServiceName, endpoint, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                // Minimal fallback throttling to avoid hammering the API when adaptive limiter is unavailable
                TimeSpan? wait = null;
                lock (_fallbackLock)
                {
                    var now = DateTime.UtcNow;
                    while (_fallbackRequestTimes.Count > 0 && now - _fallbackRequestTimes.Peek() > _fallbackWindow)
                    {
                        _fallbackRequestTimes.Dequeue();
                    }
                    if (_fallbackRequestTimes.Count >= QobuzConstants.Api.RateLimitPerMinute)
                    {
                        var oldest = _fallbackRequestTimes.Peek();
                        wait = _fallbackWindow - (now - oldest);
                        _fallbackRequestTimes.Dequeue();
                    }
                    _fallbackRequestTimes.Enqueue(now);
                }
                if (wait.HasValue && wait.Value > TimeSpan.Zero)
                {
                    await Task.Delay(wait.Value, cancellationToken).ConfigureAwait(false);
                }
            }

            var safeUrl = GetSafeUrlForLogging(request);
            _logger.Debug("Executing HTTP {0} request to {1}", request.Method, safeUrl);

            // BackendHealthCache fast-path: if a prior connection-class failure is still within
            // the grace window, fail immediately without burning the retry budget.
            var requestHost = request?.Url?.Host ?? string.Empty;
            if (_healthCache.IsKnownDown(BackendProvider, requestHost, out var knownDownReason))
            {
                _logger.Debug("BackendHealthCache: skipping HTTP call — {0}", knownDownReason);
                throw new QobuzApiException("Qobuz API backend is temporarily unreachable: " + knownDownReason, 0, "BackendDown");
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                // Enhanced retry with Retry-After + per-host gate + budget
                var maxRetries = QobuzConstants.Api.MaxRetries;
                var retryBudget = TimeSpan.FromSeconds(QobuzarrConstants.Defaults.RetryBudgetSeconds);
                var deadline = DateTime.UtcNow + retryBudget;
                var attempt = 0;
                var host = request?.Url?.Host ?? "__unknown__";
                var hostGate = GetHostGate(host, maxConcurrencyPerHost: QobuzarrConstants.Defaults.DefaultMaxConcurrencyPerHost);

                await hostGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    while (true)
                    {
                        attempt++;
                        HttpException? lastHttpException = null;
                        HttpResponse response;
                        try
                        {
                            response = await ExecuteWithRateLimitHandling(request).ConfigureAwait(false);
                        }
                        catch (HttpException ex)
                        {
                            // Preserve exception type for callers, but allow retry logic to run based on status code.
                            lastHttpException = ex;
                            response = ex.Response;
                        }

                        if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests && !response.HasHttpError)
                        {
                            stopwatch.Stop();
                            _performanceMonitor?.RecordApiCall(request.Url.ToString(), stopwatch.Elapsed, false);
                            if (_adaptiveRateLimiter != null)
                            {
                                using var msgOk = new HttpResponseMessage(response.StatusCode);
                                _adaptiveRateLimiter.RecordResponse(QobuzarrConstants.ServiceName, endpoint, msgOk);
                            }
                            // Successful response: clear any previous down-state for this host.
                            _healthCache.MarkUp(BackendProvider, requestHost);
                            return response;
                        }

                        // If not retryable or out of attempts, return immediately
                        var status = (int)response.StatusCode;
                        var retryable = status == 408 || status == 429 || (status >= 500 && status <= 599);
                        if (!retryable || attempt >= maxRetries)
                        {
                            stopwatch.Stop();
                            if (_adaptiveRateLimiter != null)
                            {
                                using var msgFail = new HttpResponseMessage(response.StatusCode);
                                _adaptiveRateLimiter.RecordResponse(QobuzarrConstants.ServiceName, endpoint, msgFail);
                            }
                            if (lastHttpException != null)
                            {
                                throw lastHttpException;
                            }

                            return response;
                        }

                        // Compute delay from Retry-After or exponential backoff with jitter
                        var delay = HttpResponseHelpers.ParseRetryAfter(response?.Headers) ??
                                    TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt))) + GetJitter();

                        // Respect retry budget
                        var now = DateTime.UtcNow;
                        if (now + delay > deadline)
                        {
                            _logger.Warn("Retry budget exceeded for {0}", safeUrl);
                            stopwatch.Stop();
                            if (_adaptiveRateLimiter != null)
                            {
                                using var msgBudget = new HttpResponseMessage(response.StatusCode);
                                _adaptiveRateLimiter.RecordResponse(QobuzarrConstants.ServiceName, endpoint, msgBudget);
                            }
                            return response;
                        }

                        _logger.Warn("Transient HTTP error {0} for {1}; delaying {2}ms before retry {3}/{4}",
                            status, safeUrl, (int)delay.TotalMilliseconds, attempt, maxRetries);
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    hostGate.Release();
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.Error(ex, "HTTP request failed for URL {0}", safeUrl);
                if (_adaptiveRateLimiter != null)
                {
                    using var msg = new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
                    _adaptiveRateLimiter.RecordResponse(QobuzarrConstants.ServiceName, endpoint, msg);
                }
                // Record connection-class failures (SocketException, DNS failure, connection refused)
                // so subsequent callers can fail-fast within the grace window instead of retrying.
                if (BackendHealthCache.IsConnectionClassFailure(ex))
                {
                    _healthCache.MarkDown(BackendProvider, requestHost, ex);
                    _logger.Warn("BackendHealthCache: marked {0} down for {1}s — {2}", requestHost, BackendHealthCache.DefaultGraceSeconds, ex.Message);
                }
                throw;
            }
        }

        /// <inheritdoc/>
        public HttpRequestBuilder BuildRequest(string url, string method = "GET")
        {
            var builder = new HttpRequestBuilder(url)
                // Don't set a custom User-Agent: Lidarr's IHttpClient enforces its own UA and may reject non-Lidarr values.
                .SetHeader("Accept", "application/json");

            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                builder.Post();
            }

            return builder;
        }

        private async Task<HttpResponse> ExecuteWithRateLimitHandling(HttpRequest request)
        {
            // Execute raw request and return the response.
            // Any retry/backoff handling (including 429 Retry-After) is performed by the caller loop
            // in ExecuteAsync to ensure there's a single, consistent backoff policy.
            var response = await _httpClient.ExecuteAsync(request).ConfigureAwait(false);
            return response;
        }

        private static TimeSpan GetJitter()
        {
            var ms = Random.Shared.Next(50, 250);
            return TimeSpan.FromMilliseconds(ms);
        }

        /// <summary>
        /// Builds the coarse, per-endpoint rate-limit bucket key (host + first path segment, e.g.
        /// "www.qobuz.com:api.json") via Common's <see cref="RateLimitHeaderUtilities"/>. Collapses
        /// the per-item album/track URLs (distinct ids + signed query params) into ONE stable bucket
        /// so the adaptive limiter accumulates pressure across all calls instead of seeing each URL
        /// exactly once.
        /// </summary>
        private static string BuildRateLimitKey(HttpRequest? request)
        {
            var url = request?.Url?.ToString();
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return "unknown";
            }

            return RateLimitHeaderUtilities.BuildHostFirstSegmentKey(uri);
        }

        private static string GetSafeUrlForLogging(HttpRequest? request)
        {
            var url = request?.Url?.ToString();
            if (string.IsNullOrWhiteSpace(url))
            {
                return "unknown";
            }

            // Wave 17F: unified on Common.Scrub.Url which delegates to LogRedactor.IsSensitiveParameter
            // for both exact-match and contains-rule recognition. The sentinel changes from
            // [redacted] to *** (Common's convention across observability surfaces).
            return Scrub.Url(url);
        }
    }
}
