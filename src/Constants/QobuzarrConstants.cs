namespace Lidarr.Plugin.Qobuzarr.Constants
{
    internal static class QobuzarrConstants
    {
        // General identifiers used across the plugin
        public const string PluginName = "Qobuzarr";
        public const string ServiceName = "Qobuz";
        // Matches default deploy path used in csproj (plugins\\RicherTunes\\Qobuzarr)
        public const string PluginFolderName = "RicherTunes\\Qobuzarr";
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
