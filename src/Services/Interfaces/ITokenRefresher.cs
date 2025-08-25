using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace Lidarr.Plugin.Qobuzarr.Services.Interfaces
{
    /// <summary>
    /// Interface for refreshing Qobuz authentication tokens.
    /// </summary>
    /// <remarks>
    /// This interface handles automatic token refresh to maintain authentication
    /// sessions without requiring user re-authentication.
    /// 
    /// Key Features:
    /// - Automatic token refresh based on expiration
    /// - Proactive refresh before token expiration
    /// - Fallback to credential-based re-authentication
    /// - Thread-safe token refresh operations
    /// - Retry logic for failed refresh attempts
    /// - Integration with session management
    /// 
    /// Token refresh helps maintain uninterrupted access to the Qobuz API
    /// and provides a better user experience by avoiding authentication prompts.
    /// </remarks>
    public interface ITokenRefresher
    {
        /// <summary>
        /// Refreshes an authentication token if possible.
        /// </summary>
        /// <param name="currentToken">The current token to refresh</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>The refreshed token or null if refresh failed</returns>
        Task<string?> RefreshTokenAsync(string currentToken, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a token needs to be refreshed based on expiration.
        /// </summary>
        /// <param name="token">The token to check</param>
        /// <param name="gracePeriod">Grace period before actual expiration to trigger refresh</param>
        /// <returns>True if the token should be refreshed</returns>
        bool ShouldRefreshToken(string token, System.TimeSpan? gracePeriod = null);

        /// <summary>
        /// Attempts to refresh a complete session including all tokens.
        /// </summary>
        /// <param name="session">The session to refresh</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>The refreshed session or null if refresh failed</returns>
        Task<QobuzSession?> RefreshSessionAsync(QobuzSession session, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if token refresh is available for the current authentication method.
        /// </summary>
        /// <param name="session">The current session</param>
        /// <returns>True if refresh is available</returns>
        bool CanRefreshSession(QobuzSession session);
    }
}