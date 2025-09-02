using System;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Services.Interfaces;

namespace QobuzCLI.Services.Adapters
{
    // Minimal ISessionManager implementation for CLI usage.
    // Stores the current session in memory and delegates authentication to the provided auth service.
    internal class CliSessionManager : ISessionManager
    {
        private readonly Lidarr.Plugin.Qobuzarr.Authentication.IQobuzAuthenticationService _authService;
        private QobuzSession? _session;
        private readonly object _lock = new object();

        public CliSessionManager(Lidarr.Plugin.Qobuzarr.Authentication.IQobuzAuthenticationService authService)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _session = _authService.GetCachedSession();
        }

        public async Task<QobuzSession?> CreateSessionAsync(QobuzCredentials credentials, CancellationToken cancellationToken = default)
        {
            var newSession = await _authService.AuthenticateAsync(credentials).ConfigureAwait(false);
            lock (_lock)
            {
                _session = newSession;
            }
            return newSession;
        }

        public Task<QobuzSession?> GetCurrentSessionAsync(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                return Task.FromResult(_session);
            }
        }

        public async Task<bool> IsSessionValidAsync(QobuzSession session, CancellationToken cancellationToken = default)
        {
            if (session == null) return false;
            return await _authService.ValidateSessionAsync(session).ConfigureAwait(false);
        }

        public Task InvalidateSessionAsync(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _session = null;
            }
            _authService.ClearSession();
            return Task.CompletedTask;
        }

        public Task<QobuzSession?> RefreshSessionAsync(QobuzSession session, CancellationToken cancellationToken = default)
        {
            // Qobuz does not support refresh; caller should re-authenticate.
            return Task.FromResult<QobuzSession?>(null);
        }

        public bool HasValidSession()
        {
            lock (_lock)
            {
                return _session != null && !string.IsNullOrWhiteSpace(_session.AuthToken) && _session.ExpiresAt > DateTime.UtcNow;
            }
        }
    }
}

