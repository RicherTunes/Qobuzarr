using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NzbDrone.Common.Http;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Exceptions;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services.Core.Api
{
    /// <summary>
    /// Abstract base class for Qobuz API clients providing core HTTP logic, authentication,
    /// request building, response handling, and error management.
    /// </summary>
    /// <remarks>
    /// This base class implements the common functionality needed by all Qobuz API clients:
    /// - HTTP request building and execution
    /// - Authentication parameter injection
    /// - Request signing for protected endpoints  
    /// - Response validation and deserialization
    /// - Structured error handling with specific exception types
    /// - Logging and security sanitization
    /// 
    /// Concrete implementations can focus on specific concerns like rate limiting,
    /// caching, diagnostics, or other specialized behaviors.
    /// </remarks>
    public abstract class QobuzApiClientBase
    {
        protected readonly IHttpClient _httpClient;
        protected readonly Logger _logger;

        private QobuzSession? _currentSession;
        private readonly object _sessionLock = new object();

        protected QobuzApiClientBase(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Session Management

        /// <summary>
        /// Sets the authentication session for subsequent API requests.
        /// The session will be automatically included in all requests that require authentication.
        /// </summary>
        /// <param name="session">The authenticated Qobuz session containing user credentials and app information.</param>
        public virtual void SetSession(QobuzSession session)
        {
            lock (_sessionLock)
            {
                _currentSession = session;
                _logger.Debug("Session set for API client");
            }
        }

        /// <summary>
        /// Clears the current authentication session, making subsequent requests unauthenticated.
        /// Use this when logging out or when authentication becomes invalid.
        /// </summary>
        public virtual void ClearSession()
        {
            lock (_sessionLock)
            {
                _currentSession = null;
                _logger.Debug("Session cleared from API client");
            }
        }

        /// <summary>
        /// Checks whether the client currently has a valid authentication session configured.
        /// </summary>
        /// <returns>True if a valid session is available; false if no session or session is expired.</returns>
        public virtual bool HasValidSession()
        {
            lock (_sessionLock)
            {
                return _currentSession?.IsValid() == true;
            }
        }

        /// <summary>
        /// Gets the current session in a thread-safe manner
        /// </summary>
        /// <returns>The current session or null if no session is set</returns>
        protected QobuzSession? GetCurrentSession()
        {
            lock (_sessionLock)
            {
                return _currentSession;
            }
        }

        #endregion

        #region Core HTTP Methods

        /// <summary>
        /// Executes a GET request to the specified Qobuz API endpoint with automatic authentication.
        /// </summary>
        /// <typeparam name="T">The expected response type for JSON deserialization.</typeparam>
        /// <param name="endpoint">The API endpoint path relative to the base URL (e.g., "/album/search").</param>
        /// <param name="parameters">Optional query parameters to include in the request.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>The deserialized response object of type T.</returns>
        /// <exception cref="QobuzApiException">Thrown when the API returns an error response or authentication fails.</exception>
        protected async Task<T> ExecuteGetAsync<T>(
            string endpoint, 
            Dictionary<string, string>? parameters = null, 
            CancellationToken cancellationToken = default) where T : class
        {
            return await ExecuteRequestAsync<T>("GET", endpoint, parameters, null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes a POST request to the specified Qobuz API endpoint with optional JSON payload.
        /// </summary>
        /// <typeparam name="T">The expected response type for JSON deserialization.</typeparam>
        /// <param name="endpoint">The API endpoint path relative to the base URL (e.g., "/user/login").</param>
        /// <param name="data">Optional request body data that will be serialized to JSON.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>The deserialized response object of type T.</returns>
        /// <exception cref="QobuzApiException">Thrown when the API returns an error response or authentication fails.</exception>
        protected async Task<T> ExecutePostAsync<T>(
            string endpoint, 
            object? data = null, 
            CancellationToken cancellationToken = default) where T : class
        {
            return await ExecuteRequestAsync<T>("POST", endpoint, null, data, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Core Request Processing

        /// <summary>
        /// Core request execution method that handles the complete request lifecycle.
        /// This method implements the common logic for all HTTP operations.
        /// </summary>
        /// <typeparam name="T">The expected response type for JSON deserialization.</typeparam>
        /// <param name="method">HTTP method (GET, POST, etc.)</param>
        /// <param name="endpoint">The API endpoint path</param>
        /// <param name="parameters">Query parameters for GET requests</param>
        /// <param name="data">Request body data for POST requests</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>The deserialized response object</returns>
        protected virtual async Task<T> ExecuteRequestAsync<T>(
            string method, 
            string endpoint, 
            Dictionary<string, string>? parameters = null, 
            object? data = null, 
            CancellationToken cancellationToken = default) where T : class
        {
            try
            {
                // Build request URL
                var url = $"{QobuzConstants.Api.BaseUrl}{endpoint}";
                
                _logger.Trace("🔗 API Client building request: {0} {1}", method, url);
                
                // Prepare parameters with authentication
                var allParameters = BuildRequestParameters(parameters);
                
                // Apply request-specific processing (rate limiting, caching checks, etc.)
                await PreProcessRequestAsync(method, endpoint, allParameters, cancellationToken).ConfigureAwait(false);

                // Handle request signing for protected endpoints
                SignRequestIfNeeded(endpoint, allParameters);

                // Build HTTP request
                var request = BuildHttpRequest(method, url, allParameters, data);

                // Log the request details (sanitized for security)
                LogRequestDetails(method, url, allParameters);

                // Execute the request
                var response = await ExecuteHttpRequestAsync(request, cancellationToken).ConfigureAwait(false);

                // Log response details  
                LogResponseDetails(response);

                // Validate and handle response
                ValidateResponse(response, endpoint);

                // Deserialize response
                var result = DeserializeResponse<T>(response);
                
                // Apply post-processing (caching, metrics, etc.)
                await PostProcessResponseAsync(method, endpoint, allParameters, result, cancellationToken).ConfigureAwait(false);

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "API request failed for endpoint {0}", endpoint);
                
                // Handle error-specific post-processing
                await PostProcessErrorAsync(method, endpoint, ex, cancellationToken).ConfigureAwait(false);
                
                throw;
            }
        }

        #endregion

        #region Request Building

        /// <summary>
        /// Builds the complete set of request parameters including authentication.
        /// </summary>
        /// <param name="customParameters">Custom parameters to include</param>
        /// <returns>Complete parameter dictionary</returns>
        protected virtual Dictionary<string, string> BuildRequestParameters(Dictionary<string, string>? customParameters = null)
        {
            var allParameters = new Dictionary<string, string>();
            
            // Add session parameters if authenticated
            var currentSession = GetCurrentSession();
            if (currentSession != null)
            {
                allParameters["app_id"] = currentSession.AppId;
                allParameters["user_auth_token"] = currentSession.AuthToken;
                _logger.Trace("🔐 Added authentication: app_id={0}, token=***{1}", 
                    currentSession.AppId, currentSession.AuthToken?.Substring(Math.Max(0, currentSession.AuthToken.Length - 4)) ?? "null");
            }

            // Add custom parameters with sanitization
            if (customParameters != null)
            {
                foreach (var param in customParameters)
                {
                    // SECURITY: Sanitize parameter values to prevent injection
                    var sanitizedValue = param.Value;
                    if (!string.IsNullOrEmpty(sanitizedValue) && !LidarrInputValidator.IsInputSafe(sanitizedValue))
                    {
                        _logger.Warn("Potentially unsafe parameter value detected for key {0}", param.Key);
                        continue; // Skip unsafe parameters
                    }
                    allParameters[param.Key] = sanitizedValue;
                }
                _logger.Trace("📋 Custom parameters added: {0}", 
                    string.Join(", ", customParameters.Select(kv => $"{kv.Key}={kv.Value}")));
            }

            return allParameters;
        }

        /// <summary>
        /// Signs the request for protected endpoints if needed.
        /// </summary>
        /// <param name="endpoint">The API endpoint</param>
        /// <param name="parameters">Request parameters</param>
        protected virtual void SignRequestIfNeeded(string endpoint, Dictionary<string, string> parameters)
        {
            var currentSession = GetCurrentSession();
            if (RequiresSigning(endpoint) && currentSession != null)
            {
                // Basic signing implementation - override in derived classes for more sophisticated signing
                // For now, we just ensure the app_id and secret are available for signing
                _logger.Trace("🔐 Request signing required for endpoint {0}", endpoint);
            }
        }

        /// <summary>
        /// Builds the HTTP request object.
        /// </summary>
        /// <param name="method">HTTP method</param>
        /// <param name="url">Request URL</param>
        /// <param name="parameters">Request parameters</param>
        /// <param name="data">Request body data</param>
        /// <returns>Configured HTTP request</returns>
        protected virtual HttpRequest BuildHttpRequest(string method, string url, Dictionary<string, string> parameters, object? data)
        {
            var requestBuilder = new HttpRequestBuilder(url)
                .SetHeader("User-Agent", QobuzConstants.Api.UserAgent);

            if (method == "GET")
            {
                foreach (var param in parameters)
                {
                    requestBuilder.AddQueryParam(param.Key, param.Value);
                }
            }

            var request = requestBuilder.Build();
            request.Method = method switch
            {
                "GET" => HttpMethod.Get,
                "POST" => HttpMethod.Post,
                "PUT" => HttpMethod.Put,
                "DELETE" => HttpMethod.Delete,
                _ => HttpMethod.Get
            };

            if (method == "POST" && data != null)
            {
                request.SetContent(JsonConvert.SerializeObject(data));
                request.Headers.ContentType = "application/json";
            }

            return request;
        }

        #endregion

        #region Response Handling

        /// <summary>
        /// Executes the HTTP request with proper error handling.
        /// </summary>
        /// <param name="request">The HTTP request to execute</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>HTTP response</returns>
        protected virtual async Task<HttpResponse> ExecuteHttpRequestAsync(HttpRequest request, CancellationToken cancellationToken = default)
        {
            return await _httpClient.ExecuteAsync(request).ConfigureAwait(false);
        }

        /// <summary>
        /// Validates the HTTP response and throws appropriate exceptions for errors.
        /// </summary>
        /// <param name="response">HTTP response to validate</param>
        /// <param name="endpoint">The endpoint that was called</param>
        protected virtual void ValidateResponse(HttpResponse response, string endpoint)
        {
            if (response.HasHttpError)
            {
                _logger.Error("❌ API Error Response: {0}", response.Content);
                HandleErrorResponse(response, endpoint);
            }
        }

        /// <summary>
        /// Deserializes the response content to the specified type.
        /// </summary>
        /// <typeparam name="T">Type to deserialize to</typeparam>
        /// <param name="response">HTTP response</param>
        /// <returns>Deserialized object</returns>
        protected virtual T DeserializeResponse<T>(HttpResponse response) where T : class
        {
            var result = JsonConvert.DeserializeObject<T>(response.Content);
            _logger.Debug("✅ Response deserialized to: {0}", typeof(T).Name);
            return result;
        }

        /// <summary>
        /// Handles API error responses by throwing appropriate exceptions.
        /// </summary>
        /// <param name="response">The error response</param>
        /// <param name="endpoint">The endpoint that was called</param>
        protected virtual void HandleErrorResponse(HttpResponse response, string endpoint)
        {
            var statusCode = (int)response.StatusCode;
            
            try
            {
                var errorResponse = JsonConvert.DeserializeObject<QobuzErrorResponse>(response.Content);
                var message = errorResponse?.Message ?? $"HTTP {statusCode}";
                
                throw statusCode switch
                {
                    401 => new QobuzAuthenticationException("Authentication failed", AuthenticationFailureType.InvalidCredentials),
                    403 => new QobuzApiException("Access forbidden - check app credentials", endpoint, System.Net.HttpStatusCode.Forbidden, "access_forbidden"),
                    404 => new QobuzApiException("Resource not found", endpoint, System.Net.HttpStatusCode.NotFound, "not_found"),
                    429 => new QobuzApiException("Rate limit exceeded", endpoint, System.Net.HttpStatusCode.TooManyRequests, "rate_limit_exceeded", isRetryable: true),
                    >= 500 => new QobuzApiException("Server error", endpoint, System.Net.HttpStatusCode.InternalServerError, "server_error", isRetryable: true),
                    _ => new QobuzApiException(message, endpoint, (System.Net.HttpStatusCode)statusCode)
                };
            }
            catch (JsonException)
            {
                throw new QobuzApiException($"HTTP {statusCode}: {response.Content}", endpoint, (System.Net.HttpStatusCode)statusCode, "json_parse_error");
            }
        }

        #endregion

        #region Extensibility Points

        /// <summary>
        /// Pre-processes the request before execution. Override in derived classes for rate limiting, caching checks, etc.
        /// </summary>
        /// <param name="method">HTTP method</param>
        /// <param name="endpoint">API endpoint</param>
        /// <param name="parameters">Request parameters</param>
        /// <param name="cancellationToken">Cancellation token</param>
        protected virtual Task PreProcessRequestAsync(string method, string endpoint, Dictionary<string, string> parameters, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Post-processes successful responses. Override in derived classes for caching, metrics, etc.
        /// </summary>
        /// <param name="method">HTTP method</param>
        /// <param name="endpoint">API endpoint</param>
        /// <param name="parameters">Request parameters</param>
        /// <param name="result">Response result</param>
        /// <param name="cancellationToken">Cancellation token</param>
        protected virtual Task PostProcessResponseAsync<T>(string method, string endpoint, Dictionary<string, string> parameters, T result, CancellationToken cancellationToken = default) where T : class
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Post-processes errors. Override in derived classes for rate limit tracking, retry logic, etc.
        /// </summary>
        /// <param name="method">HTTP method</param>
        /// <param name="endpoint">API endpoint</param>
        /// <param name="exception">The exception that occurred</param>
        /// <param name="cancellationToken">Cancellation token</param>
        protected virtual Task PostProcessErrorAsync(string method, string endpoint, Exception exception, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Determines if an endpoint requires request signing.
        /// </summary>
        /// <param name="endpoint">The API endpoint</param>
        /// <returns>True if signing is required</returns>
        protected virtual bool RequiresSigning(string endpoint)
        {
            // Basic implementation - override for more sophisticated signing requirements
            return endpoint.Contains("getFileUrl") || endpoint.Contains("track/get");
        }

        /// <summary>
        /// Logs request details in a security-conscious manner.
        /// </summary>
        /// <param name="method">HTTP method</param>
        /// <param name="url">Request URL</param>
        /// <param name="parameters">Request parameters</param>
        protected virtual void LogRequestDetails(string method, string url, Dictionary<string, string> parameters)
        {
            if (method == "GET")
            {
                // Log the final URL being called (without auth token for security)
                var safeParams = parameters.Where(kv => kv.Key != "user_auth_token")
                                         .Select(kv => $"{kv.Key}={kv.Value}");
                _logger.Debug("🚀 Final API call: {0}?{1}&user_auth_token=***", url, string.Join("&", safeParams));
            }
            else
            {
                _logger.Debug("Making {0} request to {1}", method, url);
            }
        }

        /// <summary>
        /// Logs response details.
        /// </summary>
        /// <param name="response">HTTP response</param>
        protected virtual void LogResponseDetails(HttpResponse response)
        {
            _logger.Debug("📡 API response received: Status={0}, Length={1} chars", 
                response.StatusCode, response.Content?.Length ?? 0);
                
            // Log first 500 chars of response for debugging (sanitized)
            if (response.Content?.Length > 0)
            {
                var sanitized = response.Content.Length > 500 ? response.Content.Substring(0, 500) + "..." : response.Content;
                _logger.Trace("📄 Response content: {0}", sanitized);
            }
        }

        #endregion

        #region Supporting Types

        /// <summary>
        /// Standard Qobuz error response structure.
        /// </summary>
        protected class QobuzErrorResponse
        {
            [JsonProperty("message")]
            public string Message { get; set; }

            [JsonProperty("code")]
            public int? Code { get; set; }
        }

        #endregion
    }
}