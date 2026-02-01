using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace Lidarr.Plugin.Qobuzarr.Services.Interfaces
{
    /// <summary>
    /// Interface for orchestrating authentication-related services.
    /// </summary>
    /// <remarks>
    /// This interface coordinates credential validation, session management,
    /// token refresh, and authentication workflows across the authentication domain services.
    /// 
    /// Key Features:
    /// - Complete authentication workflows
    /// - Session lifecycle management
    /// - Automatic token refresh
    /// - Credential validation integration
    /// - Authentication health monitoring
    /// 
    /// This orchestrator provides a unified interface for all authentication
    /// operations while coordinating multiple underlying services.
    /// </remarks>
    public interface IAuthenticationOrchestrator
    {
        /// <summary>
        /// Performs complete authentication workflow from credentials.
        /// </summary>
        /// <param name="credentials">The user credentials</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Authentication result with session</returns>
        Task<AuthenticationResult> AuthenticateAsync(QobuzCredentials credentials, CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates existing authentication and refreshes if needed.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Authentication validation result</returns>
        Task<AuthenticationValidationResult> ValidateAuthenticationAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Refreshes the current session if possible.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Session refresh result</returns>
        Task<SessionRefreshResult> RefreshCurrentSessionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs complete logout and session cleanup.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Logout result</returns>
        Task<LogoutResult> LogoutAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current authentication status.
        /// </summary>
        /// <returns>Current authentication status</returns>
        AuthenticationStatus GetAuthenticationStatus();

        /// <summary>
        /// Checks if the user is currently authenticated.
        /// </summary>
        /// <returns>True if authenticated</returns>
        bool IsAuthenticated();

        /// <summary>
        /// Gets the current session if available.
        /// </summary>
        /// <returns>The current session or null</returns>
        QobuzSession? GetCurrentSession();
    }

    /// <summary>
    /// Result of authentication operation.
    /// </summary>
    public class AuthenticationResult
    {
        public bool Success { get; set; }
        public QobuzSession? Session { get; set; }
        public AuthenticationMethod Method { get; set; }
        public string Error { get; set; }
        public System.TimeSpan AuthenticationTime { get; set; }
        public bool RequiresReauthentication { get; set; }
    }

    /// <summary>
    /// Result of authentication validation.
    /// </summary>
    public class AuthenticationValidationResult
    {
        public bool IsValid { get; set; }
        public bool WasRefreshed { get; set; }
        public QobuzSession? RefreshedSession { get; set; }
        public string Error { get; set; }
        public System.TimeSpan ValidationTime { get; set; }
    }

    /// <summary>
    /// Result of session refresh operation.
    /// </summary>
    public class SessionRefreshResult
    {
        public bool Success { get; set; }
        public QobuzSession? RefreshedSession { get; set; }
        public bool RequiresReauthentication { get; set; }
        public string Error { get; set; }
        public System.TimeSpan RefreshTime { get; set; }
    }

    /// <summary>
    /// Result of logout operation.
    /// </summary>
    public class LogoutResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public System.TimeSpan LogoutTime { get; set; }
    }

    /// <summary>
    /// Authentication status information.
    /// </summary>
    public class AuthenticationStatus
    {
        public bool IsAuthenticated { get; set; }
        public AuthenticationMethod? Method { get; set; }
        public System.DateTime? AuthenticatedAt { get; set; }
        public System.DateTime? ExpiresAt { get; set; }
        public string UserId { get; set; }
        public bool RequiresRefresh { get; set; }
        public System.TimeSpan? TimeUntilExpiration { get; set; }
    }

    /// <summary>
    /// Authentication methods.
    /// </summary>
    public enum AuthenticationMethod
    {
        Unknown = 0,
        EmailPassword = 1,
        Token = 2,
        RefreshToken = 3
    }
}
