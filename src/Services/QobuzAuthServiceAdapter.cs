using System;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Exceptions;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Adapter that bridges between IQobuzAuthenticationService and IQobuzAuthService
    /// for use with AuthTokenManager
    /// </summary>
    public class QobuzAuthServiceAdapter : IQobuzAuthService
    {
        private readonly IQobuzAuthenticationService _authenticationService;
        private readonly QobuzCredentials _credentials;
        private readonly Logger _logger;

        public QobuzAuthServiceAdapter(
            IQobuzAuthenticationService authenticationService,
            QobuzCredentials credentials,
            Logger logger = null)
        {
            _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
            _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            _logger = logger ?? LogManager.GetCurrentClassLogger();
        }

        public async Task<AuthResult> AuthenticateAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // First try to get cached session
                var cachedSession = _authenticationService.GetCachedSession();
                if (cachedSession != null && cachedSession.IsValid() && !cachedSession.NeedsRefresh())
                {
                    _logger.Debug("Using cached Qobuz session, expires at: {0}", cachedSession.ExpiresAt);
                    return ConvertToAuthResult(cachedSession);
                }

                // Authenticate with fresh credentials
                _logger.Debug("Authenticating with Qobuz API");
                var session = await _authenticationService.AuthenticateAsync(_credentials);
                
                if (session == null)
                {
                    throw new AuthenticationException("Authentication service returned null session");
                }

                // Store the new session for future use
                _authenticationService.StoreSession(session);
                
                _logger.Info("Successfully authenticated with Qobuz, session expires at: {0}", session.ExpiresAt);
                return ConvertToAuthResult(session);
            }
            catch (QobuzAuthenticationException ex)
            {
                _logger.Error(ex, "Qobuz authentication failed");
                throw new AuthenticationException($"Qobuz authentication failed: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unexpected error during authentication");
                throw new AuthenticationException($"Authentication error: {ex.Message}", ex);
            }
        }

        private AuthResult ConvertToAuthResult(QobuzSession session)
        {
            return new AuthResult
            {
                Token = session.AuthToken,
                ExpiryTime = session.ExpiresAt,
                UserId = session.UserId,
                UserType = session.Subscription?.Type ?? "unknown"
            };
        }
    }
}