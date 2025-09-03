using System;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Indexers.Core
{
    /// <summary>
    /// Handles authentication concerns for the Qobuz indexer.
    /// Extracted from QobuzIndexer god class to improve maintainability.
    /// </summary>
    public class IndexerAuthenticationManager : IIndexerAuthenticationManager
    {
        private readonly IQobuzAuthenticationService _authService;
        private readonly QobuzIndexerSettings _settings;
        private readonly Logger _logger;

        public IndexerAuthenticationManager(
            IQobuzAuthenticationService authService,
            QobuzIndexerSettings settings,
            Logger logger)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task EnsureAuthenticatedAsync()
        {
            _logger.Debug("🔑 Ensuring authentication for Qobuz API access");

            try
            {
                var session = _authService.GetCachedSession();
                
                if (session == null || !session.IsValid())
                {
                    _logger.Info("🔄 No valid session found, authenticating with Qobuz");
                    
                    var credentials = CreateCredentialsFromSettings();
                    session = await _authService.AuthenticateAsync(credentials).ConfigureAwait(false);
                    
                    if (session == null || !session.IsValid())
                    {
                        throw new InvalidOperationException("Authentication failed: Invalid session returned");
                    }
                    
                    _logger.Info("✅ Successfully authenticated with Qobuz - Session expires: {0}", 
                        session.ExpiresAt.ToString("yyyy-MM-dd HH:mm:ss"));
                }
                else
                {
                    _logger.Debug("✅ Using cached valid session (expires: {0})", 
                        session.ExpiresAt.ToString("yyyy-MM-dd HH:mm:ss"));
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ Authentication failed: {0}", ex.Message);
                throw new InvalidOperationException($"Failed to authenticate with Qobuz: {ex.Message}", ex);
            }
        }

        public QobuzSession? GetCachedSession()
        {
            return _authService.GetCachedSession();
        }

        public async Task<(bool IsSuccess, string ErrorMessage)> TestAuthenticationAsync()
        {
            _logger.Debug("🧪 Testing authentication configuration");

            try
            {
                var credentials = CreateCredentialsFromSettings();
                var session = await _authService.AuthenticateAsync(credentials).ConfigureAwait(false);
                
                if (session == null)
                {
                    return (false, "Authentication returned null session");
                }

                if (!session.IsValid())
                {
                    return (false, "Authentication returned invalid session");
                }

                if (!session.IsValid())
                {
                    return (false, "Authentication returned expired session");
                }

                _logger.Info("✅ Authentication test successful");
                return (true, "Authentication successful");
            }
            catch (QobuzAuthenticationException authEx)
            {
                _logger.Warn(authEx, "❌ Authentication test failed with auth exception");
                return (false, $"Authentication failed: {authEx.Message}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ Authentication test failed with unexpected exception");
                return (false, $"Authentication test failed: {ex.Message}");
            }
        }

        private QobuzCredentials CreateCredentialsFromSettings()
        {
            var credentials = new QobuzCredentials();

            // Primary: Email/Password authentication
            if (!string.IsNullOrWhiteSpace(_settings.Email) && !string.IsNullOrWhiteSpace(_settings.Password))
            {
                credentials.Email = _settings.Email.Trim();
                // Auto-detect plain vs MD5-hashed password
                var pwd = _settings.Password.Trim();
                if (IsMd5Hash(pwd))
                {
                    credentials.MD5Password = pwd;
                }
                else
                {
                    credentials.MD5Password = HashingUtility.ComputePasswordMD5Hash(pwd);
                }
                _logger.Debug("📧 Using email/password authentication");
            }
            // Secondary: Token authentication  
            else if (!string.IsNullOrWhiteSpace(_settings.UserId) && !string.IsNullOrWhiteSpace(_settings.AuthToken))
            {
                credentials.UserId = _settings.UserId.Trim();
                credentials.AuthToken = _settings.AuthToken.Trim();
                _logger.Debug("🎟️ Using token authentication");
            }
            else
            {
                throw new InvalidOperationException(
                    "No valid authentication method configured. " +
                    "Either provide Email/Password or UserId/AuthToken.");
            }

            // Optional: Custom app credentials
            if (!string.IsNullOrWhiteSpace(_settings.AppId) && !string.IsNullOrWhiteSpace(_settings.AppSecret))
            {
                credentials.AppId = _settings.AppId.Trim();
                credentials.AppSecret = _settings.AppSecret.Trim();
                _logger.Debug("🔧 Using custom app credentials");
            }

            return credentials;
        }

        private static bool IsMd5Hash(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 32) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isHex = (c >= '0' && c <= '9') ||
                             (c >= 'a' && c <= 'f') ||
                             (c >= 'A' && c <= 'F');
                if (!isHex) return false;
            }
            return true;
        }
    }
}
