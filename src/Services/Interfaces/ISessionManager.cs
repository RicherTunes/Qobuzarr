using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace Lidarr.Plugin.Qobuzarr.Services.Interfaces
{
    /// <summary>
    /// Interface for managing Qobuz authentication sessions.
    /// </summary>
    /// <remarks>
    /// This interface handles the lifecycle of Qobuz authentication sessions,
    /// including creation, validation, persistence, and cleanup.
    /// 
    /// Key Features:
    /// - Session creation from credentials
    /// - Session validation and health checks
    /// - Persistent session storage and retrieval
    /// - Session cleanup and expiration handling
    /// - Thread-safe session management
    /// - Automatic session refresh when possible
    /// 
    /// Sessions are persisted to survive application restarts and
    /// provide seamless authentication across plugin lifecycle.
    /// </remarks>
    public interface ISessionManager
    {
        /// <summary>
        /// Creates a new authentication session from credentials.
        /// </summary>
        /// <param name="credentials">The validated credentials</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>The created session or null if creation failed</returns>
        Task<QobuzSession?> CreateSessionAsync(QobuzCredentials credentials, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current active session if available and valid.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>The current session or null if no valid session exists</returns>
        Task<QobuzSession?> GetCurrentSessionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates an existing session and checks if it's still active.
        /// </summary>
        /// <param name="session">The session to validate</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>True if the session is valid and active</returns>
        Task<bool> IsSessionValidAsync(QobuzSession session, CancellationToken cancellationToken = default);

        /// <summary>
        /// Invalidates and cleans up the current session.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Task representing the cleanup operation</returns>
        Task InvalidateSessionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Refreshes an existing session if possible.
        /// </summary>
        /// <param name="session">The session to refresh</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>The refreshed session or null if refresh failed</returns>
        Task<QobuzSession?> RefreshSessionAsync(QobuzSession session, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if there's a valid session available.
        /// </summary>
        /// <returns>True if a valid session is available</returns>
        bool HasValidSession();
    }
}