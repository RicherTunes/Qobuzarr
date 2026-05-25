using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
using Lidarr.Plugin.Common.Observability;
using Lidarr.Plugin.Common.Services.Caching;
using Lidarr.Plugin.Common.Services.Http;
using Lidarr.Plugin.Common.Utilities;
using IRequestSigner = Lidarr.Plugin.Common.Services.Http.IRequestSigner;

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
    /// - Request signing: Lidarr.Plugin.Common.Services.Http.IRequestSigner
    /// - Response caching: IQobuzResponseCache
    /// </remarks>
    public class QobuzApiClient : IQobuzApiClient
    {
        private readonly IQobuzHttpClient _httpClient;
        private readonly ISessionManager _sessionManager;
        private readonly IRequestSigner _requestSigner;
        private readonly IQobuzResponseCache _responseCache;
        private readonly Logger _logger;
        private IQobuzAuthenticationService? _authService;
        private Lidarr.Plugin.Common.Services.Authentication.StreamingTokenManager<QobuzSession, QobuzCredentials>? _tokenManager;
        private Func<Task<QobuzCredentials>>? _credentialsProvider;
        private IPreRequestHandler? _preRequestHandler;
        // Fallback session storage for managers that don't expose concrete Store/Clear
        private QobuzSession? _fallbackSession;

        // Lazily initialized cache+conditional+resilience executor (common Phase 3a unification).
        // Built once per QobuzApiClient instance; the executor itself is stateless across requests.
        // The executor is configured with the common Phase 5e ResiliencePolicy.Passthrough preset:
        // retries, per-host gates, adaptive rate limiting and the retry budget already live in
        // QobuzHttpClient.ExecuteAsync (the underlying transport via LidarrHttpClientInvoker), so
        // the executor's resilience layer must be a no-op (MaxRetries=1, no timeout, ~no backoff).
        // Stacking another retry layer on a transport that already retries leads to multiplied
        // retry counts and explosive backoff. Source: Phase 3b adoption feedback.
        private CachingHttpExecutor? _cachingExecutor;
        private static readonly ResiliencePolicy ExecutorResiliencePolicy = ResiliencePolicy.Passthrough;

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
            IRequestSigner requestSigner,
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
            try
            {
                if (_sessionManager is SessionManager concrete)
                {
                    concrete.StoreSession(session);
                }
                else
                {
                    _fallbackSession = session;
                }
                _logger.Debug("Session set for API client");
            }
            catch (Exception ex)
            {
                _fallbackSession = session;
                _logger.Warn(ex, "Falling back to local session storage");
            }
        }

        /// <summary>
        /// Clears the current authentication session, making subsequent requests unauthenticated.
        /// Use this when logging out or when authentication becomes invalid.
        /// </summary>
        public void ClearSession()
        {
            try
            {
                if (_sessionManager is SessionManager concrete)
                {
                    concrete.ClearSession();
                }
                else
                {
                    // NullSessionManager.InvalidateSessionAsync() is a synchronous no-op (Task.CompletedTask).
                    // Avoid sync-over-async; just clear the fallback session directly.
                    _fallbackSession = null;
                }
                _logger.Debug("Session cleared from API client");
            }
            catch (Exception ex)
            {
                _fallbackSession = null;
                _logger.Warn(ex, "Error clearing session; cleared local fallback");
            }
        }

        /// <summary>
        /// Checks whether the client currently has a valid authentication session configured.
        /// </summary>
        /// <returns>True if a valid session is available; false if no session or session is expired.</returns>
        public bool HasValidSession()
        {
            try
            {
                return _sessionManager.HasValidSession() || (_fallbackSession?.IsValid() == true);
            }
            catch
            {
                return _fallbackSession?.IsValid() == true;
            }
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
                        if (_sessionManager is SessionManager concrete)
                        {
                            concrete.StoreSession(validSession);
                        }
                        else
                        {
                            _fallbackSession = validSession;
                        }
                    }
                }

                // Build request URL ensuring exactly one '/'
                var baseUrl = QobuzConstants.Api.BaseUrl?.TrimEnd('/') ?? string.Empty;
                var ep = string.IsNullOrWhiteSpace(endpoint)
                    ? string.Empty
                    : (endpoint.StartsWith("/") ? endpoint : "/" + endpoint);
                var url = $"{baseUrl}{ep}";

                _logger.Trace("🔗 API Client building request: {0} {1}", method, url);

                // Prepare parameters
                var allParameters = new Dictionary<string, string>();

                // Inject auth params via pre-handler if present
                var currentSession = _preRequestHandler == null
                    ? (await _sessionManager.GetCurrentSessionAsync().ConfigureAwait(false) ?? _fallbackSession)
                    : null;
                if (_preRequestHandler != null)
                {
                    _preRequestHandler.InjectAuthParameters(allParameters);
                }
                else if (currentSession != null)
                {
                    allParameters["app_id"] = currentSession.AppId;
                    allParameters["user_auth_token"] = currentSession.AuthToken;
                    // SECURITY (Wave-22): canonical Scrub.Secret leading-3 mask instead of trailing-4.
                    // Trailing chars enabled "is this leaked log fragment from THE token?" enumeration
                    // attacks; leading chars are meaningfully redacted by the Scrub helper.
                    _logger.Trace("🔐 Added authentication: app_id={0}, token={1}",
                        Scrub.Secret(currentSession.AppId, leadingVisible: 2),
                        Scrub.Secret(currentSession.AuthToken));
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
                    _requestSigner.Sign(endpoint, allParameters, currentSession.AppId, currentSession.AppSecret);
                }

                _logger.Debug("Making {0} request to {1}", method, endpoint);

                if (method == "GET")
                {
                    return await ExecuteCachedGetAsync<T>(endpoint, url, allParameters).ConfigureAwait(false);
                }

                // POST path — uncached, goes through Lidarr's IHttpClient directly.
                return await ExecuteUncachedAsync<T>(method, url, allParameters, data).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "API request failed for endpoint {0}", endpoint);
                throw;
            }
        }

        /// <summary>
        /// Routes a cacheable GET through the common <see cref="CachingHttpExecutor"/>. Cache lookup,
        /// soft-revalidate, stale-if-error and 404/410 terminal eviction are owned by the executor;
        /// rate limiting and HTTP retries remain in <see cref="QobuzHttpClient.ExecuteAsync"/>.
        /// </summary>
        private async Task<T> ExecuteCachedGetAsync<T>(string endpoint, string url, Dictionary<string, string> allParameters) where T : class
        {
            // Build the StreamingApiRequestBuilder. Qobuz puts auth in the query string, not headers,
            // so we encode allParameters as query params on a path-less endpoint URL. The CachingHttpExecutor
            // then drives the request through LidarrHttpClientInvoker -> IQobuzHttpClient.ExecuteAsync,
            // which applies adaptive rate limiting, per-host gating and HTTP retries as before.
            var builder = new StreamingApiRequestBuilder(url)
                .Get()
                .QueryParams(allParameters);

            // Log the final URL being called (without auth token for security)
            // SECURITY (Wave-23): mask appSecret-derivative signing params in the Debug log.
            // request_sig is a function of appSecret + endpoint + params + ts — logging it
            // at Debug enables offline correlation/timing analysis if logs leak. user_auth_token
            // is the obvious one but request_sig was previously missed.
            var safeParams = allParameters
                .Where(kv => kv.Key != "user_auth_token" && kv.Key != "request_sig")
                .Select(kv => $"{kv.Key}={kv.Value}");
            _logger.Debug("🚀 Final API call: {0}?{1}&user_auth_token=***&request_sig=***", url, string.Join("&", safeParams));

            var key = new CacheKey(endpoint, allParameters);
            var policy = ResolveCachePolicy(endpoint);

            // We deserialize after SendAsync (rather than via a ParseAsync hook) so that
            // JsonReaderException surfaces to the caller; the executor swallows hook exceptions
            // by design, but the legacy QobuzApiClient contract is to propagate Newtonsoft errors.
            var hooks = new CachingHttpHooks<object?>(
                OnHit: (kind, ck) =>
                {
                    if (kind != CacheHitKind.Miss)
                    {
                        _logger.Trace("Cache outcome for {0}: {1}", endpoint, kind);
                    }
                });

            var executor = GetOrCreateExecutor();
            var result = await executor.SendAsync(builder, key, policy, hooks).ConfigureAwait(false);

            // Surface non-success statuses as exceptions (mirrors legacy behavior where
            // HttpException from IQobuzHttpClient.ExecuteAsync would have been thrown).
            // The executor returns 5xx/4xx as Passthrough/EvictOnTerminal without throwing.
            var statusInt = (int)result.StatusCode;
            var bodyText = result.Body != null && result.Body.Length > 0
                ? System.Text.Encoding.UTF8.GetString(result.Body)
                : string.Empty;
            if (statusInt >= 400)
            {
                _logger.Error("❌ API Error Response: {0}", bodyText);
                HandleErrorResponse(result.StatusCode, bodyText);
            }

            // Log first 500 chars of response for debugging (sanitized)
            if (!string.IsNullOrEmpty(bodyText))
            {
                var sanitized = bodyText.Length > 500 ? bodyText.Substring(0, 500) + "..." : bodyText;
                _logger.Trace("📄 Response content: {0}", sanitized);
            }

            // Deserialize. JsonReaderException propagates by design — callers depend on this.
            var payload = JsonConvert.DeserializeObject<T>(bodyText);
            _logger.Debug("✅ Response deserialized to: {0}", typeof(T).Name);
            return payload;
        }

        /// <summary>
        /// Legacy uncached path for POST (and any other non-GET methods). Caching/conditional
        /// revalidation does not apply here.
        /// </summary>
        private async Task<T> ExecuteUncachedAsync<T>(string method, string url, Dictionary<string, string> allParameters, object? data) where T : class
        {
            var requestBuilder = _httpClient.BuildRequest(url, method);
            var request = requestBuilder.Build();

            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) && data != null)
            {
                request.SetContent(JsonConvert.SerializeObject(data));
                request.Headers.ContentType = "application/json";
            }

            var response = await _httpClient.ExecuteAsync(request).ConfigureAwait(false);

            _logger.Debug("📡 API response received: Status={0}, Length={1} chars",
                response.StatusCode, response.Content?.Length ?? 0);

            if (response.HasHttpError)
            {
                _logger.Error("❌ API Error Response: {0}", response.Content);
                HandleErrorResponse(response);
            }

            var result = JsonConvert.DeserializeObject<T>(response.Content);

            _logger.Debug("✅ Response deserialized to: {0}", typeof(T).Name);

            if (response.Content?.Length > 0)
            {
                var sanitized = response.Content.Length > 500 ? response.Content.Substring(0, 500) + "..." : response.Content;
                _logger.Trace("📄 Response content: {0}", sanitized);
            }

            return result;
        }

        private CachingHttpExecutor GetOrCreateExecutor()
        {
            if (_cachingExecutor != null) return _cachingExecutor;

            var invoker = new LidarrHttpClientInvoker(_httpClient, _logger);
            _cachingExecutor = new CachingHttpExecutor(
                invoker: invoker,
                cache: _responseCache,
                resiliencePolicy: ExecutorResiliencePolicy);
            return _cachingExecutor;
        }

        private CachePolicy ResolveCachePolicy(string endpoint)
        {
            if (_responseCache.ShouldCache(endpoint))
            {
                var duration = _responseCache.GetCacheDuration(endpoint);
                // Qobuz API does not emit ETag/Last-Modified, so conditional revalidation is unavailable.
                // HotHitMode = EnabledForFreshEntries enables the executor's hot-cache-hit fast path:
                // before invoking the resilience pipeline, any cached entry still within its nominal
                // Duration is returned as CacheHitKind.Hit without contacting the origin. This expresses
                // traditional "if cached and fresh, return cached" semantics directly, replacing the
                // earlier workaround that abused SoftRevalidateWindow=duration to achieve the same effect
                // on an API without validators. Stale-if-error and terminal eviction continue to provide
                // resilience for the past-duration / 5xx / 404 paths.
                // Source: Phase 5e common refinement motivated by qobuzarr Phase 3b adoption feedback.
                // Wave 17K: merged into a single .With(...) call. The previous .With(duration).WithExecutor(...)
                // chain was preserved across the Wave 19 deprecation but Common v1.14.0 removes WithExecutor.
                return CachePolicy.Default.With(
                    duration: duration,
                    hotHitMode: HotCacheHitMode.EnabledForFreshEntries,
                    staleIfErrorTtl: duration,
                    evictOnTerminalStatus: true);
            }

            return CachePolicy.Disabled;
        }


        private void HandleErrorResponse(HttpResponse response)
        {
            HandleErrorResponse(response.StatusCode, response.Content);
        }

        // Internal for testability — lets us assert that each HTTP status maps to a
        // user-actionable error message. Not part of the public API.
        internal static void HandleErrorResponse(HttpStatusCode statusCode, string? content)
        {
            var status = (int)statusCode;

            try
            {
                var errorResponse = string.IsNullOrEmpty(content)
                    ? null
                    : JsonConvert.DeserializeObject<QobuzErrorResponse>(content);
                var message = errorResponse?.Message ?? $"HTTP {status}";

                // Wave 62 UX: messages now name a remediation, not just the failure mode.
                throw status switch
                {
                    401 => new QobuzApiException(
                        "Authentication failed (HTTP 401). Verify your Qobuz email and password are correct, or re-authenticate from plugin settings if your session expired.",
                        status, "AuthenticationFailed"),
                    403 => new QobuzApiException(
                        "Access forbidden (HTTP 403). This usually means your Qobuz subscription does not include the requested resource (e.g. a Hi-Res track on a non-Hi-Res plan). Check your subscription tier or app credentials.",
                        status, "AccessForbidden"),
                    404 => new QobuzApiException("Resource not found", status, "NotFound"),
                    429 => new QobuzApiException(
                        "Qobuz is rate-limiting requests (HTTP 429). The plugin will wait and retry automatically. If this persists, slow down concurrent searches/downloads in plugin settings.",
                        status, "RateLimited"),
                    >= 500 => new QobuzApiException(
                        $"Qobuz server error (HTTP {status}). This is a temporary problem on Qobuz's side, not your configuration. The plugin will retry; check Qobuz status if it persists.",
                        status, "ServerError"),
                    _ => new QobuzApiException(message, status, "ApiError")
                };
            }
            catch (JsonException)
            {
                throw new QobuzApiException($"HTTP {status}: {content}", status, "UnknownError");
            }
        }


        /// <summary>
        /// Gets the streaming URL for a track
        /// </summary>
        public async Task<string> GetStreamingUrlAsync(string trackId, int formatId, CancellationToken cancellationToken = default)
        {
            var streamingInfo = await GetStreamingInfoAsync(trackId, formatId, cancellationToken).ConfigureAwait(false);
            return streamingInfo.Url;
        }

        public async Task<QobuzStreamResponse> GetStreamingInfoAsync(string trackId, int formatId, CancellationToken cancellationToken = default)
        {
            _logger.Debug("Getting streaming URL for track {0} with format {1}", trackId, formatId);

            var parameters = new Dictionary<string, string>
            {
                ["track_id"] = trackId,
                ["format_id"] = formatId.ToString(),
                ["intent"] = "stream"
            };

            var streamingInfo = await GetAsync<QobuzStreamResponse>("track/getFileUrl", parameters).ConfigureAwait(false);

            if (streamingInfo == null)
            {
                throw new InvalidOperationException("Qobuz streaming response was null.");
            }

            if (streamingInfo.Sample == true)
            {
                throw new InvalidOperationException("Qobuz returned a sample stream; subscription or quality may be restricted.");
            }

            if (streamingInfo.HasRestrictions())
            {
                if (streamingInfo.IsQualityFallbackOnly())
                {
                    _logger.Debug("Track {0}: Requested format {1} not available; using fallback format {2} ({3})",
                        trackId,
                        formatId,
                        streamingInfo.FormatId,
                        streamingInfo.GetQualityDescription());
                }
                else
                {
                    var message = streamingInfo.GetRestrictionMessage() ?? "Qobuz stream is restricted.";
                    throw new InvalidOperationException(message);
                }
            }

            if (string.IsNullOrWhiteSpace(streamingInfo.Url))
            {
                var details = streamingInfo.Message;
                if (string.IsNullOrWhiteSpace(details))
                {
                    details = streamingInfo.Status;
                }

                throw new InvalidOperationException($"Qobuz returned an empty stream URL. {details}".Trim());
            }

            _logger.Debug("Streaming URL acquired for track {0}: format={1}, mime={2}, expires={3}",
                trackId,
                streamingInfo.FormatId,
                streamingInfo.MimeType,
                streamingInfo.ExpiresAt?.ToString("O") ?? "unknown");

            return streamingInfo;
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
