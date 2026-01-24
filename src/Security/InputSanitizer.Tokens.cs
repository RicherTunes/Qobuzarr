using System;

namespace Lidarr.Plugin.Qobuzarr.Security
{
    public static partial class InputSanitizer
    {
        /// <summary>
        /// Sanitizes App ID for API calls
        /// </summary>
        public static string SanitizeAppId(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId))
                throw new ArgumentException("App ID cannot be empty");

            appId = appId.Trim();

            if (!AppIdRegex().IsMatch(appId))
                throw new ArgumentException("App ID contains invalid characters");

            return appId;
        }

        /// <summary>
        /// Sanitizes App Secret (validates format only)
        /// </summary>
        public static string ValidateAppSecret(string appSecret)
        {
            if (string.IsNullOrWhiteSpace(appSecret))
                throw new ArgumentException("App Secret cannot be empty");

            appSecret = appSecret.Trim();

            // App secrets are typically hex strings or base64
            if (appSecret.Length > 100)
                throw new ArgumentException("App Secret exceeds maximum length");

            // Check for obviously malicious patterns
            if (appSecret.Contains("'") || appSecret.Contains("\"") || appSecret.Contains(";"))
                throw new ArgumentException("App Secret contains invalid characters");

            return appSecret;
        }

        /// <summary>
        /// Sanitizes authentication tokens
        /// </summary>
        public static string SanitizeAuthToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("Auth token cannot be empty");

            token = token.Trim();

            if (token.Length > MaxTokenLength)
                throw new ArgumentException($"Auth token exceeds maximum length of {MaxTokenLength} characters");

            // Tokens should typically be alphanumeric with some special chars
            if (!AuthTokenRegex().IsMatch(token))
                throw new ArgumentException("Auth token contains invalid characters");

            return token;
        }

        /// <summary>
        /// Sanitizes user ID
        /// </summary>
        public static string SanitizeUserId(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("User ID cannot be empty");

            userId = userId.Trim();

            // User IDs are typically numeric or alphanumeric
            if (!UserIdRegex().IsMatch(userId))
                throw new ArgumentException("User ID contains invalid characters");

            if (userId.Length > 50)
                throw new ArgumentException("User ID exceeds maximum length");

            return userId;
        }

        /// <summary>
        /// Sanitizes country code
        /// </summary>
        public static string SanitizeCountryCode(string countryCode)
        {
            if (string.IsNullOrWhiteSpace(countryCode))
                return "US"; // Default to US

            countryCode = countryCode.Trim().ToUpperInvariant();

            if (!CountryCodeRegex().IsMatch(countryCode))
                throw new ArgumentException("Invalid country code format. Must be 2-letter ISO code.");

            return countryCode;
        }

        /// <summary>
        /// Sanitizes query parameters for API requests
        /// </summary>
        public static string SanitizeQueryParam(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Query parameter key cannot be empty");

            // Sanitize based on the parameter type
            switch (key.ToLowerInvariant())
            {
                case "email":
                    return SanitizeEmail(value);
                case "password":
                    return value; // Don't modify password, just validate
                case "app_id":
                    return SanitizeAppId(value);
                case "app_secret":
                    return value; // Don't modify secret, just validate
                case "user_auth_token":
                case "auth_token":
                    return SanitizeAuthToken(value);
                case "user_id":
                    return SanitizeUserId(value);
                case "query":
                case "q":
                    return SanitizeSearchQuery(value);
                case "country_code":
                case "country":
                    return SanitizeCountryCode(value);
                default:
                    // Generic sanitization for unknown parameters
                    return SanitizeUrlParameter(value);
            }
        }
    }
}

