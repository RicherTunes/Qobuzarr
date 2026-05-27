using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Common.Observability;
using Lidarr.Plugin.Common.Services.Diagnostics;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Authentication token management service that handles token refresh during long operations
    /// Prevents authentication failures during large batch operations that exceed token validity
    /// </summary>
    /// <remarks>
    /// Critical Issue: Long-running operations fail due to:
    /// - Auth tokens expiring during large discography downloads (30min+ operations)
    /// - No automatic refresh mechanism for active operations
    /// - Batch failures requiring full restart instead of token refresh
    /// - Inconsistent authentication state across concurrent operations
    /// 
    /// This manager provides:
    /// 1. Proactive token refresh before expiration
    /// 2. Automatic retry with new tokens on auth failures
    /// 3. Thread-safe token management for concurrent operations
    /// 4. Background monitoring and preemptive refresh
    /// 5. Graceful handling of refresh failures and fallback strategies
    /// </remarks>
    public class AuthTokenManager : IDisposable
    {
        private readonly Logger _logger;
        private readonly IQobuzAuthService _authService;
        private readonly Timer _refreshTimer;
        private readonly SemaphoreSlim _refreshSemaphore;
        private readonly object _tokenLock = new();

        // Token management state
        private volatile string _currentToken;
        private DateTime _tokenExpiryTime; // Not volatile - accessed within locks
        private volatile bool _isRefreshing = false;
        private volatile int _refreshAttempts = 0;

        // Configuration
        private readonly TimeSpan _refreshBufferTime = TimeSpan.FromMinutes(5); // Refresh 5 minutes before expiry
        private readonly TimeSpan _refreshCheckInterval = TimeSpan.FromMinutes(1); // Check every minute
        private readonly int _maxRefreshAttempts = 3;
        private readonly TimeSpan _refreshRetryDelay = TimeSpan.FromSeconds(30);

        // Events
        public event EventHandler<TokenRefreshEventArgs> TokenRefreshed;
        public event EventHandler<TokenRefreshFailedEventArgs> TokenRefreshFailed;

        public AuthTokenManager(IQobuzAuthService authService, Logger logger = null)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _refreshSemaphore = new SemaphoreSlim(1, 1);

            // Start background refresh monitoring
            _refreshTimer = new Timer(CheckTokenRefreshAsync, null,
                _refreshCheckInterval, _refreshCheckInterval);

            _logger.Info("🔐 AUTH TOKEN MANAGER: Initialized with {0}min refresh buffer",
                        _refreshBufferTime.TotalMinutes);
        }

        /// <summary>
        /// Gets current valid token, refreshing if necessary
        /// </summary>
        /// <param name="forceRefresh">Forces token refresh even if not expired</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Current valid authentication token</returns>
        public async Task<string> GetValidTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            // Quick check for valid token without locking
            if (!forceRefresh && IsTokenValid() && !string.IsNullOrEmpty(_currentToken))
            {
                return _currentToken;
            }

            // Need to refresh or get new token
            await _refreshSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Double-check after acquiring lock
                if (!forceRefresh && IsTokenValid() && !string.IsNullOrEmpty(_currentToken))
                {
                    return _currentToken;
                }

                _logger.Debug("🔐 TOKEN REFRESH: {0}", forceRefresh ? "Forced refresh" : "Token expired/invalid");

                await RefreshTokenInternalAsync(cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrEmpty(_currentToken))
                {
                    throw new AuthenticationException("Failed to obtain valid authentication token");
                }

                return _currentToken;
            }
            finally
            {
                _refreshSemaphore.Release();
            }
        }

        /// <summary>
        /// Executes an operation with automatic token refresh on authentication failure
        /// </summary>
        /// <typeparam name="T">Return type of the operation</typeparam>
        /// <param name="operation">Operation to execute with token</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result of the operation</returns>
        public async Task<T> ExecuteWithTokenRefreshAsync<T>(
            Func<string, CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            var maxAttempts = 2; // Original attempt + one retry after refresh
            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var token = await GetValidTokenAsync(attempt > 1, cancellationToken).ConfigureAwait(false);
                    return await operation(token, cancellationToken).ConfigureAwait(false);
                }
                catch (AuthenticationException authEx) when (attempt < maxAttempts)
                {
                    lastException = authEx;
                    _logger.Warn("🔐 AUTH FAILURE: Attempt {0} failed, refreshing token: {1}", attempt, authEx.Message);

                    // Mark token as invalid to force refresh
                    lock (_tokenLock)
                    {
                        _tokenExpiryTime = DateTime.UtcNow.AddSeconds(-1);
                    }

                    // Continue to next attempt which will force refresh
                }
                catch (Exception ex) when (IsAuthenticationError(ex) && attempt < maxAttempts)
                {
                    lastException = ex;
                    _logger.Warn("🔐 POTENTIAL AUTH ERROR: Attempt {0} failed, trying token refresh: {1}", attempt, ex.Message);

                    // Mark token as invalid to force refresh
                    lock (_tokenLock)
                    {
                        _tokenExpiryTime = DateTime.UtcNow.AddSeconds(-1);
                    }

                    // Continue to next attempt
                }
            }

            // All attempts failed
            _logger.Error("🔐 AUTH EXHAUSTED: All authentication attempts failed");
            throw lastException ?? new AuthenticationException("Operation failed after token refresh attempts");
        }

        /// <summary>
        /// Initializes token with initial authentication
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the initialization</returns>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            _logger.Info("🔐 INITIALIZING: Getting initial authentication token");

            await _refreshSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await RefreshTokenInternalAsync(cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrEmpty(_currentToken))
                {
                    throw new AuthenticationException("Failed to obtain initial authentication token");
                }

                _logger.Info("✅ INITIALIZED: Authentication token obtained, expires: {0}", _tokenExpiryTime);
            }
            finally
            {
                _refreshSemaphore.Release();
            }
        }

        /// <summary>
        /// Gets current token status information
        /// </summary>
        public TokenStatus GetTokenStatus()
        {
            lock (_tokenLock)
            {
                var now = DateTime.UtcNow;
                var timeToExpiry = _tokenExpiryTime > now ? _tokenExpiryTime - now : TimeSpan.Zero;
                var shouldRefreshSoon = timeToExpiry <= _refreshBufferTime;

                return new TokenStatus
                {
                    HasToken = !string.IsNullOrEmpty(_currentToken),
                    ExpiryTime = _tokenExpiryTime,
                    TimeToExpiry = timeToExpiry,
                    IsExpired = now >= _tokenExpiryTime,
                    ShouldRefreshSoon = shouldRefreshSoon,
                    IsRefreshing = _isRefreshing,
                    RefreshAttempts = _refreshAttempts
                };
            }
        }

        #region Private Methods

        /// <summary>
        /// Checks if current token is valid and not near expiration
        /// </summary>
        private bool IsTokenValid()
        {
            lock (_tokenLock)
            {
                return !string.IsNullOrEmpty(_currentToken) &&
                       DateTime.UtcNow < (_tokenExpiryTime - _refreshBufferTime);
            }
        }

        /// <summary>
        /// Internal token refresh implementation. Refresh-slot claiming is atomic:
        /// at most one thread enters the refresh branch even under heavy concurrent
        /// load; everyone else falls through to the wait loop. The previous code did
        /// the check-and-set on `_isRefreshing` outside the lock, which let two
        /// threads both observe `_isRefreshing == false` and both call
        /// `_authService.AuthenticateAsync()` — last writer to `_currentToken` won,
        /// silently overwriting the earlier (still-valid) refresh.
        /// </summary>
        private async Task RefreshTokenInternalAsync(CancellationToken cancellationToken)
        {
            bool weOwnRefresh;
            lock (_tokenLock)
            {
                if (_isRefreshing)
                {
                    weOwnRefresh = false;
                }
                else
                {
                    _isRefreshing = true;
                    weOwnRefresh = true;
                }
            }

            if (!weOwnRefresh)
            {
                // Some other thread is performing the refresh. Wait for completion.
                // Polling _isRefreshing is safe because it's volatile and the owner
                // clears it in `finally`, so the read sees a fresh value once the
                // owner exits.
                while (_isRefreshing && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }
                return;
            }

            var refreshStartTime = DateTime.UtcNow;

            using var _scope = PluginLogContext.Push("Qobuzarr", "AuthRefresh");
            try
            {
                _logger.Debug($"{PluginLogContext.Current?.LinePrefix()}TOKEN REFRESH: Starting refresh process");

                var authResult = await _authService.AuthenticateAsync(cancellationToken).ConfigureAwait(false);

                if (authResult == null || string.IsNullOrEmpty(authResult.Token))
                {
                    throw new AuthenticationException("Authentication service returned null or empty token");
                }

                lock (_tokenLock)
                {
                    _currentToken = authResult.Token;
                    _tokenExpiryTime = authResult.ExpiryTime ?? DateTime.UtcNow.AddHours(1); // Default 1 hour if not provided
                    _refreshAttempts = 0; // Reset attempt counter on success
                }

                var refreshDuration = DateTime.UtcNow - refreshStartTime;
                _logger.Info("✅ TOKEN REFRESHED: New token obtained in {0:F1}s, expires: {1}",
                           refreshDuration.TotalSeconds, _tokenExpiryTime);

                // Notify subscribers
                TokenRefreshed?.Invoke(this, new TokenRefreshEventArgs
                {
                    NewToken = _currentToken,
                    ExpiryTime = _tokenExpiryTime,
                    RefreshDuration = refreshDuration
                });
            }
            catch (Exception ex)
            {
                _refreshAttempts++;
                var refreshDuration = DateTime.UtcNow - refreshStartTime;

                _logger.Error(ex, "❌ TOKEN REFRESH FAILED: Attempt {0}/{1} failed after {2:F1}s",
                             _refreshAttempts, _maxRefreshAttempts, refreshDuration.TotalSeconds);

                // Notify subscribers of failure
                TokenRefreshFailed?.Invoke(this, new TokenRefreshFailedEventArgs
                {
                    Exception = ex,
                    AttemptNumber = _refreshAttempts,
                    MaxAttempts = _maxRefreshAttempts,
                    RefreshDuration = refreshDuration
                });

                // If we've exhausted attempts, clear the token
                if (_refreshAttempts >= _maxRefreshAttempts)
                {
                    lock (_tokenLock)
                    {
                        _currentToken = null;
                        _tokenExpiryTime = DateTime.UtcNow.AddSeconds(-1);
                    }

                    _logger.Error("💥 TOKEN EXHAUSTED: All refresh attempts failed, token cleared");
                }

                throw;
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        /// <summary>
        /// Background timer callback to check for token refresh needs
        /// </summary>
        private async void CheckTokenRefreshAsync(object? state)
        {
            try
            {
                var status = GetTokenStatus();

                if (status.HasToken && status.ShouldRefreshSoon && !status.IsRefreshing)
                {
                    _logger.Debug("⏰ PROACTIVE REFRESH: Token expires in {0:F1}min, refreshing preemptively",
                                 status.TimeToExpiry.TotalMinutes);

                    // Use a short timeout for background refresh to avoid blocking
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

                    try
                    {
                        await GetValidTokenAsync(false, cts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "⚠️ BACKGROUND REFRESH FAILED: Will retry on next check");
                        // Don't throw - this is background maintenance
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in background token refresh check");
            }
        }

        /// <summary>
        /// Determines if an exception indicates an authentication error
        /// </summary>
        private bool IsAuthenticationError(Exception ex)
        {
            if (ex is AuthenticationException)
                return true;

            return HttpExceptionClassifier.Classify(ex).Category == HttpFailureCategory.Auth;
        }

        #endregion

        /// <summary>
        /// Disposes the auth token manager
        /// </summary>
        public void Dispose()
        {
            try
            {
                _refreshTimer?.Dispose();
                _refreshSemaphore?.Dispose();
                _logger.Debug("AuthTokenManager disposed");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during AuthTokenManager disposal");
            }
            finally
            {
                GC.SuppressFinalize(this);
            }
        }
    }

    #region Supporting Classes

    /// <summary>
    /// Current token status information
    /// </summary>
    public class TokenStatus
    {
        public bool HasToken { get; set; }
        public DateTime ExpiryTime { get; set; }
        public TimeSpan TimeToExpiry { get; set; }
        public bool IsExpired { get; set; }
        public bool ShouldRefreshSoon { get; set; }
        public bool IsRefreshing { get; set; }
        public int RefreshAttempts { get; set; }

        public double MinutesToExpiry => TimeToExpiry.TotalMinutes;
        public bool IsHealthy => HasToken && !IsExpired && !ShouldRefreshSoon;
    }

    /// <summary>
    /// Event args for successful token refresh
    /// </summary>
    public class TokenRefreshEventArgs : EventArgs
    {
        public string NewToken { get; set; }
        public DateTime ExpiryTime { get; set; }
        public TimeSpan RefreshDuration { get; set; }
    }

    /// <summary>
    /// Event args for failed token refresh
    /// </summary>
    public class TokenRefreshFailedEventArgs : EventArgs
    {
        public Exception Exception { get; set; }
        public int AttemptNumber { get; set; }
        public int MaxAttempts { get; set; }
        public TimeSpan RefreshDuration { get; set; }

        public bool IsLastAttempt => AttemptNumber >= MaxAttempts;
    }

    /// <summary>
    /// Authentication exception for token-related errors
    /// </summary>
    public class AuthenticationException : Exception
    {
        public AuthenticationException(string message) : base(message) { }
        public AuthenticationException(string message, Exception innerException) : base(message, innerException) { }
    }

    #endregion
}
