using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace Lidarr.Plugin.Qobuzarr.Authentication
{
    /// <summary>
    /// Defines the contract for Qobuz authentication operations.
    /// Manages user authentication, session lifecycle, and credential validation for the Qobuz API.
    /// </summary>
    public interface IQobuzAuthenticationService
    {
        /// <summary>
        /// Authenticates with the Qobuz API using the provided credentials.
        /// Supports both email/password and user ID/token authentication methods.
        /// </summary>
        /// <param name="credentials">The authentication credentials containing either email/password or userId/token.</param>
        /// <returns>A valid QobuzSession containing the auth token and user information.</returns>
        /// <exception cref="QobuzAuthenticationException">Thrown when authentication fails due to invalid credentials.</exception>
        /// <exception cref="InvalidOperationException">Thrown when credentials are incomplete or invalid.</exception>
        Task<QobuzSession> AuthenticateAsync(QobuzCredentials credentials);

        /// <summary>
        /// Refreshes an existing session using the refresh token.
        /// Note: Qobuz doesn't support traditional refresh tokens - sessions expire after 24 hours.
        /// </summary>
        /// <param name="refreshToken">The refresh token (not currently supported by Qobuz).</param>
        /// <returns>A new session (not implemented - will throw NotSupportedException).</returns>
        /// <exception cref="NotSupportedException">Always thrown as Qobuz requires re-authentication.</exception>
        Task<QobuzSession> RefreshSessionAsync(string refreshToken);

        /// <summary>
        /// Validates if a session is still active and accepted by the Qobuz API.
        /// Makes a test API call to verify the session credentials are still valid.
        /// </summary>
        /// <param name="session">The session to validate.</param>
        /// <returns>True if the session is valid and active; false otherwise.</returns>
        Task<bool> ValidateSessionAsync(QobuzSession session);

        /// <summary>
        /// Retrieves the currently cached session from memory.
        /// Returns null if no session is cached or if the cached session has expired.
        /// </summary>
        /// <returns>The cached QobuzSession or null if none exists or is invalid.</returns>
        QobuzSession GetCachedSession();

        /// <summary>
        /// Clears the cached session from memory.
        /// Should be called when the user logs out or when authentication errors occur.
        /// </summary>
        void ClearSession();

        /// <summary>
        /// Stores a valid session in the cache for reuse.
        /// Sessions are cached for up to 24 hours (Qobuz session lifetime).
        /// </summary>
        /// <param name="session">The session to store in cache.</param>
        void StoreSession(QobuzSession session);
    }
}
