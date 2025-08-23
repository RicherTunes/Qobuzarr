using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Service responsible for testing Lidarr connectivity and API permissions.
    /// Part of the plugin-first architecture where business logic resides in the plugin.
    /// </summary>
    public interface ILidarrConnectionTestService
    {
        /// <summary>
        /// Tests connection to Lidarr server.
        /// </summary>
        /// <param name="url">Lidarr server URL.</param>
        /// <param name="apiKey">Lidarr API key.</param>
        /// <param name="timeoutSeconds">Connection timeout in seconds.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Connection test result.</returns>
        Task<ConnectionTestResult> TestConnectionAsync(
            string url,
            string apiKey,
            int timeoutSeconds = 30,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Tests API permissions by attempting to retrieve wanted albums.
        /// </summary>
        /// <param name="url">Lidarr server URL.</param>
        /// <param name="apiKey">Lidarr API key.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Permission test result.</returns>
        Task<PermissionTestResult> TestPermissionsAsync(
            string url,
            string apiKey,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Result of a connection test.
    /// </summary>
    public class ConnectionTestResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string ServerVersion { get; set; }
        public TimeSpan ResponseTime { get; set; }
        public Exception Error { get; set; }
    }

    /// <summary>
    /// Result of a permission test.
    /// </summary>
    public class PermissionTestResult
    {
        public bool Success { get; set; }
        public bool CanRead { get; set; }
        public bool CanWrite { get; set; }
        public int? WantedAlbumCount { get; set; }
        public string Message { get; set; }
        public Exception Error { get; set; }
    }
}