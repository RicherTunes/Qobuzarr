using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Common.Cache;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Security;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Lidarr.Plugin.Qobuzarr.Services.Interfaces;
using Lidarr.Plugin.Common.Interfaces;
using Lidarr.Plugin.Common.Services.Authentication;

namespace Lidarr.Plugin.Qobuzarr.Authentication
{
    /// <summary>
    /// Implements Qobuz API authentication functionality including session management and credential validation.
    /// Supports both email/password and user ID/token authentication methods.
    /// </summary>
    public class QobuzAuthenticationService : IQobuzAuthenticationService,
        IStreamingAuthenticationService<QobuzSession, QobuzCredentials>,
        IStreamingTokenProvider,
        IStreamingTokenAuthenticationService<QobuzSession, QobuzCredentials>
    {
        private readonly IHttpClient _httpClient;
        private readonly IConfigService _configService;
        private readonly ILocalizationService _localizationService;
        private readonly ICacheManager _cacheManager;
        private readonly Logger _logger;
        private readonly ICredentialValidator _credentialValidator;

        private const string LOGIN_ENDPOINT = "/user/login";
        private const string SESSION_CACHE_KEY = "qobuz_session";
        // API base, app ID and secret moved to QobuzConstants

        private const string LidarrUserAgent = "Lidarr";
        private const string BrowserUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        private static readonly System.Net.Http.HttpClient RawHttpClient = CreateRawHttpClient();
        private static readonly System.Net.Http.HttpClient WebPlayerHttpClient = CreateWebPlayerHttpClient();

        private readonly ICached<QobuzSession> _sessionCache;

        public QobuzAuthenticationService(IHttpClient httpClient,
                                        IConfigService configService,
                                        ILocalizationService localizationService,
                                        ICacheManager cacheManager,
                                        Logger logger,
                                        ICredentialValidator credentialValidator = null)
        {
            _httpClient = httpClient;
            _configService = configService;
            _localizationService = localizationService;
            _cacheManager = cacheManager;
            _logger = logger;
            _credentialValidator = credentialValidator ?? new CredentialValidator(logger);
            _sessionCache = _cacheManager.GetCache<QobuzSession>(GetType());
        }

        private static System.Net.Http.HttpClient CreateRawHttpClient()
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                MaxConnectionsPerServer = 8
            };

            return new System.Net.Http.HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
        }

        private static System.Net.Http.HttpClient CreateWebPlayerHttpClient()
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                MaxConnectionsPerServer = 2
            };

            var client = new System.Net.Http.HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

            return client;
        }

        private static string BuildUrl(string baseUrl, Dictionary<string, string> queryParams)
        {
            if (queryParams == null || queryParams.Count == 0)
            {
                return baseUrl;
            }

            var parts = new List<string>(queryParams.Count);
            foreach (var (key, value) in queryParams)
            {
                if (string.IsNullOrEmpty(key) || value == null)
                {
                    continue;
                }

                parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
            }

            if (parts.Count == 0)
            {
                return baseUrl;
            }

            return $"{baseUrl}?{string.Join("&", parts)}";
        }

        // Shared IStreamingAuthenticationService implementation (adapters)
        async Task<AuthResult<QobuzSession>> IStreamingAuthenticationService<QobuzSession, QobuzCredentials>.AuthenticateAsync(QobuzCredentials credentials)
        {
            try
            {
                var session = await AuthenticateAsync(credentials).ConfigureAwait(false);
                return AuthResult<QobuzSession>.Successful(session);
            }
            catch (Exception ex)
            {
                return AuthResult<QobuzSession>.Failed(ex.Message);
            }
        }

        async Task<QobuzSession?> IStreamingAuthenticationService<QobuzSession, QobuzCredentials>.GetValidSessionAsync()
        {
            var session = GetCachedSession();
            if (session == null) return null;
            var valid = await ValidateSessionAsync(session).ConfigureAwait(false);
            return valid ? session : null;
        }

        async Task<bool> IStreamingAuthenticationService<QobuzSession, QobuzCredentials>.ValidateSessionAsync(QobuzSession session)
            => await ValidateSessionAsync(session).ConfigureAwait(false);

        Task<QobuzSession?> IStreamingAuthenticationService<QobuzSession, QobuzCredentials>.RefreshSessionAsync(QobuzSession session)
            => Task.FromResult<QobuzSession?>(null); // Not supported by Qobuz

        Task IStreamingAuthenticationService<QobuzSession, QobuzCredentials>.RevokeSessionAsync(QobuzSession session)
        {
            ClearSession();
            return Task.CompletedTask;
        }

        QobuzSession IStreamingAuthenticationService<QobuzSession, QobuzCredentials>.GetCachedSession() => GetCachedSession();
        void IStreamingAuthenticationService<QobuzSession, QobuzCredentials>.ClearSession() => ClearSession();
        void IStreamingAuthenticationService<QobuzSession, QobuzCredentials>.StoreSession(QobuzSession session) => StoreSession(session);

        // Shared IStreamingTokenProvider implementation (token-centric contract)
        public async Task<string> GetAccessTokenAsync()
        {
            var session = GetCachedSession();
            if (session == null)
                return null;
            var valid = await ValidateSessionAsync(session).ConfigureAwait(false);
            return valid ? session.AuthToken : null;
        }

        public Task<string> RefreshTokenAsync()
        {
            // Qobuz does not support token refresh; full re-auth is required
            return Task.FromResult<string>(null);
        }

        public async Task<bool> ValidateTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            var session = GetCachedSession();
            if (session == null) return false;
            if (!string.Equals(session.AuthToken, token, StringComparison.Ordinal)) return false;
            return await ValidateSessionAsync(session).ConfigureAwait(false);
        }

        public DateTime? GetTokenExpiration(string token)
        {
            var session = GetCachedSession();
            if (session == null) return null;
            if (!string.Equals(session.AuthToken, token, StringComparison.Ordinal)) return null;
            return session.ExpiresAt;
        }

        public void ClearAuthenticationCache() => ClearSession();
        public bool SupportsRefresh => false;
        public string ServiceName => "Qobuz";

        public async Task<QobuzSession> AuthenticateAsync(QobuzCredentials credentials)
        {
            try
            {
                // Enhanced validation using comprehensive credential validator
                var validationResult = _credentialValidator.ValidateCredentials(credentials);
                if (!validationResult.IsValid)
                {
                    var errorMessage = string.Join("; ", validationResult.Errors);
                    throw new QobuzAuthenticationException($"Credential validation failed: {errorMessage}");
                }

                // Use sanitized values from validator for enhanced security
                var email = validationResult.SanitizedEmail ?? credentials.Email;
                var userId = validationResult.SanitizedUserId ?? credentials.UserId;
                var authToken = validationResult.SanitizedAuthToken ?? credentials.AuthToken;
                var appId = validationResult.SanitizedAppId ?? credentials.AppId;
                var appSecret = validationResult.SanitizedAppSecret ?? credentials.AppSecret;

                // Use sanitized app credentials or environment variables
                // If empty, QobuzApiSharp library will use its built-in defaults
                appId = appId.IsNotNullOrWhiteSpace() ? appId : 
                           Environment.GetEnvironmentVariable(QobuzConstants.Authentication.AppIdEnvironmentVariable);
                appSecret = appSecret.IsNotNullOrWhiteSpace() ? appSecret : 
                               Environment.GetEnvironmentVariable(QobuzConstants.Authentication.AppSecretEnvironmentVariable);
                
                // Both must be provided together if using custom credentials
                if (!string.IsNullOrWhiteSpace(appId) && string.IsNullOrWhiteSpace(appSecret))
                {
                    _logger.Warn("Custom App ID provided without App Secret. Using default credentials to avoid authentication failures. To use custom credentials, provide both App ID and App Secret as a matching pair.");
                    appId = null; // Reset to use defaults
                }
                else if (string.IsNullOrWhiteSpace(appId) && !string.IsNullOrWhiteSpace(appSecret))
                {
                    _logger.Warn("Custom App Secret provided without App ID. Using default credentials to avoid authentication failures. To use custom credentials, provide both App ID and App Secret as a matching pair.");
                    appSecret = null; // Reset to use defaults
                }

                QobuzSession session;

                if (credentials.IsEmailAuth())
                {
                    session = await AuthenticateWithEmailAsync(credentials.Email, credentials.MD5Password, appId, appSecret).ConfigureAwait(false);
                }
                else if (credentials.IsTokenAuth())
                {
                    session = await AuthenticateWithTokenAsync(credentials.UserId, credentials.AuthToken, appId, appSecret).ConfigureAwait(false);
                }
                else
                {
                    throw new InvalidOperationException("No valid authentication method provided");
                }

                // Store the session
                StoreSession(session);
                
                _logger.Info("✅ Successfully authenticated with Qobuz API using {0}", 
                    credentials.IsEmailAuth() ? "email/password" : "token");
                
                // Check if subscription supports the desired quality levels
                if (session.Subscription != null)
                {
                    CheckSubscriptionCapabilities(session.Subscription);
                }

                return session;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to authenticate with Qobuz API");
                throw;
            }
        }

        private async Task<QobuzSession> AuthenticateWithEmailAsync(string email, string md5Password, string appId, string appSecret)
        {
            // Sanitize email input to prevent injection attacks
            email = InputSanitizer.SanitizeEmail(email);
            
            // Use fallback chain: provided -> environment -> dynamic fetch from web player
            var effectiveAppId = !string.IsNullOrWhiteSpace(appId) ? appId : 
                                Environment.GetEnvironmentVariable(QobuzConstants.Authentication.AppIdEnvironmentVariable);
            var effectiveAppSecret = !string.IsNullOrWhiteSpace(appSecret) ? appSecret : 
                                    Environment.GetEnvironmentVariable(QobuzConstants.Authentication.AppSecretEnvironmentVariable);
            
            // If no credentials provided, try to get them dynamically like TrevTV's plugin
            if (string.IsNullOrWhiteSpace(effectiveAppId) || string.IsNullOrWhiteSpace(effectiveAppSecret))
            {
                var (dynamicAppId, dynamicAppSecret) = await GetDynamicCredentialsAsync().ConfigureAwait(false);
                effectiveAppId = effectiveAppId ?? dynamicAppId;
                effectiveAppSecret = effectiveAppSecret ?? dynamicAppSecret;
            }
            
            // Sanitize app credentials
            if (!string.IsNullOrWhiteSpace(effectiveAppId))
                effectiveAppId = InputSanitizer.SanitizeAppId(effectiveAppId);
            if (!string.IsNullOrWhiteSpace(effectiveAppSecret))
                effectiveAppSecret = InputSanitizer.ValidateAppSecret(effectiveAppSecret);
            
            var requestBuilder = new HttpRequestBuilder($"{QobuzConstants.Api.BaseUrl}{LOGIN_ENDPOINT}")
                .AddQueryParam("app_id", effectiveAppId)
                .AddQueryParam("email", email)
                .AddQueryParam("password", md5Password)
                .SetHeader("User-Agent", QobuzConstants.Api.UserAgent);

            var request = requestBuilder.Build();
            var response = await _httpClient.ExecuteAsync(request).ConfigureAwait(false);

            if (response.HasHttpError)
            {
                throw new HttpException(request, response);
            }

            var loginResponse = JsonConvert.DeserializeObject<QobuzLoginResponse>(response.Content);

            if (!loginResponse.IsSuccess)
            {
                throw new InvalidOperationException($"Authentication failed: {loginResponse.Message}");
            }

            var subscription = loginResponse.User?.Subscription?.ToSubscription();
            
            // Log subscription tier
            if (subscription != null)
            {
                _logger.Info("User subscription: {0}", subscription.GetTierDescription());
            }
            
            return QobuzSession.CreateSession(
                loginResponse.User.Id,
                loginResponse.UserAuthToken,
                effectiveAppId,
                effectiveAppSecret,
                subscription
            );
        }

        private async Task<QobuzSession> AuthenticateWithTokenAsync(string userId, string authToken, string appId, string appSecret = null)
        {
            // Sanitize user inputs
            userId = InputSanitizer.SanitizeUserId(userId);
            authToken = InputSanitizer.SanitizeAuthToken(authToken);
            
            // Use fallback chain: provided -> environment -> dynamic fetch from web player
            var effectiveAppId = !string.IsNullOrWhiteSpace(appId) ? appId : 
                                Environment.GetEnvironmentVariable(QobuzConstants.Authentication.AppIdEnvironmentVariable);
            var effectiveAppSecret = !string.IsNullOrWhiteSpace(appSecret) ? appSecret : 
                                    Environment.GetEnvironmentVariable(QobuzConstants.Authentication.AppSecretEnvironmentVariable);
            
            // If no credentials provided, try to get them dynamically like TrevTV's plugin
            if (string.IsNullOrWhiteSpace(effectiveAppId) || string.IsNullOrWhiteSpace(effectiveAppSecret))
            {
                var (dynamicAppId, dynamicAppSecret) = await GetDynamicCredentialsAsync().ConfigureAwait(false);
                effectiveAppId = effectiveAppId ?? dynamicAppId;
                effectiveAppSecret = effectiveAppSecret ?? dynamicAppSecret;
            }
            
            // Sanitize app credentials
            if (!string.IsNullOrWhiteSpace(effectiveAppId))
                effectiveAppId = InputSanitizer.SanitizeAppId(effectiveAppId);
            if (!string.IsNullOrWhiteSpace(effectiveAppSecret))
                effectiveAppSecret = InputSanitizer.ValidateAppSecret(effectiveAppSecret);
            
            // For token auth, we create a session and validate it
            var session = QobuzSession.CreateSession(userId, authToken, effectiveAppId, effectiveAppSecret);
            
            // Validate the token by making a test API call
            var isValid = await ValidateSessionAsync(session).ConfigureAwait(false);
            
            if (!isValid)
            {
                throw new InvalidOperationException("Invalid user ID or auth token");
            }

            return session;
        }

        public async Task<QobuzSession> RefreshSessionAsync(string refreshToken)
        {
            // Qobuz doesn't have a traditional refresh token mechanism
            // Sessions are valid for 24 hours and need re-authentication
            throw new NotSupportedException("Qobuz does not support session refresh. Re-authentication is required.");
        }

        public async Task<bool> ValidateSessionAsync(QobuzSession session)
        {
            try
            {
                if (session == null || !session.IsValid())
                {
                    return false;
                }

                var url = BuildUrl($"{QobuzConstants.Api.BaseUrl}{LOGIN_ENDPOINT}", new Dictionary<string, string>
                {
                    ["app_id"] = session.AppId,
                    ["user_id"] = session.UserId,
                    ["user_auth_token"] = session.AuthToken
                });

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd(LidarrUserAgent);

                using var response = await RawHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false);

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Session validation failed");
                return false;
            }
        }

        public QobuzSession GetCachedSession()
        {
            try
            {
                var session = _sessionCache.Find(SESSION_CACHE_KEY);      

                if (session == null || !session.IsValid())
                {
                    return null;
                }

                return CloneSession(session);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to retrieve cached session");   
                return null;
            }
        }

        public void StoreSession(QobuzSession session)
        {
            try
            {
                if (session == null || !session.IsValid())
                {
                    _sessionCache.Remove(SESSION_CACHE_KEY);
                    return;
                }

                _sessionCache.Set(SESSION_CACHE_KEY, CloneSession(session), TimeSpan.FromHours(24));
                _logger.Debug("Session stored in cache");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to store session in cache");
            }
        }

        private static QobuzSession CloneSession(QobuzSession session)
        {
            return new QobuzSession
            {
                UserId = session.UserId,
                AuthToken = session.AuthToken,
                ExpiresAt = session.ExpiresAt,
                AppId = session.AppId,
                AppSecret = session.AppSecret,
                CreatedAt = session.CreatedAt,
                Subscription = session.Subscription == null
                    ? null
                    : new QobuzSubscription
                    {
                        Type = session.Subscription.Type,
                        IsHiRes = session.Subscription.IsHiRes,
                        MaxSampleRate = session.Subscription.MaxSampleRate,
                        MaxBitDepth = session.Subscription.MaxBitDepth,
                        CanStream = session.Subscription.CanStream,
                        CanDownload = session.Subscription.CanDownload
                    }
            };
        }

        // Optional: factory to create a StreamingTokenManager wired to this service
        public StreamingTokenManager<QobuzSession, QobuzCredentials> CreateTokenManager()
        {
            return new StreamingTokenManager<QobuzSession, QobuzCredentials>(this, new NoopLogger<StreamingTokenManager<QobuzSession, QobuzCredentials>>() );
        }

        private sealed class NoopLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopDisposable.Instance;
            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;
            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }

            private sealed class NoopDisposable : IDisposable { public static readonly NoopDisposable Instance = new NoopDisposable(); public void Dispose() { } }
        }

        public void ClearSession()
        {
            try
            {
                _sessionCache.Remove(SESSION_CACHE_KEY);
                _logger.Debug("Session cleared from cache");
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to clear session from cache");
            }
        }


        /// <summary>
        /// Check if subscription supports commonly requested quality levels
        /// </summary>
        private void CheckSubscriptionCapabilities(QobuzSubscription subscription)
        {
            if (!subscription.SupportsQuality(7))
            {
                _logger.Warn("Subscription does not support Hi-Res audio - upgrade required for quality above CD");
            }
        }

        /// <summary>
        /// Dynamically fetch working App ID and Secret from Qobuz web player (using QobuzApiSharp's exact method)
        /// </summary>
        private async Task<(string appId, string appSecret)> GetDynamicCredentialsAsync()
        {
            try
            {
                _logger.Debug("Attempting to fetch dynamic credentials from Qobuz web player using QobuzApiSharp method");

                // Step 1: Fetch the login page to get bundle.js URL      
                var loginHtml = await WebPlayerHttpClient.GetStringAsync("https://play.qobuz.com/login")
                    .ConfigureAwait(false);

                // Step 2: Extract bundle.js URL using QobuzApiSharp's regex pattern
                var bundleMatch = System.Text.RegularExpressions.Regex.Match(
                    loginHtml,
                    @"<script\s+src=""(?<bundleJS>/resources/\d+\.\d+\.\d+-[a-z]\d{3}/bundle\.js)""",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(250));

                if (!bundleMatch.Success)
                {
                    throw new InvalidOperationException("Failed to find bundle.js link in Qobuz web player");
                }

                var bundleSuffix = bundleMatch.Groups["bundleJS"].Value;
                var bundleUrl = "https://play.qobuz.com" + bundleSuffix;  

                _logger.Debug($"Found bundle.js URL: {bundleUrl}");

                // Step 3: Fetch bundle.js
                var bundleContent = await WebPlayerHttpClient.GetStringAsync(bundleUrl)
                    .ConfigureAwait(false);

                // Step 4: Extract App ID using QobuzApiSharp's regex pattern
                var appIdMatch = System.Text.RegularExpressions.Regex.Match(
                    bundleContent,
                    "production:{api:{appId:\"(?<appID>.*?)\",appSecret:",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(250));
                
                if (!appIdMatch.Success)
                {
                    throw new InvalidOperationException("Failed to find production app_id in bundle.js");
                }
                
                var appId = appIdMatch.Groups[1].Value;
                
                // Step 5: Extract App Secret using QobuzApiSharp's complex method
                var appSecret = ExtractAppSecretFromBundle(bundleContent);
                
                if (string.IsNullOrEmpty(appSecret))
                {
                    throw new InvalidOperationException("Failed to extract app_secret from bundle.js");
                }
                
                _logger.Info($"Successfully extracted dynamic credentials: App ID {appId}");
                return (appId, appSecret);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to fetch dynamic credentials from Qobuz web player");
                throw new InvalidOperationException("Dynamic credential retrieval failed. Please provide custom App ID and Secret in settings.", ex);
            }
        }

        /// <summary>
        /// Extract App Secret from bundle.js using QobuzApiSharp's complex algorithm
        /// </summary>
        private string ExtractAppSecretFromBundle(string bundleContent)
        {
            try
            {
                // Step 1: Find seed and timezone pattern (QobuzApiSharp's exact regex)
                const string seedAndTimezonePattern = "\\):[a-z]\\.initialSeed\\(\"(?<seed>.*?)\",window\\.utimezone\\.(?<timezone>[a-z]+)\\)";
                var seedAndTimezoneMatch = System.Text.RegularExpressions.Regex.Match(bundleContent, seedAndTimezonePattern);
                
                if (!seedAndTimezoneMatch.Success)
                {
                    throw new InvalidOperationException("Failed to find seed and timezone in bundle.js");
                }
                
                var seed = seedAndTimezoneMatch.Groups[1].Value;
                var productionTimezone = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(seedAndTimezoneMatch.Groups[2].Value);
                
                _logger.Debug($"Found seed: {seed}, timezone: {productionTimezone}");
                
                // Step 2: Find info and extras for the production timezone
                var infoAndExtrasPattern = "name:\"[^\"]*/" + productionTimezone + "\",info:\"(?<info>[^\"]*)\",extras:\"(?<extras>[^\"]*)\"";
                var infoAndExtrasMatch = System.Text.RegularExpressions.Regex.Match(bundleContent, infoAndExtrasPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                if (!infoAndExtrasMatch.Success)
                {
                    throw new InvalidOperationException($"Failed to find info and extras for timezone {productionTimezone} in bundle.js");
                }
                
                var info = infoAndExtrasMatch.Groups[1].Value;
                var extras = infoAndExtrasMatch.Groups[2].Value;
                
                _logger.Debug($"Found info: {info}, extras: {extras}");
                
                // Step 3: Concatenate seed, info, and extras
                var base64EncodedAppSecret = seed + info + extras;
                
                // Step 4: Remove last 44 characters
                if (base64EncodedAppSecret.Length <= 44)
                {
                    throw new InvalidOperationException("Concatenated seed+info+extras string is too short");
                }
                
                base64EncodedAppSecret = base64EncodedAppSecret.Remove(base64EncodedAppSecret.Length - 44, 44);
                
                // Step 5: Base64 decode to get app secret bytes
                var decodedAppSecretBytes = Convert.FromBase64String(base64EncodedAppSecret);
                
                // Step 6: UTF-8 decode to get final app secret string
                var appSecret = Encoding.UTF8.GetString(decodedAppSecretBytes);
                
                _logger.Debug($"Successfully extracted app secret (length: {appSecret.Length})");
                return appSecret;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to extract app secret from bundle.js");
                return null;
            }
        }

        /// <summary>
        /// Hash a password using MD5 (as required by Qobuz API)
        /// </summary>
        public static string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                return string.Empty;

            return HashingUtility.ComputeMD5Hash(password);
        }
    }
}
