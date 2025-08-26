using System.ComponentModel;

namespace Lidarr.Plugin.Common.Base
{
    /// <summary>
    /// Base class for streaming service plugin settings.
    /// Provides common configuration properties that all streaming services need.
    /// </summary>
    public abstract class BaseStreamingSettings
    {
        protected BaseStreamingSettings()
        {
            // Set reasonable defaults
            SearchLimit = 100;
            ConnectionTimeout = 30;
            ApiRateLimit = 60;
            SearchCacheDuration = 5;
            CountryCode = "US";
            IncludeSingles = false;
            IncludeCompilations = false;
            EarlyReleaseDayLimit = 0;
        }

        /// <summary>
        /// Base URL for the streaming service API.
        /// </summary>
        public virtual string BaseUrl { get; set; }

        /// <summary>
        /// User's email address for authentication.
        /// </summary>
        public virtual string Email { get; set; }

        /// <summary>
        /// User's password for authentication.
        /// </summary>
        public virtual string Password { get; set; }

        /// <summary>
        /// Authentication token (for services that support token-based auth).
        /// </summary>
        public virtual string AuthToken { get; set; }

        /// <summary>
        /// User ID (for services that require it).
        /// </summary>
        public virtual string UserId { get; set; }

        /// <summary>
        /// Country/region code for content availability.
        /// </summary>
        public virtual string CountryCode { get; set; }

        /// <summary>
        /// Maximum number of search results to fetch per query.
        /// </summary>
        public virtual int SearchLimit { get; set; }

        /// <summary>
        /// Whether to include singles and EPs in search results.
        /// </summary>
        public virtual bool IncludeSingles { get; set; }

        /// <summary>
        /// Whether to include compilation albums in search results.
        /// </summary>
        public virtual bool IncludeCompilations { get; set; }

        /// <summary>
        /// Maximum API requests per minute to avoid rate limiting.
        /// </summary>
        public virtual int ApiRateLimit { get; set; }

        /// <summary>
        /// How long to cache search results (in minutes).
        /// </summary>
        public virtual int SearchCacheDuration { get; set; }

        /// <summary>
        /// Connection timeout for API requests (in seconds).
        /// </summary>
        public virtual int ConnectionTimeout { get; set; }

        /// <summary>
        /// Include albums up to this many days before official release.
        /// </summary>
        public virtual int EarlyReleaseDayLimit { get; set; }

        /// <summary>
        /// Gets the cache duration as a TimeSpan.
        /// </summary>
        public System.TimeSpan CacheDuration => System.TimeSpan.FromMinutes(SearchCacheDuration);

        /// <summary>
        /// Gets the connection timeout as a TimeSpan.
        /// </summary>
        public System.TimeSpan RequestTimeout => System.TimeSpan.FromSeconds(ConnectionTimeout);

        /// <summary>
        /// Gets the rate limit as requests per minute.
        /// </summary>
        public System.TimeSpan RateLimitWindow => System.TimeSpan.FromMinutes(1);

        /// <summary>
        /// Validates the settings configuration.
        /// Override in derived classes to add service-specific validation.
        /// </summary>
        public virtual bool IsValid(out string errorMessage)
        {
            errorMessage = null;

            if (SearchLimit < 1 || SearchLimit > 1000)
            {
                errorMessage = "Search limit must be between 1 and 1000";
                return false;
            }

            if (ConnectionTimeout < 5 || ConnectionTimeout > 300)
            {
                errorMessage = "Connection timeout must be between 5 and 300 seconds";
                return false;
            }

            if (ApiRateLimit < 1 || ApiRateLimit > 1000)
            {
                errorMessage = "API rate limit must be between 1 and 1000 requests per minute";
                return false;
            }

            if (SearchCacheDuration < 0 || SearchCacheDuration > 1440)
            {
                errorMessage = "Cache duration must be between 0 and 1440 minutes (24 hours)";
                return false;
            }

            if (EarlyReleaseDayLimit < 0 || EarlyReleaseDayLimit > 90)
            {
                errorMessage = "Early release window must be between 0 and 90 days";
                return false;
            }

            if (string.IsNullOrWhiteSpace(CountryCode) || CountryCode.Length != 2)
            {
                errorMessage = "Country code must be a valid 2-letter code (e.g., 'US', 'GB', 'FR')";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets a masked version of sensitive settings for logging.
        /// </summary>
        public virtual object GetMaskedForLogging()
        {
            return new
            {
                BaseUrl,
                Email = MaskEmail(Email),
                Password = string.IsNullOrEmpty(Password) ? "[not set]" : "[MASKED]",
                AuthToken = string.IsNullOrEmpty(AuthToken) ? "[not set]" : "[MASKED]", 
                UserId,
                CountryCode,
                SearchLimit,
                IncludeSingles,
                IncludeCompilations,
                ApiRateLimit,
                SearchCacheDuration,
                ConnectionTimeout,
                EarlyReleaseDayLimit
            };
        }

        private static string MaskEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return "[not set]";

            var atIndex = email.IndexOf('@');
            if (atIndex <= 0)
                return "[invalid]";

            var localPart = email.Substring(0, atIndex);
            var domain = email.Substring(atIndex);

            if (localPart.Length <= 2)
                return $"{new string('*', localPart.Length)}{domain}";

            return $"{localPart.Substring(0, 1)}{new string('*', localPart.Length - 2)}{localPart.Substring(localPart.Length - 1)}{domain}";
        }
    }
}