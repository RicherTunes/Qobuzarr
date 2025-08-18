using System;
using System.Security;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace Lidarr.Plugin.Qobuzarr.Security
{
    /// <summary>
    /// Enhanced session manager with secure credential handling and session validation.
    /// Provides additional security layers beyond the basic authentication service.
    /// </summary>
    public class SecureSessionManager : IDisposable
    {
        private readonly SecureCredentialManager _credentialManager;
        private readonly IQobuzLogger _logger;
        private QobuzSession _currentSession;
        private SecureString _secureAuthToken;
        private SecureString _secureAppSecret;
        private DateTime _lastSecurityCheck = DateTime.MinValue;
        private readonly TimeSpan _securityCheckInterval = TimeSpan.FromMinutes(30);
        private bool _disposed = false;

        public SecureSessionManager(SecureCredentialManager credentialManager, IQobuzLogger logger)
        {
            _credentialManager = credentialManager ?? throw new ArgumentNullException(nameof(credentialManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Securely stores a session with enhanced protection for sensitive data.
        /// </summary>
        /// <param name="session">Session to store securely</param>
        public void StoreSessionSecurely(QobuzSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            try
            {
                // Clear existing secure strings
                ClearSecureData();

                // Store sensitive data in secure strings
                _secureAuthToken = _credentialManager.CreateSecureString(session.AuthToken);
                _secureAppSecret = _credentialManager.CreateSecureString(session.AppSecret);

                // Store session with cleared sensitive fields
                _currentSession = new QobuzSession
                {
                    UserId = session.UserId,
                    AppId = session.AppId,
                    ExpiresAt = session.ExpiresAt,
                    CreatedAt = session.CreatedAt,
                    Subscription = session.Subscription,
                    AuthToken = null, // Stored in secure string
                    AppSecret = null  // Stored in secure string
                };

                _lastSecurityCheck = DateTime.UtcNow;

                _logger.Debug("Session stored securely for user {0}", 
                    _credentialManager.MaskSensitiveData(session.UserId));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to store session securely");
                ClearSecureData();
                throw;
            }
        }

        /// <summary>
        /// Retrieves the current session with sensitive data from secure storage.
        /// </summary>
        /// <returns>Complete session with sensitive data, or null if no session exists</returns>
        public QobuzSession GetSecureSession()
        {
            if (_currentSession == null || _secureAuthToken == null)
                return null;

            try
            {
                // Check if session has expired
                if (_currentSession.ExpiresAt <= DateTime.UtcNow)
                {
                    _logger.Debug("Stored session has expired, clearing");
                    ClearSession();
                    return null;
                }

                // Periodic security validation
                if (ShouldPerformSecurityCheck())
                {
                    if (!PerformSecurityCheck())
                    {
                        _logger.Warn("Session failed security validation, clearing");
                        ClearSession();
                        return null;
                    }
                }

                // Reconstruct session with secure data
                var session = new QobuzSession
                {
                    UserId = _currentSession.UserId,
                    AppId = _currentSession.AppId,
                    ExpiresAt = _currentSession.ExpiresAt,
                    CreatedAt = _currentSession.CreatedAt,
                    Subscription = _currentSession.Subscription,
                    AuthToken = _credentialManager.SecureStringToString(_secureAuthToken),
                    AppSecret = _credentialManager.SecureStringToString(_secureAppSecret)
                };

                return session;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to retrieve secure session");
                ClearSession();
                return null;
            }
        }

        /// <summary>
        /// Validates session integrity and security status.
        /// </summary>
        /// <returns>True if session passes security validation</returns>
        public bool ValidateSessionSecurity()
        {
            if (_currentSession == null || _secureAuthToken == null)
                return false;

            try
            {
                // Check expiration
                if (_currentSession.ExpiresAt <= DateTime.UtcNow)
                {
                    _logger.Debug("Session expired");
                    return false;
                }

                // Validate auth token format (should be non-empty)
                var authToken = _credentialManager.SecureStringToString(_secureAuthToken);
                bool isValid = !string.IsNullOrWhiteSpace(authToken);
                
                // Clear the plaintext token immediately
                _credentialManager.ClearString(ref authToken);

                if (!isValid)
                {
                    _logger.Debug("Auth token validation failed");
                    return false;
                }

                // Additional security checks
                if (!_credentialManager.ValidateCredentialSecurity(_currentSession.UserId, "User ID"))
                {
                    _logger.Warn("User ID failed security validation");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Debug("Session security validation failed: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Checks if the session is still valid and active.
        /// </summary>
        /// <returns>True if session exists and is valid</returns>
        public bool HasValidSession()
        {
            return _currentSession != null && 
                   _secureAuthToken != null && 
                   _currentSession.ExpiresAt > DateTime.UtcNow &&
                   ValidateSessionSecurity();
        }

        /// <summary>
        /// Clears the current session and all secure data.
        /// </summary>
        public void ClearSession()
        {
            try
            {
                ClearSecureData();
                _currentSession = null;
                _lastSecurityCheck = DateTime.MinValue;

                _logger.Debug("Session cleared securely");
            }
            catch (Exception ex)
            {
                _logger.Debug("Error during session cleanup: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Gets session expiration information without exposing sensitive data.
        /// </summary>
        /// <returns>Session expiration info or null if no session</returns>
        public DateTime? GetSessionExpiration()
        {
            return _currentSession?.ExpiresAt;
        }

        /// <summary>
        /// Gets masked user ID for display/logging purposes.
        /// </summary>
        /// <returns>Masked user ID or null if no session</returns>
        public string GetMaskedUserId()
        {
            if (_currentSession?.UserId == null)
                return null;

            return _credentialManager.MaskSensitiveData(_currentSession.UserId);
        }

        private bool ShouldPerformSecurityCheck()
        {
            return DateTime.UtcNow - _lastSecurityCheck > _securityCheckInterval;
        }

        private bool PerformSecurityCheck()
        {
            try
            {
                // Validate session integrity
                if (!ValidateSessionSecurity())
                    return false;

                // Check for obvious signs of compromise
                var authTokenPlain = _credentialManager.SecureStringToString(_secureAuthToken);
                bool isSecure = _credentialManager.ValidateCredentialSecurity(authTokenPlain, "Auth Token");
                
                // Clear plaintext immediately
                _credentialManager.ClearString(ref authTokenPlain);

                if (!isSecure)
                {
                    _logger.Warn("Session failed enhanced security validation");
                    return false;
                }

                _lastSecurityCheck = DateTime.UtcNow;
                return true;
            }
            catch (Exception ex)
            {
                _logger.Debug("Security check failed: {0}", ex.Message);
                return false;
            }
        }

        private void ClearSecureData()
        {
            try
            {
                _secureAuthToken?.Dispose();
                _secureAuthToken = null;

                _secureAppSecret?.Dispose();
                _secureAppSecret = null;
            }
            catch (Exception ex)
            {
                _logger.Debug("Error clearing secure data: {0}", ex.Message);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                ClearSession();
                _disposed = true;
            }
        }
    }
}