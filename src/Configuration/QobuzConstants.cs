using System;
using Lidarr.Plugin.Qobuzarr.Constants;

namespace Lidarr.Plugin.Qobuzarr.Configuration
{
    /// <summary>
    /// Centralized constants for the Qobuzarr plugin.
    /// This single source of truth consolidates all constants from various files.
    /// </summary>
    public static class QobuzConstants
    {
        /// <summary>
        /// Plugin metadata and information constants
        /// </summary>
        public static class Plugin
        {
            public const string Name = "Qobuzarr";
            public const string DisplayName = "Qobuzarr - Qobuz Downloader";
            public const string Author = "RicherTunes";
            public const string Version = "0.0.13";
            public const string Description = "High-quality music indexer and download client for Qobuz streaming service with ML optimization";
            public const string ProjectUrl = "https://github.com/RicherTunes/Lidarr.Plugin.Qobuzarr";
            public const string MinimumLidarrVersion = "2.13.0.4664";
        }

        /// <summary>
        /// API configuration constants
        /// </summary>
        public static class Api
        {
            public const string BaseUrl = "https://www.qobuz.com/api.json/0.2";
            public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:120.0) Gecko/20100101 Firefox/120.0";
            public const string DefaultAppId = "798273057";  // Will be fetched dynamically from web player
            public const int MaxRetries = QobuzarrConstants.Defaults.GlobalMaxRetryAttempts;
            public const int RequestTimeoutSeconds = 60;
            public const int RateLimitPerMinute = 60;
            public const int DefaultRateLimitPerSecond = 10;
            public const int BurstSize = 20;
            public const int DefaultPageSize = 50;
            public const int MaxPageSize = 500;
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
            // Session management
            public const int SessionLifetimeHours = 24;
            public const int SessionRenewalThresholdHours = 2;
            public const string SessionCacheKey = "qobuz_session";
            public const string CredentialsCacheKey = "qobuz_credentials";
            public const int MaxAuthenticationAttempts = 3;
            public const int AuthenticationLockoutMinutes = 15;
            
            // App credentials - will be fetched dynamically from Qobuz web player
            // Users can override by providing their own App ID and Secret in settings  
            public const string DefaultAppId = "";  // Fetched dynamically
            public const string DefaultAppSecret = "";  // Fetched dynamically
            
            public const string AppIdEnvironmentVariable = "QOBUZ_APP_ID";
            public const string AppSecretEnvironmentVariable = "QOBUZ_APP_SECRET";
            
            /// <summary>
            /// Gets the Qobuz App ID from environment variable or default (for internal use)
            /// </summary>
            public static string GetDefaultAppId() => Environment.GetEnvironmentVariable(AppIdEnvironmentVariable) ?? Api.DefaultAppId;
            
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
            // Quality IDs as used by Qobuz API
            public const int MP3_320 = 5;
            public const int FLAC_CD = 6;
            public const int FLAC_HI_RES_96 = 7;
            public const int FLAC_HI_RES_192 = 27;
            
            // Display names
            public const string MP3_320_NAME = "MP3 320kbps";
            public const string FLAC_CD_NAME = "FLAC CD 16bit/44.1kHz";
            public const string FLAC_HI_RES_96_NAME = "FLAC Hi-Res 24bit/96kHz";
            public const string FLAC_HI_RES_192_NAME = "FLAC Hi-Res 24bit/192kHz";
            
            // Thresholds and bitrates
            public const int HiResThreshold = 96; // kHz
            public const int CDQualityBitrate = 1411; // kbps
            public const int MinAcceptableBitrate = 320; // kbps
            public const int MP3_320_BITRATE = 320;
            public const int FLAC_HI_RES_96_BITRATE = 4608;
            public const int FLAC_HI_RES_192_BITRATE = 9216;
            
            // File extensions
            public const string MP3_EXTENSION = ".mp3";
            public const string FLAC_EXTENSION = ".flac";
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
        /// Machine Learning optimization constants
        /// </summary>
        public static class MachineLearning
        {
            public const string ModelFileName = "qobuz_query_optimizer.onnx";
            public const string PatternsFileName = "ml-baseline-patterns.json";
            public const double MinConfidenceThreshold = 0.7;
            public const int MaxQueryOptimizationMs = 100;
            public const int TrainingDataMinSamples = 1000;
            public const bool EnableAdaptiveLearning = true;
        }

        /// <summary>
        /// Security constants
        /// </summary>
        public static class Security
        {
            public const int MaxPasswordLength = 128;
            public const int MinPasswordLength = 8;
            public const int MaxEmailLength = 256;
            public const int SaltSize = 32;
            public const int KeyDerivationIterations = 10000;
            public const bool RequireHttps = true;
            public const bool ValidateCertificates = true;
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
