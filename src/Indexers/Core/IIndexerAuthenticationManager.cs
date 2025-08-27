using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace Lidarr.Plugin.Qobuzarr.Indexers.Core
{
    /// <summary>
    /// Manages authentication concerns for the Qobuz indexer.
    /// Extracted from QobuzIndexer to follow Single Responsibility Principle.
    /// </summary>
    public interface IIndexerAuthenticationManager
    {
        /// <summary>
        /// Ensures the indexer has valid authentication before making API requests.
        /// </summary>
        Task EnsureAuthenticatedAsync();

        /// <summary>
        /// Gets the current cached session if available.
        /// </summary>
        QobuzSession? GetCachedSession();

        /// <summary>
        /// Tests the authentication configuration without performing actual searches.
        /// </summary>
        Task<(bool IsSuccess, string ErrorMessage)> TestAuthenticationAsync();
    }
}