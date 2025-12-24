using Newtonsoft.Json;

namespace QobuzCLI.Models.Configuration
{
    /// <summary>
    /// Composite configuration class that brings together all focused configuration sections.
    /// Provides a clean separation of concerns while maintaining a unified interface.
    /// </summary>
    public class QobuzConfiguration
    {
        [JsonProperty("authentication")]
        public AuthenticationConfig Authentication { get; set; } = new();

        [JsonProperty("quality")]
        public QualityConfig Quality { get; set; } = new();

        [JsonProperty("download")]
        public DownloadConfig Download { get; set; } = new();

        [JsonProperty("search")]
        public SearchConfig Search { get; set; } = new();

        [JsonProperty("system")]
        public SystemConfig System { get; set; } = new();

        [JsonProperty("duplicateHandling")]
        public DuplicateHandlingConfig DuplicateHandling { get; set; } = new();

        [JsonProperty("lidarr")]
        public LidarrConfig Lidarr { get; set; } = new();

        /// <summary>
        /// Validates all configuration sections and applies any necessary corrections
        /// </summary>
        public void ValidateConfiguration()
        {
            Download.ValidateConcurrencySettings();
            Search.ValidateSearchLimit();
            System.ValidateSystemSettings();
            DuplicateHandling.ValidateQualityDifference();
            Lidarr.ValidateLidarrSettings();
        }

        /// <summary>
        /// Checks if the configuration has valid authentication credentials
        /// </summary>
        public bool HasValidAuth()
        {
            return Authentication.HasValidAuth();
        }

        /// <summary>
        /// Checks if Lidarr integration is properly configured
        /// </summary>
        public bool HasValidLidarrConfig()
        {
            return Lidarr.HasValidConnectionSettings();
        }

        /// <summary>
        /// Creates a legacy QobuzConfig for backward compatibility with existing code
        /// </summary>
        public QobuzConfig ToLegacyConfig()
        {
            return new QobuzConfig
            {
                // Authentication
                Email = Authentication.Email,
                Password = Authentication.Password,
                UserId = Authentication.UserId,
                AuthToken = Authentication.AuthToken,
                AuthMethod = Authentication.AuthMethod,
                AppId = Authentication.AppId,
                AppSecret = Authentication.AppSecret,
                Region = Authentication.Region,
                CountryCode = Authentication.CountryCode,

                // Quality
                Quality = Quality.Quality,
                AutoQualityFallback = Quality.AutoQualityFallback,
                QualityFallbackOrder = Quality.QualityFallbackOrder,

                // Download
                OutputDirectory = Download.OutputDirectory,
                MaxConcurrentDownloads = Download.MaxConcurrentDownloads,
                MaxConcurrentApiRequests = Download.MaxConcurrentApiRequests,
                MaxConcurrentSearches = Download.MaxConcurrentSearches,
                MaxConcurrentArtistAlbums = Download.MaxConcurrentArtistAlbums,
                CreateArtistFolders = Download.CreateArtistFolders,
                CreateAlbumFolders = Download.CreateAlbumFolders,
                FileNamingPattern = Download.FileNamingPattern,
                AlbumFolderPattern = Download.AlbumFolderPattern,
                EnableMetadataTagging = Download.EnableMetadataTagging,
                ValidateDownloads = Download.ValidateDownloads,
                PartialSizeTolerancePercent = Download.PartialSizeTolerancePercent,
                ExistingFileBehavior = Download.ExistingFileBehavior,

                // Search
                SearchResultLimit = Search.SearchResultLimit,
                AutoResolveExactMatches = Search.AutoResolveExactMatches,
                SearchPreference = Search.SearchPreference,

                // System
                ApiTimeoutSeconds = System.ApiTimeoutSeconds,
                RetryAttempts = System.RetryAttempts,
                VerboseLogging = System.VerboseLogging,
                StateSaveIntervalSeconds = System.StateSaveIntervalSeconds,
                MaxHistoryItems = System.MaxHistoryItems,
                EnableMemoryOptimizations = System.EnableMemoryOptimizations,
                EnableLocalCache = System.EnableLocalCache,

                // Duplicate Handling
                EnableDuplicateDetection = DuplicateHandling.EnableDuplicateDetection,
                EnableQualityUpgrades = DuplicateHandling.EnableQualityUpgrades,
                MinQualityDifferencePercent = DuplicateHandling.MinQualityDifferencePercent,
                KeepReplacedFiles = DuplicateHandling.KeepReplacedFiles,
                ReplacedFilesSuffix = DuplicateHandling.ReplacedFilesSuffix,
                PreferredFormats = Quality.PreferredFormats
            };
        }

        /// <summary>
        /// Creates a new QobuzConfiguration from a legacy QobuzConfig for migration
        /// </summary>
        public static QobuzConfiguration FromLegacyConfig(QobuzConfig legacy)
        {
            return new QobuzConfiguration
            {
                Authentication = new AuthenticationConfig
                {
                    Email = legacy.Email,
                    Password = legacy.Password,
                    UserId = legacy.UserId,
                    AuthToken = legacy.AuthToken,
                    AuthMethod = legacy.AuthMethod,
                    AppId = legacy.AppId,
                    AppSecret = legacy.AppSecret,
                    Region = legacy.Region,
                    CountryCode = legacy.CountryCode
                },

                Quality = new QualityConfig
                {
                    Quality = legacy.Quality,
                    AutoQualityFallback = legacy.AutoQualityFallback,
                    QualityFallbackOrder = legacy.QualityFallbackOrder,
                    PreferredFormats = legacy.PreferredFormats
                },

                Download = new DownloadConfig
                {
                    OutputDirectory = legacy.OutputDirectory,
                    MaxConcurrentDownloads = legacy.MaxConcurrentDownloads,
                    MaxConcurrentApiRequests = legacy.MaxConcurrentApiRequests,
                    MaxConcurrentSearches = legacy.MaxConcurrentSearches,
                    MaxConcurrentArtistAlbums = legacy.MaxConcurrentArtistAlbums,
                    CreateArtistFolders = legacy.CreateArtistFolders,
                    CreateAlbumFolders = legacy.CreateAlbumFolders,
                    FileNamingPattern = string.IsNullOrWhiteSpace(legacy.FileNamingPattern)
                        ? new DownloadConfig().FileNamingPattern
                        : legacy.FileNamingPattern,
                    AlbumFolderPattern = string.IsNullOrWhiteSpace(legacy.AlbumFolderPattern)
                        ? new DownloadConfig().AlbumFolderPattern
                        : legacy.AlbumFolderPattern,
                    EnableMetadataTagging = legacy.EnableMetadataTagging,
                    ValidateDownloads = legacy.ValidateDownloads,
                    PartialSizeTolerancePercent = legacy.PartialSizeTolerancePercent,
                    ExistingFileBehavior = legacy.ExistingFileBehavior
                },

                Search = new SearchConfig
                {
                    SearchResultLimit = legacy.SearchResultLimit,
                    AutoResolveExactMatches = legacy.AutoResolveExactMatches,
                    SearchPreference = legacy.SearchPreference
                },

                System = new SystemConfig
                {
                    ApiTimeoutSeconds = legacy.ApiTimeoutSeconds,
                    RetryAttempts = legacy.RetryAttempts,
                    VerboseLogging = legacy.VerboseLogging,
                    StateSaveIntervalSeconds = legacy.StateSaveIntervalSeconds,
                    MaxHistoryItems = legacy.MaxHistoryItems,
                    EnableMemoryOptimizations = legacy.EnableMemoryOptimizations,
                    EnableLocalCache = legacy.EnableLocalCache
                },

                DuplicateHandling = new DuplicateHandlingConfig
                {
                    EnableDuplicateDetection = legacy.EnableDuplicateDetection,
                    EnableQualityUpgrades = legacy.EnableQualityUpgrades,
                    MinQualityDifferencePercent = legacy.MinQualityDifferencePercent,
                    KeepReplacedFiles = legacy.KeepReplacedFiles,
                    ReplacedFilesSuffix = legacy.ReplacedFilesSuffix
                }
            };
        }
    }
}
