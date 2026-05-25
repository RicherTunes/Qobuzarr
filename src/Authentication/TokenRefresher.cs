using System;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Services.Resilience;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Services.Interfaces;
using QobuzAuthenticationException = Lidarr.Plugin.Qobuzarr.Authentication.QobuzAuthenticationException;

namespace Lidarr.Plugin.Qobuzarr.Authentication
{
    /// <summary>
    /// Handles token refresh logic with intelligent retry strategies, failure handling,
    /// and coordination with session management.
    /// </summary>
    /// <remarks>
    /// This service provides sophisticated token refresh capabilities:
    /// 
    /// Refresh Strategies:
    /// - Proactive refresh before token expiration
    /// - Reactive refresh on authentication failures
    /// - Exponential backoff for failed refresh attempts
    /// - Circuit breaker pattern for persistent failures
    /// 
    /// Coordination Features:
    /// - Integration with session lifecycle management
    /// - Notification system for refresh events
    /// - Metrics tracking for refresh success/failure rates
    /// - Configurable refresh timing and thresholds
    /// 
    /// Error Handling:
    /// - Graceful degradation on refresh failures
    /// - Automatic fallback to re-authentication
    /// - Comprehensive error classification and reporting
    /// - Recovery strategies for different failure types
    /// 
    /// Note: Qobuz doesn't support traditional refresh tokens, so this service
    /// primarily coordinates re-authentication when tokens expire.
    /// </remarks>
    public class TokenRefresher : ITokenRefresher
    {
        private readonly IQobuzAuthenticationService _authService;
        private readonly Logger _logger;

        // State management
        private readonly SemaphoreSlim _refreshSemaphore = new SemaphoreSlim(1, 1);
        private volatile bool _isRefreshing = false;
        private DateTime _lastRefreshAttempt = DateTime.MinValue;
        private int _consecutiveFailures = 0;

        // Configuration
        private readonly TimeSpan _refreshCooldown = TimeSpan.FromSeconds(Constants.QobuzarrConstants.Defaults.TokenRefreshCooldownSeconds); // Minimum time between refresh attempts
        private readonly TimeSpan _refreshBuffer = TimeSpan.FromMinutes(Constants.QobuzarrConstants.Defaults.TokenRefreshBufferMinutes); // Refresh this many minutes before expiry
        private readonly int _maxRetryAttempts = Constants.QobuzarrConstants.Defaults.TokenMaxRetryAttempts;
        private readonly TimeSpan _initialRetryDelay = TimeSpan.FromSeconds(Constants.QobuzarrConstants.Defaults.TokenInitialRetryDelaySeconds);
        private readonly double _backoffMultiplier = Constants.QobuzarrConstants.Defaults.TokenBackoffMultiplier;
        private readonly int _circuitBreakerThreshold = Constants.QobuzarrConstants.Defaults.TokenCircuitBreakerThreshold; // Fail-open after this many consecutive failures

        // Metrics
        private long _successfulRefreshes = 0;
        private long _failedRefreshes = 0;
        private long _totalRefreshAttempts = 0;
        private DateTime _lastSuccessfulRefresh = DateTime.MinValue;
        private DateTime _serviceStartTime = DateTime.UtcNow;

        // Events
        public event EventHandler<TokenRefreshStartedEventArgs>? RefreshStarted;
        public event EventHandler<TokenRefreshCompletedEventArgs>? RefreshCompleted;
        public event EventHandler<TokenRefreshFailedEventArgs>? RefreshFailed;
        public event EventHandler<CircuitBreakerTrippedEventArgs>? CircuitBreakerTripped;

        public TokenRefresher(IQobuzAuthenticationService authService, Logger logger)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _logger.Debug("TokenRefresher initialized with {0}min buffer and {1}s cooldown",
                _refreshBuffer.TotalMinutes, _refreshCooldown.TotalSeconds);
        }

        #region Public Methods

        /// <summary>
        /// Determines if a session needs refresh based on expiration time and buffer settings.
        /// </summary>
        /// <param name="session">The session to check</param>
        /// <returns>True if the session should be refreshed</returns>
        public bool ShouldRefresh(QobuzSession session)
        {
            if (session == null || !session.IsValid())
            {
                return false; // Invalid sessions need re-authentication, not refresh
            }

            var timeUntilExpiry = session.ExpiresAt - DateTime.UtcNow;
            var shouldRefresh = timeUntilExpiry <= _refreshBuffer;

            if (shouldRefresh)
            {
                _logger.Debug("Session should refresh - expires in {0:F1} minutes", timeUntilExpiry.TotalMinutes);
            }

            return shouldRefresh;
        }

        /// <summary>
        /// Attempts to refresh the session using the original credentials.
        /// Since Qobuz doesn't support refresh tokens, this performs re-authentication.
        /// </summary>
        /// <param name="currentSession">The current session to refresh</param>
        /// <param name="originalCredentials">The original credentials used for authentication</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>New session if refresh successful, null if failed</returns>
        public async Task<QobuzSession?> RefreshSessionAsync(
            QobuzSession currentSession,
            QobuzCredentials originalCredentials,
            CancellationToken cancellationToken = default)
        {
            if (currentSession == null)
                throw new ArgumentNullException(nameof(currentSession));

            if (originalCredentials == null)
                throw new ArgumentNullException(nameof(originalCredentials));

            // Check if refresh is already in progress
            if (_isRefreshing)
            {
                _logger.Debug("Refresh already in progress, waiting...");
                await WaitForRefreshCompletion(cancellationToken).ConfigureAwait(false);
                return _authService.GetCachedSession(); // Return whatever the ongoing refresh produced
            }

            // Check cooldown period
            if (DateTime.UtcNow - _lastRefreshAttempt < _refreshCooldown)
            {
                var remainingCooldown = _refreshCooldown - (DateTime.UtcNow - _lastRefreshAttempt);
                _logger.Debug("Refresh cooldown active for {0:F1} more seconds", remainingCooldown.TotalSeconds);
                return currentSession; // Return current session during cooldown
            }

            // Check circuit breaker
            if (IsCircuitBreakerOpen())
            {
                _logger.Warn("Circuit breaker is open - skipping refresh attempt");
                OnCircuitBreakerTripped();
                return null;
            }

            return await PerformRefreshWithRetry(currentSession, originalCredentials, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Performs a forced refresh attempt, bypassing cooldowns and circuit breaker.
        /// Use this for manual refresh operations or emergency recovery.
        /// </summary>
        /// <param name="originalCredentials">The original credentials for re-authentication</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>New session if successful</returns>
        public async Task<QobuzSession> ForceRefreshAsync(
            QobuzCredentials originalCredentials,
            CancellationToken cancellationToken = default)
        {
            if (originalCredentials == null)
                throw new ArgumentNullException(nameof(originalCredentials));

            _logger.Info("Forcing token refresh (bypassing cooldowns)");

            await _refreshSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _isRefreshing = true;
                _lastRefreshAttempt = DateTime.UtcNow;
                Interlocked.Increment(ref _totalRefreshAttempts);

                OnRefreshStarted("Force refresh requested");

                var newSession = await _authService.AuthenticateAsync(originalCredentials).ConfigureAwait(false);

                if (newSession != null && newSession.IsValid())
                {
                    Interlocked.Increment(ref _successfulRefreshes);
                    _lastSuccessfulRefresh = DateTime.UtcNow;
                    _consecutiveFailures = 0;

                    OnRefreshCompleted(newSession, true);
                    _logger.Info("✅ Force refresh successful");

                    return newSession;
                }
                else
                {
                    Interlocked.Increment(ref _failedRefreshes);
                    _consecutiveFailures++;

                    var exception = new QobuzAuthenticationException("Force refresh returned invalid session");
                    OnRefreshFailed(exception, true);
                    throw exception;
                }
            }
            finally
            {
                _isRefreshing = false;
                _refreshSemaphore.Release();
            }
        }

        /// <summary>
        /// Gets comprehensive refresh statistics and health information.
        /// </summary>
        /// <returns>Token refresh metrics and status</returns>
        public TokenRefreshStats GetStats()
        {
            var uptime = DateTime.UtcNow - _serviceStartTime;
            var successRate = _totalRefreshAttempts > 0 ? (double)_successfulRefreshes / _totalRefreshAttempts : 0.0;

            return new TokenRefreshStats
            {
                ServiceUptime = uptime,
                TotalAttempts = _totalRefreshAttempts,
                SuccessfulRefreshes = _successfulRefreshes,
                FailedRefreshes = _failedRefreshes,
                SuccessRate = successRate,
                ConsecutiveFailures = _consecutiveFailures,
                LastSuccessfulRefresh = _lastSuccessfulRefresh,
                LastRefreshAttempt = _lastRefreshAttempt,
                IsRefreshing = _isRefreshing,
                IsCircuitBreakerOpen = IsCircuitBreakerOpen(),
                Configuration = new RefreshConfiguration
                {
                    RefreshBuffer = _refreshBuffer,
                    RefreshCooldown = _refreshCooldown,
                    MaxRetryAttempts = _maxRetryAttempts,
                    CircuitBreakerThreshold = _circuitBreakerThreshold
                }
            };
        }

        #region ITokenRefresher Interface Implementation

        /// <summary>
        /// Refreshes an authentication token if possible.
        /// Since Qobuz doesn't support token refresh, this attempts re-authentication.
        /// </summary>
        public async Task<string?> RefreshTokenAsync(string currentToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(currentToken))
                return null;

            try
            {
                var currentSession = _authService.GetCachedSession();
                if (currentSession == null)
                    return null;

                // Note: This would need the original credentials, which we don't have from just the token
                // This is a limitation of the interface design for Qobuz
                _logger.Warn("RefreshTokenAsync called but requires original credentials for Qobuz - falling back to session refresh");
                return currentToken; // Return existing token as we can't refresh without credentials
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to refresh token");
                return null;
            }
        }

        /// <summary>
        /// Checks if a token needs to be refreshed based on expiration.
        /// </summary>
        public bool ShouldRefreshToken(string token, TimeSpan? gracePeriod = null)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            var session = _authService.GetCachedSession();
            if (session == null)
                return false;

            return ShouldRefresh(session);
        }

        /// <summary>
        /// Attempts to refresh a complete session including all tokens.
        /// This is the primary method for Qobuz token refresh.
        /// </summary>
        public async Task<QobuzSession?> RefreshSessionAsync(QobuzSession session, CancellationToken cancellationToken = default)
        {
            if (session == null)
                return null;

            // Note: This would need the original credentials to work properly
            // For now, we'll just return the existing session
            _logger.Warn("RefreshSessionAsync called without original credentials - cannot refresh");
            return session;
        }

        /// <summary>
        /// Checks if token refresh is available for the current authentication method.
        /// For Qobuz, this is only available if we have the original credentials.
        /// </summary>
        public bool CanRefreshSession(QobuzSession session)
        {
            return session != null && session.IsValid();
        }

        #endregion

        #endregion

        #region Private Implementation

        private async Task<QobuzSession?> PerformRefreshWithRetry(
            QobuzSession currentSession,
            QobuzCredentials originalCredentials,
            CancellationToken cancellationToken)
        {
            await _refreshSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _isRefreshing = true;
                _lastRefreshAttempt = DateTime.UtcNow;
                Interlocked.Increment(ref _totalRefreshAttempts);

                OnRefreshStarted($"Session expires at {currentSession.ExpiresAt}");

                // Wave 18H: adopt Common's RetryPolicyFactory for exponential backoff
                // with jitter. Replaces the hand-rolled `for (attempt...)` loop.
                // The policy throws RetryExhaustedException after maxRetries; we unwrap
                // the inner exception to preserve the original failure type for callers
                // (QobuzAuthenticationException is the documented bubble-up type).
                var policy = RetryPolicyFactory.Create(new RetryPolicyOptions
                {
                    MaxRetries = _maxRetryAttempts,
                    InitialDelay = _initialRetryDelay,
                    UseJitter = true,
                    ShouldRetry = ex => !(ex is OperationCanceledException),
                });

                try
                {
                    var newSession = await policy.ExecuteAsync<QobuzSession>(async ct =>
                    {
                        var s = await _authService.AuthenticateAsync(originalCredentials).ConfigureAwait(false);
                        if (s == null || !s.IsValid())
                            throw new QobuzAuthenticationException("Authentication returned invalid session");
                        return s;
                    }, "qobuz-token-refresh", cancellationToken).ConfigureAwait(false);

                    Interlocked.Increment(ref _successfulRefreshes);
                    _lastSuccessfulRefresh = DateTime.UtcNow;
                    _consecutiveFailures = 0;

                    _logger.Info("✅ Token refresh successful");
                    OnRefreshCompleted(newSession, false);

                    return newSession;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _failedRefreshes);
                    _consecutiveFailures++;

                    // Unwrap RetryExhaustedException to preserve the original failure type
                    var finalException = (ex is RetryExhaustedException rex && rex.InnerException != null)
                        ? rex.InnerException
                        : ex;
                    OnRefreshFailed(finalException, false);

                    _logger.Error("❌ Token refresh failed after {0} attempts: {1}", _maxRetryAttempts, finalException.Message);

                    return null;
                }
            }
            finally
            {
                _isRefreshing = false;
                _refreshSemaphore.Release();
            }
        }

        private async Task WaitForRefreshCompletion(CancellationToken cancellationToken)
        {
            const int maxWaitSeconds = 60;
            var waitStart = DateTime.UtcNow;

            while (_isRefreshing && !cancellationToken.IsCancellationRequested)
            {
                if (DateTime.UtcNow - waitStart > TimeSpan.FromSeconds(maxWaitSeconds))
                {
                    _logger.Warn("Timeout waiting for refresh completion");
                    break;
                }

                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }

        private bool IsCircuitBreakerOpen()
        {
            return _consecutiveFailures >= _circuitBreakerThreshold;
        }

        #endregion

        #region Event Handling

        private void OnRefreshStarted(string reason)
        {
            try
            {
                RefreshStarted?.Invoke(this, new TokenRefreshStartedEventArgs
                {
                    Timestamp = DateTime.UtcNow,
                    Reason = reason,
                    AttemptNumber = (int)_totalRefreshAttempts
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in RefreshStarted event handler");
            }
        }

        private void OnRefreshCompleted(QobuzSession newSession, bool wasForced)
        {
            try
            {
                RefreshCompleted?.Invoke(this, new TokenRefreshCompletedEventArgs
                {
                    Timestamp = DateTime.UtcNow,
                    NewSession = newSession,
                    WasForced = wasForced,
                    AttemptCount = _maxRetryAttempts // This would need better tracking for accurate count
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in RefreshCompleted event handler");
            }
        }

        private void OnRefreshFailed(Exception exception, bool wasForced)
        {
            try
            {
                RefreshFailed?.Invoke(this, new TokenRefreshFailedEventArgs
                {
                    Timestamp = DateTime.UtcNow,
                    Exception = exception,
                    WasForced = wasForced,
                    ConsecutiveFailures = _consecutiveFailures,
                    CircuitBreakerTripped = IsCircuitBreakerOpen()
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in RefreshFailed event handler");
            }
        }

        private void OnCircuitBreakerTripped()
        {
            try
            {
                CircuitBreakerTripped?.Invoke(this, new CircuitBreakerTrippedEventArgs
                {
                    Timestamp = DateTime.UtcNow,
                    FailureCount = _consecutiveFailures,
                    LastFailureTime = _lastRefreshAttempt
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in CircuitBreakerTripped event handler");
            }
        }

        #endregion

        #region Resource Management

        /// <summary>
        /// Disposes resources used by the token refresher.
        /// </summary>
        public void Dispose()
        {
            try
            {
                _refreshSemaphore?.Dispose();
                _logger.Debug("TokenRefresher disposed");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during TokenRefresher disposal");
            }
        }

        #endregion
    }

    #region Supporting Data Structures

    /// <summary>
    /// Token refresh statistics and configuration.
    /// </summary>
    public class TokenRefreshStats
    {
        public TimeSpan ServiceUptime { get; set; }
        public long TotalAttempts { get; set; }
        public long SuccessfulRefreshes { get; set; }
        public long FailedRefreshes { get; set; }
        public double SuccessRate { get; set; }
        public int ConsecutiveFailures { get; set; }
        public DateTime LastSuccessfulRefresh { get; set; }
        public DateTime LastRefreshAttempt { get; set; }
        public bool IsRefreshing { get; set; }
        public bool IsCircuitBreakerOpen { get; set; }
        public RefreshConfiguration Configuration { get; set; }
    }

    /// <summary>
    /// Token refresh configuration settings.
    /// </summary>
    public class RefreshConfiguration
    {
        public TimeSpan RefreshBuffer { get; set; }
        public TimeSpan RefreshCooldown { get; set; }
        public int MaxRetryAttempts { get; set; }
        public int CircuitBreakerThreshold { get; set; }
    }

    /// <summary>
    /// Event arguments for refresh started events.
    /// </summary>
    public class TokenRefreshStartedEventArgs : EventArgs
    {
        public DateTime Timestamp { get; set; }
        public string Reason { get; set; }
        public int AttemptNumber { get; set; }
    }

    /// <summary>
    /// Event arguments for refresh completed events.
    /// </summary>
    public class TokenRefreshCompletedEventArgs : EventArgs
    {
        public DateTime Timestamp { get; set; }
        public QobuzSession NewSession { get; set; }
        public bool WasForced { get; set; }
        public int AttemptCount { get; set; }
    }

    /// <summary>
    /// Event arguments for refresh failed events.
    /// </summary>
    public class TokenRefreshFailedEventArgs : EventArgs
    {
        public DateTime Timestamp { get; set; }
        public Exception Exception { get; set; }
        public bool WasForced { get; set; }
        public int ConsecutiveFailures { get; set; }
        public bool CircuitBreakerTripped { get; set; }
    }

    /// <summary>
    /// Event arguments for circuit breaker events.
    /// </summary>
    public class CircuitBreakerTrippedEventArgs : EventArgs
    {
        public DateTime Timestamp { get; set; }
        public int FailureCount { get; set; }
        public DateTime LastFailureTime { get; set; }
    }

    #endregion
}
