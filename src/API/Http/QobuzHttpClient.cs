using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Utilities;

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

        public QobuzHttpClient(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rateLimiter = new RateLimiter(QobuzConstants.Api.RateLimitPerMinute, TimeSpan.FromMinutes(1));
        }

        /// <inheritdoc/>
        public async Task<HttpResponse> ExecuteAsync(HttpRequest request, CancellationToken cancellationToken = default)
        {
            // Apply rate limiting
            await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

            _logger.Debug("Executing HTTP {0} request to {1}", request.Method, request.Url);

            try
            {
                // Execute with retry logic for transient failures
                var response = await RetryUtilities.ExecuteWithRetryAsync(
                    () => ExecuteWithRateLimitHandling(request),
                    QobuzConstants.Api.MaxRetries,
                    1000,
                    $"HTTP request to {request.Url}")
                    .ConfigureAwait(false);

                return response;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "HTTP request failed for URL {0}", request.Url);
                throw;
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para><b>Parameter Validation:</b></para>
        /// <list type="bullet">
        /// <item><b>url:</b> Must be a valid absolute or relative URL. Null or empty values will throw ArgumentException.</item>
        /// <item><b>method:</b> Supports GET (default) and POST. Case-insensitive. Other HTTP methods can be set via the returned builder.</item>
        /// </list>
        /// 
        /// <para><b>Supported HTTP Methods:</b></para>
        /// While this method defaults to GET and has special handling for POST, the returned HttpRequestBuilder
        /// supports all standard HTTP methods (GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS) through its fluent API.
        /// 
        /// <para><b>Usage Examples:</b></para>
        /// <code>
        /// // Simple GET request
        /// var builder = BuildRequest("https://api.qobuz.com/album/search");
        /// 
        /// // POST request
        /// var builder = BuildRequest("https://api.qobuz.com/login", "POST");
        /// 
        /// // Custom method via builder
        /// var builder = BuildRequest(url).Delete();
        /// </code>
        /// </remarks>
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

        /// <summary>
        /// Executes an HTTP request with intelligent rate limit handling and recovery.
        /// </summary>
        /// <remarks>
        /// <para><b>Error Recovery Strategy:</b></para>
        /// This method implements a multi-layered approach to handle rate limiting and transient failures:
        /// 
        /// <list type="number">
        /// <item><b>Rate Limit Detection (HTTP 429):</b> Checks for "Retry-After" header and honors the server's requested delay</item>
        /// <item><b>Adaptive Backoff:</b> If no Retry-After header, uses exponential backoff starting at RequestTimeoutSeconds</item>
        /// <item><b>Exception Propagation:</b> Throws HttpRequestException to trigger upstream retry logic with exponential backoff</item>
        /// <item><b>Circuit Breaking:</b> After MaxRetries attempts, allows exception to bubble up for circuit breaker handling</item>
        /// </list>
        /// 
        /// <para><b>Retry Strategy Details:</b></para>
        /// <code>
        /// Attempt 1: Immediate execution
        /// Attempt 2: Wait 1 second (or Retry-After value)
        /// Attempt 3: Wait 2 seconds (exponential backoff)
        /// Attempt 4: Wait 4 seconds (capped at max timeout)
        /// </code>
        /// 
        /// <para><b>Common Failure Scenarios and Recovery:</b></para>
        /// <list type="table">
        /// <listheader>
        ///   <term>Error</term>
        ///   <description>Recovery Action</description>
        /// </listheader>
        /// <item>
        ///   <term>429 with Retry-After</term>
        ///   <description>Wait exact duration specified by server, then retry</description>
        /// </item>
        /// <item>
        ///   <term>429 without Retry-After</term>
        ///   <description>Wait default timeout (30s), then retry with exponential backoff</description>
        /// </item>
        /// <item>
        ///   <term>503 Service Unavailable</term>
        ///   <description>Handled by RetryUtilities with exponential backoff</description>
        /// </item>
        /// <item>
        ///   <term>Network timeout</term>
        ///   <description>Handled by RetryUtilities, increases timeout on retry</description>
        /// </item>
        /// </list>
        /// 
        /// <para><b>Integration with Rate Limiter:</b></para>
        /// Works in conjunction with the proactive RateLimiter to prevent hitting server limits.
        /// If server limits are still hit despite local rate limiting, this provides graceful recovery.
        /// </remarks>
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