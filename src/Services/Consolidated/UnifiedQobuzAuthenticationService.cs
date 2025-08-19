using System;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Exceptions;
using Lidarr.Plugin.Qobuzarr.Authentication;

namespace Lidarr.Plugin.Qobuzarr.Services.Consolidated
{
    /// <summary>
    /// Unified authentication service consolidating all auth-related functionality
    /// Replaces: QobuzAuthenticationService, QobuzAuthenticationManager, 
    /// QobuzAuthService, AuthTokenManager, QobuzAuthServiceAdapter
    /// </summary>
    public class UnifiedQobuzAuthenticationService : IQobuzAuthenticationService
    {
        private readonly IQobuzApiClient _apiClient;
        private readonly Logger _logger;
        private QobuzSession _cachedSession;
        private readonly object _sessionLock = new object();

        public UnifiedQobuzAuthenticationService(IQobuzApiClient apiClient, Logger logger)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<QobuzSession> AuthenticateAsync(QobuzCredentials credentials)
        {
            if (credentials == null)
                throw new ArgumentNullException(nameof(credentials));

            try
            {
                _logger.Info("Authenticating with Qobuz...");

                QobuzSession session;
                
                // Determine authentication method
                if (!string.IsNullOrEmpty(credentials.Email) && !string.IsNullOrEmpty(credentials.MD5Password))
                {
                    session = await AuthenticateWithEmailAsync(credentials).ConfigureAwait(false);
                }
                else if (!string.IsNullOrEmpty(credentials.UserId) && !string.IsNullOrEmpty(credentials.AuthToken))
                {
                    session = await AuthenticateWithTokenAsync(credentials).ConfigureAwait(false);
                }
                else
                {
                    throw QobuzAuthenticationException.MissingCredentials();
                }

                // Cache the session
                lock (_sessionLock)
                {
                    _cachedSession = session;
                }

                _logger.Info("Successfully authenticated as user {0}", session.UserId);
                return session;
            }
            catch (Exception ex) when (!(ex is QobuzAuthenticationException))
            {
                _logger.Error(ex, "Authentication failed");
                throw new QobuzAuthenticationException("Authentication failed", AuthenticationFailureType.TemporaryFailure, ex, true);
            }
        }

        public QobuzSession GetCachedSession()
        {
            lock (_sessionLock)
            {
                if (_cachedSession != null && !_cachedSession.NeedsRefresh())
                {
                    return _cachedSession;
                }
                return null;
            }
        }

        public void ClearSession()
        {
            lock (_sessionLock)
            {
                _cachedSession = null;
            }
            _logger.Debug("Session cleared");
        }

        public async Task<QobuzSession> RefreshSessionAsync(string refreshToken)
        {
            // Qobuz doesn't support traditional refresh tokens
            throw new NotSupportedException("Qobuz does not support refresh tokens. Re-authentication is required.");
        }

        public async Task<QobuzSession> RefreshSessionAsync(QobuzSession existingSession)
        {
            if (existingSession == null)
                throw new ArgumentNullException(nameof(existingSession));

            try
            {
                _logger.Debug("Refreshing session for user {0}", existingSession.UserId);
                
                // Create credentials from existing session
                var credentials = new QobuzCredentials
                {
                    UserId = existingSession.UserId,
                    AuthToken = existingSession.AuthToken,
                    AppId = existingSession.AppId,
                    AppSecret = existingSession.AppSecret
                };

                return await AuthenticateWithTokenAsync(credentials).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to refresh session");
                throw new QobuzAuthenticationException("Session refresh failed", AuthenticationFailureType.TemporaryFailure, ex, true);
            }
        }

        public async Task<bool> ValidateSessionAsync(QobuzSession session)
        {
            if (session == null)
                return false;

            try
            {
                // Make a simple API call to validate the session
                await _apiClient.GetAsync<dynamic>("user/profile").ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Session validation failed for user {0}", session.UserId);
                return false;
            }
        }

        public void StoreSession(QobuzSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            lock (_sessionLock)
            {
                _cachedSession = session;
            }
            
            _logger.Debug("Session stored for user {0}", session.UserId);
        }

        public bool IsAuthenticated()
        {
            var session = GetCachedSession();
            return session != null && !session.NeedsRefresh();
        }

        public static string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                return string.Empty;

            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var inputBytes = System.Text.Encoding.UTF8.GetBytes(password);
                var hashBytes = md5.ComputeHash(inputBytes);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }
        }

        private async Task<QobuzSession> AuthenticateWithEmailAsync(QobuzCredentials credentials)
        {
            var loginData = new
            {
                email = credentials.Email,
                password = credentials.MD5Password,
                app_id = credentials.AppId,
                app_secret = credentials.AppSecret
            };

            var response = await _apiClient.PostAsync<dynamic>("user/login", loginData).ConfigureAwait(false);
            
            return ParseSessionFromResponse(response, credentials);
        }

        private async Task<QobuzSession> AuthenticateWithTokenAsync(QobuzCredentials credentials)
        {
            var loginData = new
            {
                user_id = credentials.UserId,
                user_auth_token = credentials.AuthToken,
                app_id = credentials.AppId,
                app_secret = credentials.AppSecret
            };

            var response = await _apiClient.PostAsync<dynamic>("user/login", loginData).ConfigureAwait(false);
            
            return ParseSessionFromResponse(response, credentials);
        }

        private QobuzSession ParseSessionFromResponse(dynamic response, QobuzCredentials credentials)
        {
            if (response == null)
                throw new QobuzAuthenticationException("Empty response from Qobuz API", AuthenticationFailureType.TemporaryFailure);

            var session = new QobuzSession
            {
                UserId = response.user?.id?.ToString(),
                AuthToken = response.user_auth_token?.ToString(),
                AppId = credentials.AppId,
                AppSecret = credentials.AppSecret,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1) // Default 1 hour expiry
            };

            // Parse subscription info if available
            if (response.user?.subscription != null)
            {
                session.Subscription = new QobuzSubscription
                {
                    SubscriptionType = response.user.subscription.offer?.ToString(),
                    ExpiryDate = ParseDateTime(response.user.subscription.end_date?.ToString())
                };
            }

            return session;
        }

        private DateTime? ParseDateTime(string dateStr)
        {
            if (string.IsNullOrEmpty(dateStr))
                return null;

            if (DateTime.TryParse(dateStr, out var result))
                return result;

            return null;
        }
    }
}