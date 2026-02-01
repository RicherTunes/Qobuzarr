using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Interface for Qobuz authentication service used by AuthTokenManager
    /// </summary>
    public interface IQobuzAuthService
    {
        /// <summary>
        /// Authenticates with Qobuz and returns authentication result
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Authentication result containing token and metadata</returns>
        Task<AuthResult> AuthenticateAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Authentication result from service
    /// </summary>
    public class AuthResult
    {
        /// <summary>
        /// The authentication token to use for API requests
        /// </summary>
        public string Token { get; set; }

        /// <summary>
        /// When the token expires (optional, defaults to 1 hour if not specified)
        /// </summary>
        public DateTime? ExpiryTime { get; set; }

        /// <summary>
        /// The user ID associated with this token
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// The user's subscription type
        /// </summary>
        public string UserType { get; set; }
    }
}
