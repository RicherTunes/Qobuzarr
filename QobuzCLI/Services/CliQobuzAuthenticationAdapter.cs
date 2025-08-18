using System;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Core;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace QobuzCLI.Services
{
    /// <summary>
    /// Adapter that wraps the core QobuzAuthService to implement IQobuzAuthenticationService interface.
    /// This allows the CLI to expose plugin-compatible authentication service to commands.
    /// </summary>
    public class CliQobuzAuthenticationAdapter : IQobuzAuthenticationService
    {
        private readonly QobuzAuthService _coreAuthService;
        private QobuzSession? _cachedSession;

        public CliQobuzAuthenticationAdapter(QobuzAuthService coreAuthService)
        {
            _coreAuthService = coreAuthService ?? throw new ArgumentNullException(nameof(coreAuthService));
        }

        public async Task<QobuzSession> AuthenticateAsync(QobuzCredentials credentials)
        {
            var session = await _coreAuthService.AuthenticateAsync(credentials);
            _cachedSession = session;
            return session;
        }

        public Task<QobuzSession> RefreshSessionAsync(string refreshToken)
        {
            // Qobuz doesn't support refresh tokens
            throw new NotSupportedException("Qobuz does not support refresh tokens. Re-authentication is required after session expiry.");
        }

        public async Task<bool> ValidateSessionAsync(QobuzSession session)
        {
            // The core auth service doesn't have session validation, so we implement a basic check
            if (session == null || string.IsNullOrEmpty(session.AuthToken))
                return false;
                
            // Check if session has expired (Qobuz sessions are typically valid for 24 hours)
            if (session.ExpiresAt < DateTime.UtcNow)
                return false;
                
            // For now, assume session is valid if it has required fields and hasn't expired
            // In a more robust implementation, we could make a test API call
            return true;
        }

        public QobuzSession GetCachedSession()
        {
            return _cachedSession;
        }

        public void ClearSession()
        {
            _cachedSession = null;
        }

        public void StoreSession(QobuzSession session)
        {
            _cachedSession = session;
        }
    }
}