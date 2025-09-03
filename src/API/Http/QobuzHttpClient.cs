using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Lidarr.Plugin.Common.Services.Performance;
using Lidarr.Plugin.Qobuzarr.Constants;
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
        private readonly object _fallbackLock = new object();
        private readonly System.Collections.Generic.Queue<DateTime> _fallbackRequestTimes = new System.Collections.Generic.Queue<DateTime>();
        private readonly TimeSpan _fallbackWindow = TimeSpan.FromMinutes(1);

        // Per-host concurrency gates
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _hostGates = new();
        private static SemaphoreSlim GetHostGate(string? host, int maxConcurrencyPerHost)
        {
            host ??= "__unknown__";
            return _hostGates.GetOrAdd(host, _ => new SemaphoreSlim(maxConcurrencyPerHost, maxConcurrencyPerHost));
        }

        public QobuzHttpClient(
            IHttpClient httpClient,
            Logger logger,
            IPerformanceMonitoringService? performanceMonitor = null,
            IUniversalAdaptiveRateLimiter? adaptiveRateLimiter = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _performanceMonitor = performanceMonitor;
            _adaptiveRateLimiter = adaptiveRateLimiter; // Optional: relies on host DI; falls back gracefully when null
        }

        /// <inheritdoc/>
        public async Task<HttpResponse> ExecuteAsync(HttpRequest request, CancellationToken cancellationToken = default)
        {
            // Apply adaptive rate limiting when available
            var endpoint = request?.Url?.ToString() ?? "unknown";
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

            _logger.Debug("Executing HTTP {0} request to {1}", request.Method, request.Url);

            var stopwatch = Stopwatch.StartNew();
            try
            {
                // Enhanced retry with Retry-After + per-host gate + budget
                var maxRetries = QobuzConstants.Api.MaxRetries;
                var retryBudget = TimeSpan.FromSeconds(60);
                var deadline = DateTime.UtcNow + retryBudget;
                var attempt = 0;
                var host = request?.Url?.Host ?? "__unknown__";
                var hostGate = GetHostGate(host, maxConcurrencyPerHost: 6);

                await hostGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    while (true)
                    {
                        attempt++;
                        var response = await ExecuteWithRateLimitHandling(request).ConfigureAwait(false);

                        if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests && !response.HasHttpError)
                        {
                            stopwatch.Stop();
                            _performanceMonitor?.RecordApiCall(request.Url.ToString(), stopwatch.Elapsed, false);
                            if (_adaptiveRateLimiter != null)
                            {
                                var msgOk = new HttpResponseMessage(response.StatusCode);
                                _adaptiveRateLimiter.RecordResponse(QobuzarrConstants.ServiceName, endpoint, msgOk);
                            }
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
                                var msgFail = new HttpResponseMessage(response.StatusCode);
                                _adaptiveRateLimiter.RecordResponse(QobuzarrConstants.ServiceName, endpoint, msgFail);
                            }
                            return response;
                        }

                        // Compute delay from Retry-After or exponential backoff with jitter
                        var delay = GetRetryAfterDelay(response) ??
                                    TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt))) + GetJitter();

                        // Respect retry budget
                        var now = DateTime.UtcNow;
                        if (now + delay > deadline)
                        {
                            _logger.Warn("Retry budget exceeded for {0}", request.Url);
                            stopwatch.Stop();
                            if (_adaptiveRateLimiter != null)
                            {
                                var msgBudget = new HttpResponseMessage(response.StatusCode);
                                _adaptiveRateLimiter.RecordResponse(QobuzarrConstants.ServiceName, endpoint, msgBudget);
                            }
                            return response;
                        }

                        _logger.Warn("Transient HTTP error {0} for {1}; delaying {2}ms before retry {3}/{4}",
                            status, request.Url, (int)delay.TotalMilliseconds, attempt, maxRetries);
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
                _logger.Error(ex, "HTTP request failed for URL {0}", request.Url);
                if (_adaptiveRateLimiter != null)
                {
                    var msg = new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
                    _adaptiveRateLimiter.RecordResponse(QobuzarrConstants.ServiceName, endpoint, msg);
                }
                throw;
            }
        }

        /// <inheritdoc/>
        public HttpRequestBuilder BuildRequest(string url, string method = "GET")
        {
            var builder = new HttpRequestBuilder(url)
                .SetHeader("User-Agent", QobuzConstants.Api.UserAgent);

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

        private static TimeSpan? GetRetryAfterDelay(HttpResponse response)
        {
            try
            {
                var retryAfterHeader = response.Headers.GetValues("Retry-After")?.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(retryAfterHeader)) return null;

                if (int.TryParse(retryAfterHeader, out var seconds))
                {
                    return TimeSpan.FromSeconds(Math.Max(0, seconds));
                }

                if (DateTimeOffset.TryParse(retryAfterHeader, out var when))
                {
                    var delta = when - DateTimeOffset.UtcNow;
                    if (delta > TimeSpan.Zero) return delta;
                }
            }
            catch
            {
                // ignore parse issues
            }
            return null;
        }

        private static TimeSpan GetJitter()
        {
            var ms = Random.Shared.Next(50, 250);
            return TimeSpan.FromMilliseconds(ms);
        }
    }
}
