using System;

namespace Lidarr.Plugin.Qobuzarr.Configuration
{
    /// <summary>
    /// Configuration constants for the Qobuz plugin.
    /// Centralizes magic numbers and hardcoded values to improve maintainability.
    /// </summary>
    public static class QobuzConstants
    {
        /// <summary>
        /// API configuration constants
        /// </summary>
        public static class Api
        {
            public const string BaseUrl = "https://www.qobuz.com/api.json/0.2";
            public const string UserAgent = "Qobuzarr/1.0.0";
            public const int MaxRetries = 3;
            public const int RequestTimeoutSeconds = 60;
            public const int RateLimitPerMinute = 60;
        }

        /// <summary>
        /// Cache duration constants
        /// </summary>
        public static class Cache
        {
            public static readonly TimeSpan ShortDuration = TimeSpan.FromMinutes(5);
            public static readonly TimeSpan MediumDuration = TimeSpan.FromHours(1);
            public static readonly TimeSpan LongDuration = TimeSpan.FromHours(24);
            public static readonly TimeSpan SessionDuration = TimeSpan.FromMinutes(15);
        }

        /// <summary>
        /// Download client configuration
        /// </summary>
        public static class Download
        {
            public static readonly TimeSpan CleanupCutoff = TimeSpan.FromHours(24);
            public const int MaxFolderNameLength = 200;
            public const int MaxFileNameLength = 255;
            public const int MaxRetries = 5; // Increased for better resilience against network interruptions
            public const int RetryDelayMs = 2000; // Increased base delay for network recovery
            public const int LargeFileThresholdBytes = 50 * 1024 * 1024; // 50MB
            public const int BufferSize = 81920; // 80KB
            public const int ChunkSize = 8192; // 8KB
        }

        /// <summary>
        /// Parser and filtering constants
        /// </summary>
        public static class Parser
        {
            public const int SingleTrackMinCount = 3;
            public static readonly TimeSpan SingleTrackMinDuration = TimeSpan.FromMinutes(20);
            public const int MaxSearchResults = 100;
        }

        /// <summary>
        /// Authentication constants
        /// </summary>
        public static class Authentication
        {
            // Default App ID and Secret - will be fetched dynamically from Qobuz web player
            // Users can override by providing their own App ID and Secret in settings  
            public const string DefaultAppId = "";  // Fetched dynamically
            public const string DefaultAppSecret = "";  // Fetched dynamically
            
            public const string AppIdEnvironmentVariable = "QOBUZ_APP_ID";
            public const string AppSecretEnvironmentVariable = "QOBUZ_APP_SECRET";
            
            /// <summary>
            /// Gets the Qobuz App ID from environment variable or default (for internal use)
            /// </summary>
            public static string GetDefaultAppId() => Environment.GetEnvironmentVariable(AppIdEnvironmentVariable) ?? DefaultAppId;
            
            /// <summary>
            /// Gets the Qobuz App Secret from environment variable or default (for internal use)
            /// </summary>
            public static string GetDefaultAppSecret() => Environment.GetEnvironmentVariable(AppSecretEnvironmentVariable) ?? DefaultAppSecret;
        }

        /// <summary>
        /// Logging and debugging constants
        /// </summary>
        public static class Logging
        {
            public const string ApiRequestTemplate = "API request to {0} took {1}ms";
            public const string AuthenticationTemplate = "Authentication for user {0} {1}";
            public const string DownloadTemplate = "Download {0}: {1}";
        }

        /// <summary>
        /// Quality and format constants
        /// </summary>
        public static class Quality
        {
            // Audio quality format IDs
            public const int Mp3320 = 5;
            public const int FlacCd = 6;
            public const int Flac24_96 = 7;
            public const int Flac24_192 = 27;
            
            // Bitrate and sampling thresholds
            public const int HiResThreshold = 96; // kHz
            public const int CDQualityBitrate = 1411; // kbps
            public const int MinAcceptableBitrate = 320; // kbps
            
            // Quality scoring thresholds for file analysis
            public const int HighQualityThreshold = 2400; // Hi-Res FLAC 24bit/192kHz
            public const int StandardQualityThreshold = 2200; // Hi-Res FLAC 24bit/96kHz
            public const int MediumQualityThreshold = 1600; // FLAC 16bit/44.1kHz
            public const int BasicQualityThreshold = 620; // MP3 320kbps
        }

        /// <summary>
        /// Timing constants for delays and retries
        /// </summary>
        public static class Timing
        {
            /// <summary>
            /// File operation timing constants
            /// </summary>
            public static class FileOperations
            {
                /// <summary>
                /// Delay to wait for file system operations to complete (e.g., after file change events)
                /// </summary>
                public const int FileSystemStabilizationDelayMs = 100;
                
                /// <summary>
                /// Base delay between retry attempts for file operations (multiplied by attempt number)
                /// </summary>
                public const int RetryBaseDelayMs = 100;
            }
        }

        /// <summary>
        /// File size limits and thresholds
        /// </summary>
        public static class FileLimits
        {
            public const long MinValidFileSize = 1024; // 1KB
            public const long MaxExpectedFileSize = 200 * 1024 * 1024; // 200MB
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
        /// Audio file validation magic bytes and patterns
        /// </summary>
        public static class AudioValidation
        {
            public static readonly byte[] FlacMagic = { 0x66, 0x4C, 0x61, 0x43 }; // "fLaC"
            public static readonly byte[] Mp3MagicPattern = { 0xFF, 0xE0 }; // MP3 frame header start
            public static readonly byte[] WavMagic = { 0x52, 0x49, 0x46, 0x46 }; // "RIFF"
            public const int MagicBytesToCheck = 4;
        }

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

        /// <summary>
        /// Lidarr integration constants
        /// </summary>
        public static class Lidarr
        {
            public const string DefaultBaseUrl = "http://localhost:8686";
            public const string ApiKeyHeader = "X-Api-Key";
            public const string UserAgent = "Qobuzarr/1.0.0";
            public const int DefaultTimeoutSeconds = 30;
            public const int MaxRetries = 3;
            
            /// <summary>
            /// Default page size for paginated requests
            /// </summary>
            public const int DefaultPageSize = 50;
            
            /// <summary>
            /// Maximum page size allowed by Lidarr API
            /// </summary>
            public const int MaxPageSize = 250;
            
            /// <summary>
            /// Cache duration for different types of Lidarr data
            /// </summary>
            public static class CacheDuration
            {
                public static readonly TimeSpan SystemStatus = TimeSpan.FromMinutes(5);
                public static readonly TimeSpan WantedAlbums = TimeSpan.FromMinutes(2);
                public static readonly TimeSpan AlbumDetails = TimeSpan.FromMinutes(10);
                public static readonly TimeSpan ArtistDetails = TimeSpan.FromHours(1);
                public static readonly TimeSpan HealthCheck = TimeSpan.FromMinutes(1);
            }
            
            /// <summary>
            /// Common sort fields for Lidarr API requests
            /// </summary>
            public static class SortFields
            {
                public const string Title = "title";
                public const string ReleaseDate = "releaseDate";
                public const string ArtistName = "artistName";
                public const string DateAdded = "dateAdded";
                public const string Id = "id";
            }
            
            /// <summary>
            /// Common album types in Lidarr
            /// </summary>
            public static class AlbumTypes
            {
                public const string Studio = "Studio";
                public const string Compilation = "Compilation";
                public const string Soundtrack = "Soundtrack";
                public const string Live = "Live";
                public const string Remix = "Remix";
                public const string Bootleg = "Bootleg";
                public const string Interview = "Interview";
                public const string Mixtape = "Mixtape";
                public const string Demo = "Demo";
                public const string Single = "Single";
                public const string EP = "EP";
                public const string Broadcast = "Broadcast";
                public const string Other = "Other";
            }
        }
    }
}