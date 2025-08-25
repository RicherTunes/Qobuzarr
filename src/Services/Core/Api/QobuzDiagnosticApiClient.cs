using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Common.Http;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Services.Core.Api;
using Lidarr.Plugin.Qobuzarr.Services.Interfaces;

namespace Lidarr.Plugin.Qobuzarr.Services.Core.Api
{
    /// <summary>
    /// Diagnostic implementation of Qobuz API client without rate limiting or caching.
    /// This client is designed for troubleshooting, testing, and diagnostic purposes
    /// where you need raw, unthrottled access to the Qobuz API.
    /// </summary>
    /// <remarks>
    /// Use Cases:
    /// - API connectivity testing and health checks
    /// - Performance benchmarking and latency measurement
    /// - Debugging authentication and request signing issues
    /// - Testing API endpoints without interference from rate limiting
    /// - Emergency data recovery when rate limits are too restrictive
    /// 
    /// WARNING: This client bypasses all rate limiting protection. Use carefully
    /// to avoid hitting Qobuz API rate limits or getting your application blocked.
    /// Only use this client for diagnostic purposes or when explicitly needed.
    /// 
    /// Features:
    /// - No rate limiting - direct API access
    /// - No response caching - always fresh data
    /// - Detailed timing and performance metrics
    /// - Enhanced logging for troubleshooting
    /// - Request/response debugging capabilities
    /// - API health monitoring and connectivity testing
    /// </remarks>
    public class QobuzDiagnosticApiClient : QobuzApiClientBase, IQobuzApiClient
    {
        private readonly Dictionary<string, DiagnosticMetrics> _endpointMetrics;
        private readonly object _metricsLock = new object();
        private long _totalRequests = 0;
        private long _totalErrors = 0;

        public QobuzDiagnosticApiClient(IHttpClient httpClient, Logger logger) 
            : base(httpClient, logger)
        {
            _endpointMetrics = new Dictionary<string, DiagnosticMetrics>();
            
            _logger.Warn("🚨 DIAGNOSTIC API CLIENT: Rate limiting and caching are DISABLED. Use for testing only!");
        }

        #region IQobuzApiClient Implementation

        /// <summary>
        /// Executes a GET request with full diagnostic tracking but no rate limiting.
        /// </summary>
        /// <typeparam name="T">The expected response type for JSON deserialization.</typeparam>
        /// <param name="endpoint">The API endpoint path relative to the base URL (e.g., "/album/search").</param>
        /// <param name="parameters">Optional query parameters to include in the request.</param>
        /// <returns>The deserialized response object of type T.</returns>
        /// <exception cref="QobuzApiException">Thrown when the API returns an error response or authentication fails.</exception>
        public async Task<T> GetAsync<T>(string endpoint, Dictionary<string, string>? parameters = null) where T : class
        {
            return await ExecuteWithDiagnostics(() => ExecuteGetAsync<T>(endpoint, parameters), "GET", endpoint).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes a POST request with full diagnostic tracking but no rate limiting.
        /// </summary>
        /// <typeparam name="T">The expected response type for JSON deserialization.</typeparam>
        /// <param name="endpoint">The API endpoint path relative to the base URL (e.g., "/user/login").</param>
        /// <param name="data">Optional request body data that will be serialized to JSON.</param>
        /// <returns>The deserialized response object of type T.</returns>
        /// <exception cref="QobuzApiException">Thrown when the API returns an error response or authentication fails.</exception>
        public async Task<T> PostAsync<T>(string endpoint, object? data = null) where T : class
        {
            return await ExecuteWithDiagnostics(() => ExecutePostAsync<T>(endpoint, data), "POST", endpoint).ConfigureAwait(false);
        }

        #endregion

        #region Diagnostic-Specific Methods

        /// <summary>
        /// Tests API connectivity and authentication without making actual content requests.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Connectivity test results</returns>
        public async Task<ApiConnectivityTestResult> TestConnectivityAsync(CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new ApiConnectivityTestResult
            {
                StartTime = DateTime.UtcNow,
                TestEndpoint = "/user/login"
            };

            try
            {
                _logger.Info("🔍 DIAGNOSTIC: Starting API connectivity test");

                // Test basic connectivity
                result.ConnectivityLatency = await MeasureConnectivityLatency(cancellationToken).ConfigureAwait(false);
                result.IsConnectable = result.ConnectivityLatency.HasValue;

                if (result.IsConnectable)
                {
                    // Test authentication if session is available
                    if (HasValidSession())
                    {
                        result.AuthenticationLatency = await MeasureAuthenticationLatency(cancellationToken).ConfigureAwait(false);
                        result.IsAuthenticated = result.AuthenticationLatency.HasValue;
                    }
                    else
                    {
                        _logger.Info("🔍 DIAGNOSTIC: No valid session - skipping authentication test");
                        result.IsAuthenticated = false;
                    }
                }

                result.Success = result.IsConnectable;
                _logger.Info("✅ DIAGNOSTIC: Connectivity test completed - Connectable: {0}, Authenticated: {1}", 
                    result.IsConnectable, result.IsAuthenticated);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                result.Exception = ex;
                _logger.Error(ex, "❌ DIAGNOSTIC: Connectivity test failed");
            }
            finally
            {
                stopwatch.Stop();
                result.TotalDuration = stopwatch.Elapsed;
                result.EndTime = DateTime.UtcNow;
            }

            return result;
        }

        /// <summary>
        /// Performs a comprehensive API health check across multiple endpoints.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Health check results</returns>
        public async Task<ApiHealthCheckResult> PerformHealthCheckAsync(CancellationToken cancellationToken = default)
        {
            var result = new ApiHealthCheckResult
            {
                StartTime = DateTime.UtcNow
            };

            var endpoints = new[]
            {
                "/album/search",
                "/track/search", 
                "/artist/search"
            };

            _logger.Info("🏥 DIAGNOSTIC: Starting comprehensive health check for {0} endpoints", endpoints.Length);

            foreach (var endpoint in endpoints)
            {
                var endpointResult = new EndpointHealthResult
                {
                    Endpoint = endpoint,
                    StartTime = DateTime.UtcNow
                };

                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    
                    // Make a minimal test request to each endpoint
                    var parameters = new Dictionary<string, string>
                    {
                        ["query"] = "test",
                        ["limit"] = "1"
                    };

                    await ExecuteGetAsync<object>(endpoint, parameters, cancellationToken).ConfigureAwait(false);
                    
                    stopwatch.Stop();
                    endpointResult.ResponseTime = stopwatch.Elapsed;
                    endpointResult.Success = true;
                    endpointResult.StatusCode = 200;
                }
                catch (Exception ex)
                {
                    endpointResult.Success = false;
                    endpointResult.Error = ex.Message;
                    endpointResult.Exception = ex;
                    
                    // Try to extract status code from exception
                    if (ex.Message.Contains("401"))
                        endpointResult.StatusCode = 401;
                    else if (ex.Message.Contains("403"))
                        endpointResult.StatusCode = 403;
                    else if (ex.Message.Contains("404"))
                        endpointResult.StatusCode = 404;
                    else if (ex.Message.Contains("429"))
                        endpointResult.StatusCode = 429;
                    else if (ex.Message.Contains("500"))
                        endpointResult.StatusCode = 500;
                    else
                        endpointResult.StatusCode = 0; // Unknown
                }
                finally
                {
                    endpointResult.EndTime = DateTime.UtcNow;
                }

                result.EndpointResults.Add(endpointResult);
                
                _logger.Debug("🏥 ENDPOINT CHECK: {0} - Success: {1}, Time: {2:F0}ms", 
                    endpoint, endpointResult.Success, endpointResult.ResponseTime?.TotalMilliseconds ?? -1);
            }

            result.EndTime = DateTime.UtcNow;
            result.Success = result.EndpointResults.All(r => r.Success);
            result.AverageResponseTime = result.EndpointResults
                .Where(r => r.ResponseTime.HasValue)
                .Select(r => r.ResponseTime.Value)
                .DefaultIfEmpty(TimeSpan.Zero)
                .Average(ts => ts.TotalMilliseconds);

            _logger.Info("✅ DIAGNOSTIC: Health check completed - Success: {0}, Avg Response: {1:F0}ms", 
                result.Success, result.AverageResponseTime);

            return result;
        }

        /// <summary>
        /// Gets detailed diagnostic metrics for all endpoints.
        /// </summary>
        /// <returns>Comprehensive diagnostic metrics</returns>
        public DiagnosticReport GetDiagnosticReport()
        {
            lock (_metricsLock)
            {
                var report = new DiagnosticReport
                {
                    GeneratedAt = DateTime.UtcNow,
                    TotalRequests = _totalRequests,
                    TotalErrors = _totalErrors,
                    ErrorRate = _totalRequests > 0 ? (double)_totalErrors / _totalRequests : 0,
                    EndpointMetrics = new Dictionary<string, DiagnosticMetrics>(_endpointMetrics)
                };

                return report;
            }
        }

        #endregion

        #region Enhanced Diagnostic Processing

        /// <summary>
        /// Executes a request with comprehensive diagnostic tracking.
        /// </summary>
        private async Task<T> ExecuteWithDiagnostics<T>(Func<Task<T>> operation, string method, string endpoint)
        {
            var stopwatch = Stopwatch.StartNew();
            var startTime = DateTime.UtcNow;

            Interlocked.Increment(ref _totalRequests);

            try
            {
                _logger.Debug("🔬 DIAGNOSTIC REQUEST: {0} {1} starting", method, endpoint);

                var result = await operation().ConfigureAwait(false);

                stopwatch.Stop();
                RecordSuccessMetrics(endpoint, stopwatch.Elapsed, startTime);

                _logger.Debug("✅ DIAGNOSTIC SUCCESS: {0} {1} completed in {2:F0}ms", 
                    method, endpoint, stopwatch.Elapsed.TotalMilliseconds);

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Interlocked.Increment(ref _totalErrors);
                RecordErrorMetrics(endpoint, stopwatch.Elapsed, startTime, ex);

                _logger.Error("❌ DIAGNOSTIC ERROR: {0} {1} failed after {2:F0}ms - {3}", 
                    method, endpoint, stopwatch.Elapsed.TotalMilliseconds, ex.Message);

                throw;
            }
        }

        /// <summary>
        /// Records success metrics for an endpoint.
        /// </summary>
        private void RecordSuccessMetrics(string endpoint, TimeSpan duration, DateTime startTime)
        {
            lock (_metricsLock)
            {
                if (!_endpointMetrics.TryGetValue(endpoint, out var metrics))
                {
                    metrics = new DiagnosticMetrics { Endpoint = endpoint };
                    _endpointMetrics[endpoint] = metrics;
                }

                metrics.TotalRequests++;
                metrics.SuccessfulRequests++;
                metrics.TotalResponseTime = metrics.TotalResponseTime.Add(duration);
                metrics.AverageResponseTime = TimeSpan.FromMilliseconds(
                    metrics.TotalResponseTime.TotalMilliseconds / metrics.SuccessfulRequests);

                if (!metrics.FastestResponse.HasValue || duration < metrics.FastestResponse)
                    metrics.FastestResponse = duration;

                if (!metrics.SlowestResponse.HasValue || duration > metrics.SlowestResponse)
                    metrics.SlowestResponse = duration;

                metrics.LastRequestTime = startTime;
                metrics.LastSuccessTime = startTime;
            }
        }

        /// <summary>
        /// Records error metrics for an endpoint.
        /// </summary>
        private void RecordErrorMetrics(string endpoint, TimeSpan duration, DateTime startTime, Exception exception)
        {
            lock (_metricsLock)
            {
                if (!_endpointMetrics.TryGetValue(endpoint, out var metrics))
                {
                    metrics = new DiagnosticMetrics { Endpoint = endpoint };
                    _endpointMetrics[endpoint] = metrics;
                }

                metrics.TotalRequests++;
                metrics.ErrorCount++;
                metrics.LastRequestTime = startTime;
                metrics.LastErrorTime = startTime;
                metrics.LastError = exception.Message;
                
                // Track error types
                if (exception.Message.Contains("401"))
                    metrics.AuthenticationErrors++;
                else if (exception.Message.Contains("429"))
                    metrics.RateLimitErrors++;
                else if (exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                    metrics.TimeoutErrors++;
                else
                    metrics.OtherErrors++;
            }
        }

        /// <summary>
        /// Measures raw connectivity latency to Qobuz API.
        /// </summary>
        private async Task<TimeSpan?> MeasureConnectivityLatency(CancellationToken cancellationToken)
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                
                // Make a minimal request to test connectivity
                var request = new HttpRequestBuilder($"{QobuzConstants.Api.BaseUrl}/user/login")
                    .SetHeader("User-Agent", QobuzConstants.Api.UserAgent)
                    .Build();

                await _httpClient.ExecuteAsync(request).ConfigureAwait(false);
                stopwatch.Stop();

                return stopwatch.Elapsed;
            }
            catch (Exception ex)
            {
                _logger.Debug("Connectivity test failed: {0}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Measures authentication latency with current session.
        /// </summary>
        private async Task<TimeSpan?> MeasureAuthenticationLatency(CancellationToken cancellationToken)
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                
                // Make an authenticated request
                var parameters = new Dictionary<string, string>
                {
                    ["query"] = "test",
                    ["limit"] = "1"
                };

                await ExecuteGetAsync<object>("/album/search", parameters, cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();

                return stopwatch.Elapsed;
            }
            catch (Exception ex)
            {
                _logger.Debug("Authentication test failed: {0}", ex.Message);
                return null;
            }
        }

        #endregion

        #region No Rate Limiting Override

        /// <summary>
        /// Overrides pre-processing to skip rate limiting entirely.
        /// </summary>
        protected override Task PreProcessRequestAsync(string method, string endpoint, Dictionary<string, string> parameters, CancellationToken cancellationToken = default)
        {
            _logger.Trace("🚨 DIAGNOSTIC: Skipping rate limiting for {0} {1}", method, endpoint);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Overrides post-processing to skip caching but maintain diagnostics.
        /// </summary>
        protected override Task PostProcessResponseAsync<T>(string method, string endpoint, Dictionary<string, string> parameters, T result, CancellationToken cancellationToken = default)
        {
            _logger.Trace("🚨 DIAGNOSTIC: Skipping response caching for {0} {1}", method, endpoint);
            return Task.CompletedTask;
        }

        #endregion

        #region IQobuzApiClient Implementation

        /// <summary>
        /// Gets the streaming URL for a track with the specified format.
        /// WARNING: No rate limiting applied - use carefully.
        /// </summary>
        public async Task<string> GetStreamingUrlAsync(string trackId, int formatId, CancellationToken cancellationToken = default)
        {
            _logger.Warn("🚨 DIAGNOSTIC: Getting streaming URL without rate limiting for track {0}", trackId);
            
            var parameters = new Dictionary<string, string>
            {
                ["track_id"] = trackId,
                ["format_id"] = formatId.ToString()
            };
            
            var response = await GetAsync<dynamic>("track/getFileUrl", parameters);
            return response?.url?.ToString();
        }

        /// <summary>
        /// Gets detailed metadata for a track.
        /// WARNING: No rate limiting applied - use carefully.
        /// </summary>
        public async Task<QobuzTrack> GetTrackMetadataAsync(string trackId, CancellationToken cancellationToken = default)
        {
            _logger.Warn("🚨 DIAGNOSTIC: Getting track metadata without rate limiting for track {0}", trackId);
            
            var parameters = new Dictionary<string, string>
            {
                ["track_id"] = trackId
            };
            
            return await GetAsync<QobuzTrack>("track/get", parameters);
        }

        /// <summary>
        /// Searches for albums with the specified query.
        /// WARNING: No rate limiting applied - use carefully.
        /// </summary>
        public async Task<QobuzAlbumSearchResponse> SearchAlbumsAsync(string query, int limit = 50, int offset = 0, CancellationToken cancellationToken = default)
        {
            _logger.Warn("🚨 DIAGNOSTIC: Searching albums without rate limiting for query: {0}", query);
            
            var parameters = new Dictionary<string, string>
            {
                ["query"] = query,
                ["limit"] = limit.ToString(),
                ["offset"] = offset.ToString()
            };
            
            return await GetAsync<QobuzAlbumSearchResponse>("album/search", parameters);
        }

        /// <summary>
        /// Gets album details including tracks.
        /// WARNING: No rate limiting applied - use carefully.
        /// </summary>
        public async Task<QobuzAlbum> GetAlbumAsync(string albumId, CancellationToken cancellationToken = default)
        {
            _logger.Warn("🚨 DIAGNOSTIC: Getting album without rate limiting for album {0}", albumId);
            
            var parameters = new Dictionary<string, string>
            {
                ["album_id"] = albumId
            };
            
            return await GetAsync<QobuzAlbum>("album/get", parameters);
        }

        #endregion
    }

    #region Diagnostic Data Structures

    /// <summary>
    /// API connectivity test results.
    /// </summary>
    public class ApiConnectivityTestResult
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public string TestEndpoint { get; set; }
        public bool Success { get; set; }
        public bool IsConnectable { get; set; }
        public bool IsAuthenticated { get; set; }
        public TimeSpan? ConnectivityLatency { get; set; }
        public TimeSpan? AuthenticationLatency { get; set; }
        public string Error { get; set; }
        public Exception Exception { get; set; }
    }

    /// <summary>
    /// Comprehensive API health check results.
    /// </summary>
    public class ApiHealthCheckResult
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool Success { get; set; }
        public double AverageResponseTime { get; set; }
        public List<EndpointHealthResult> EndpointResults { get; set; } = new List<EndpointHealthResult>();
    }

    /// <summary>
    /// Individual endpoint health check results.
    /// </summary>
    public class EndpointHealthResult
    {
        public string Endpoint { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool Success { get; set; }
        public TimeSpan? ResponseTime { get; set; }
        public int StatusCode { get; set; }
        public string Error { get; set; }
        public Exception Exception { get; set; }
    }

    /// <summary>
    /// Diagnostic metrics for an individual endpoint.
    /// </summary>
    public class DiagnosticMetrics
    {
        public string Endpoint { get; set; }
        public long TotalRequests { get; set; }
        public long SuccessfulRequests { get; set; }
        public long ErrorCount { get; set; }
        public TimeSpan TotalResponseTime { get; set; }
        public TimeSpan AverageResponseTime { get; set; }
        public TimeSpan? FastestResponse { get; set; }
        public TimeSpan? SlowestResponse { get; set; }
        public DateTime LastRequestTime { get; set; }
        public DateTime? LastSuccessTime { get; set; }
        public DateTime? LastErrorTime { get; set; }
        public string LastError { get; set; }
        
        // Error type breakdown
        public long AuthenticationErrors { get; set; }
        public long RateLimitErrors { get; set; }
        public long TimeoutErrors { get; set; }
        public long OtherErrors { get; set; }

        public double SuccessRate => TotalRequests > 0 ? (double)SuccessfulRequests / TotalRequests : 0;
        public double ErrorRate => TotalRequests > 0 ? (double)ErrorCount / TotalRequests : 0;
    }

    /// <summary>
    /// Comprehensive diagnostic report for all endpoints.
    /// </summary>
    public class DiagnosticReport
    {
        public DateTime GeneratedAt { get; set; }
        public long TotalRequests { get; set; }
        public long TotalErrors { get; set; }
        public double ErrorRate { get; set; }
        public Dictionary<string, DiagnosticMetrics> EndpointMetrics { get; set; } = new Dictionary<string, DiagnosticMetrics>();
    }

    #endregion
}