using Microsoft.Extensions.Logging;
using QobuzCLI.Models;

namespace QobuzCLI.Services
{
    /// <summary>
    /// Enhanced configuration service that provides secure storage for sensitive credentials.
    /// Works alongside the existing ConfigService to encrypt/decrypt sensitive fields.
    /// </summary>
    public class SecureConfigService : ISecureConfigService
    {
        private readonly IConfigService _baseConfigService;
        private readonly ISecureCredentialStorage _credentialStorage;
        private readonly ILogger<SecureConfigService> _logger;

        // Keys for secure storage
        private const string PASSWORD_KEY = "qobuz_password";
        private const string AUTH_TOKEN_KEY = "qobuz_auth_token";
        private const string APP_SECRET_KEY = "qobuz_app_secret";

        public SecureConfigService(
            IConfigService baseConfigService,
            ISecureCredentialStorage credentialStorage,
            ILogger<SecureConfigService> logger)
        {
            _baseConfigService = baseConfigService;
            _credentialStorage = credentialStorage;
            _logger = logger;
        }

        /// <summary>
        /// Loads configuration with secure credential retrieval
        /// </summary>
        public async Task<QobuzConfig> LoadConfigAsync()
        {
            var config = await _baseConfigService.LoadConfigAsync();
            
            // Load sensitive fields from secure storage
            await LoadSecureFieldsAsync(config);
            
            return config;
        }

        /// <summary>
        /// Saves configuration with secure credential storage
        /// </summary>
        public async Task SaveConfigAsync(QobuzConfig config)
        {
            // Save sensitive fields to secure storage
            await SaveSecureFieldsAsync(config);
            
            // Create a copy without sensitive fields for regular config storage
            var publicConfig = CreatePublicConfig(config);
            
            // Save the public config through the base service
            await _baseConfigService.SaveConfigAsync(publicConfig);
            
            _logger.LogInformation("Configuration saved with secure credential storage");
        }

        /// <summary>
        /// Updates a credential securely
        /// </summary>
        public async Task UpdateCredentialAsync(string credentialType, string value)
        {
            var key = GetCredentialKey(credentialType);
            if (key != null)
            {
                if (string.IsNullOrEmpty(value))
                {
                    await _credentialStorage.RemoveCredentialAsync(key);
                    _logger.LogDebug("Removed credential: {Type}", credentialType);
                }
                else
                {
                    await _credentialStorage.StoreCredentialAsync(key, value);
                    _logger.LogDebug("Updated credential: {Type}", credentialType);
                }
            }
        }

        /// <summary>
        /// Checks if secure credentials are available
        /// </summary>
        public async Task<bool> HasSecureCredentialsAsync()
        {
            var hasPassword = await _credentialStorage.HasCredentialAsync(PASSWORD_KEY);
            var hasToken = await _credentialStorage.HasCredentialAsync(AUTH_TOKEN_KEY);
            
            return hasPassword || hasToken;
        }

        /// <summary>
        /// Migrates existing plain-text credentials to secure storage
        /// </summary>
        public async Task MigrateToSecureStorageAsync()
        {
            var config = await _baseConfigService.LoadConfigAsync();
            
            var migrated = false;
            
            // Migrate password if present
            if (!string.IsNullOrEmpty(config.Password))
            {
                await _credentialStorage.StoreCredentialAsync(PASSWORD_KEY, config.Password);
                config.Password = null; // Clear from plain text
                migrated = true;
                _logger.LogInformation("Migrated password to secure storage");
            }
            
            // Migrate auth token if present
            if (!string.IsNullOrEmpty(config.AuthToken))
            {
                await _credentialStorage.StoreCredentialAsync(AUTH_TOKEN_KEY, config.AuthToken);
                config.AuthToken = null; // Clear from plain text
                migrated = true;
                _logger.LogInformation("Migrated auth token to secure storage");
            }
            
            // Migrate app secret if present
            if (!string.IsNullOrEmpty(config.AppSecret))
            {
                await _credentialStorage.StoreCredentialAsync(APP_SECRET_KEY, config.AppSecret);
                config.AppSecret = null; // Clear from plain text
                migrated = true;
                _logger.LogInformation("Migrated app secret to secure storage");
            }
            
            if (migrated)
            {
                await _baseConfigService.SaveConfigAsync(config);
                _logger.LogInformation("Migration to secure storage completed");
            }
        }

        private async Task LoadSecureFieldsAsync(QobuzConfig config)
        {
            try
            {
                // Load password from secure storage if not already present
                if (string.IsNullOrEmpty(config.Password))
                {
                    config.Password = await _credentialStorage.RetrieveCredentialAsync(PASSWORD_KEY);
                }
                
                // Load auth token from secure storage if not already present
                if (string.IsNullOrEmpty(config.AuthToken))
                {
                    config.AuthToken = await _credentialStorage.RetrieveCredentialAsync(AUTH_TOKEN_KEY);
                }
                
                // Load app secret from secure storage if not already present
                if (string.IsNullOrEmpty(config.AppSecret))
                {
                    config.AppSecret = await _credentialStorage.RetrieveCredentialAsync(APP_SECRET_KEY);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load secure credentials");
                // Continue with what we have - don't fail the entire config load
            }
        }

        private async Task SaveSecureFieldsAsync(QobuzConfig config)
        {
            try
            {
                // Store password securely if present
                if (!string.IsNullOrEmpty(config.Password))
                {
                    await _credentialStorage.StoreCredentialAsync(PASSWORD_KEY, config.Password);
                }
                
                // Store auth token securely if present
                if (!string.IsNullOrEmpty(config.AuthToken))
                {
                    await _credentialStorage.StoreCredentialAsync(AUTH_TOKEN_KEY, config.AuthToken);
                }
                
                // Store app secret securely if present
                if (!string.IsNullOrEmpty(config.AppSecret))
                {
                    await _credentialStorage.StoreCredentialAsync(APP_SECRET_KEY, config.AppSecret);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save secure credentials");
                throw;
            }
        }

        private QobuzConfig CreatePublicConfig(QobuzConfig originalConfig)
        {
            // Create a copy of the config without sensitive fields
            var publicConfig = new QobuzConfig
            {
                Email = originalConfig.Email,
                UserId = originalConfig.UserId,
                AuthMethod = originalConfig.AuthMethod,
                AppId = originalConfig.AppId,
                Region = originalConfig.Region,
                CountryCode = originalConfig.CountryCode,
                Quality = originalConfig.Quality,
                AutoQualityFallback = originalConfig.AutoQualityFallback,
                QualityFallbackOrder = originalConfig.QualityFallbackOrder,
                OutputDirectory = originalConfig.OutputDirectory,
                MaxConcurrentDownloads = originalConfig.MaxConcurrentDownloads,
                MaxConcurrentApiRequests = originalConfig.MaxConcurrentApiRequests,
                MaxConcurrentSearches = originalConfig.MaxConcurrentSearches,
                MaxConcurrentArtistAlbums = originalConfig.MaxConcurrentArtistAlbums,
                CreateArtistFolders = originalConfig.CreateArtistFolders,
                CreateAlbumFolders = originalConfig.CreateAlbumFolders,
                FileNamingPattern = originalConfig.FileNamingPattern,
                AlbumFolderPattern = originalConfig.AlbumFolderPattern,
                SearchResultLimit = originalConfig.SearchResultLimit,
                AutoResolveExactMatches = originalConfig.AutoResolveExactMatches,
                SearchPreference = originalConfig.SearchPreference,
                ApiTimeoutSeconds = originalConfig.ApiTimeoutSeconds,
                RetryAttempts = originalConfig.RetryAttempts,
                EnableMetadataTagging = originalConfig.EnableMetadataTagging,
                VerboseLogging = originalConfig.VerboseLogging,
                StateSaveIntervalSeconds = originalConfig.StateSaveIntervalSeconds,
                MaxHistoryItems = originalConfig.MaxHistoryItems,
                EnableMemoryOptimizations = originalConfig.EnableMemoryOptimizations,
                EnableLocalCache = originalConfig.EnableLocalCache,
                EnableDuplicateDetection = originalConfig.EnableDuplicateDetection,
                EnableQualityUpgrades = originalConfig.EnableQualityUpgrades,
                MinQualityDifferencePercent = originalConfig.MinQualityDifferencePercent,
                KeepReplacedFiles = originalConfig.KeepReplacedFiles,
                ReplacedFilesSuffix = originalConfig.ReplacedFilesSuffix,
                ValidateDownloads = originalConfig.ValidateDownloads,
                PartialSizeTolerancePercent = originalConfig.PartialSizeTolerancePercent,
                PreferredFormats = originalConfig.PreferredFormats,
                
                // Exclude sensitive fields - they will be stored securely
                Password = null,
                AuthToken = null,
                AppSecret = null
            };

            return publicConfig;
        }

        private string? GetCredentialKey(string credentialType)
        {
            return credentialType.ToLower() switch
            {
                "password" => PASSWORD_KEY,
                "authtoken" => AUTH_TOKEN_KEY,
                "appsecret" => APP_SECRET_KEY,
                _ => null
            };
        }
    }
}