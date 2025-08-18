using Microsoft.Extensions.Logging;
using QobuzCLI.Models.Configuration;

namespace QobuzCLI.Services
{
    /// <summary>
    /// Service for managing secure Lidarr API credentials.
    /// Integrates with the existing SecureCredentialStorage for consistent security.
    /// </summary>
    public class LidarrCredentialService
    {
        private readonly ISecureCredentialStorage _credentialStorage;
        private readonly ILogger<LidarrCredentialService> _logger;

        public LidarrCredentialService(
            ISecureCredentialStorage credentialStorage,
            ILogger<LidarrCredentialService> logger)
        {
            _credentialStorage = credentialStorage ?? throw new ArgumentNullException(nameof(credentialStorage));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Stores the Lidarr API key securely and updates the configuration
        /// </summary>
        /// <param name="config">Lidarr configuration to update</param>
        /// <param name="apiKey">API key to store securely</param>
        public async Task SetApiKeyAsync(LidarrConfig config, string apiKey)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API key cannot be empty", nameof(apiKey));

            try
            {
                // Validate API key format
                if (!Lidarr.Plugin.Qobuzarr.Utilities.LidarrInputValidator.IsApiKeyFormatValid(apiKey))
                {
                    _logger.LogWarning("API key format validation failed - storing anyway but may not work");
                }

                // Store the API key securely
                await _credentialStorage.StoreCredentialAsync(LidarrConfig.CredentialKeys.LIDARR_API_KEY, apiKey);

                // Update config state
                config.ApiKey = string.Empty; // Clear in-memory copy
                config.HasSecureApiKey = true;

                _logger.LogInformation("Lidarr API key stored securely");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store Lidarr API key securely");
                throw;
            }
        }

        /// <summary>
        /// Retrieves the Lidarr API key from secure storage
        /// </summary>
        /// <param name="config">Lidarr configuration</param>
        /// <returns>The API key or null if not found</returns>
        public async Task<string?> GetApiKeyAsync(LidarrConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            try
            {
                // If we have an in-memory copy, use that first
                if (!string.IsNullOrWhiteSpace(config.ApiKey))
                {
                    return config.ApiKey;
                }

                // Try to retrieve from secure storage
                if (config.HasSecureApiKey)
                {
                    var apiKey = await _credentialStorage.RetrieveCredentialAsync(LidarrConfig.CredentialKeys.LIDARR_API_KEY);
                    if (!string.IsNullOrWhiteSpace(apiKey))
                    {
                        _logger.LogDebug("Lidarr API key retrieved from secure storage");
                        return apiKey;
                    }
                    else
                    {
                        _logger.LogWarning("Lidarr API key not found in secure storage despite HasSecureApiKey being true");
                        config.HasSecureApiKey = false; // Reset the flag
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve Lidarr API key from secure storage");
                return null;
            }
        }

        /// <summary>
        /// Removes the stored Lidarr API key
        /// </summary>
        /// <param name="config">Lidarr configuration to update</param>
        public async Task RemoveApiKeyAsync(LidarrConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            try
            {
                await _credentialStorage.RemoveCredentialAsync(LidarrConfig.CredentialKeys.LIDARR_API_KEY);
                
                // Update config state
                config.ApiKey = string.Empty;
                config.HasSecureApiKey = false;

                _logger.LogInformation("Lidarr API key removed from secure storage");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove Lidarr API key from secure storage");
                throw;
            }
        }

        /// <summary>
        /// Checks if a Lidarr API key is securely stored
        /// </summary>
        /// <param name="config">Lidarr configuration</param>
        /// <returns>True if an API key is available (in memory or secure storage)</returns>
        public async Task<bool> HasApiKeyAsync(LidarrConfig config)
        {
            if (config == null)
                return false;

            try
            {
                // Check in-memory first
                if (!string.IsNullOrWhiteSpace(config.ApiKey))
                    return true;

                // Check secure storage
                if (config.HasSecureApiKey)
                {
                    return await _credentialStorage.HasCredentialAsync(LidarrConfig.CredentialKeys.LIDARR_API_KEY);
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check for Lidarr API key existence");
                return false;
            }
        }

        /// <summary>
        /// Validates that the current API key configuration is usable
        /// </summary>
        /// <param name="config">Lidarr configuration to validate</param>
        /// <returns>True if the API key configuration appears valid</returns>
        public async Task<bool> ValidateApiKeyConfigurationAsync(LidarrConfig config)
        {
            if (config == null)
                return false;

            try
            {
                var apiKey = await GetApiKeyAsync(config);
                return !string.IsNullOrWhiteSpace(apiKey) && 
                       Lidarr.Plugin.Qobuzarr.Utilities.LidarrInputValidator.IsApiKeyFormatValid(apiKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate Lidarr API key configuration");
                return false;
            }
        }
    }
}