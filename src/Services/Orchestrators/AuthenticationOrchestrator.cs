using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Services.Core.Auth;
using Lidarr.Plugin.Qobuzarr.Exceptions;

namespace Lidarr.Plugin.Qobuzarr.Services.Orchestrators
{
    /// <summary>
    /// Orchestrates the complete authentication workflow by coordinating credential validation,
    /// authentication, session management, and token refresh operations.
    /// </summary>
    /// <remarks>
    /// This orchestrator provides a unified interface for all authentication-related operations
    /// by coordinating the following domain services:
    /// 
    /// Service Coordination:
    /// - CredentialValidator: Validates and sanitizes input credentials
    /// - IQobuzAuthenticationService: Handles API authentication
    /// - SessionManager: Manages session lifecycle and storage
    /// - TokenRefresher: Handles token refresh and retry logic
    /// 
    /// Workflow Management:
    /// - Complete authentication flow from credentials to valid session
    /// - Automatic session validation and refresh coordination
    /// - Error recovery and fallback authentication strategies
    /// - Background session monitoring and proactive refresh
    /// 
    /// Enterprise Features:
    /// - Comprehensive logging and audit trail
    /// - Metrics collection for authentication performance
    /// - Event-driven architecture for integration notifications
    /// - Graceful error handling and user feedback
    /// 
    /// This orchestrator encapsulates the complexity of authentication workflows
    /// while providing a simple, reliable interface for consuming services.
    /// </remarks>
    public class AuthenticationOrchestrator
    {
        private readonly CredentialValidator _credentialValidator;
        private readonly IQobuzAuthenticationService _authService;
        private readonly SessionManager _sessionManager;
        private readonly TokenRefresher _tokenRefresher;
        private readonly Logger _logger;

        // State management
        private QobuzCredentials? _lastSuccessfulCredentials;
        private readonly object _credentialsLock = new object();
        private readonly Timer _backgroundMonitor;

        // Configuration
        private readonly TimeSpan _backgroundMonitorInterval = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _healthCheckInterval = TimeSpan.FromMinutes(15);

        // Metrics
        private long _totalAuthentications = 0;
        private long _successfulAuthentications = 0;
        private long _failedAuthentications = 0;
        private DateTime _serviceStartTime = DateTime.UtcNow;
        private DateTime _lastHealthCheck = DateTime.MinValue;

        // Events
        public event EventHandler<AuthenticationStartedEventArgs>? AuthenticationStarted;
        public event EventHandler<AuthenticationCompletedEventArgs>? AuthenticationCompleted;
        public event EventHandler<AuthenticationFailedEventArgs>? AuthenticationFailed;
        public event EventHandler<SessionRefreshedEventArgs>? SessionRefreshed;

        public AuthenticationOrchestrator(
            CredentialValidator credentialValidator,
            IQobuzAuthenticationService authService,
            SessionManager sessionManager,
            TokenRefresher tokenRefresher,
            Logger logger)
        {
            _credentialValidator = credentialValidator ?? throw new ArgumentNullException(nameof(credentialValidator));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _tokenRefresher = tokenRefresher ?? throw new ArgumentNullException(nameof(tokenRefresher));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Subscribe to events from managed services
            _sessionManager.SessionExpiring += OnSessionExpiring;
            _sessionManager.SessionExpired += OnSessionExpired;
            _tokenRefresher.RefreshCompleted += OnTokenRefreshCompleted;
            _tokenRefresher.RefreshFailed += OnTokenRefreshFailed;

            // Start background monitoring
            _backgroundMonitor = new Timer(BackgroundMonitorCallback, null, 
                _backgroundMonitorInterval, _backgroundMonitorInterval);

            _logger.Info("AuthenticationOrchestrator initialized with background monitoring");
        }

        #region Primary Authentication Methods

        /// <summary>
        /// Performs complete authentication workflow from credentials to valid session.
        /// </summary>
        /// <param name="credentials">The credentials to authenticate with</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Authentication result with session and status details</returns>
        public async Task<AuthenticationResult> AuthenticateAsync(
            QobuzCredentials credentials, 
            CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            Interlocked.Increment(ref _totalAuthentications);

            var result = new AuthenticationResult
            {
                StartTime = startTime,
                Credentials = credentials
            };

            try
            {
                _logger.Info("🔐 Starting authentication process");
                OnAuthenticationStarted(credentials);

                // Step 1: Validate credentials
                _logger.Debug("Step 1: Validating credentials");
                var validationResult = _credentialValidator.ValidateCredentials(credentials);
                result.CredentialValidation = validationResult;

                if (!validationResult.IsValid)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Invalid credentials: {string.Join(", ", validationResult.Errors)}";
                    result.ErrorType = AuthenticationErrorType.InvalidCredentials;
                    
                    _logger.Warn("❌ Credential validation failed: {0}", result.ErrorMessage);
                    OnAuthenticationFailed(result);
                    Interlocked.Increment(ref _failedAuthentications);
                    return result;
                }

                if (validationResult.HasWarnings)
                {
                    _logger.Warn("⚠️ Credential validation warnings: {0}", 
                        string.Join(", ", validationResult.Warnings));
                }

                // Step 2: Attempt authentication
                _logger.Debug("Step 2: Performing API authentication");
                QobuzSession session;
                
                try
                {
                    session = await _authService.AuthenticateAsync(credentials).ConfigureAwait(false);
                }
                catch (QobuzAuthenticationException ex)
                {
                    result.Success = false;
                    result.ErrorMessage = ex.Message;
                    result.ErrorType = AuthenticationErrorType.AuthenticationFailed;
                    result.Exception = ex;
                    
                    _logger.Error("❌ API authentication failed: {0}", ex.Message);
                    OnAuthenticationFailed(result);
                    Interlocked.Increment(ref _failedAuthentications);
                    return result;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Authentication error: {ex.Message}";
                    result.ErrorType = AuthenticationErrorType.NetworkError;
                    result.Exception = ex;
                    
                    _logger.Error(ex, "❌ Unexpected authentication error");
                    OnAuthenticationFailed(result);
                    Interlocked.Increment(ref _failedAuthentications);
                    return result;
                }

                // Step 3: Validate and store session
                _logger.Debug("Step 3: Storing session");
                if (session == null || !session.IsValid())
                {
                    result.Success = false;
                    result.ErrorMessage = "Authentication service returned invalid session";
                    result.ErrorType = AuthenticationErrorType.InvalidSession;
                    
                    _logger.Error("❌ Invalid session returned from authentication service");
                    OnAuthenticationFailed(result);
                    Interlocked.Increment(ref _failedAuthentications);
                    return result;
                }

                _sessionManager.StoreSession(session);
                result.Session = session;

                // Step 4: Store credentials for future refresh operations
                lock (_credentialsLock)
                {
                    _lastSuccessfulCredentials = credentials;
                }

                // Step 5: Success!
                result.Success = true;
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;

                Interlocked.Increment(ref _successfulAuthentications);
                _logger.Info("✅ Authentication successful - User: {0}, Expires: {1}", 
                    session.UserId, session.ExpiresAt);

                OnAuthenticationCompleted(result);
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Orchestration error: {ex.Message}";
                result.ErrorType = AuthenticationErrorType.InternalError;
                result.Exception = ex;
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;

                _logger.Error(ex, "❌ Authentication orchestration error");
                OnAuthenticationFailed(result);
                Interlocked.Increment(ref _failedAuthentications);
                return result;
            }
        }

        /// <summary>
        /// Gets the current valid session, refreshing if necessary.
        /// </summary>
        /// <param name="forceRefresh">Forces session refresh even if not expired</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Current valid session or null if no session available</returns>
        public async Task<QobuzSession?> GetValidSessionAsync(
            bool forceRefresh = false, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                var currentSession = _sessionManager.GetCurrentSession();
                
                if (currentSession == null)
                {
                    _logger.Debug("No current session available");
                    return null;
                }

                // Check if refresh is needed or forced
                if (forceRefresh || _tokenRefresher.ShouldRefresh(currentSession))
                {
                    _logger.Debug("Session refresh {0}", forceRefresh ? "forced" : "needed");
                    
                    lock (_credentialsLock)
                    {
                        if (_lastSuccessfulCredentials == null)
                        {
                            _logger.Warn("Cannot refresh session - no stored credentials");
                            return currentSession; // Return current session, let caller handle expiration
                        }
                    }

                    var refreshedSession = await RefreshSessionAsync(cancellationToken).ConfigureAwait(false);
                    return refreshedSession ?? currentSession; // Fallback to current session if refresh fails
                }

                return currentSession;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting valid session");
                return _sessionManager.GetCurrentSession(); // Fallback to whatever session we have
            }
        }

        /// <summary>
        /// Manually refreshes the current session using stored credentials.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>New session if refresh successful</returns>
        public async Task<QobuzSession?> RefreshSessionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var currentSession = _sessionManager.GetCurrentSession();
                if (currentSession == null)
                {
                    _logger.Debug("No session to refresh");
                    return null;
                }

                QobuzCredentials credentials;
                lock (_credentialsLock)
                {
                    if (_lastSuccessfulCredentials == null)
                    {
                        _logger.Warn("Cannot refresh - no stored credentials");
                        return null;
                    }
                    credentials = _lastSuccessfulCredentials;
                }

                _logger.Info("🔄 Refreshing session for user {0}", currentSession.UserId);
                
                var refreshedSession = await _tokenRefresher.RefreshSessionAsync(
                    currentSession, credentials, cancellationToken).ConfigureAwait(false);

                if (refreshedSession != null && refreshedSession.IsValid())
                {
                    _sessionManager.StoreSession(refreshedSession);
                    OnSessionRefreshed(currentSession, refreshedSession);
                    return refreshedSession;
                }
                else
                {
                    _logger.Warn("Session refresh failed");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error refreshing session");
                return null;
            }
        }

        #endregion

        #region Session Management

        /// <summary>
        /// Checks if there is a currently valid session.
        /// </summary>
        /// <returns>True if a valid session exists</returns>
        public bool HasValidSession()
        {
            return _sessionManager.HasValidSession();
        }

        /// <summary>
        /// Gets the current session without any validation or refresh.
        /// </summary>
        /// <returns>Current session or null</returns>
        public QobuzSession? GetCurrentSession()
        {
            return _sessionManager.GetCurrentSession();
        }

        /// <summary>
        /// Clears the current session and stored credentials.
        /// </summary>
        public void ClearSession()
        {
            _logger.Info("Clearing authentication session and credentials");
            
            _sessionManager.ClearSession();
            
            lock (_credentialsLock)
            {
                _lastSuccessfulCredentials = null;
            }
        }

        /// <summary>
        /// Validates the current session and returns detailed status.
        /// </summary>
        /// <returns>Session validation result</returns>
        public SessionValidationResult ValidateCurrentSession()
        {
            return _sessionManager.ValidateSession(forceCheck: true);
        }

        #endregion

        #region Health Monitoring

        /// <summary>
        /// Gets comprehensive authentication health report.
        /// </summary>
        /// <returns>Detailed health and metrics report</returns>
        public AuthenticationHealthReport GetHealthReport()
        {
            var sessionHealth = _sessionManager.GetHealthReport();
            var refreshStats = _tokenRefresher.GetStats();
            var uptime = DateTime.UtcNow - _serviceStartTime;
            var successRate = _totalAuthentications > 0 ? 
                (double)_successfulAuthentications / _totalAuthentications : 0.0;

            return new AuthenticationHealthReport
            {
                GeneratedAt = DateTime.UtcNow,
                ServiceUptime = uptime,
                OverallHealth = DetermineOverallHealth(sessionHealth, refreshStats),
                SessionHealth = sessionHealth,
                RefreshStats = refreshStats,
                AuthenticationMetrics = new AuthenticationMetrics
                {
                    TotalAuthentications = _totalAuthentications,
                    SuccessfulAuthentications = _successfulAuthentications,
                    FailedAuthentications = _failedAuthentications,
                    SuccessRate = successRate
                },
                HasStoredCredentials = _lastSuccessfulCredentials != null,
                LastHealthCheck = _lastHealthCheck
            };
        }

        /// <summary>
        /// Performs comprehensive health check of all authentication components.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Health check result</returns>
        public async Task<HealthCheckResult> PerformHealthCheckAsync(CancellationToken cancellationToken = default)
        {
            _lastHealthCheck = DateTime.UtcNow;
            
            try
            {
                _logger.Debug("Performing authentication health check");
                
                var result = new HealthCheckResult
                {
                    Timestamp = _lastHealthCheck
                };

                // Check session health
                var sessionValidation = _sessionManager.ValidateSession(forceCheck: true);
                result.SessionHealthy = sessionValidation.Status == SessionStatus.Valid;
                result.SessionStatus = sessionValidation.Status;

                // Check if we have credentials for refresh
                lock (_credentialsLock)
                {
                    result.HasRefreshCredentials = _lastSuccessfulCredentials != null;
                }

                // Check refresh service health
                var refreshStats = _tokenRefresher.GetStats();
                result.RefreshServiceHealthy = !refreshStats.IsCircuitBreakerOpen;

                // Overall health determination
                result.OverallHealthy = result.SessionHealthy && 
                                      result.RefreshServiceHealthy &&
                                      (result.HasRefreshCredentials || result.SessionHealthy);

                _logger.Debug("Health check completed - Overall: {0}, Session: {1}, Refresh: {2}", 
                    result.OverallHealthy, result.SessionHealthy, result.RefreshServiceHealthy);

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during health check");
                return new HealthCheckResult
                {
                    Timestamp = _lastHealthCheck,
                    OverallHealthy = false,
                    Error = ex.Message,
                    Exception = ex
                };
            }
        }

        #endregion

        #region Event Handling

        private void OnSessionExpiring(object? sender, SessionExpiringEventArgs e)
        {
            _logger.Info("Session expiring in {0:F1} minutes - scheduling refresh", 
                e.TimeRemaining.TotalMinutes);
            
            // Trigger background refresh
            _ = Task.Run(async () =>
            {
                try
                {
                    await RefreshSessionAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Background session refresh failed");
                }
            });
        }

        private void OnSessionExpired(object? sender, SessionExpiredEventArgs e)
        {
            _logger.Warn("Session expired - authentication will be required for next API call");
        }

        private void OnTokenRefreshCompleted(object? sender, TokenRefreshCompletedEventArgs e)
        {
            _logger.Info("✅ Token refresh completed successfully");
            OnSessionRefreshed(null, e.NewSession);
        }

        private void OnTokenRefreshFailed(object? sender, TokenRefreshFailedEventArgs e)
        {
            _logger.Error(e.Exception, "❌ Token refresh failed ({0} consecutive failures)", 
                e.ConsecutiveFailures);
        }

        private void BackgroundMonitorCallback(object? state)
        {
            try
            {
                _logger.Trace("Background authentication monitor running");
                
                // Perform maintenance tasks
                _sessionManager.PerformMaintenance();
                
                // Schedule health check if needed
                if (DateTime.UtcNow - _lastHealthCheck > _healthCheckInterval)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await PerformHealthCheckAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Background health check failed");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in background monitor");
            }
        }

        #endregion

        #region Private Event Notifications

        private void OnAuthenticationStarted(QobuzCredentials credentials)
        {
            try
            {
                AuthenticationStarted?.Invoke(this, new AuthenticationStartedEventArgs
                {
                    Timestamp = DateTime.UtcNow,
                    AuthenticationType = credentials.IsEmailAuth() ? "Email" : "Token"
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in AuthenticationStarted event handler");
            }
        }

        private void OnAuthenticationCompleted(AuthenticationResult result)
        {
            try
            {
                AuthenticationCompleted?.Invoke(this, new AuthenticationCompletedEventArgs
                {
                    Timestamp = DateTime.UtcNow,
                    Result = result,
                    Duration = result.Duration
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in AuthenticationCompleted event handler");
            }
        }

        private void OnAuthenticationFailed(AuthenticationResult result)
        {
            try
            {
                AuthenticationFailed?.Invoke(this, new AuthenticationFailedEventArgs
                {
                    Timestamp = DateTime.UtcNow,
                    Result = result,
                    ErrorType = result.ErrorType
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in AuthenticationFailed event handler");
            }
        }

        private void OnSessionRefreshed(QobuzSession? oldSession, QobuzSession newSession)
        {
            try
            {
                SessionRefreshed?.Invoke(this, new SessionRefreshedEventArgs
                {
                    Timestamp = DateTime.UtcNow,
                    OldSession = oldSession,
                    NewSession = newSession
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in SessionRefreshed event handler");
            }
        }

        #endregion

        #region Utility Methods

        private OverallHealthStatus DetermineOverallHealth(
            SessionHealthReport sessionHealth, 
            TokenRefreshStats refreshStats)
        {
            if (sessionHealth.ValidationResult.Status == SessionStatus.Valid && 
                !refreshStats.IsCircuitBreakerOpen)
            {
                return OverallHealthStatus.Healthy;
            }
            else if (sessionHealth.ValidationResult.Status == SessionStatus.NeedsRefresh ||
                     refreshStats.ConsecutiveFailures > 0)
            {
                return OverallHealthStatus.Warning;
            }
            else
            {
                return OverallHealthStatus.Unhealthy;
            }
        }

        #endregion

        #region Resource Management

        /// <summary>
        /// Disposes resources used by the authentication orchestrator.
        /// </summary>
        public void Dispose()
        {
            try
            {
                _backgroundMonitor?.Dispose();
                _tokenRefresher?.Dispose();
                _logger.Info("AuthenticationOrchestrator disposed");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during AuthenticationOrchestrator disposal");
            }
        }

        #endregion
    }

    #region Supporting Data Structures

    /// <summary>
    /// Comprehensive authentication result.
    /// </summary>
    public class AuthenticationResult
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public AuthenticationErrorType ErrorType { get; set; }
        public Exception? Exception { get; set; }
        public QobuzCredentials Credentials { get; set; }
        public CredentialValidationResult CredentialValidation { get; set; }
        public QobuzSession? Session { get; set; }
    }

    /// <summary>
    /// Authentication error type enumeration.
    /// </summary>
    public enum AuthenticationErrorType
    {
        None,
        InvalidCredentials,
        AuthenticationFailed,
        NetworkError,
        InvalidSession,
        InternalError
    }

    /// <summary>
    /// Authentication health report.
    /// </summary>
    public class AuthenticationHealthReport
    {
        public DateTime GeneratedAt { get; set; }
        public TimeSpan ServiceUptime { get; set; }
        public OverallHealthStatus OverallHealth { get; set; }
        public SessionHealthReport SessionHealth { get; set; }
        public TokenRefreshStats RefreshStats { get; set; }
        public AuthenticationMetrics AuthenticationMetrics { get; set; }
        public bool HasStoredCredentials { get; set; }
        public DateTime LastHealthCheck { get; set; }
    }

    /// <summary>
    /// Overall health status enumeration.
    /// </summary>
    public enum OverallHealthStatus
    {
        Healthy,
        Warning,
        Unhealthy
    }

    /// <summary>
    /// Authentication performance metrics.
    /// </summary>
    public class AuthenticationMetrics
    {
        public long TotalAuthentications { get; set; }
        public long SuccessfulAuthentications { get; set; }
        public long FailedAuthentications { get; set; }
        public double SuccessRate { get; set; }
    }

    /// <summary>
    /// Health check result.
    /// </summary>
    public class HealthCheckResult
    {
        public DateTime Timestamp { get; set; }
        public bool OverallHealthy { get; set; }
        public bool SessionHealthy { get; set; }
        public bool RefreshServiceHealthy { get; set; }
        public bool HasRefreshCredentials { get; set; }
        public SessionStatus SessionStatus { get; set; }
        public string? Error { get; set; }
        public Exception? Exception { get; set; }
    }

    #region Event Arguments

    /// <summary>
    /// Event arguments for authentication started events.
    /// </summary>
    public class AuthenticationStartedEventArgs : EventArgs
    {
        public DateTime Timestamp { get; set; }
        public string AuthenticationType { get; set; }
    }

    /// <summary>
    /// Event arguments for authentication completed events.
    /// </summary>
    public class AuthenticationCompletedEventArgs : EventArgs
    {
        public DateTime Timestamp { get; set; }
        public AuthenticationResult Result { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Event arguments for authentication failed events.
    /// </summary>
    public class AuthenticationFailedEventArgs : EventArgs
    {
        public DateTime Timestamp { get; set; }
        public AuthenticationResult Result { get; set; }
        public AuthenticationErrorType ErrorType { get; set; }
    }

    /// <summary>
    /// Event arguments for session refreshed events.
    /// </summary>
    public class SessionRefreshedEventArgs : EventArgs
    {
        public DateTime Timestamp { get; set; }
        public QobuzSession? OldSession { get; set; }
        public QobuzSession NewSession { get; set; }
    }

    #endregion

    #endregion
}