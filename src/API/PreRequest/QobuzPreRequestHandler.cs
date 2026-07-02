using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Services.Http;
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
        private readonly IRequestSigner _signer;
        private readonly Func<Task<QobuzCredentials>> _credentialsProvider;
        private readonly Logger _logger;

        // LOOP-011 (#23): single-flight gate. Qobuz has no refresh token, so renewal is a full re-login
        // (which also scrapes the web player and is login-rate-limited). Serialize renewals so N concurrent
        // requests that all find the session invalid trigger ONE re-auth, not N parallel re-logins.
        private readonly SemaphoreSlim _renewGate = new SemaphoreSlim(1, 1);

        public QobuzPreRequestHandler(
            IQobuzAuthenticationService authService,
            IRequestSigner signer,
            Func<Task<QobuzCredentials>> credentialsProvider,
            Logger logger)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _signer = signer ?? throw new ArgumentNullException(nameof(signer));
            _credentialsProvider = credentialsProvider ?? throw new ArgumentNullException(nameof(credentialsProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task EnsureValidSessionAsync()
        {
            // Fast path (lock-free): cached + valid -> nothing to do.
            if (await IsCachedSessionValidAsync().ConfigureAwait(false))
            {
                return;
            }

            _logger.Debug("Cached Qobuz session is invalid; will re-authenticate.");

            // Single-flight: serialize renewals. The first caller re-authenticates; the rest wait, then the
            // recheck-under-gate below short-circuits because the session is now valid.
            await _renewGate.WaitAsync().ConfigureAwait(false);
            try
            {
                // Recheck under the gate: a previous holder may have already renewed the session.
                if (await IsCachedSessionValidAsync().ConfigureAwait(false))
                {
                    return;
                }

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
            finally
            {
                _renewGate.Release();
            }
        }

        private async Task<bool> IsCachedSessionValidAsync()
        {
            var cached = _authService.GetCachedSession();
            if (cached == null)
            {
                return false;
            }
            return await _authService.ValidateSessionAsync(cached).ConfigureAwait(false);
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
            if (_signer == null || !_signer.RequiresSigning(endpoint)) return;

            var session = _authService.GetCachedSession();
            if (session == null)
            {
                _logger.Warn("Cannot sign request for {0} without a valid session.", endpoint);
                return;
            }
            _signer.Sign(endpoint, parameters, session.AppId, session.AppSecret);
        }
    }
}
