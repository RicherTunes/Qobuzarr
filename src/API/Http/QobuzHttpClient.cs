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
        private readonly RateLimiter _rateLimiter;
        private readonly Logger _logger;
        private readonly IPerformanceMonitoringService? _performanceMonitor;

        public QobuzHttpClient(IHttpClient httpClient, Logger logger, IPerformanceMonitoringService? performanceMonitor = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _performanceMonitor = performanceMonitor;
            _rateLimiter = new RateLimiter(QobuzConstants.Api.RateLimitPerMinute, TimeSpan.FromMinutes(1));
        }

        /// <inheritdoc/>
        public async Task<HttpResponse> ExecuteAsync(HttpRequest request, CancellationToken cancellationToken = default)
        {
            // Apply rate limiting
            await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

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

                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.Error(ex, "HTTP request failed for URL {0}", request.Url);
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
