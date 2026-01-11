using Lidarr.Plugin.Qobuzarr.Constants;

namespace Lidarr.Plugin.Qobuzarr.Configuration
{
    /// <summary>
    /// Configuration constants for Qobuz plugin
    /// Centralizes magic numbers and configurable values
    /// </summary>
    public static class QobuzPluginConstants
    {
        /// <summary>
        /// Audio quality format IDs
        /// </summary>
        public static class QualityFormats
        {
            public const int Mp3320 = 5;
            public const int FlacCd = 6;
            public const int Flac24_96 = 7;
            public const int Flac24_192 = 27;
        }

        /// <summary>
        /// Download configuration
        /// </summary>
        public static class Download
        {
            public const int MaxRetries = QobuzarrConstants.Defaults.GlobalMaxRetryAttempts; // Unified retry attempts
            public const int RetryDelayMs = 2000; // Increased base delay for network recovery
            public const int LargeFileThresholdBytes = 50 * 1024 * 1024; // 50MB
            public const int BufferSize = 81920; // 80KB
            public const int ChunkSize = 8192; // 8KB
        }

        /// <summary>
        /// File size limits and thresholds
        /// </summary>
        public static class FileLimits
        {
            public const int MaxFileNameLength = 200;
            public const long MinValidFileSize = 1024; // 1KB
            public const long MaxExpectedFileSize = 200 * 1024 * 1024; // 200MB
        }

        /// <summary>
        /// Quality scoring thresholds for file analysis
        /// </summary>
        public static class QualityScores
        {
            public const int HighQualityThreshold = 2400; // Hi-Res FLAC 24bit/192kHz
            public const int StandardQualityThreshold = 2200; // Hi-Res FLAC 24bit/96kHz
            public const int MediumQualityThreshold = 1600; // FLAC 16bit/44.1kHz
            public const int BasicQualityThreshold = 620; // MP3 320kbps
        }

        /// <summary>
        /// API rate limiting and request configuration
        /// </summary>
        public static class ApiLimits
        {
            public const int RequestsPerSecond = 10;
            public const int MaxConcurrentRequests = 5;
            public const int RequestTimeoutSeconds = 30;
            public const int ConnectionTimeoutSeconds = 15;
        }

        /// <summary>
        /// Cache configuration
        /// </summary>
        public static class Cache
        {
            public const int DefaultCacheSize = 1000;
            public const int MaxCacheSize = 10000;
            public const int CacheExpiryMinutes = 60;
            public const int StringCacheMaxLength = 500;
        }

        /// <summary>
        /// Metadata processing thresholds
        /// </summary>
        public static class Metadata
        {
            public const double MinTrackMatchScore = 0.8;
            public const double HighConfidenceMatchScore = 0.95;
            public const int MaxTrackTitleLength = 500;
            public const int MaxAlbumTitleLength = 300;
            public const int MaxArtistNameLength = 200;
        }

        /// <summary>
        /// User agent and HTTP headers
        /// </summary>
        public static class Http
        {
            public const string UserAgent = "Qobuzarr/1.0.0";
            public const string AcceptHeader = "application/json";
            public const int MaxRedirects = 5;
        }

        // NOTE: Audio validation magic bytes removed - use DownloadPayloadValidator from Common library instead

        /// <summary>
        /// Preview detection patterns
        /// </summary>
        public static class PreviewPatterns
        {
            public static readonly string[] PreviewUrlPatterns = 
            {
                "_preview_",
                "_sample_",
                "/preview/",
                "/sample/",
                "preview=true",
                "sample=true"
            };

            public static readonly string[] PreviewContentPatterns =
            {
                "preview",
                "sample",
                "demo",
                "excerpt"
            };
        }

        /// <summary>
        /// File naming configuration
        /// </summary>
        public static class FileNaming
        {
            public const string TrackNumberFormat = "D2"; // Zero-padded 2 digits
            public const string DateFormat = "yyyy-MM-dd";
            public static readonly char[] InvalidFileNameChars = 
            {
                '<', '>', ':', '"', '/', '\\', '|', '?', '*'
            };
            public const string InvalidCharReplacement = "_";
        }
    }
}
