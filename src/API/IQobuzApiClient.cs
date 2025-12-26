using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;

namespace Lidarr.Plugin.Qobuzarr.API
{
    /// <summary>
    /// Defines the contract for interacting with the Qobuz REST API.
    /// Handles HTTP communication, rate limiting, request signing, and response caching.
    /// </summary>
    public interface IQobuzApiClient
    {
        /// <summary>
        /// Executes a GET request to the specified Qobuz API endpoint.
        /// Includes automatic rate limiting, caching, and retry logic.
        /// </summary>
        /// <typeparam name="T">The expected response type to deserialize.</typeparam>
        /// <param name="endpoint">The API endpoint path (e.g., "/album/search").</param>
        /// <param name="parameters">Optional query parameters to include in the request.</param>
        /// <returns>The deserialized response of type T.</returns>
        /// <exception cref="QobuzApiException">Thrown when the API returns an error response.</exception>
        /// <exception cref="HttpException">Thrown when network or HTTP errors occur.</exception>
        Task<T> GetAsync<T>(string endpoint, Dictionary<string, string>? parameters = null) where T : class;

        /// <summary>
        /// Executes a POST request to the specified Qobuz API endpoint.
        /// Includes automatic rate limiting and retry logic.
        /// </summary>
        /// <typeparam name="T">The expected response type to deserialize.</typeparam>
        /// <param name="endpoint">The API endpoint path (e.g., "/user/login").</param>
        /// <param name="data">Optional request body data to serialize as JSON.</param>
        /// <returns>The deserialized response of type T.</returns>
        /// <exception cref="QobuzApiException">Thrown when the API returns an error response.</exception>
        /// <exception cref="HttpException">Thrown when network or HTTP errors occur.</exception>
        Task<T> PostAsync<T>(string endpoint, object? data = null) where T : class;

        /// <summary>
        /// Sets the authentication session for subsequent API requests.
        /// The session includes the user auth token and app ID required for authenticated endpoints.
        /// </summary>
        /// <param name="session">The authenticated session containing user credentials.</param>
        void SetSession(QobuzSession session);

        /// <summary>
        /// Clears the current authentication session.
        /// Subsequent requests will be made without authentication headers.
        /// </summary>
        void ClearSession();

        /// <summary>
        /// Checks if the client has a valid authentication session configured.
        /// </summary>
        /// <returns>True if a valid session is set; false otherwise.</returns>
        bool HasValidSession();

        /// <summary>
        /// Gets the streaming URL for a track with the specified quality.
        /// </summary>
        /// <param name="trackId">The Qobuz track ID.</param>
        /// <param name="formatId">The desired quality format ID (5=MP3, 6=CD, 7=Hi-Res, 27=Studio).</param>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>The streaming URL for downloading the track.</returns>
        Task<string> GetStreamingUrlAsync(string trackId, int formatId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the streaming response for a track with the specified quality.
        /// Includes the resolved URL and the actual format returned by Qobuz, which may differ from the requested format when Qobuz falls back.
        /// </summary>
        Task<QobuzStreamResponse> GetStreamingInfoAsync(string trackId, int formatId, CancellationToken cancellationToken = default);
    }
}
