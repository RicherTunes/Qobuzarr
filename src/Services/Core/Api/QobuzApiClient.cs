using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Common.Http;
using NzbDrone.Common.Cache;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Services.Core.Api;
using Lidarr.Plugin.Qobuzarr.Services.Interfaces;

namespace Lidarr.Plugin.Qobuzarr.Services.Core.Api
{
    /// <summary>
    /// Standard implementation of Qobuz API client with integrated rate limiting and response caching.
    /// This client provides production-ready API access with automatic rate limit management,
    /// intelligent response caching, and comprehensive error handling.
    /// </summary>
    /// <remarks>
    /// Features:
    /// - Adaptive rate limiting that adjusts based on API responses
    /// - Intelligent response caching with configurable TTL
    /// - Request deduplication to prevent duplicate API calls
    /// - Comprehensive metrics and performance monitoring
    /// - Automatic retry logic with exponential backoff
    /// - Thread-safe operation for concurrent requests
    /// 
    /// This client is optimized for high-throughput scenarios like batch operations,
    /// discography downloads, and search operations where rate limiting and caching
    /// provide significant performance improvements.
    /// </remarks>
    public class QobuzApiClient : QobuzApiClientBase, IQobuzApiClient
    {
        private readonly IAdaptiveRateLimiter _rateLimiter;
        private readonly ICacheManager _cacheManager;
        private readonly ICached<object> _responseCache;
        private readonly RequestDeduplicator _requestDeduplicator;
        
        // Configuration
        private readonly TimeSpan _defaultCacheTtl = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _searchCacheTtl = TimeSpan.FromMinutes(15);
        private readonly TimeSpan _metadataCacheTtl = TimeSpan.FromHours(1);

        public QobuzApiClient(
            IHttpClient httpClient,
            IAdaptiveRateLimiter rateLimiter,
            ICacheManager cacheManager,
            Logger logger) 
            : base(httpClient, logger)
        {
            _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
            _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
            
            _responseCache = _cacheManager.GetCache<object>(GetType(), "responses");
            _requestDeduplicator = new RequestDeduplicator();
            
            _logger.Info("QobuzApiClient initialized with rate limiting and caching");
        }

        #region IQobuzApiClient Implementation

        /// <summary>
        /// Executes a GET request to the specified Qobuz API endpoint with automatic rate limiting and caching.
        /// </summary>
        /// <typeparam name="T">The expected response type for JSON deserialization.</typeparam>
        /// <param name="endpoint">The API endpoint path relative to the base URL (e.g., "/album/search").</param>
        /// <param name="parameters">Optional query parameters to include in the request.</param>
        /// <returns>The deserialized response object of type T.</returns>
        /// <exception cref="QobuzApiException">Thrown when the API returns an error response or authentication fails.</exception>
        public async Task<T> GetAsync<T>(string endpoint, Dictionary<string, string>? parameters = null) where T : class
        {
            return await ExecuteGetAsync<T>(endpoint, parameters).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes a POST request to the specified Qobuz API endpoint with optional JSON payload.
        /// </summary>
        /// <typeparam name="T">The expected response type for JSON deserialization.</typeparam>
        /// <param name="endpoint">The API endpoint path relative to the base URL (e.g., "/user/login").</param>
        /// <param name="data">Optional request body data that will be serialized to JSON.</param>
        /// <returns>The deserialized response object of type T.</returns>
        /// <exception cref="QobuzApiException">Thrown when the API returns an error response or authentication fails.</exception>
        public async Task<T> PostAsync<T>(string endpoint, object? data = null) where T : class
        {
            return await ExecutePostAsync<T>(endpoint, data).ConfigureAwait(false);
        }

        #endregion

        #region Enhanced API Methods

        /// <summary>
        /// Gets the streaming URL for a track with automatic caching and rate limiting.
        /// </summary>
        public async Task<string> GetStreamingUrlAsync(string trackId, int formatId, CancellationToken cancellationToken = default)
        {
            _logger.Debug("Getting streaming URL for track {0} with format {1}", trackId, formatId);
            
            var parameters = new Dictionary<string, string>
            {
                ["track_id"] = trackId,
                ["format_id"] = formatId.ToString()
            };
            
            var streamingInfo = await GetAsync<QobuzStreamResponse>("track/getFileUrl", parameters);
            
            return streamingInfo?.Url;
        }

        /// <summary>
        /// Gets detailed metadata for a track with extended caching.
        /// </summary>
        public async Task<QobuzTrack> GetTrackMetadataAsync(string trackId, CancellationToken cancellationToken = default)
        {
            _logger.Debug("Getting metadata for track {0}", trackId);
            
            var parameters = new Dictionary<string, string>
            {
                ["track_id"] = trackId
            };
            
            return await GetAsync<QobuzTrack>("track/get", parameters);
        }

        /// <summary>
        /// Search for albums with intelligent result caching.
        /// </summary>
        public async Task<QobuzAlbumSearchResponse> SearchAlbumsAsync(string query, int limit = 50, int offset = 0, CancellationToken cancellationToken = default)
        {
            var parameters = new Dictionary<string, string>
            {
                ["query"] = query,
                ["limit"] = limit.ToString(),
                ["offset"] = offset.ToString()
            };

            return await GetAsync<QobuzAlbumSearchResponse>("album/search", parameters);
        }

        /// <summary>
        /// Get album details with extended metadata caching.
        /// </summary>
        public async Task<QobuzAlbum> GetAlbumAsync(string albumId, CancellationToken cancellationToken = default)
        {
            var parameters = new Dictionary<string, string>
            {
                ["album_id"] = albumId,
                ["extra"] = "tracks"
            };

            return await GetAsync<QobuzAlbum>("album/get", parameters);
        }

        #endregion

        #region Rate Limiting and Caching Overrides

        /// <summary>
        /// Pre-processes requests with rate limiting and cache checks.
        /// </summary>
        protected override async Task PreProcessRequestAsync(string method, string endpoint, Dictionary<string, string> parameters, CancellationToken cancellationToken = default)
        {
            // Apply rate limiting for all requests
            await _rateLimiter.WaitIfNeededAsync(endpoint, cancellationToken).ConfigureAwait(false);
            
            // For GET requests, check if we can serve from cache
            if (method == "GET")
            {
                var cacheKey = BuildCacheKey(endpoint, parameters);
                var cached = _responseCache.Find(cacheKey);
                if (cached != null)
                {
                    _logger.Debug("Cache hit for {0}", endpoint);
                    // Cache hit will be handled in the calling method
                }
            }
            
            // Apply request deduplication for expensive operations
            if (IsExpensiveOperation(endpoint))
            {
                var requestKey = BuildRequestKey(method, endpoint, parameters);
                await _requestDeduplicator.WaitForPendingRequest(requestKey, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Post-processes successful responses with caching and metrics.
        /// </summary>
        protected override async Task PostProcessResponseAsync<T>(string method, string endpoint, Dictionary<string, string> parameters, T result, CancellationToken cancellationToken = default)
        {
            // Record successful response for rate limiting
            _rateLimiter.RecordResponse(endpoint, new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK));
            
            // Cache successful GET responses
            if (method == "GET" && result != null)
            {
                var cacheKey = BuildCacheKey(endpoint, parameters);
                var ttl = GetCacheTtl(endpoint);
                _responseCache.Set(cacheKey, result, ttl);
                
                _logger.Trace("Response cached for {0} with TTL {1}", endpoint, ttl);
            }
            
            // Complete request deduplication
            if (IsExpensiveOperation(endpoint))
            {
                var requestKey = BuildRequestKey(method, endpoint, parameters);
                _requestDeduplicator.CompleteRequest(requestKey, result);
            }
        }

        /// <summary>
        /// Post-processes errors with rate limit tracking and request deduplication cleanup.
        /// </summary>
        protected override async Task PostProcessErrorAsync(string method, string endpoint, Exception exception, CancellationToken cancellationToken = default)
        {
            // Record error response for rate limiting
            var statusCode = ExtractStatusCodeFromException(exception);
            var errorResponse = new System.Net.Http.HttpResponseMessage(statusCode);
            _rateLimiter.RecordResponse(endpoint, errorResponse);
            
            // Clean up request deduplication on error
            if (IsExpensiveOperation(endpoint))
            {
                var requestKey = BuildRequestKey(method, endpoint, null);
                _requestDeduplicator.FailRequest(requestKey, exception);
            }
        }

        /// <summary>
        /// Enhanced cache checking for GET requests.
        /// </summary>
        protected override async Task<T> ExecuteRequestAsync<T>(string method, string endpoint, Dictionary<string, string>? parameters = null, object? data = null, CancellationToken cancellationToken = default)
        {
            // Check cache first for GET requests
            if (method == "GET")
            {
                var cacheKey = BuildCacheKey(endpoint, parameters);
                var cached = _responseCache.Find(cacheKey);
                if (cached is T cachedResult)
                {
                    _logger.Debug("Returning cached response for {0}", endpoint);
                    return cachedResult;
                }
            }
            
            // Proceed with regular request processing
            return await base.ExecuteRequestAsync<T>(method, endpoint, parameters, data, cancellationToken);
        }

        #endregion

        #region Cache Management

        /// <summary>
        /// Builds a cache key for the given endpoint and parameters.
        /// </summary>
        private string BuildCacheKey(string endpoint, Dictionary<string, string>? parameters)
        {
            var key = endpoint;
            if (parameters?.Count > 0)
            {
                var sortedParams = string.Join("&", 
                    parameters
                        .Where(kv => kv.Key != "user_auth_token") // Exclude auth token from cache key
                        .OrderBy(kv => kv.Key)
                        .Select(kv => $"{kv.Key}={kv.Value}"));
                key = $"{endpoint}?{sortedParams}";
            }
            return key;
        }

        /// <summary>
        /// Determines the appropriate cache TTL for an endpoint.
        /// </summary>
        private TimeSpan GetCacheTtl(string endpoint)
        {
            return endpoint switch
            {
                var e when e.Contains("/search") => _searchCacheTtl,
                var e when e.Contains("/get") => _metadataCacheTtl,
                var e when e.Contains("getFileUrl") => TimeSpan.FromMinutes(30), // Stream URLs expire quickly
                _ => _defaultCacheTtl
            };
        }

        /// <summary>
        /// Clears cached responses for a specific endpoint pattern.
        /// </summary>
        public void ClearCache(string endpointPattern = null)
        {
            if (string.IsNullOrEmpty(endpointPattern))
            {
                _responseCache.Clear();
                _logger.Info("All cached responses cleared");
            }
            else
            {
                // Note: This is a simplified implementation. In production, you might want
                // more sophisticated cache invalidation based on patterns
                _logger.Info("Cache clearing by pattern not fully implemented: {0}", endpointPattern);
            }
        }

        #endregion

        #region Request Deduplication

        /// <summary>
        /// Determines if an operation is expensive and should be deduplicated.
        /// </summary>
        private bool IsExpensiveOperation(string endpoint)
        {
            return endpoint.Contains("/search") || 
                   endpoint.Contains("/get") ||
                   endpoint.Contains("getFileUrl");
        }

        /// <summary>
        /// Builds a request key for deduplication.
        /// </summary>
        private string BuildRequestKey(string method, string endpoint, Dictionary<string, string>? parameters)
        {
            return BuildCacheKey(endpoint, parameters); // Reuse cache key logic
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Extracts HTTP status code from exception for rate limiting.
        /// </summary>
        private System.Net.HttpStatusCode ExtractStatusCodeFromException(Exception exception)
        {
            if (exception.Message.Contains("429") || exception.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            {
                return System.Net.HttpStatusCode.TooManyRequests;
            }
            else if (exception.Message.Contains("401") || exception.Message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
            {
                return System.Net.HttpStatusCode.Unauthorized;
            }
            else if (exception.Message.Contains("403") || exception.Message.Contains("forbidden", StringComparison.OrdinalIgnoreCase))
            {
                return System.Net.HttpStatusCode.Forbidden;
            }
            else if (exception.Message.Contains("404") || exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                return System.Net.HttpStatusCode.NotFound;
            }
            
            return System.Net.HttpStatusCode.InternalServerError;
        }

        #endregion

        #region Metrics and Diagnostics

        /// <summary>
        /// Gets current API client statistics.
        /// </summary>
        public ApiClientStats GetStats()
        {
            var rateLimitStats = _rateLimiter.GetStats();
            
            return new ApiClientStats
            {
                RateLimitStats = rateLimitStats,
                CacheHitRatio = CalculateCacheHitRatio(),
                ActiveRequests = _requestDeduplicator.GetActiveRequestCount(),
                TotalRequests = rateLimitStats.TotalRequests
            };
        }

        /// <summary>
        /// Calculates cache hit ratio (simplified implementation).
        /// </summary>
        private double CalculateCacheHitRatio()
        {
            // This is a simplified implementation
            // In production, you'd track cache hits/misses more precisely
            return 0.0; // Placeholder
        }

        #endregion
    }

    /// <summary>
    /// API client performance statistics.
    /// </summary>
    public class ApiClientStats
    {
        public RateLimitStats RateLimitStats { get; set; }
        public double CacheHitRatio { get; set; }
        public int ActiveRequests { get; set; }
        public long TotalRequests { get; set; }
    }

    /// <summary>
    /// Simple request deduplicator to prevent duplicate API calls.
    /// </summary>
    public class RequestDeduplicator
    {
        private readonly Dictionary<string, TaskCompletionSource<object>> _pendingRequests = new();
        private readonly object _lock = new object();

        public async Task WaitForPendingRequest(string requestKey, CancellationToken cancellationToken = default)
        {
            TaskCompletionSource<object>? tcs = null;
            
            lock (_lock)
            {
                if (_pendingRequests.TryGetValue(requestKey, out tcs))
                {
                    // Request is already in progress, wait for it
                }
                else
                {
                    // Start new request
                    tcs = new TaskCompletionSource<object>();
                    _pendingRequests[requestKey] = tcs;
                    return; // This is the primary request, proceed immediately
                }
            }
            
            // Wait for the pending request to complete
            if (tcs != null)
            {
                await tcs.Task.ConfigureAwait(false);
            }
        }

        public void CompleteRequest(string requestKey, object result)
        {
            lock (_lock)
            {
                if (_pendingRequests.TryGetValue(requestKey, out var tcs))
                {
                    _pendingRequests.Remove(requestKey);
                    tcs.SetResult(result);
                }
            }
        }

        public void FailRequest(string requestKey, Exception exception)
        {
            lock (_lock)
            {
                if (_pendingRequests.TryGetValue(requestKey, out var tcs))
                {
                    _pendingRequests.Remove(requestKey);
                    tcs.SetException(exception);
                }
            }
        }

        public int GetActiveRequestCount()
        {
            lock (_lock)
            {
                return _pendingRequests.Count;
            }
        }
    }
}