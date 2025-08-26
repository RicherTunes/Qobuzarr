using System;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Common.Interfaces
{
    /// <summary>
    /// Generic authentication service interface for streaming services.
    /// Provides a common contract for different authentication patterns (OAuth2, token-based, etc.).
    /// </summary>
    /// <typeparam name="TSession">The session type specific to the streaming service</typeparam>
    /// <typeparam name="TCredentials">The credentials type specific to the streaming service</typeparam>
    public interface IStreamingAuthenticationService<TSession, TCredentials>
        where TSession : class, IAuthSession
        where TCredentials : class, IAuthCredentials
    {
        /// <summary>
        /// Authenticates with the streaming service using the provided credentials.
        /// </summary>
        /// <param name="credentials">The authentication credentials</param>
        /// <returns>A valid session containing auth tokens and user information</returns>
        Task<AuthResult<TSession>> AuthenticateAsync(TCredentials credentials);

        /// <summary>
        /// Gets the current valid session, refreshing if necessary and possible.
        /// </summary>
        /// <returns>A valid session or null if authentication is required</returns>
        Task<TSession> GetValidSessionAsync();

        /// <summary>
        /// Validates if a session is still active with the streaming service.
        /// </summary>
        /// <param name="session">The session to validate</param>
        /// <returns>True if the session is valid and active</returns>
        Task<bool> ValidateSessionAsync(TSession session);

        /// <summary>
        /// Refreshes an expired session if the service supports it.
        /// </summary>
        /// <param name="session">The session to refresh</param>
        /// <returns>A new valid session or null if refresh is not possible</returns>
        Task<TSession?> RefreshSessionAsync(TSession session);

        /// <summary>
        /// Revokes/logs out the current session.
        /// </summary>
        /// <param name="session">The session to revoke</param>
        Task RevokeSessionAsync(TSession session);

        /// <summary>
        /// Gets the currently cached session without validation.
        /// </summary>
        /// <returns>The cached session or null if none exists</returns>
        TSession GetCachedSession();

        /// <summary>
        /// Clears the cached session from memory/storage.
        /// </summary>
        void ClearSession();

        /// <summary>
        /// Stores a valid session in the cache.
        /// </summary>
        /// <param name="session">The session to cache</param>
        void StoreSession(TSession session);
    }

    /// <summary>
    /// Base interface for authentication sessions.
    /// </summary>
    public interface IAuthSession
    {
        /// <summary>
        /// The primary access token for API calls.
        /// </summary>
        string AccessToken { get; }

        /// <summary>
        /// When the session expires (if known).
        /// </summary>
        DateTime? ExpiresAt { get; }

        /// <summary>
        /// Whether the session has expired.
        /// </summary>
        bool IsExpired { get; }

        /// <summary>
        /// Service-specific metadata about the session.
        /// </summary>
        System.Collections.Generic.Dictionary<string, object> Metadata { get; }
    }

    /// <summary>
    /// Base interface for authentication credentials.
    /// </summary>
    public interface IAuthCredentials
    {
        /// <summary>
        /// The type of authentication these credentials represent.
        /// </summary>
        AuthenticationType Type { get; }

        /// <summary>
        /// Validates that the credentials are complete and properly formatted.
        /// </summary>
        /// <param name="errorMessage">Error message if validation fails</param>
        /// <returns>True if credentials are valid</returns>
        bool IsValid(out string errorMessage);
    }

    /// <summary>
    /// Types of authentication supported by streaming services.
    /// </summary>
    public enum AuthenticationType
    {
        /// <summary>
        /// Username/email and password authentication.
        /// </summary>
        UsernamePassword,
        
        /// <summary>
        /// OAuth2 authorization code flow.
        /// </summary>
        OAuth2,
        
        /// <summary>
        /// Simple API key authentication.
        /// </summary>
        ApiKey,
        
        /// <summary>
        /// Pre-existing token authentication.
        /// </summary>
        Token,
        
        /// <summary>
        /// Certificate-based authentication.
        /// </summary>
        Certificate
    }

    /// <summary>
    /// Result of an authentication attempt.
    /// </summary>
    /// <typeparam name="TSession">The session type</typeparam>
    public class AuthResult<TSession> where TSession : class
    {
        /// <summary>
        /// Whether authentication was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The authenticated session (if successful).
        /// </summary>
        public TSession Session { get; set; }

        /// <summary>
        /// Error message if authentication failed.
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// The type of error that occurred.
        /// </summary>
        public AuthErrorType? ErrorType { get; set; }

        /// <summary>
        /// Additional context about the error.
        /// </summary>
        public System.Collections.Generic.Dictionary<string, object> ErrorContext { get; set; } = 
            new System.Collections.Generic.Dictionary<string, object>();

        /// <summary>
        /// Creates a successful authentication result.
        /// </summary>
        public static AuthResult<TSession> Successful(TSession session) =>
            new AuthResult<TSession> { Success = true, Session = session };

        /// <summary>
        /// Creates a failed authentication result.
        /// </summary>
        public static AuthResult<TSession> Failed(string errorMessage, AuthErrorType errorType = AuthErrorType.Unknown) =>
            new AuthResult<TSession> { Success = false, ErrorMessage = errorMessage, ErrorType = errorType };
    }

    /// <summary>
    /// Types of authentication errors.
    /// </summary>
    public enum AuthErrorType
    {
        Unknown,
        InvalidCredentials,
        AccountLocked,
        TwoFactorRequired,
        NetworkError,
        ServiceUnavailable,
        RateLimited,
        SubscriptionRequired,
        RegionBlocked
    }
}