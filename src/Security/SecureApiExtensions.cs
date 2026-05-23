using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using CommonUtilities = Lidarr.Plugin.Common.Utilities;
using Lidarr.Plugin.Common.Observability;

namespace Lidarr.Plugin.Qobuzarr.Security
{
    /// <summary>
    /// Security extensions for API operations that handle sensitive data.
    /// Provides secure string handling for request signing and parameter management.
    /// </summary>
    public static class SecureApiExtensions
    {
        /// <summary>
        /// Securely generates request signature for track/getFileUrl endpoint.
        /// Minimizes exposure time of sensitive app secret during signature generation.
        /// </summary>
        /// <param name="formatId">Track format ID</param>
        /// <param name="trackId">Track ID</param>
        /// <param name="timestamp">Request timestamp</param>
        /// <param name="appSecret">App secret (will be cleared from memory after use)</param>
        /// <param name="logger">Logger for security events</param>
        /// <returns>MD5 signature for the request</returns>
        public static string GenerateSecureTrackSignature(
            string formatId,
            string trackId,
            string timestamp,
            ref string appSecret,
            IQobuzLogger logger)
        {
            if (string.IsNullOrWhiteSpace(appSecret))
            {
                throw new InvalidOperationException("App Secret is required for signed requests. Ensure App ID and App Secret are a matching pair from Qobuz.");
            }

            string? signature = null;
            string? signatureString = null;

            try
            {
                // TrevTV's exact concatenation order from QobuzApiSharp:
                // "trackgetFileUrlformat_id" + format_id + "intentstreamtrack_id" + track_id + timestamp + app_secret
                signatureString = $"trackgetFileUrlformat_id{formatId}intentstreamtrack_id{trackId}{timestamp}{appSecret}";

                signature = CommonUtilities.HashingUtility.ComputeMD5Hash(signatureString);

                logger.Debug("Generated secure signature for track/getFileUrl: format_id={0}, track_id={1}",
                    formatId, trackId);

                return signature;
            }
            finally
            {
                // Securely clear sensitive data from memory
                if (signatureString != null)
                {
                    ClearSensitiveString(ref signatureString);
                }

                // Clear the app secret parameter
                ClearSensitiveString(ref appSecret);
            }
        }

        /// <summary>
        /// Securely generates generic request signature with parameter sorting.
        /// </summary>
        /// <param name="endpoint">API endpoint path</param>
        /// <param name="parameters">Request parameters (excluding sensitive ones)</param>
        /// <param name="appId">Application ID</param>
        /// <param name="logger">Logger for security events</param>
        /// <returns>MD5 signature for the request</returns>
        public static string GenerateSecureGenericSignature(
            string endpoint,
            Dictionary<string, string> parameters,
            string appId,
            IQobuzLogger logger)
        {
            string? signature = null;
            string? signatureString = null;

            try
            {
                var method = endpoint.Split('/').LastOrDefault();
                var objectName = endpoint.Split('/').FirstOrDefault()?.TrimStart('/');

                // Sort parameters (excluding sensitive ones)
                var signatureParams = parameters
                    .Where(p => p.Key != "app_id" &&
                               p.Key != "user_auth_token" &&
                               p.Key != "request_sig" &&
                               p.Key != "request_ts")
                    .OrderBy(p => p.Key)
                    .Select(p => $"{p.Key}={p.Value}")
                    .ToList();

                signatureString = $"{objectName}{method}{string.Join("", signatureParams)}{appId}";
                signature = CommonUtilities.HashingUtility.ComputeMD5Hash(signatureString);

                logger.Debug("Generated secure generic signature for {0}", endpoint);

                return signature;
            }
            finally
            {
                // Clear sensitive signature string
                if (signatureString != null)
                {
                    ClearSensitiveString(ref signatureString);
                }
            }
        }

        /// <summary>
        /// Validates that request parameters don't contain obvious security issues.
        /// </summary>
        /// <param name="parameters">Parameters to validate</param>
        /// <param name="logger">Logger for security warnings</param>
        /// <returns>True if parameters pass security validation</returns>
        public static bool ValidateRequestSecurity(Dictionary<string, string> parameters, IQobuzLogger logger)
        {
            if (parameters == null)
                return true;

            foreach (var param in parameters)
            {
                // Check for accidentally exposed sensitive data
                if (IsPotentiallyExposedCredential(param.Key, param.Value))
                {
                    logger.Warn("Potentially exposed credential detected in parameter {0}", param.Key);
                    return false;
                }

                // Check for injection attempts
                if (ContainsPotentialInjection(param.Value))
                {
                    logger.Warn("Potential injection attempt detected in parameter {0}", param.Key);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Masks sensitive parameters for safe logging.
        /// </summary>
        /// <param name="parameters">Parameters to mask</param>
        /// <returns>Dictionary with sensitive values masked</returns>
        public static Dictionary<string, string> MaskSensitiveParameters(Dictionary<string, string> parameters)
        {
            if (parameters == null)
                return new Dictionary<string, string>();

            var maskedParams = new Dictionary<string, string>();

            foreach (var param in parameters)
            {
                if (IsSensitiveParameter(param.Key))
                {
                    maskedParams[param.Key] = MaskValue(param.Value);
                }
                else
                {
                    maskedParams[param.Key] = param.Value;
                }
            }

            return maskedParams;
        }

        /// <summary>
        /// Creates a secure copy of parameters with sensitive data removed.
        /// </summary>
        /// <param name="parameters">Source parameters</param>
        /// <returns>New dictionary without sensitive parameters</returns>
        public static Dictionary<string, string> CreateSecureParameterCopy(Dictionary<string, string> parameters)
        {
            if (parameters == null)
                return new Dictionary<string, string>();

            return parameters
                .Where(p => !IsSensitiveParameter(p.Key))
                .ToDictionary(p => p.Key, p => p.Value);
        }

        private static void ClearSensitiveString(ref string sensitiveString)
        {
            if (string.IsNullOrEmpty(sensitiveString))
                return;

            // Clear the reference - the GC will handle memory reclamation
            // Note: String immutability in .NET means the actual string data
            // remains in memory until GC runs naturally. Forcing GC.Collect()
            // is an anti-pattern that degrades performance without guaranteeing
            // security. For true security, use SecureString throughout the pipeline.
            sensitiveString = null;
        }

        private static bool IsSensitiveParameter(string parameterName)
        {
            // Delegate to the canonical detector in Lidarr.Plugin.Common.Observability.LogRedactor.
            return LogRedactor.IsSensitiveParameter(parameterName);
        }

        private static bool IsPotentiallyExposedCredential(string paramName, string paramValue)
        {
            if (string.IsNullOrWhiteSpace(paramValue))
                return false;

            var lowerValue = paramValue.ToLowerInvariant();

            // Check if non-sensitive parameter contains sensitive-looking data
            if (!IsSensitiveParameter(paramName))
            {
                return lowerValue.Contains("bearer ") ||
                       lowerValue.Contains("basic ") ||
                       lowerValue.Contains("token=") ||
                       lowerValue.Contains("secret=") ||
                       lowerValue.Contains("password=");
            }

            return false;
        }

        private static bool ContainsPotentialInjection(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var lowerValue = value.ToLowerInvariant();

            // Basic injection pattern detection
            return lowerValue.Contains("'; ") ||
                   lowerValue.Contains("\"; ") ||
                   lowerValue.Contains("' or ") ||
                   lowerValue.Contains("\" or ") ||
                   lowerValue.Contains("1=1") ||
                   lowerValue.Contains("union select") ||
                   lowerValue.Contains("<script") ||
                   lowerValue.Contains("javascript:");
        }

        private static string MaskValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "[empty]";

            if (value.Length <= 4)
                return new string('*', value.Length);

            return $"{value.Substring(0, 2)}{"*".PadLeft(value.Length - 4, '*')}{value.Substring(value.Length - 2)}";
        }
    }
}
