namespace Lidarr.Plugin.Qobuzarr.Constants
{
    internal static class QobuzarrConstants
    {
        // General identifiers used across the plugin
        public const string PluginName = "Qobuzarr";
        public const string ServiceName = "Qobuz";
        // Plugin vendor and folder naming (use to compose cross-platform path)
        public const string PluginVendor = "RicherTunes";
        // Backward-compat alias kept for any legacy path joins
        public const string PluginFolderName = PluginVendor + "/" + PluginName;
        public const string DownloadCategory = "qobuz";

        internal static class Limits
        {
            public const int MaxEmailLength = 254;
            public const int MaxPasswordLength = 128;
            public const int MaxQueryLength = 200;
            public const int MaxPathLength = 260;
            public const int MaxUrlLength = 2048;
            public const int MaxTokenLength = 500;
            public const int MaxAlbumTitleLength = 2000; // protects classifier from pathological inputs
        }

        internal static class Defaults
        {
            // Concurrency
            public const int DefaultConcurrentDownloads = 3;
            public const int MaxConcurrentDownloads = 20;

            // Unified retry policy
            // Single source of truth for retry attempts across HTTP, stream URL acquisition, and file downloads
            public const int GlobalMaxRetryAttempts = 3;

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
