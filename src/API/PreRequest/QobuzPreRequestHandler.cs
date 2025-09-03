using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Services.Http;
using Lidarr.Plugin.Qobuzarr.API.Signing;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Authentication;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.API.PreRequest
{
    /// <summary>
    /// Qobuz-specific pre-request handler that ensures session validity and injects required
    /// auth parameters/signature consistently before each API call.
    /// </summary>
    public class QobuzPreRequestHandler : IPreRequestHandler
    {
        private readonly IQobuzAuthenticationService _authService;
        private readonly IQobuzRequestSigner _requestSigner;
        private readonly Func<Task<QobuzCredentials>> _credentialsProvider;
        private readonly Logger _logger;

        public QobuzPreRequestHandler(
            IQobuzAuthenticationService authService,
            IQobuzRequestSigner requestSigner,
            Func<Task<QobuzCredentials>> credentialsProvider,
            Logger logger)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _requestSigner = requestSigner ?? throw new ArgumentNullException(nameof(requestSigner));
            _credentialsProvider = credentialsProvider ?? throw new ArgumentNullException(nameof(credentialsProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task EnsureValidSessionAsync()
        {
            // Fast path: cached + valid
            var cached = _authService.GetCachedSession();
            if (cached != null)
            {
                var valid = await _authService.ValidateSessionAsync(cached).ConfigureAwait(false);
                if (valid)
                {
                    return;
                }
                _logger.Debug("Cached Qobuz session is invalid; will re-authenticate.");
            }

            // Attempt re-authentication via provided credentials
            try
            {
                var creds = await _credentialsProvider().ConfigureAwait(false);
                if (creds == null)
                {
                    _logger.Warn("No credentials available for session renewal.");
                    return;
                }
                var newSession = await _authService.AuthenticateAsync(creds).ConfigureAwait(false);
                if (newSession != null)
                {
                    // Store via auth service so other consumers can retrieve
                    _authService.StoreSession(newSession);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Qobuz pre-request session renewal failed.");
            }
        }

        public void InjectAuthParameters(IDictionary<string, string> parameters)
        {
            if (parameters == null) return;
            var session = _authService.GetCachedSession();
            if (session == null)
            {
                _logger.Trace("No session available; skipping auth parameter injection.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(session.AppId))
            {
                parameters["app_id"] = session.AppId;
            }
            if (!string.IsNullOrWhiteSpace(session.AuthToken))
            {
                parameters["user_auth_token"] = session.AuthToken;
            }
        }

        public void SignIfRequired(string endpoint, IDictionary<string, string> parameters)
        {
            if (!_requestSigner.RequiresSigning(endpoint)) return;
            var session = _authService.GetCachedSession();
            if (session == null)
            {
                _logger.Warn("Cannot sign request for {0} without a valid session.", endpoint);
                return;
            }
            _requestSigner.SignRequest(endpoint, (Dictionary<string, string>)parameters, session.AppId, session.AppSecret);
        }
    }
}
