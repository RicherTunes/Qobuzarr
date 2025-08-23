using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.API.Signing
{
    /// <summary>
    /// Implementation of request signing for Qobuz API endpoints.
    /// Generates MD5 signatures for protected endpoints like streaming URLs.
    /// </summary>
    public class QobuzRequestSigner : IQobuzRequestSigner
    {
        private readonly Logger _logger;

        public QobuzRequestSigner(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public void SignRequest(string endpoint, Dictionary<string, string> parameters, string appId, string appSecret)
        {
            if (string.IsNullOrWhiteSpace(appSecret))
            {
                throw new InvalidOperationException("App Secret is required for signed requests. Ensure App ID and App Secret are a matching pair from Qobuz.");
            }

            // Add timestamp
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            parameters["request_ts"] = timestamp;

            // Generate and add signature based on endpoint type
            if (endpoint.Contains("track/getFileUrl"))
            {
                var signature = GenerateTrackUrlSignature(
                    parameters.GetValueOrDefault("track_id", ""),
                    parameters.GetValueOrDefault("format_id", ""),
                    timestamp,
                    appSecret);

                parameters["request_sig"] = signature;
                
                _logger.Debug("Added signature for track/getFileUrl: track_id={0}, format_id={1}, app_id={2}",
                    parameters.GetValueOrDefault("track_id"),
                    parameters.GetValueOrDefault("format_id"),
                    appId);
            }
            else if (RequiresSigning(endpoint))
            {
                var signature = GenerateGenericSignature(endpoint, parameters, appId, appSecret);
                parameters["request_sig"] = signature;
                
                _logger.Debug("Added generic signature for {0}", endpoint);
            }
        }

        /// <inheritdoc/>
        public bool RequiresSigning(string endpoint)
        {
            // Currently only track/getFileUrl requires signing
            // Add other endpoints here if Qobuz adds more protected endpoints
            return endpoint.Contains("track/getFileUrl");
        }

        /// <inheritdoc/>
        public string GenerateTrackUrlSignature(string trackId, string formatId, string timestamp, string appSecret)
        {
            // Exact concatenation order from QobuzApiSharp:
            // "trackgetFileUrlformat_id" + format_id + "intentstreamtrack_id" + track_id + timestamp + app_secret
            var signatureString = $"trackgetFileUrlformat_id{formatId}intentstreamtrack_id{trackId}{timestamp}{appSecret}";
            
            return HashingUtility.ComputeMD5Hash(signatureString);
        }

        /// <inheritdoc/>
        public string GenerateGenericSignature(string endpoint, Dictionary<string, string> parameters, string appId, string appSecret)
        {
            // Extract method and object name from endpoint
            var parts = endpoint.Split('/');
            var method = parts.LastOrDefault();
            var objectName = parts.FirstOrDefault()?.TrimStart('/');

            // Sort parameters (excluding app_id and user_auth_token)
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