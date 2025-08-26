using System;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Interfaces;
using Lidarr.Plugin.Common.Utilities;

namespace Lidarr.Plugin.Common.Services.Authentication
{
    /// <summary>
    /// Base implementation of streaming service authentication.
    /// Provides common functionality like session management, caching, and retry logic.
    /// </summary>
    /// <typeparam name="TSession">The service-specific session type</typeparam>
    /// <typeparam name="TCredentials">The service-specific credentials type</typeparam>
    public abstract class BaseStreamingAuthenticationService<TSession, TCredentials> 
        : IStreamingAuthenticationService<TSession, TCredentials>
        where TSession : class, IAuthSession
        where TCredentials : class, IAuthCredentials
    {
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
        protected TSession _cachedSession;
        protected readonly object _sessionLock = new object();

        /// <summary>
        /// Authenticates with the streaming service using the provided credentials.
        /// Includes retry logic and error handling.
        /// </summary>
        public virtual async Task<AuthResult<TSession>> AuthenticateAsync(TCredentials credentials)
        {
            if (credentials == null)
                throw new ArgumentNullException(nameof(credentials));

            if (!credentials.IsValid(out string validationError))
                return AuthResult<TSession>.Failed(validationError, AuthErrorType.InvalidCredentials);

            try
            {
                // Use retry logic for transient network issues
                var session = await RetryUtilities.ExecuteWithRetryAsync(
                    () => PerformAuthenticationAsync(credentials),
                    maxRetries: 3,
                    initialDelayMs: 1000,
                    "Authentication",
                    (ex, attempt, message) => OnAuthenticationRetry(credentials, ex, attempt, message));

                if (session == null)
                    return AuthResult<TSession>.Failed("Authentication failed - no session returned", AuthErrorType.Unknown);

                // Store the session
                StoreSession(session);
                OnAuthenticationSuccess(session);

                return AuthResult<TSession>.Successful(session);
            }
            catch (Exception ex)
            {
                OnAuthenticationError(credentials, ex);
                return AuthResult<TSession>.Failed(ex.Message, ClassifyAuthenticationError(ex));
            }
        }

        /// <summary>
        /// Gets a valid session, refreshing if necessary and supported.
        /// </summary>
        public virtual async Task<TSession> GetValidSessionAsync()
        {
            var cachedSession = GetCachedSession();
            
            if (cachedSession == null)
                return null;

            if (!cachedSession.IsExpired)
                return cachedSession;

            // Try to refresh if supported
            if (SupportsRefresh())
            {
                await _refreshLock.WaitAsync();
                try
                {
                    // Double-check after acquiring lock
                    cachedSession = GetCachedSession();
                    if (cachedSession != null && !cachedSession.IsExpired)
                        return cachedSession;

                    // Attempt refresh
                    var refreshedSession = await RefreshSessionAsync(cachedSession);
                    if (refreshedSession != null)
                    {
                        StoreSession(refreshedSession);
                        OnSessionRefreshed(cachedSession, refreshedSession);
                        return refreshedSession;
                    }
                }
                finally
                {
                    _refreshLock.Release();
                }
            }

            // Clear expired session and return null to indicate re-authentication needed
            ClearSession();
            OnSessionExpired(cachedSession);
            return null;
        }

        /// <summary>
        /// Validates if a session is still active with the streaming service.
        /// </summary>
        public virtual async Task<bool> ValidateSessionAsync(TSession session)
        {
            if (session == null || session.IsExpired)
                return false;

            try
            {
                return await PerformSessionValidationAsync(session);
            }
            catch (Exception ex)
            {
                OnSessionValidationError(session, ex);
                return false;
            }
        }

        /// <summary>
        /// Refreshes an expired session if the service supports it.
        /// </summary>
        public virtual async Task<TSession?> RefreshSessionAsync(TSession session)
        {
            if (session == null)
                return null;

            if (!SupportsRefresh())
            {
                OnRefreshNotSupported(session);
                return null;
            }

            try
            {
                return await PerformSessionRefreshAsync(session);
            }
            catch (Exception ex)
            {
                OnRefreshError(session, ex);
                return null;
            }
        }

        /// <summary>
        /// Revokes/logs out the current session.
        /// </summary>
        public virtual async Task RevokeSessionAsync(TSession session)
        {
            if (session == null)
                return;

            try
            {
                if (SupportsRevocation())
                {
                    await PerformSessionRevocationAsync(session);
                }
                
                ClearSession();
                OnSessionRevoked(session);
            }
            catch (Exception ex)
            {
                OnRevocationError(session, ex);
                // Still clear the local session even if remote revocation failed
                ClearSession();
            }
        }

        /// <summary>
        /// Gets the currently cached session without validation.
        /// </summary>
        public virtual TSession GetCachedSession()
        {
            lock (_sessionLock)
            {
                return _cachedSession;
            }
        }

        /// <summary>
        /// Clears the cached session from memory.
        /// </summary>
        public virtual void ClearSession()
        {
            lock (_sessionLock)
            {
                var previousSession = _cachedSession;
                _cachedSession = null;
                
                if (previousSession != null)
                {
                    OnSessionCleared(previousSession);
                }
            }
        }

        /// <summary>
        /// Stores a valid session in the cache.
        /// </summary>
        public virtual void StoreSession(TSession session)
        {
            if (session == null)
                return;

            lock (_sessionLock)
            {
                _cachedSession = session;
                OnSessionStored(session);
            }
        }

        /// <summary>
        /// Performs the actual authentication with the streaming service.
        /// Must be implemented by derived classes.
        /// </summary>
        protected abstract Task<TSession> PerformAuthenticationAsync(TCredentials credentials);

        /// <summary>
        /// Performs session validation with the streaming service.
        /// Should be overridden by derived classes if supported.
        /// </summary>
        protected virtual Task<bool> PerformSessionValidationAsync(TSession session)
        {
            // Default implementation - assume valid if not expired
            return Task.FromResult(!session.IsExpired);
        }

        /// <summary>
        /// Performs session refresh with the streaming service.
        /// Should be overridden by derived classes if supported.
        /// </summary>
        protected virtual Task<TSession?> PerformSessionRefreshAsync(TSession session)
        {
            // Default implementation - not supported
            return Task.FromResult((TSession?)null);
        }

        /// <summary>
        /// Performs session revocation with the streaming service.
        /// Should be overridden by derived classes if supported.
        /// </summary>
        protected virtual Task PerformSessionRevocationAsync(TSession session)
        {
            // Default implementation - no action needed
            return Task.CompletedTask;
        }

        /// <summary>
        /// Whether this service supports session refresh.
        /// </summary>
        protected virtual bool SupportsRefresh() => false;

        /// <summary>
        /// Whether this service supports session revocation.
        /// </summary>
        protected virtual bool SupportsRevocation() => false;

        /// <summary>
        /// Classifies an authentication exception into an error type.
        /// </summary>
        protected virtual AuthErrorType ClassifyAuthenticationError(Exception ex)
        {
            // Basic classification - derived classes should override for service-specific errors
            if (ex.Message.ToLowerInvariant().Contains("invalid") ||
                ex.Message.ToLowerInvariant().Contains("unauthorized"))
                return AuthErrorType.InvalidCredentials;

            if (ex.Message.ToLowerInvariant().Contains("network") ||
                ex.Message.ToLowerInvariant().Contains("timeout"))
                return AuthErrorType.NetworkError;

            if (ex.Message.ToLowerInvariant().Contains("rate") ||
                ex.Message.ToLowerInvariant().Contains("limit"))
                return AuthErrorType.RateLimited;

            return AuthErrorType.Unknown;
        }

        // Event methods for derived classes to override

        protected virtual void OnAuthenticationSuccess(TSession session) { }
        protected virtual void OnAuthenticationError(TCredentials credentials, Exception ex) { }
        protected virtual void OnAuthenticationRetry(TCredentials credentials, Exception ex, int attempt, string message) { }
        protected virtual void OnSessionRefreshed(TSession oldSession, TSession newSession) { }
        protected virtual void OnSessionExpired(TSession session) { }
        protected virtual void OnSessionRevoked(TSession session) { }
        protected virtual void OnSessionCleared(TSession session) { }
        protected virtual void OnSessionStored(TSession session) { }
        protected virtual void OnSessionValidationError(TSession session, Exception ex) { }
        protected virtual void OnRefreshNotSupported(TSession session) { }
        protected virtual void OnRefreshError(TSession session, Exception ex) { }
        protected virtual void OnRevocationError(TSession session, Exception ex) { }

        /// <summary>
        /// Disposes of resources used by this authentication service.
        /// </summary>
        public virtual void Dispose()
        {
            _refreshLock?.Dispose();
            ClearSession();
        }
    }
}