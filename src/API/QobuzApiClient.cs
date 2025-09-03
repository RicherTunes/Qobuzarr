using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NzbDrone.Common.Http;
using NzbDrone.Common.Cache;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Authentication;
using SessionManager = Lidarr.Plugin.Qobuzarr.Authentication.SessionManager;
using Lidarr.Plugin.Qobuzarr.API.Http;
using Lidarr.Plugin.Qobuzarr.Services.Interfaces;
using Lidarr.Plugin.Qobuzarr.API.Signing;
using Lidarr.Plugin.Qobuzarr.API.Caching;
using Lidarr.Plugin.Common.Services.Http;
using Lidarr.Plugin.Common.Utilities;

namespace Lidarr.Plugin.Qobuzarr.API
{
    /// <summary>
    /// Orchestrator for Qobuz API operations, coordinating HTTP communication, authentication,
    /// request signing, and response caching through specialized components.
    /// </summary>
    /// <remarks>
    /// This refactored implementation delegates specific responsibilities to focused components:
    /// - HTTP communication: IQobuzHttpClient
    /// - Authentication: IQobuzAuthenticationManager
    /// - Request signing: IQobuzRequestSigner
    /// - Response caching: IQobuzResponseCache
    /// </remarks>
    public class QobuzApiClient : IQobuzApiClient
    {
        private readonly IQobuzHttpClient _httpClient;
        private readonly ISessionManager _sessionManager;
        private readonly IQobuzRequestSigner _requestSigner;
        private readonly IQobuzResponseCache _responseCache;
        private readonly Logger _logger;
        private IQobuzAuthenticationService? _authService;
        private Lidarr.Plugin.Common.Services.Authentication.StreamingTokenManager<QobuzSession, QobuzCredentials>? _tokenManager;
        private Func<Task<QobuzCredentials>>? _credentialsProvider;
        private IPreRequestHandler? _preRequestHandler;

        /// <summary>
        /// Initializes a new instance of the QobuzApiClient with the required dependencies.
        /// </summary>
        /// <param name="httpClient">The HTTP client for pure HTTP communication.</param>
        /// <param name="authManager">The authentication manager for session handling.</param>
        /// <param name="requestSigner">The request signer for API signature generation.</param>
        /// <param name="responseCache">The response cache for caching API responses.</param>
        /// <param name="logger">The logger for recording API interactions.</param>
        public QobuzApiClient(
            IQobuzHttpClient httpClient,
            ISessionManager sessionManager,
            IQobuzRequestSigner requestSigner,
            IQobuzResponseCache responseCache,
            Logger logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _requestSigner = requestSigner ?? throw new ArgumentNullException(nameof(requestSigner));
            _responseCache = responseCache ?? throw new ArgumentNullException(nameof(responseCache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Backward-compatible constructor that creates the decomposed components internally.
        /// This constructor maintains compatibility with existing code while using the new architecture.
        /// </summary>
        /// <param name="httpClient">The Lidarr HTTP client.</param>
        /// <param name="cacheManager">The cache manager.</param>
        /// <param name="logger">The logger.</param>
        [Obsolete("Use the DI container to create instances with properly injected dependencies. This constructor will be removed in a future version.")]
        public QobuzApiClient(IHttpClient httpClient, ICacheManager cacheManager, Logger logger)
            : this(
                new QobuzHttpClient(httpClient, logger),
                new NullSessionManager(),
                new QobuzRequestSigner(logger),
                new QobuzResponseCache(logger),
                logger)
        {
            // This constructor maintains backward compatibility with existing code
            _logger.Debug("QobuzApiClient created using backward-compatible constructor");
        }

        private sealed class NullSessionManager : Services.Interfaces.ISessionManager
        {
            public Task<Models.Authentication.QobuzSession?> CreateSessionAsync(Models.Authentication.QobuzCredentials credentials, CancellationToken cancellationToken = default) => Task.FromResult<Models.Authentication.QobuzSession?>(null);
            public Task<Models.Authentication.QobuzSession?> GetCurrentSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult<Models.Authentication.QobuzSession?>(null);
            public Task<bool> IsSessionValidAsync(Models.Authentication.QobuzSession session, CancellationToken cancellationToken = default) => Task.FromResult(false);
            public Task InvalidateSessionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<Models.Authentication.QobuzSession?> RefreshSessionAsync(Models.Authentication.QobuzSession session, CancellationToken cancellationToken = default) => Task.FromResult<Models.Authentication.QobuzSession?>(null);
            public bool HasValidSession() => false;
        }

        /// <summary>
        /// Set the authentication service for session renewal
        /// </summary>
        public void SetAuthenticationService(IQobuzAuthenticationService authService)
        {
            _authService = authService;
            if (authService is QobuzAuthenticationService realAuth)
            {
                _tokenManager = realAuth.CreateTokenManager();
            }
        }

        /// <summary>
        /// Sets a credentials provider used for token renewal (re-auth) when sessions expire.
        /// </summary>
        public void SetCredentialsProvider(Func<Task<QobuzCredentials>> credentialsProvider)
        {
            _credentialsProvider = credentialsProvider;
        }

        /// <summary>
        /// Optionally provide a pre-request handler that centralizes session checks and
        /// auth/signature injection before each API call.
        /// </summary>
        public void SetPreRequestHandler(IPreRequestHandler preRequestHandler)
        {
            _preRequestHandler = preRequestHandler;
        }

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
            return await ExecuteRequestAsync<T>("GET", endpoint, parameters).ConfigureAwait(false);
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
            return await ExecuteRequestAsync<T>("POST", endpoint, null, data).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets the authentication session for subsequent API requests.
        /// The session will be automatically included in all requests that require authentication.
        /// </summary>
        /// <param name="session">The authenticated Qobuz session containing user credentials and app information.</param>
        public void SetSession(QobuzSession session)
        {
            // Use the existing StoreSession method from SessionManager implementation
            ((SessionManager)_sessionManager).StoreSession(session);
            _logger.Debug("Session set for API client");
        }

        /// <summary>
        /// Clears the current authentication session, making subsequent requests unauthenticated.
        /// Use this when logging out or when authentication becomes invalid.
        /// </summary>
        public void ClearSession()
        {
            ((SessionManager)_sessionManager).ClearSession();
            _logger.Debug("Session cleared from API client");
        }

        /// <summary>
        /// Checks whether the client currently has a valid authentication session configured.
        /// </summary>
        /// <returns>True if a valid session is available; false if no session or session is expired.</returns>
        public bool HasValidSession()
        {
            return ((SessionManager)_sessionManager).HasValidSession();
        }

        private async Task<T> ExecuteRequestAsync<T>(string method, string endpoint, Dictionary<string, string>? parameters = null, object? data = null) where T : class
        {
            try
            {
                // Ensure valid session prior to request
                if (_preRequestHandler != null)
                {
                    await _preRequestHandler.EnsureValidSessionAsync().ConfigureAwait(false);
                }
                else if (_tokenManager != null)
                {
                    // Fallback to built-in token manager if pre-handler not provided
                    var fallbackCreds = _credentialsProvider != null ? await _credentialsProvider().ConfigureAwait(false) : null;
                    var validSession = await _tokenManager.GetValidSessionAsync(fallbackCreds).ConfigureAwait(false);
                    if (validSession != null)
                    {
                        ((SessionManager)_sessionManager).StoreSession(validSession);
                    }
                }

                // Build request URL
                var url = $"{QobuzConstants.Api.BaseUrl}{endpoint}";
                
                _logger.Trace("🔗 API Client building request: {0} {1}", method, url);
                
                // Prepare parameters
                var allParameters = new Dictionary<string, string>();
                
                // Inject auth params via pre-handler if present
                var currentSession = ((SessionManager)_sessionManager).GetCurrentSession();
                if (_preRequestHandler != null)
                {
                    _preRequestHandler.InjectAuthParameters(allParameters);
                }
                else if (currentSession != null)
                {
                    allParameters["app_id"] = currentSession.AppId;
                    allParameters["user_auth_token"] = currentSession.AuthToken;
                    _logger.Trace("🔐 Added authentication: app_id={0}, token=***{1}",
                        currentSession.AppId, currentSession.AuthToken?.Substring(Math.Max(0, currentSession.AuthToken.Length - 4)) ?? "null");
                }

                // Add custom parameters (builder handles URL encoding)
                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        // Avoid over-sanitizing; rely on HttpRequestBuilder for URL encoding
                        var value = param.Value?.Trim() ?? string.Empty;
                        allParameters[param.Key] = value;
                    }
                    _logger.Trace("📋 Custom parameters added: {0}", 
                        string.Join(", ", parameters.Select(kv => $"{kv.Key}={kv.Value}")));
                }

                // Handle request signing for protected endpoints
                if (_preRequestHandler != null)
                {
                    _preRequestHandler.SignIfRequired(endpoint, allParameters);
                }
                else if (_requestSigner.RequiresSigning(endpoint) && currentSession != null)
                {
                    _requestSigner.SignRequest(endpoint, allParameters, currentSession.AppId, currentSession.AppSecret);
                }

                // Check cache first for GET requests
                if (method == "GET")
                {
                    var cached = _responseCache.Get<T>(endpoint, allParameters);
                    if (cached != null)
                    {
                        _logger.Debug("Returning cached response for {0}", endpoint);
                        return cached;
                    }
                }

                // Build HTTP request
                var requestBuilder = _httpClient.BuildRequest(url, method);

                if (method == "GET")
                {
                    foreach (var param in allParameters)
                    {
                        requestBuilder.AddQueryParam(param.Key, param.Value);
                    }
                    
                    // Log the final URL being called (without auth token for security)
                    var safeParams = allParameters.Where(kv => kv.Key != "user_auth_token")
                                                 .Select(kv => $"{kv.Key}={kv.Value}");
                    _logger.Debug("🚀 Final API call: {0}?{1}&user_auth_token=***", url, string.Join("&", safeParams));
                }

                var request = requestBuilder.Build();

                if (method == "POST" && data != null)
                {
                    request.SetContent(JsonConvert.SerializeObject(data));
                    request.Headers.ContentType = "application/json";
                }

                _logger.Debug("Making {0} request to {1}", method, endpoint);

                // Execute request through HTTP client (includes rate limiting and retries)
                var response = await _httpClient.ExecuteAsync(request).ConfigureAwait(false);

                _logger.Debug("📡 API response received: Status={0}, Length={1} chars", 
                    response.StatusCode, response.Content?.Length ?? 0);

                if (response.HasHttpError)
                {
                    _logger.Error("❌ API Error Response: {0}", response.Content);
                    HandleErrorResponse(response);
                }

                // Deserialize response
                var result = JsonConvert.DeserializeObject<T>(response.Content);
                
                _logger.Debug("✅ Response deserialized to: {0}", typeof(T).Name);
                
                // Log first 500 chars of response for debugging (sanitized)
                if (response.Content?.Length > 0)
                {
                    var sanitized = response.Content.Length > 500 ? response.Content.Substring(0, 500) + "..." : response.Content;
                    _logger.Trace("📄 Response content: {0}", sanitized);
                }

                // Cache successful GET responses
                if (method == "GET" && result != null)
                {
                    _responseCache.Set(endpoint, allParameters, result);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "API request failed for endpoint {0}", endpoint);
                throw;
            }
        }


        private void HandleErrorResponse(HttpResponse response)
        {
            var statusCode = (int)response.StatusCode;
            
            try
            {
                var errorResponse = JsonConvert.DeserializeObject<QobuzErrorResponse>(response.Content);
                var message = errorResponse?.Message ?? $"HTTP {statusCode}";
                
                throw statusCode switch
                {
                    401 => new QobuzApiException("Authentication failed", statusCode, "AuthenticationFailed"),
                    403 => new QobuzApiException("Access forbidden - check app credentials", statusCode, "AccessForbidden"),
                    404 => new QobuzApiException("Resource not found", statusCode, "NotFound"),
                    429 => new QobuzApiException("Rate limit exceeded", statusCode, "RateLimited"),
                    >= 500 => new QobuzApiException("Server error", statusCode, "ServerError"),
                    _ => new QobuzApiException(message, statusCode, "ApiError")
                };
            }
            catch (JsonException)
            {
                throw new QobuzApiException($"HTTP {statusCode}: {response.Content}", statusCode, "UnknownError");
            }
        }


        /// <summary>
        /// Gets the streaming URL for a track
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
        /// Gets detailed metadata for a track
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
        /// Get playlist details by ID
        /// </summary>
        public async Task<QobuzPlaylist> GetPlaylistAsync(string playlistId, int limit = 500, int offset = 0, CancellationToken cancellationToken = default)
        {
            var parameters = new Dictionary<string, string>
            {
                ["playlist_id"] = playlistId,
                ["limit"] = limit.ToString(),
                ["offset"] = offset.ToString(),
                ["extra"] = "tracks"
            };

            return await GetAsync<QobuzPlaylist>("playlist/get", parameters);
        }

        /// <summary>
        /// Get all tracks from a playlist (handles pagination)
        /// </summary>
        public async Task<List<QobuzTrack>> GetPlaylistTracksAsync(string playlistId, CancellationToken cancellationToken = default)
        {
            var allTracks = new List<QobuzTrack>();
            const int pageSize = 500;
            int offset = 0;

            while (true)
            {
                var playlist = await GetPlaylistAsync(playlistId, pageSize, offset, cancellationToken);
                
                if (playlist?.Tracks?.Items == null || playlist.Tracks.Items.Count == 0)
                    break;

                // Extract tracks from playlist track items
                foreach (var item in playlist.Tracks.Items)
                {
                    if (item.Track != null)
                        allTracks.Add(item.Track);
                }

                offset += pageSize;
                
                // Check if we've fetched all tracks
                if (allTracks.Count >= playlist.Tracks.Total)
                    break;
            }

            return allTracks;
        }

        /// <summary>
        /// Search for playlists
        /// </summary>
        public async Task<QobuzPlaylistSearchResponse> SearchPlaylistsAsync(string query, int limit = 50, CancellationToken cancellationToken = default)
        {
            var parameters = new Dictionary<string, string>
            {
                ["query"] = query,
                ["limit"] = limit.ToString()
            };

            return await GetAsync<QobuzPlaylistSearchResponse>("playlist/search", parameters);
        }

        /// <summary>
        /// Get label details by ID
        /// </summary>
        public async Task<QobuzLabel> GetLabelAsync(string labelId, CancellationToken cancellationToken = default)
        {
            var parameters = new Dictionary<string, string>
            {
                ["label_id"] = labelId
            };

            return await GetAsync<QobuzLabel>("label/get", parameters);
        }

        /// <summary>
        /// Get all albums from a label (handles pagination)
        /// </summary>
        public async Task<List<QobuzAlbum>> GetLabelAlbumsAsync(string labelId, CancellationToken cancellationToken = default)
        {
            var allAlbums = new List<QobuzAlbum>();
            const int pageSize = 500;
            int offset = 0;

            // Note: We need to use label/getAlbums endpoint for album list
            while (true)
            {
                var parameters = new Dictionary<string, string>
                {
                    ["label_id"] = labelId,
                    ["limit"] = pageSize.ToString(),
                    ["offset"] = offset.ToString()
                };

                var response = await GetAsync<QobuzAlbumSearchResponse>("label/getAlbums", parameters);
                
                if (response?.Albums?.Items == null || response.Albums.Items.Count == 0)
                    break;

                allAlbums.AddRange(response.Albums.Items);
                offset += pageSize;
                
                // Check if we've fetched all albums
                if (allAlbums.Count >= response.Albums.Total)
                    break;
            }

            return allAlbums;
        }

        /// <summary>
        /// Get artist details by ID
        /// </summary>
        public async Task<QobuzArtist> GetArtistAsync(string artistId, CancellationToken cancellationToken = default)
        {
            var parameters = new Dictionary<string, string>
            {
                ["artist_id"] = artistId
            };

            return await GetAsync<QobuzArtist>("artist/get", parameters);
        }

        /// <summary>
        /// Get all albums from an artist (handles pagination)
        /// </summary>
        public async Task<List<QobuzAlbum>> GetArtistAlbumsAsync(string artistId, CancellationToken cancellationToken = default)
        {
            var allAlbums = new List<QobuzAlbum>();
            const int pageSize = 500;
            int offset = 0;

            // Note: We need to use artist/getAlbums endpoint for album list
            while (true)
            {
                var parameters = new Dictionary<string, string>
                {
                    ["artist_id"] = artistId,
                    ["limit"] = pageSize.ToString(),
                    ["offset"] = offset.ToString()
                };

                var response = await GetAsync<QobuzAlbumSearchResponse>("artist/getAlbums", parameters);
                
                if (response?.Albums?.Items == null || response.Albums.Items.Count == 0)
                    break;

                allAlbums.AddRange(response.Albums.Items);
                offset += pageSize;
                
                // Check if we've fetched all albums
                if (allAlbums.Count >= response.Albums.Total)
                    break;
            }

            return allAlbums;
        }

        /// <summary>
        /// Search for labels
        /// </summary>
        public async Task<QobuzLabelSearchResponse> SearchLabelsAsync(string query, int limit = 50, CancellationToken cancellationToken = default)
        {
            var parameters = new Dictionary<string, string>
            {
                ["query"] = query,
                ["limit"] = limit.ToString()
            };

            return await GetAsync<QobuzLabelSearchResponse>("label/search", parameters);
        }

        private class QobuzErrorResponse
        {
            [JsonProperty("message")]
            public string Message { get; set; }

            [JsonProperty("code")]
            public int? Code { get; set; }
        }
    }

    /// <summary>
    /// Exception thrown when the Qobuz API returns an error response or when API communication fails.
    /// Provides structured access to HTTP status codes and categorized error types for proper error handling.
    /// </summary>
    public class QobuzApiException : Exception
    {
        /// <summary>
        /// Gets the HTTP status code returned by the Qobuz API.
        /// </summary>
        public int StatusCode { get; }
        
        /// <summary>
        /// Gets the categorized error type for programmatic error handling.
        /// Common values: AuthenticationFailed, AccessForbidden, NotFound, RateLimited, ServerError, ApiError.
        /// </summary>
        public string ErrorType { get; }

        /// <summary>
        /// Initializes a new instance of QobuzApiException with detailed error information.
        /// </summary>
        /// <param name="message">The error message describing what went wrong.</param>
        /// <param name="statusCode">The HTTP status code returned by the API.</param>
        /// <param name="errorType">The categorized error type for programmatic handling.</param>
        public QobuzApiException(string message, int statusCode, string errorType) : base(message)
        {
            StatusCode = statusCode;
            ErrorType = errorType;
        }
    }
}
