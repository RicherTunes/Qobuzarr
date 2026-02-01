using Newtonsoft.Json;

namespace QobuzCLI.Models.Configuration
{
    /// <summary>
    /// Configuration settings for system behavior, caching, logging, and advanced features.
    /// </summary>
    public class SystemConfig
    {
        [JsonProperty("apiTimeoutSeconds")]
        public int ApiTimeoutSeconds { get; set; } = 30;

        [JsonProperty("retryAttempts")]
        public int RetryAttempts { get; set; } = 3;

        [JsonProperty("verboseLogging")]
        public bool VerboseLogging { get; set; } = false;

        [JsonProperty("stateSaveIntervalSeconds")]
        public int StateSaveIntervalSeconds { get; set; } = 30;

        [JsonProperty("maxHistoryItems")]
        public int MaxHistoryItems { get; set; } = 1000;

        [JsonProperty("enableMemoryOptimizations")]
        public bool EnableMemoryOptimizations { get; set; } = true;

        [JsonProperty("enableLocalCache")]
        public bool EnableLocalCache { get; set; } = true;

        /// <summary>
        /// Validates and clamps system settings to reasonable bounds
        /// </summary>
        public void ValidateSystemSettings()
        {
            ApiTimeoutSeconds = Math.Max(5, Math.Min(300, ApiTimeoutSeconds));
            RetryAttempts = Math.Max(0, Math.Min(10, RetryAttempts));
            StateSaveIntervalSeconds = Math.Max(5, Math.Min(300, StateSaveIntervalSeconds));
            MaxHistoryItems = Math.Max(10, Math.Min(10000, MaxHistoryItems));
        }
    }
}
