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
                // Execute with retry logic for transient failures
                var response = await SharedRetryUtilities.ExecuteWithRetryAsync(
                    () => ExecuteWithRateLimitHandling(request),
                    QobuzConstants.Api.MaxRetries,
                    1000,
                    $"HTTP request to {request.Url}")
                    .ConfigureAwait(false);

                stopwatch.Stop();
                
                // Record API call performance (not cached since this is direct HTTP)
                _performanceMonitor?.RecordApiCall(request.Url.ToString(), stopwatch.Elapsed, false);

                // Feed response into adaptive limiter if configured
                if (_adaptiveRateLimiter != null)
                {
                    var msg = new HttpResponseMessage(response.StatusCode);
                    _adaptiveRateLimiter.RecordResponse(QobuzarrConstants.ServiceName, endpoint, msg);
                }

                return response;
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
            var response = await _httpClient.ExecuteAsync(request).ConfigureAwait(false);

            // Handle rate limiting (429 Too Many Requests)
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                var retryAfterHeader = response.Headers.GetValues("Retry-After")?.FirstOrDefault();
                var retryAfter = TimeSpan.FromSeconds(QobuzConstants.Api.RequestTimeoutSeconds);
                
                if (retryAfterHeader != null && int.TryParse(retryAfterHeader, out var seconds))
                {
                    retryAfter = TimeSpan.FromSeconds(seconds);
                }

                _logger.Warn("Rate limited by API, waiting {0} seconds before retry", retryAfter.TotalSeconds);
                await Task.Delay(retryAfter).ConfigureAwait(false);

                // Throw to trigger retry logic
                throw new HttpRequestException($"Rate limited, retry after {retryAfter.TotalSeconds} seconds");
            }

            return response;
        }
    }
}
