using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Common.Http;

namespace Lidarr.Plugin.Qobuzarr.API.Http
{
    /// <summary>
    /// Handles pure HTTP communication with the Qobuz API.
    /// This interface is responsible only for making HTTP requests and returning responses,
    /// without any business logic, authentication, or caching concerns.
    /// </summary>
    public interface IQobuzHttpClient
    {
        /// <summary>
        /// Executes an HTTP request to the Qobuz API.
        /// </summary>
        /// <param name="request">The HTTP request to execute.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>The HTTP response from the API.</returns>
        Task<HttpResponse> ExecuteAsync(HttpRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Builds an HTTP request for the Qobuz API with standard headers.
        /// </summary>
        /// <param name="url">The full URL for the request.</param>
        /// <param name="method">The HTTP method (GET, POST, etc.).</param>
        /// <returns>A configured HTTP request builder.</returns>
        HttpRequestBuilder BuildRequest(string url, string method = "GET");
    }
}