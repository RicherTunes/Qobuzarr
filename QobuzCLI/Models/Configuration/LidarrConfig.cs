using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace QobuzCLI.Models.Configuration
{
    /// <summary>
    /// Configuration for Lidarr integration settings.
    /// Manages connection details and export preferences for the Lidarr API integration.
    /// </summary>
    public class LidarrConfig
    {
        /// <summary>
        /// Lidarr server URL (e.g., http://localhost:8686)
        /// </summary>
        [JsonProperty("url")]
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Lidarr API key for authentication (stored securely, not serialized)
        /// </summary>
        [JsonIgnore]
        public string ApiKey { get; set; } = string.Empty;
        
        /// <summary>
        /// Indicates whether a secure API key is stored
        /// </summary>
        [JsonProperty("hasSecureApiKey")]
        public bool HasSecureApiKey { get; set; } = false;

        /// <summary>
        /// API request timeout in seconds
        /// </summary>
        [JsonProperty("timeoutSeconds")]
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Python executable path for running Lidarr scripts
        /// </summary>
        [JsonProperty("pythonPath")]
        public string PythonPath { get; set; } = "python";

        /// <summary>
        /// Default export format for wanted albums (json, txt, csv)
        /// </summary>
        [JsonProperty("defaultExportFormat")]
        public string DefaultExportFormat { get; set; } = "json";

        /// <summary>
        /// Default sort order for exported albums
        /// </summary>
        [JsonProperty("defaultSortOrder")]
        public string DefaultSortOrder { get; set; } = "release_date_desc";

        /// <summary>
        /// Default limit for number of albums to export (0 = no limit)
        /// </summary>
        [JsonProperty("defaultExportLimit")]
        public int DefaultExportLimit { get; set; } = 0;

        /// <summary>
        /// Enable automatic download after export
        /// </summary>
        [JsonProperty("autoDownloadAfterExport")]
        public bool AutoDownloadAfterExport { get; set; } = false;

        /// <summary>
        /// Default filter mode for export operations (and, or)
        /// </summary>
        [JsonProperty("defaultFilterMode")]
        public string DefaultFilterMode { get; set; } = "and";

        /// <summary>
        /// Enable validation of albums before adding to queue
        /// </summary>
        [JsonProperty("enablePreDownloadValidation")]
        public bool EnablePreDownloadValidation { get; set; } = true;

        /// <summary>
        /// Default album types to include in exports (comma-separated: album,ep,single)
        /// </summary>
        [JsonProperty("defaultAlbumTypes")]
        public string DefaultAlbumTypes { get; set; } = "album";

        /// <summary>
        /// Default minimum year filter (0 = no filter)
        /// </summary>
        [JsonProperty("defaultMinYear")]
        public int DefaultMinYear { get; set; } = 0;

        /// <summary>
        /// Default maximum year filter (0 = no filter) 
        /// </summary>
        [JsonProperty("defaultMaxYear")]
        public int DefaultMaxYear { get; set; } = 0;

        /// <summary>
        /// Default minimum track count filter (0 = no filter)
        /// </summary>
        [JsonProperty("defaultMinTracks")]
        public int DefaultMinTracks { get; set; } = 0;

        /// <summary>
        /// Cache exported data locally to avoid re-querying Lidarr
        /// </summary>
        [JsonProperty("enableExportCaching")]
        public bool EnableExportCaching { get; set; } = true;

        /// <summary>
        /// Cache expiry time in hours
        /// </summary>
        [JsonProperty("cacheExpiryHours")]
        public int CacheExpiryHours { get; set; } = 24;

        /// <summary>
        /// Generate reports after batch downloads
        /// </summary>
        [JsonProperty("generateDownloadReports")]
        public bool GenerateDownloadReports { get; set; } = true;

        /// <summary>
        /// Default report format (html, text, json)
        /// </summary>
        [JsonProperty("defaultReportFormat")]
        public string DefaultReportFormat { get; set; } = "html";

        /// <summary>
        /// Validates the Lidarr configuration settings
        /// </summary>
        public void ValidateLidarrSettings()
        {
            // Validate URL format
            if (!string.IsNullOrEmpty(Url))
            {
                if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || 
                    (uri.Scheme != "http" && uri.Scheme != "https"))
                {
                    throw new ValidationException($"Invalid Lidarr URL: {Url}. Must be a valid HTTP/HTTPS URL.");
                }
            }

            // Validate timeout
            if (TimeoutSeconds <= 0 || TimeoutSeconds > 300)
            {
                TimeoutSeconds = 30;
            }

            // Validate export format
            var validFormats = new[] { "json", "txt", "csv" };
            if (!validFormats.Contains(DefaultExportFormat.ToLower()))
            {
                DefaultExportFormat = "json";
            }

            // Validate sort order
            var validSortOrders = new[]
            {
                "release_date_desc", "release_date_asc", "artist_name", "album_name",
                "track_count_desc", "track_count_asc", "album_type", "random"
            };
            if (!validSortOrders.Contains(DefaultSortOrder.ToLower()))
            {
                DefaultSortOrder = "release_date_desc";
            }

            // Validate filter mode
            var validFilterModes = new[] { "and", "or" };
            if (!validFilterModes.Contains(DefaultFilterMode.ToLower()))
            {
                DefaultFilterMode = "and";
            }

            // Validate export limit
            if (DefaultExportLimit < 0)
            {
                DefaultExportLimit = 0;
            }

            // Validate year filters
            if (DefaultMinYear < 0)
            {
                DefaultMinYear = 0;
            }
            if (DefaultMaxYear < 0)
            {
                DefaultMaxYear = 0;
            }
            if (DefaultMinYear > 0 && DefaultMaxYear > 0 && DefaultMinYear > DefaultMaxYear)
            {
                // Swap if min > max
                (DefaultMinYear, DefaultMaxYear) = (DefaultMaxYear, DefaultMinYear);
            }

            // Validate track count filter
            if (DefaultMinTracks < 0)
            {
                DefaultMinTracks = 0;
            }

            // Validate cache expiry
            if (CacheExpiryHours <= 0 || CacheExpiryHours > 168) // Max 1 week
            {
                CacheExpiryHours = 24;
            }

            // Validate report format
            var validReportFormats = new[] { "html", "text", "json" };
            if (!validReportFormats.Contains(DefaultReportFormat.ToLower()))
            {
                DefaultReportFormat = "html";
            }
        }

        /// <summary>
        /// Checks if Lidarr connection settings are configured
        /// </summary>
        public bool HasValidConnectionSettings()
        {
            return !string.IsNullOrWhiteSpace(Url) && (HasSecureApiKey || !string.IsNullOrWhiteSpace(ApiKey));
        }

        /// <summary>
        /// Gets the formatted Lidarr URL ensuring it doesn't end with a slash
        /// </summary>
        public string GetFormattedUrl()
        {
            return string.IsNullOrEmpty(Url) ? string.Empty : Url.TrimEnd('/');
        }

        /// <summary>
        /// Gets the default album types as a list
        /// </summary>
        public List<string> GetDefaultAlbumTypesList()
        {
            if (string.IsNullOrWhiteSpace(DefaultAlbumTypes))
                return new List<string> { "album" };

            return DefaultAlbumTypes
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().ToLower())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();
        }

        /// <summary>
        /// Creates a safe copy for logging (masks sensitive data)
        /// </summary>
        public LidarrConfig ToSafeConfig()
        {
            var safeConfig = (LidarrConfig)MemberwiseClone();
            
            // Mask API key for logging - since it's now JsonIgnore, just show status
            safeConfig.ApiKey = HasSecureApiKey ? "***SECURE***" : "***NOT_SET***";

            return safeConfig;
        }
        
        /// <summary>
        /// Constants for secure credential storage keys
        /// </summary>
        public static class CredentialKeys
        {
            public const string LIDARR_API_KEY = "lidarr_api_key";
        }
    }
}