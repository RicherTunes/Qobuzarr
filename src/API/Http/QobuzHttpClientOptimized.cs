using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Common.Http;
using NLog;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Common.Services.Http;
using Lidarr.Plugin.Common.Utilities;

namespace Lidarr.Plugin.Qobuzarr.API.Http
{
    /// <summary>
    /// Optimized HTTP client for Qobuz API using shared library components
    /// Demonstrates integration with StreamingApiRequestBuilder and RetryUtilities
    /// </summary>
    public class QobuzHttpClientOptimized : IQobuzHttpClient
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly IPerformanceMonitoringService? _performanceMonitor;
        private readonly StreamingApiRequestBuilder _requestBuilder;

        public QobuzHttpClientOptimized(
            IHttpClient httpClient, 
            Logger logger, 
            IPerformanceMonitoringService? performanceMonitor = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _performanceMonitor = performanceMonitor;
            _requestBuilder = new StreamingApiRequestBuilder(QobuzConstants.Api.BaseUrl);
        }

        /// <inheritdoc/>
        public async Task<HttpResponse> ExecuteAsync(HttpRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                // ENHANCED: Use shared library retry utilities
                var response = await RetryUtilities.ExecuteWithRetryAsync(
                    () => _httpClient.ExecuteAsync(request),
                    QobuzConstants.Api.MaxRetries,
                    1000,
                    $"HTTP request to {request.Url}")
                    .ConfigureAwait(false);

                // Log performance metrics if available
                _performanceMonitor?.RecordApiCall(
                    request.Url.ToString(), 
                    TimeSpan.Zero,  // Duration not measured here
                    false);

                return response;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "HTTP request failed for URL: {Url}", request.Url);
                // HTTP errors are already logged via the logger
                throw;
            }
        }

        /// <summary>
        /// ENHANCED: Create HTTP requests using shared library StreamingApiRequestBuilder
        /// Provides fluent API with common streaming service patterns
        /// </summary>
        public HttpRequest BuildQobuzRequest(string endpoint, Dictionary<string, string>? queryParams = null)
        {
            var builder = new StreamingApiRequestBuilder(QobuzConstants.Api.BaseUrl)
                .Endpoint(endpoint)
                .Header("User-Agent", QobuzConstants.Api.UserAgent)
                .WithStreamingDefaults("Qobuzarr/1.0");

            // Add query parameters
            if (queryParams != null)
            {
                foreach (var param in queryParams)
                {
                    builder.Query(param.Key, param.Value);
                }
            }

            // Convert to NzbDrone HttpRequest
            var streamingRequest = builder.Build();
            return ConvertToNzbDroneRequest(streamingRequest);
        }

        /// <summary>
        /// ENHANCED: Build authenticated Qobuz API request
        /// </summary>
        public HttpRequest BuildAuthenticatedRequest(string endpoint, string authToken, Dictionary<string, string>? queryParams = null)
        {
            var builder = new StreamingApiRequestBuilder(QobuzConstants.Api.BaseUrl)
                .Endpoint(endpoint)
                .Header("User-Agent", QobuzConstants.Api.UserAgent)
                .Header("X-User-Auth-Token", authToken)
                .WithStreamingDefaults("Qobuzarr/1.0");

            // Add query parameters
            if (queryParams != null)
            {
                foreach (var param in queryParams)
                {
                    builder.Query(param.Key, param.Value);
                }
            }

            var streamingRequest = builder.Build();
            return ConvertToNzbDroneRequest(streamingRequest);
        }

        /// <inheritdoc/>
        public HttpRequestBuilder BuildRequest(string url, string method = "GET")
        {
            // Fallback to traditional approach for backward compatibility
            return new HttpRequestBuilder(url)
                .SetHeader("User-Agent", QobuzConstants.Api.UserAgent);
        }

        private NzbDrone.Common.Http.HttpRequest ConvertToNzbDroneRequest(HttpRequestMessage streamingRequest)
        {
            // Convert the shared library's HttpRequestMessage to NzbDrone's HttpRequest
            var builder = new HttpRequestBuilder(streamingRequest.RequestUri?.ToString())
                .SetHeader("User-Agent", QobuzConstants.Api.UserAgent);

            // Copy headers from StreamingApiRequestBuilder
            foreach (var header in streamingRequest.Headers)
            {
                foreach (var value in header.Value)
                {
                    builder.SetHeader(header.Key, value);
                }
            }

            // Build the request - NzbDrone will handle the method internally
            return builder.Build();
        }

        public void Dispose()
        {
            // IHttpClient doesn't implement IDisposable in NzbDrone
            // No cleanup needed
        }
    }
}