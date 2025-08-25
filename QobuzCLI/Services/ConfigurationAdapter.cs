using System;
using System.IO;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using QobuzCLI.Models;

namespace QobuzCLI.Services
{
    /// <summary>
    /// Adapter that bridges CLI configuration to plugin settings models.
    /// Ensures CLI uses plugin's configuration system per CLAUDE.md architecture.
    /// </summary>
    public class ConfigurationAdapter
    {
        /// <summary>
        /// Converts CLI config to plugin's QobuzIndexerSettings
        /// </summary>
        public static QobuzIndexerSettings ToIndexerSettings(QobuzConfig cliConfig)
        {
            if (cliConfig == null)
                throw new ArgumentNullException(nameof(cliConfig));

            var settings = new QobuzIndexerSettings
            {
                // Authentication
                Email = cliConfig.Email,
                Password = cliConfig.Password,
                UserId = cliConfig.UserId,
                AuthToken = cliConfig.AuthToken,
                AppId = cliConfig.AppId ?? string.Empty,
                AppSecret = cliConfig.AppSecret ?? string.Empty,
                CountryCode = cliConfig.CountryCode ?? "US",
                
                // Determine auth method
                AuthMethod = DetermineAuthMethod(cliConfig),
                
                // Search settings
                SearchLimit = cliConfig.SearchResultLimit,
                IncludeSingles = cliConfig.IncludeSingles,
                IncludeCompilations = cliConfig.IncludeCompilations,
                
                // Advanced settings
                ApiRateLimit = cliConfig.ApiRateLimit,
                SearchCacheDuration = cliConfig.SearchCacheDuration,
                ConnectionTimeout = cliConfig.ApiTimeoutSeconds,
                EarlyReleaseLimit = cliConfig.EarlyReleaseLimit
            };

            return settings;
        }

        /// <summary>
        /// Converts CLI config to plugin's QobuzDownloadSettings
        /// </summary>
        public static QobuzDownloadSettings ToDownloadSettings(QobuzConfig cliConfig)
        {
            if (cliConfig == null)
                throw new ArgumentNullException(nameof(cliConfig));

            // Map quality setting
            var qualityId = MapQualityToFormatId(cliConfig.Quality);

            var settings = new QobuzDownloadSettings
            {
                // Download settings (only using properties that actually exist)
                DownloadPath = Path.GetFullPath(cliConfig.OutputDirectory),
                PreferredQuality = qualityId,
                CreateAlbumFolders = cliConfig.CreateAlbumFolders,
                // MaxConcurrentDownloads is read-only in plugin, handled via GetEffectiveConcurrency()
                
                // Use defaults for plugin-specific settings
                MinimumSuccessRatePercent = 80,
                TreatPreviewAsFailure = false,
                SkipPreviewTracks = true
                
                // Note: QobuzDownloadSettings doesn't include authentication properties
                // Those should be handled through the QobuzConfig instead
            };

            return settings;
        }

        /// <summary>
        /// Creates a minimal CLI config from plugin settings for backward compatibility
        /// </summary>
        public static QobuzConfig FromPluginSettings(QobuzIndexerSettings indexerSettings, QobuzDownloadSettings downloadSettings = null)
        {
            var config = new QobuzConfig
            {
                // From indexer settings
                Email = indexerSettings.Email,
                Password = indexerSettings.Password,
                UserId = indexerSettings.UserId,
                AuthToken = indexerSettings.AuthToken,
                AppId = indexerSettings.AppId,
                AppSecret = indexerSettings.AppSecret,
                CountryCode = indexerSettings.CountryCode,
                Region = indexerSettings.CountryCode, // Duplicate for compatibility
                
                // Search settings (only using properties that exist in QobuzConfig)
                SearchResultLimit = indexerSettings.SearchLimit,
                ApiTimeoutSeconds = indexerSettings.ConnectionTimeout
                // Note: IncludeSingles, IncludeCompilations, EnableQueryIntelligence, ApiRateLimit, 
                // SearchCacheDuration, EarlyReleaseLimit don't exist in current QobuzConfig model
            };

            // Add download settings if provided
            if (downloadSettings != null)
            {
                // Only assign properties that actually exist in both source and target models
                config.Quality = MapFormatIdToQuality(downloadSettings.PreferredQuality);
                // Note: Most download settings properties don't exist in current QobuzConfig model
                // Using defaults for missing properties
                config.CreateArtistFolders = true;
                config.CreateAlbumFolders = true;
                config.RetryAttempts = 3;
            }

            return config;
        }

        private static int DetermineAuthMethod(QobuzConfig config)
        {
            // Check which auth method is configured
            if (!string.IsNullOrEmpty(config.Email) && !string.IsNullOrEmpty(config.Password))
                return (int)AuthenticationMethod.Email;
            
            if (!string.IsNullOrEmpty(config.UserId) && !string.IsNullOrEmpty(config.AuthToken))
                return (int)AuthenticationMethod.Token;
            
            // Default to email if nothing configured
            return (int)AuthenticationMethod.Email;
        }

        private static int MapQualityToFormatId(string quality)
        {
            return quality?.ToLower() switch
            {
                "mp3-320" => 5,
                "flac-cd" => 6,
                "flac-hires" => 7,
                "flac-96" => 7,
                "flac-192" => 27,
                "flac-max" => 27,
                _ => 6 // Default to FLAC CD
            };
        }

        private static string MapFormatIdToQuality(int formatId)
        {
            return formatId switch
            {
                5 => "mp3-320",
                6 => "flac-cd",
                7 => "flac-hires",
                27 => "flac-max",
                _ => "flac-cd"
            };
        }
    }

    /// <summary>
    /// Authentication method enum matching plugin's definition
    /// </summary>
    public enum AuthenticationMethod
    {
        Email = 0,
        Token = 1
    }
}