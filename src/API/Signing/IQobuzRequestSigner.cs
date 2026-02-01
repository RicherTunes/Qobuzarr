using System.Collections.Generic;

namespace Lidarr.Plugin.Qobuzarr.API.Signing
{
    /// <summary>
    /// Handles request signing for Qobuz API endpoints that require signature verification.
    /// This includes generating MD5 hashes for protected endpoints like streaming URLs.
    /// </summary>
    public interface IQobuzRequestSigner
    {
        /// <summary>
        /// Signs a request by adding the required signature parameters.
        /// </summary>
        /// <param name="endpoint">The API endpoint being called.</param>
        /// <param name="parameters">The request parameters to sign.</param>
        /// <param name="appId">The application ID for signing.</param>
        /// <param name="appSecret">The application secret for signing.</param>
        void SignRequest(string endpoint, Dictionary<string, string> parameters, string appId, string appSecret);

        /// <summary>
        /// Determines if an endpoint requires request signing.
        /// </summary>
        /// <param name="endpoint">The API endpoint to check.</param>
        /// <returns>True if the endpoint requires signing; false otherwise.</returns>
        bool RequiresSigning(string endpoint);

        /// <summary>
        /// Generates a signature for track streaming URL requests.
        /// </summary>
        /// <param name="trackId">The track ID.</param>
        /// <param name="formatId">The format ID.</param>
        /// <param name="timestamp">The request timestamp.</param>
        /// <param name="appSecret">The application secret.</param>
        /// <returns>The MD5 signature hash.</returns>
        string GenerateTrackUrlSignature(string trackId, string formatId, string timestamp, string appSecret);

        /// <summary>
        /// Generates a generic signature for other endpoints.
        /// </summary>
        /// <param name="endpoint">The API endpoint.</param>
        /// <param name="parameters">The sorted parameters for signing.</param>
        /// <param name="appId">The application ID.</param>
        /// <param name="appSecret">The application secret.</param>
        /// <returns>The MD5 signature hash.</returns>
        string GenerateGenericSignature(string endpoint, Dictionary<string, string> parameters, string appId, string appSecret);
    }
}
