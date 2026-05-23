namespace Lidarr.Plugin.Qobuzarr.Constants
{
    /// <summary>
    /// Public constants for plugin identification and configuration.
    /// These are safe to expose as they contain no sensitive information.
    /// </summary>
    public static class QobuzarrConstants
    {
        // General identifiers used across the plugin
        public const string PluginName = "Qobuzarr";
        public const string ServiceName = "Qobuz";
        // Plugin vendor and folder naming (use to compose cross-platform path)
        public const string PluginVendor = "RicherTunes";
        // Backward-compat alias kept for any legacy path joins
        public const string PluginFolderName = PluginVendor + "/" + PluginName;
        public const string DownloadCategory = "qobuz";

        /// <summary>
        /// Validation limits for input fields.
        /// </summary>
        public static class Limits
        {
            public const int MaxEmailLength = 254;
            public const int MaxPasswordLength = 128;
            public const int MaxQueryLength = 200;
            public const int MaxPathLength = 260;
            public const int MaxUrlLength = 2048;
            public const int MaxTokenLength = 500;
            public const int MaxAlbumTitleLength = 2000; // protects classifier from pathological inputs
        }

        /// <summary>
        /// Experimental features that are behind opt-in flags. These are NOT production-ready
        /// and must be explicitly enabled by setting the corresponding configuration property.
        /// See docs/EXPERIMENTAL_FEATURES.md for documentation, risks, and removal conditions.
        /// </summary>
        public static class Experimental
        {
            /// <summary>
            /// [Experimental] HybridMLQueryOptimizer combined feature extraction.
            /// Controlled by HybridConfiguration.EnableHybridFeatureExtraction (default: false).
            /// </summary>
            public const string HybridFeatureExtraction = "HybridFeatureExtraction";
        }

        /// <summary>
        /// Default configuration values.
        /// </summary>
        public static class Defaults
        {
            // Concurrency
            public const int DefaultConcurrentDownloads = 3;
            public const int MaxConcurrentDownloads = 20;
            // Per-host HTTP connection concurrency (SocketsHttpHandler.MaxConnectionsPerServer)
            public const int DefaultMaxConcurrencyPerHost = 6;

            // Unified retry policy
            // Single source of truth for retry attempts across HTTP, stream URL acquisition, and file downloads
            public const int GlobalMaxRetryAttempts = 3;

            // Retry budget (seconds) for transient HTTP failures before returning
            public const int RetryBudgetSeconds = 60;

            // Token refresh (see TokenRefresher)
            public const int TokenRefreshBufferMinutes = 30;
            public const int TokenRefreshCooldownSeconds = 60;
            public const int TokenMaxRetryAttempts = 3;
            public const int TokenInitialRetryDelaySeconds = 30;
            public const double TokenBackoffMultiplier = 2.0;
            public const int TokenCircuitBreakerThreshold = 5;
        }
    }
}
