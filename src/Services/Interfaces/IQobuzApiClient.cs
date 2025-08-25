using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace Lidarr.Plugin.Qobuzarr.Services.Interfaces
{
    /// <summary>
    /// Interface for Qobuz API client providing access to the Qobuz streaming service API.
    /// </summary>
    /// <remarks>
    /// This interface abstracts the core API client functionality, allowing for different
    /// implementations (standard with rate limiting/caching, diagnostic without limits, etc.).
    /// 
    /// Key Features:
    /// - Generic GET/POST operations for all API endpoints
    /// - Strongly-typed responses through generic constraints
    /// - Cancellation token support for all operations
    /// - Consistent error handling across all implementations
    /// 
    /// Implementations should handle:
    /// - Authentication token management and injection
    /// - Request signing and security headers
    /// - Rate limiting (implementation-dependent)
    /// - Response caching (implementation-dependent)
    /// - Error handling and retries
    /// - Logging and observability
    /// </remarks>
    public interface IQobuzApiClient
    {
        /// <summary>
        /// Executes a GET request to the specified Qobuz API endpoint.
        /// </summary>
        /// <typeparam name="T">The expected response type for JSON deserialization.</typeparam>
        /// <param name="endpoint">The API endpoint path relative to the base URL (e.g., "/album/search").</param>
        /// <param name="parameters">Optional query parameters to include in the request.</param>
        /// <returns>The deserialized response object of type T.</returns>
        /// <exception cref="QobuzApiException">Thrown when the API returns an error response or authentication fails.</exception>
        Task<T> GetAsync<T>(string endpoint, Dictionary<string, string>? parameters = null) where T : class;

        /// <summary>
        /// Executes a POST request to the specified Qobuz API endpoint with optional JSON payload.
        /// </summary>
        /// <typeparam name="T">The expected response type for JSON deserialization.</typeparam>
        /// <param name="endpoint">The API endpoint path relative to the base URL (e.g., "/user/login").</param>
        /// <param name="data">Optional request body data that will be serialized to JSON.</param>
        /// <returns>The deserialized response object of type T.</returns>
        /// <exception cref="QobuzApiException">Thrown when the API returns an error response or authentication fails.</exception>
        Task<T> PostAsync<T>(string endpoint, object? data = null) where T : class;

        /// <summary>
        /// Gets the streaming URL for a track with the specified format.
        /// </summary>
        /// <param name="trackId">The Qobuz track ID</param>
        /// <param name="formatId">The desired audio format ID</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>The streaming URL for the track</returns>
        Task<string> GetStreamingUrlAsync(string trackId, int formatId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets detailed metadata for a track.
        /// </summary>
        /// <param name="trackId">The Qobuz track ID</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>The track metadata</returns>
        Task<QobuzTrack> GetTrackMetadataAsync(string trackId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Searches for albums with the specified query.
        /// </summary>
        /// <param name="query">The search query</param>
        /// <param name="limit">Maximum number of results</param>
        /// <param name="offset">Offset for pagination</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Album search results</returns>
        Task<QobuzAlbumSearchResponse> SearchAlbumsAsync(string query, int limit = 50, int offset = 0, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets album details including tracks.
        /// </summary>
        /// <param name="albumId">The Qobuz album ID</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>The album details</returns>
        Task<QobuzAlbum> GetAlbumAsync(string albumId, CancellationToken cancellationToken = default);
    }
}