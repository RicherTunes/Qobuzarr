using System;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Microsoft.Extensions.Logging;
// Use alias to resolve ambiguity
using IQobuzHttpClient = Lidarr.Plugin.Qobuzarr.API.Http.IQobuzHttpClient;

namespace QobuzCLI.Services
{
    /// <summary>
    /// Adapter that wraps the core QobuzAuthService to implement IQobuzAuthenticationService interface.
    /// This allows the CLI to expose plugin-compatible authentication service to commands.
    /// </summary>
    public class CliQobuzAuthenticationAdapter : IQobuzAuthenticationService
    {
        private QobuzSession? _cachedSession;
        private IQobuzHttpClient? _httpClient;
        private IQobuzLogger? _logger;

        public CliQobuzAuthenticationAdapter()
        {
            // Will be initialized later via SetDependencies
        }
        
        public void SetDependencies(IQobuzHttpClient httpClient, IQobuzLogger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<QobuzSession> AuthenticateAsync(QobuzCredentials credentials)
        {
            if (_httpClient == null || _logger == null)
            {
                throw new InvalidOperationException("Dependencies not initialized. Call SetDependencies first.");
            }
            
            // For CLI, we'll create a simple session based on the provided credentials
            // The actual authentication will be handled by the API client when making requests
            var session = new QobuzSession
            {
                AuthToken = credentials.MD5Password ?? credentials.AuthToken, // Use MD5Password or AuthToken
                AppId = credentials.AppId,
                AppSecret = credentials.AppSecret, // CRITICAL FIX: Must include AppSecret for signature generation!
                UserId = credentials.Email ?? credentials.UserId,
                ExpiresAt = DateTime.UtcNow.AddHours(24),
                CreatedAt = DateTime.UtcNow
            };
            
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

        public QobuzSession? GetCachedSession()
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