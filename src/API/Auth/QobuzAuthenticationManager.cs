using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Authentication;

namespace Lidarr.Plugin.Qobuzarr.API.Auth
{
    /// <summary>
    /// Implementation of Qobuz authentication session management.
    /// Handles session validation, renewal notifications, and expiration tracking.
    /// </summary>
    public class QobuzAuthenticationManager : IQobuzAuthenticationManager
    {
        private readonly Logger _logger;
        private readonly IQobuzAuthenticationService? _authService;
        private QobuzSession? _currentSession;
        private readonly object _sessionLock = new object();

        public event EventHandler<SessionExpiringEventArgs>? SessionExpiring;
        public event EventHandler? SessionExpired;

        /// <inheritdoc/>
        public QobuzSession? CurrentSession
        {
            get
            {
                lock (_sessionLock)
                {
                    return _currentSession;
                }
            }
        }

        public QobuzAuthenticationManager(Logger logger, IQobuzAuthenticationService? authService = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _authService = authService;
        }

        /// <inheritdoc/>
        public void SetSession(QobuzSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            lock (_sessionLock)
            {
                _currentSession = session;
                _logger.Debug("Session set for user, expires at {0}", session.ExpiresAt);
            }
        }

        /// <inheritdoc/>
        public void ClearSession()
        {
            lock (_sessionLock)
            {
                _currentSession = null;
                _logger.Debug("Session cleared");
            }
        }

        /// <inheritdoc/>
        public bool HasValidSession()
        {
            lock (_sessionLock)
            {
                return _currentSession?.IsValid() == true;
            }
        }

        /// <inheritdoc/>
        public bool NeedsRenewal()
        {
            lock (_sessionLock)
            {
                if (_currentSession == null)
                    return false;

                // Check if session needs refresh (30 minutes before expiry)
                return _currentSession.NeedsRefresh();
            }
        }

        /// <inheritdoc/>
        public async Task ValidateAndRenewIfNeededAsync(CancellationToken cancellationToken = default)
        {
            QobuzSession? session;
            lock (_sessionLock)
            {
                session = _currentSession;
            }

            if (session == null)
                return;

            try
            {
                // Check if session has expired
                if (!session.IsValid())
                {
                    _logger.Debug("Session has expired, clearing invalid session");
                    ClearSession();
                    OnSessionExpired();
                    return;
                }

                // Check if session needs refresh
                if (session.NeedsRefresh())
                {
                    _logger.Debug("Session expires soon at {0}", session.ExpiresAt);

                    // Raise expiring event with time remaining
                    var timeRemaining = session.ExpiresAt - DateTime.UtcNow;
                    OnSessionExpiring(session, timeRemaining);

                    // Note: Qobuz doesn't support refresh tokens, so we can't renew automatically
                    // We can only notify that the session is expiring
                    if (session.ExpiresWithin(TimeSpan.FromMinutes(5)))
                    {
                        _logger.Warn("Session expires in less than 5 minutes - authentication will be required soon");
                    }
                    else
                    {
                        _logger.Info("Session expires at {0} - re-authentication will be required soon", session.ExpiresAt);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error validating session - clearing to force re-authentication");
                ClearSession();
                OnSessionExpired();
            }
        }

        private void OnSessionExpiring(QobuzSession session, TimeSpan timeRemaining)
        {
            SessionExpiring?.Invoke(this, new SessionExpiringEventArgs(session, timeRemaining));
        }

        private void OnSessionExpired()
        {
            SessionExpired?.Invoke(this, EventArgs.Empty);
        }
    }
}