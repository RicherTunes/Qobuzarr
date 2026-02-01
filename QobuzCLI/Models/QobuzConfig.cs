using Newtonsoft.Json;

namespace QobuzCLI.Models;

public class ConfigParameter
{
    public string Key { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Type Type { get; set; } = typeof(string);
    public object? DefaultValue { get; set; }
    public List<string>? AllowedValues { get; set; }
    public string Category { get; set; } = "General";
    public bool IsRequired { get; set; } = false;
    public bool IsSensitive { get; set; } = false;
}

public class QobuzConfig
{
    // Authentication
    [JsonProperty("email")]
    public string? Email { get; set; }

    [JsonProperty("password")]
    public string? Password { get; set; }

    [JsonProperty("userId")]
    public string? UserId { get; set; }

    [JsonProperty("authToken")]
    public string? AuthToken { get; set; }

    [JsonProperty("authMethod")]
    public string AuthMethod { get; set; } = "email"; // email, token

    [JsonProperty("appId")]
    public string? AppId { get; set; }

    [JsonProperty("appSecret")]
    public string? AppSecret { get; set; }

    [JsonProperty("region")]
    public string Region { get; set; } = "CA"; // Default to Canada

    [JsonProperty("countryCode")]
    public string CountryCode { get; set; } = "CA"; // ISO 3166-1 alpha-2 country code

    // Quality Settings
    [JsonProperty("quality")]
    public string Quality { get; set; } = "flac-max"; // mp3-320, flac-cd, flac-hires, flac-max

    [JsonProperty("autoQualityFallback")]
    public bool AutoQualityFallback { get; set; } = true;

    [JsonProperty("qualityFallbackOrder")]
    public List<string>? QualityFallbackOrder { get; set; } = QobuzCLI.Models.Configuration.QualityConfig.DefaultQualityFallbackOrder;

    // Download Settings
    [JsonProperty("outputDirectory")]
    public string OutputDirectory { get; set; } = "./Downloads";

    [JsonProperty("maxConcurrentDownloads")]
    public int MaxConcurrentDownloads { get; set; } = 8; // Increased from 4 for better throughput

    [JsonProperty("maxConcurrentApiRequests")]
    public int MaxConcurrentApiRequests { get; set; } = 16; // Increased from 8 for higher API throughput

    [JsonProperty("maxConcurrentSearches")]
    public int MaxConcurrentSearches { get; set; } = 6; // Increased from 4 for batch operations

    [JsonProperty("maxConcurrentArtistAlbums")]
    public int MaxConcurrentArtistAlbums { get; set; } = 2;

    [JsonProperty("createArtistFolders")]
    public bool CreateArtistFolders { get; set; } = true;

    [JsonProperty("createAlbumFolders")]
    public bool CreateAlbumFolders { get; set; } = true;

    [JsonProperty("fileNamingPattern")]
    public string FileNamingPattern { get; set; } = "{track:00} - {title}";

    [JsonProperty("albumFolderPattern")]
    public string AlbumFolderPattern { get; set; } = "{artist} - {album} ({year})";

    // Search Settings
    [JsonProperty("searchResultLimit")]
    public int SearchResultLimit { get; set; } = 20;

    [JsonProperty("autoResolveExactMatches")]
    public bool AutoResolveExactMatches { get; set; } = true;

    [JsonProperty("searchPreference")]
    public string SearchPreference { get; set; } = "smart"; // smart, albums, tracks

    [JsonProperty("includeSingles")]
    public bool IncludeSingles { get; set; } = true;

    [JsonProperty("includeCompilations")]
    public bool IncludeCompilations { get; set; } = true;

    [JsonProperty("enableQueryIntelligence")]
    public bool EnableQueryIntelligence { get; set; } = true;

    [JsonProperty("apiRateLimit")]
    public int ApiRateLimit { get; set; } = 1000;

    [JsonProperty("searchCacheDuration")]
    public int SearchCacheDuration { get; set; } = 3600; // 1 hour in seconds

    [JsonProperty("earlyReleaseLimit")]
    public int EarlyReleaseLimit { get; set; } = 30;

    // Advanced
    [JsonProperty("apiTimeoutSeconds")]
    public int ApiTimeoutSeconds { get; set; } = 30;

    [JsonProperty("retryAttempts")]
    public int RetryAttempts { get; set; } = 3;

    [JsonProperty("enableMetadataTagging")]
    public bool EnableMetadataTagging { get; set; } = true;

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

    // Duplicate Handling Settings
    [JsonProperty("enableDuplicateDetection")]
    public bool EnableDuplicateDetection { get; set; } = true;

    [JsonProperty("enableQualityUpgrades")]
    public bool EnableQualityUpgrades { get; set; } = true;

    [JsonProperty("minQualityDifferencePercent")]
    public double MinQualityDifferencePercent { get; set; } = 20.0;

    [JsonProperty("keepReplacedFiles")]
    public bool KeepReplacedFiles { get; set; } = false;

    [JsonProperty("replacedFilesSuffix")]
    public string ReplacedFilesSuffix { get; set; } = ".replaced";

    [JsonProperty("validateDownloads")]
    public bool ValidateDownloads { get; set; } = true;

    [JsonProperty("partialSizeTolerancePercent")]
    public double PartialSizeTolerancePercent { get; set; } = 5.0;

    [JsonProperty("preferredFormats")]
    public List<string>? PreferredFormats { get; set; } = QobuzCLI.Models.Configuration.QualityConfig.DefaultPreferredFormats;

    // Existing file handling strategy: suffix, skip, overwrite
    [JsonProperty("existingFileBehavior")]
    public string ExistingFileBehavior { get; set; } = "overwrite";

    // Lidarr Integration Settings
    [JsonProperty("lidarrUrl")]
    public string? LidarrUrl { get; set; }

    [JsonProperty("lidarrApiKey")]
    public string? LidarrApiKey { get; set; }

    [JsonProperty("lidarrHasSecureApiKey")]
    public bool LidarrHasSecureApiKey { get; set; } = false;

    [JsonProperty("lidarrTimeoutSeconds")]
    public int LidarrTimeoutSeconds { get; set; } = 30;

    [JsonProperty("lidarrDefaultExportFormat")]
    public string LidarrDefaultExportFormat { get; set; } = "json";

    [JsonProperty("lidarrDefaultSortOrder")]
    public string LidarrDefaultSortOrder { get; set; } = "release_date_desc";

    [JsonProperty("lidarrDefaultExportLimit")]
    public int LidarrDefaultExportLimit { get; set; } = 0;

    [JsonProperty("lidarrAutoDownloadAfterExport")]
    public bool LidarrAutoDownloadAfterExport { get; set; } = false;

    [JsonProperty("lidarrDefaultFilterMode")]
    public string LidarrDefaultFilterMode { get; set; } = "and";

    [JsonProperty("lidarrEnablePreDownloadValidation")]
    public bool LidarrEnablePreDownloadValidation { get; set; } = true;

    [JsonProperty("lidarrDefaultAlbumTypes")]
    public string LidarrDefaultAlbumTypes { get; set; } = "album";

    [JsonProperty("lidarrDefaultMinYear")]
    public int LidarrDefaultMinYear { get; set; } = 0;

    [JsonProperty("lidarrDefaultMaxYear")]
    public int LidarrDefaultMaxYear { get; set; } = 0;

    [JsonProperty("lidarrDefaultMinTracks")]
    public int LidarrDefaultMinTracks { get; set; } = 0;

    [JsonProperty("lidarrEnableExportCaching")]
    public bool LidarrEnableExportCaching { get; set; } = true;

    [JsonProperty("lidarrCacheExpiryHours")]
    public int LidarrCacheExpiryHours { get; set; } = 24;

    [JsonProperty("lidarrGenerateDownloadReports")]
    public bool LidarrGenerateDownloadReports { get; set; } = true;

    [JsonProperty("lidarrDefaultReportFormat")]
    public string LidarrDefaultReportFormat { get; set; } = "html";

    // Test/diagnostics-only: allow strict initialization without using process-wide env vars
    [JsonProperty("strictInitialization")]
    public bool StrictInitialization { get; set; } = false;

    /// <summary>
    /// Determine if email auth is in effect.
    /// Prefer explicit setting, but infer from present credentials when appropriate.
    /// </summary>
    public bool IsEmailAuth()
        => AuthMethod.Equals("email", StringComparison.OrdinalIgnoreCase)
           || (!string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password));

    /// <summary>
    /// Determine if token auth is in effect.
    /// Prefer explicit setting, but infer from present credentials when appropriate.
    /// </summary>
    public bool IsTokenAuth()
        => AuthMethod.Equals("token", StringComparison.OrdinalIgnoreCase)
           || (!string.IsNullOrWhiteSpace(UserId) && !string.IsNullOrWhiteSpace(AuthToken));

    /// <summary>
    /// Check if configuration has valid authentication details.
    /// Inference allows tests to set only credentials without toggling AuthMethod.
    /// </summary>
    public bool HasValidAuth()
    {
        if (!string.IsNullOrWhiteSpace(UserId) && !string.IsNullOrWhiteSpace(AuthToken))
            return true;

        if (!string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password))
            return true;

        return false;
    }
}
