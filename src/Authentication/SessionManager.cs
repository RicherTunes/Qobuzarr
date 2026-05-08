using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Services.Interfaces;
using Lidarr.Plugin.Common.Services.Authentication;

namespace Lidarr.Plugin.Qobuzarr.Authentication
{
    /// <summary>
    /// Manages Qobuz authentication sessions backed by the common library's
    /// <see cref="StreamingTokenManager{TSession,TCredentials}"/> and
    /// <see cref="FileTokenStore{TSession}"/>. Provides cross-platform at-rest
    /// session encryption (DPAPI on Windows, Keychain on macOS, Secret Service
    /// or DataProtection on Linux) and persistence across plugin restarts.
    /// </summary>
    /// <remarks>
    /// Replaces the legacy in-memory <c>ICached</c> session storage and the
    /// Windows-only DPAPI/SecureString wrappers (<c>SecureSessionManager</c> and
    /// <c>SecureCredentialManager</c>). Sessions are persisted to a per-user
    /// JSON envelope at <see cref="GetDefaultSessionFilePath"/> and protected
    /// with the host-appropriate token protector selected by the common
    /// library's protector factory.
    /// </remarks>
    public class SessionManager : ISessionManager, IDisposable
    {
        /// <summary>Default sub-directory under <c>%AppData%/ArrPlugins</c> for token storage.</summary>
        public const string DefaultStorageFolder = "Qobuzarr";

        /// <summary>Default file name for the persisted session envelope.</summary>
        public const string DefaultSessionFileName = "session.json";

        private readonly Logger _logger;
        private readonly IQobuzAuthenticationService _authenticationService;
        private readonly StreamingTokenManager<QobuzSession, QobuzCredentials> _tokenManager;
        private readonly object _credentialsLock = new();

        // Last-known credentials supplied via Create/Refresh, used for proactive refresh.
        private QobuzCredentials? _lastCredentials;

        public SessionManager(IQobuzAuthenticationService authenticationService, Logger logger)
            : this(authenticationService, logger, sessionFilePath: null)
        {
        }

        /// <summary>
        /// Test/host-friendly constructor that allows overriding the persistent storage path.
        /// </summary>
        public SessionManager(IQobuzAuthenticationService authenticationService, Logger logger, string? sessionFilePath)
        {
            _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var effectivePath = sessionFilePath ?? GetDefaultSessionFilePath();

            // Best-effort migration of any legacy plaintext envelope produced by an
            // earlier in-memory-only storage layer. The legacy SessionManager and
            // SecureSessionManager were memory-only, so on-disk migration is a no-op
            // for users; the helper still drops a sentinel file so re-runs are idempotent.
            LegacySessionMigrator.MigrateIfNeeded(effectivePath, _logger);

            // QobuzAuthenticationService implements IStreamingTokenAuthenticationService<...>,
            // so we hand it through directly without an additional adapter.
            var authAdapter = (IStreamingTokenAuthenticationService<QobuzSession, QobuzCredentials>)authenticationService;

            var options = new StreamingTokenManagerOptions<QobuzSession>
            {
                DefaultSessionLifetime = TimeSpan.FromHours(24),
                RefreshBuffer = TimeSpan.FromMinutes(30),
                RefreshCheckInterval = TimeSpan.FromMinutes(5),
                MaxRefreshAttempts = 3,
                GetSessionExpiry = s => s.ExpiresAt,
                ProactiveRefreshCredentialsProvider = () =>
                {
                    lock (_credentialsLock)
                    {
                        return _lastCredentials;
                    }
                },
                EnableProactiveRefresh = true,
            };

            _tokenManager = new StreamingTokenManager<QobuzSession, QobuzCredentials>(
                authAdapter,
                new NLogLoggerAdapter<StreamingTokenManager<QobuzSession, QobuzCredentials>>(logger),
                new FileTokenStore<QobuzSession>(effectivePath),
                options);

            _logger.Debug("SessionManager initialized (storage={0})", effectivePath);
        }

        // --- ISessionManager surface ---

        public async Task<QobuzSession?> CreateSessionAsync(QobuzCredentials credentials, CancellationToken cancellationToken = default)
        {
            if (credentials == null) return null;

            try
            {
                lock (_credentialsLock)
                {
                    _lastCredentials = credentials;
                }

                await _tokenManager.RefreshSessionAsync(credentials).ConfigureAwait(false);
                var status = await _tokenManager.GetSessionStatusAsync().ConfigureAwait(false);
                if (!status.IsValid)
                {
                    return null;
                }

                var session = await _tokenManager.GetValidSessionAsync(credentials).ConfigureAwait(false);
                _logger.Info("Authentication session created for user: {0}", session?.UserId);
                return session;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to create authentication session");
                return null;
            }
        }

        public async Task<QobuzSession?> GetCurrentSessionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var status = await _tokenManager.GetSessionStatusAsync().ConfigureAwait(false);
                if (!status.IsValid)
                {
                    return null;
                }

                QobuzCredentials? creds;
                lock (_credentialsLock) { creds = _lastCredentials; }

                try
                {
                    return await _tokenManager.GetValidSessionAsync(creds).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    // Session exists but expired and no fallback credentials available.
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "GetCurrentSessionAsync failed");
                return null;
            }
        }

        public Task<bool> IsSessionValidAsync(QobuzSession session, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(session?.IsValid() == true);
        }

        public async Task InvalidateSessionAsync(CancellationToken cancellationToken = default)
        {
            await _tokenManager.ClearSessionAsync(cancellationToken).ConfigureAwait(false);
            _authenticationService.ClearSession();
            lock (_credentialsLock) { _lastCredentials = null; }
        }

        public async Task<QobuzSession?> RefreshSessionAsync(QobuzSession session, CancellationToken cancellationToken = default)
        {
            QobuzCredentials? creds;
            lock (_credentialsLock) { creds = _lastCredentials; }
            if (creds == null)
            {
                _logger.Debug("RefreshSessionAsync: no credentials available; cannot refresh");
                return null;
            }

            try
            {
                await _tokenManager.RefreshSessionAsync(creds).ConfigureAwait(false);
                return await _tokenManager.GetValidSessionAsync(creds).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "RefreshSessionAsync failed");
                return null;
            }
        }

        public bool HasValidSession()
        {
            try
            {
                return _tokenManager.GetSessionStatus().IsValid;
            }
            catch
            {
                return false;
            }
        }

        // --- Compatibility surface for direct callers (e.g., QobuzApiClient cast) ---

        /// <summary>
        /// Stores a session by writing it to the auth service's in-memory cache and
        /// to the persisted, encrypted on-disk envelope. Use when a session is acquired
        /// outside the token manager (e.g., direct call to <c>AuthenticateAsync</c>).
        /// </summary>
        public void StoreSession(QobuzSession session)
        {
            if (session == null || !session.IsValid())
            {
                ClearSession();
                return;
            }

            try
            {
                // The auth service's StoreSession also writes through to the FileTokenStore
                // when configured to do so (see QobuzAuthenticationService).
                _authenticationService.StoreSession(session);
                _logger.Debug("Session stored via SessionManager");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to store session");
            }
        }

        /// <summary>
        /// Clears the current session both in-memory and on disk.
        /// </summary>
        public void ClearSession()
        {
            try
            {
                _tokenManager.ClearSession();
                _authenticationService.ClearSession();
                lock (_credentialsLock) { _lastCredentials = null; }
                _logger.Debug("Session cleared");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error clearing session");
            }
        }

        /// <summary>
        /// Resolves the default cross-platform session file location.
        /// </summary>
        public static string GetDefaultSessionFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            return Path.Combine(appData, "ArrPlugins", DefaultStorageFolder, DefaultSessionFileName);
        }

        public void Dispose()
        {
            try { _tokenManager.Dispose(); } catch { }
            GC.SuppressFinalize(this);
        }

        // Adapter to surface NLog through Microsoft.Extensions.Logging.ILogger<T>.
        private sealed class NLogLoggerAdapter<T> : Microsoft.Extensions.Logging.ILogger<T>
        {
            private readonly Logger _nlog;
            public NLogLoggerAdapter(Logger nlog) { _nlog = nlog; }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopDisposable.Instance;

            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => logLevel switch
            {
                Microsoft.Extensions.Logging.LogLevel.Trace => _nlog.IsTraceEnabled,
                Microsoft.Extensions.Logging.LogLevel.Debug => _nlog.IsDebugEnabled,
                Microsoft.Extensions.Logging.LogLevel.Information => _nlog.IsInfoEnabled,
                Microsoft.Extensions.Logging.LogLevel.Warning => _nlog.IsWarnEnabled,
                Microsoft.Extensions.Logging.LogLevel.Error => _nlog.IsErrorEnabled,
                Microsoft.Extensions.Logging.LogLevel.Critical => _nlog.IsFatalEnabled,
                _ => false
            };

            public void Log<TState>(
                Microsoft.Extensions.Logging.LogLevel logLevel,
                Microsoft.Extensions.Logging.EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                var message = formatter?.Invoke(state, exception) ?? string.Empty;
                switch (logLevel)
                {
                    case Microsoft.Extensions.Logging.LogLevel.Trace:
                        if (exception != null) _nlog.Trace(exception, message); else _nlog.Trace(message);
                        break;
                    case Microsoft.Extensions.Logging.LogLevel.Debug:
                        if (exception != null) _nlog.Debug(exception, message); else _nlog.Debug(message);
                        break;
                    case Microsoft.Extensions.Logging.LogLevel.Information:
                        if (exception != null) _nlog.Info(exception, message); else _nlog.Info(message);
                        break;
                    case Microsoft.Extensions.Logging.LogLevel.Warning:
                        if (exception != null) _nlog.Warn(exception, message); else _nlog.Warn(message);
                        break;
                    case Microsoft.Extensions.Logging.LogLevel.Error:
                        if (exception != null) _nlog.Error(exception, message); else _nlog.Error(message);
                        break;
                    case Microsoft.Extensions.Logging.LogLevel.Critical:
                        if (exception != null) _nlog.Fatal(exception, message); else _nlog.Fatal(message);
                        break;
                }
            }

            private sealed class NoopDisposable : IDisposable
            {
                public static readonly NoopDisposable Instance = new();
                public void Dispose() { }
            }
        }
    }
}
