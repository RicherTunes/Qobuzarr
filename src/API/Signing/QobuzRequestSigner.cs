using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Lidarr.Plugin.Common.Observability;
using Lidarr.Plugin.Common.Services.Http;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.API.Signing
{
    /// <summary>
    /// Implementation of request signing for Qobuz API endpoints.
    /// Generates MD5 signatures for protected endpoints like streaming URLs.
    /// Implements <see cref="IRequestSigner"/> directly (no separate Qobuz-specific
    /// interface or adapter); Qobuz MD5 signing is the genuine domain logic.
    /// </summary>
    public class QobuzRequestSigner : IRequestSigner
    {
        private readonly Logger _logger;

        public QobuzRequestSigner(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public bool RequiresSigning(string endpoint)
        {
            // Currently only track/getFileUrl requires signing.
            // Add other endpoints here if Qobuz adds more protected endpoints.
            return endpoint != null && endpoint.Contains("track/getFileUrl");
        }

        /// <inheritdoc/>
        public void Sign(string endpoint, IDictionary<string, string> parameters, string appId, string appSecret)
        {
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            if (string.IsNullOrWhiteSpace(appSecret))
            {
                throw new InvalidOperationException("App Secret is required for signed requests. Ensure App ID and App Secret are a matching pair from Qobuz.");
            }

            // Add timestamp
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            parameters["request_ts"] = timestamp;

            // Generate and add signature based on endpoint type
            if (endpoint != null && endpoint.Contains("track/getFileUrl"))
            {
                parameters.TryGetValue("track_id", out var trackId);
                parameters.TryGetValue("format_id", out var formatId);

                var signature = GenerateTrackUrlSignature(
                    trackId ?? string.Empty,
                    formatId ?? string.Empty,
                    timestamp,
                    appSecret);

                parameters["request_sig"] = signature;

                _logger.Debug("Added signature for track/getFileUrl: track_id={0}, format_id={1}, app_id={2}",
                    trackId,
                    formatId,
                    Scrub.Secret(appId));
            }
            else if (RequiresSigning(endpoint))
            {
                var signature = GenerateGenericSignature(endpoint, parameters, appId, appSecret);
                parameters["request_sig"] = signature;

                _logger.Debug("Added generic signature for {0}", endpoint);
            }
        }

        /// <summary>
        /// Generates the MD5 signature for the track/getFileUrl streaming endpoint.
        /// Exposed for diagnostics/testing.
        /// </summary>
        public string GenerateTrackUrlSignature(string trackId, string formatId, string timestamp, string appSecret)
        {
            // Exact concatenation order from QobuzApiSharp:
            // "trackgetFileUrlformat_id" + format_id + "intentstreamtrack_id" + track_id + timestamp + app_secret
            var signatureString = $"trackgetFileUrlformat_id{formatId}intentstreamtrack_id{trackId}{timestamp}{appSecret}";

            return HashingUtility.ComputeMD5Hash(signatureString);
        }

        /// <summary>
        /// Generates a generic Qobuz MD5 signature for arbitrary endpoints.
        /// Exposed for diagnostics/testing.
        /// </summary>
        public string GenerateGenericSignature(string endpoint, IDictionary<string, string> parameters, string appId, string appSecret)
        {
            // Extract method and object name from endpoint
            var parts = endpoint.Split('/');
            var method = parts.LastOrDefault();
            var objectName = parts.FirstOrDefault()?.TrimStart('/');

            // Sort parameters (excluding app_id, user_auth_token, request_ts, request_sig)
            var signatureParams = parameters
                .Where(p => p.Key != "app_id" && p.Key != "user_auth_token" && p.Key != "request_ts" && p.Key != "request_sig")
                .OrderBy(p => p.Key)
                .Select(p => $"{p.Key}={p.Value}")
                .ToList();

            var signatureString = $"{objectName}{method}{string.Join("", signatureParams)}{appId}";

            return HashingUtility.ComputeMD5Hash(signatureString);
        }
    }
}
