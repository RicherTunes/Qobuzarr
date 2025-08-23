using System.Reflection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using QobuzCLI.Models;
using QobuzCLI.Models.Configuration;
using Spectre.Console;

namespace QobuzCLI.Services;

public class ConfigService : IConfigService
{
    private const string ConfigFileName = "qobuz-config.json";
    private const string NewConfigFileName = "qobuz-configuration.json";
    private readonly ILogger<ConfigService> _logger;
    private readonly string _configPath;
    private readonly string _newConfigPath;
    private QobuzConfig? _config;
    private QobuzConfiguration? _configuration;
    private readonly Dictionary<string, Models.ConfigParameter> _parameters;

    public ConfigService(ILogger<ConfigService> logger)
    {
        _logger = logger;
        _configPath = GetConfigPath();
        _newConfigPath = GetNewConfigPath();
        _parameters = BuildParameterDefinitions();
    }

    private string GetNewConfigPath()
    {
        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var qobuzDirectory = Path.Combine(homeDirectory, ".qobuz");
        
        if (!Directory.Exists(qobuzDirectory))
        {
            Directory.CreateDirectory(qobuzDirectory);
        }
        
        return Path.Combine(qobuzDirectory, NewConfigFileName);
    }

    public string GetConfigPath()
    {
        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var qobuzDirectory = Path.Combine(homeDirectory, ".qobuz");
        
        if (!Directory.Exists(qobuzDirectory))
        {
            Directory.CreateDirectory(qobuzDirectory);
        }
        
        return Path.Combine(qobuzDirectory, ConfigFileName);
    }

    public async Task<QobuzConfig> LoadConfigAsync()
    {
        if (_config != null)
            return _config;

        // Load the new configuration and convert to legacy format for backward compatibility
        var newConfig = await LoadConfigurationAsync().ConfigureAwait(false);
        _config = newConfig.ToLegacyConfig();
        
        return _config;
    }

    public async Task SaveConfigAsync(QobuzConfig config)
    {
        // Convert legacy config to new format and save
        var newConfig = QobuzConfiguration.FromLegacyConfig(config);
        await SaveConfigurationAsync(newConfig).ConfigureAwait(false);
        _config = config;
        
        _logger.LogDebug("Legacy configuration converted and saved to new format");
    }

    public async Task<QobuzConfiguration> LoadConfigurationAsync()
    {
        if (_configuration != null)
            return _configuration;

        try
        {
            // Try to load the new configuration format first
            if (File.Exists(_newConfigPath))
            {
                var json = await File.ReadAllTextAsync(_newConfigPath).ConfigureAwait(false);
                _configuration = JsonConvert.DeserializeObject<QobuzConfiguration>(json) ?? new QobuzConfiguration();
                _logger.LogDebug("New configuration loaded from {ConfigPath}", _newConfigPath);
            }
            // If new config doesn't exist, try to migrate from legacy config
            else if (File.Exists(_configPath))
            {
                _logger.LogInformation("Migrating legacy configuration to new format");
                var legacyJson = await File.ReadAllTextAsync(_configPath).ConfigureAwait(false);
                var legacyConfig = JsonConvert.DeserializeObject<QobuzConfig>(legacyJson) ?? new QobuzConfig();
                _configuration = QobuzConfiguration.FromLegacyConfig(legacyConfig);
                
                // Save the migrated configuration
                await SaveConfigurationAsync(_configuration).ConfigureAwait(false);
                _logger.LogInformation("Configuration migrated successfully to {NewConfigPath}", _newConfigPath);
            }
            else
            {
                // Create new default configuration
                _configuration = new QobuzConfiguration();
                await SaveConfigurationAsync(_configuration).ConfigureAwait(false);
                _logger.LogInformation("Created default configuration at {ConfigPath}", _newConfigPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load configuration, using defaults");
            _configuration = new QobuzConfiguration();
        }

        // Validate the loaded configuration
        _configuration.ValidateConfiguration();
        return _configuration;
    }

    public async Task SaveConfigurationAsync(QobuzConfiguration configuration)
    {
        try
        {
            var json = JsonConvert.SerializeObject(configuration, Formatting.Indented);
            await File.WriteAllTextAsync(_newConfigPath, json).ConfigureAwait(false);
            _configuration = configuration;
            _logger.LogDebug("Configuration saved to {ConfigPath}", _newConfigPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration");
            throw;
        }
    }

    public async Task<T> GetValueAsync<T>(string key, T defaultValue = default!)
    {
        var config = await LoadConfigAsync().ConfigureAwait(false);
        
        try
        {
            var property = typeof(QobuzConfig).GetProperty(ToPascalCase(key));
            if (property != null)
            {
                var value = property.GetValue(config);
                if (value != null && value is T typedValue)
                    return typedValue;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get config value for key: {Key}", key);
        }

        return defaultValue;
    }

    public async Task SetValueAsync<T>(string key, T value)
    {
        var config = await LoadConfigAsync().ConfigureAwait(false);

        try
        {
            var property = typeof(QobuzConfig).GetProperty(ToPascalCase(key));
            if (property != null)
            {
                // Validate against allowed values if specified
                if (_parameters.TryGetValue(key.ToLower(), out var param) && param.AllowedValues != null)
                {
                    var stringValue = value?.ToString();
                    if (!param.AllowedValues.Contains(stringValue, StringComparer.OrdinalIgnoreCase))
                    {
                        throw new ArgumentException($"Invalid value '{stringValue}' for {key}. Allowed values: {string.Join(", ", param.AllowedValues)}");
                    }
                }

                property.SetValue(config, value);
                await SaveConfigAsync(config).ConfigureAwait(false);
                _logger.LogInformation("Config updated: {Key} = {Value}", key, value);
            }
            else
            {
                throw new ArgumentException($"Unknown configuration key: {key}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set config value for key: {Key}", key);
            throw;
        }
    }

    public async Task<Dictionary<string, object>> GetAllValuesAsync()
    {
        var config = await LoadConfigAsync().ConfigureAwait(false);
        var result = new Dictionary<string, object>();
        var properties = typeof(QobuzConfig).GetProperties();
        
        foreach (var property in properties)
        {
            var key = ToKebabCase(property.Name);
            var value = property.GetValue(config);
            if (value != null)
                result[key] = value;
        }

        return result;
    }

    public async Task<List<Models.ConfigParameter>> GetParametersAsync()
    {
        await LoadConfigAsync().ConfigureAwait(false); // Ensure config is loaded
        return _parameters.Values.OrderBy(p => p.Category).ThenBy(p => p.Key).ToList();
    }

    // Legacy ConfigManager API compatibility methods
    public async Task<T> GetAsync<T>(string key, T? defaultValue = default)
    {
        return await GetValueAsync(key, defaultValue!).ConfigureAwait(false);
    }

    public async Task SetAsync<T>(string key, T value)
    {
        await SetValueAsync(key, value).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, object>> GetAllAsync()
    {
        return await GetAllValuesAsync().ConfigureAwait(false);
    }

    public async Task SaveAsync()
    {
        var config = await LoadConfigAsync().ConfigureAwait(false);
        await SaveConfigAsync(config).ConfigureAwait(false);
    }

    public async Task LoadAsync()
    {
        await LoadConfigAsync().ConfigureAwait(false);
    }

    private Dictionary<string, Models.ConfigParameter> BuildParameterDefinitions()
    {
        return new Dictionary<string, Models.ConfigParameter>
        {
            // Authentication
            ["email"] = new() 
            { 
                Key = "email", 
                Description = "Qobuz account email address", 
                Type = typeof(string), 
                Category = "Authentication",
                IsRequired = false
            },
            ["password"] = new() 
            { 
                Key = "password", 
                Description = "Qobuz account password", 
                Type = typeof(string), 
                Category = "Authentication",
                IsRequired = false,
                IsSensitive = true
            },
            ["user-id"] = new() 
            { 
                Key = "user-id", 
                Description = "Qobuz user ID (for token authentication)", 
                Type = typeof(string), 
                Category = "Authentication",
                IsRequired = false
            },
            ["auth-token"] = new() 
            { 
                Key = "auth-token", 
                Description = "Qobuz authentication token", 
                Type = typeof(string), 
                Category = "Authentication",
                IsRequired = false,
                IsSensitive = true
            },
            ["auth-method"] = new() 
            { 
                Key = "auth-method", 
                Description = "Authentication method to use", 
                Type = typeof(string),
                DefaultValue = "email", 
                AllowedValues = new List<string> { "email", "token" }, 
                Category = "Authentication"
            },
            ["app-id"] = new() 
            { 
                Key = "app-id", 
                Description = "Qobuz application ID (optional, uses default if not set)", 
                Type = typeof(string), 
                Category = "Authentication",
                IsRequired = false
            },
            ["app-secret"] = new() 
            { 
                Key = "app-secret", 
                Description = "Qobuz application secret (optional, uses default if not set)", 
                Type = typeof(string), 
                Category = "Authentication",
                IsRequired = false,
                IsSensitive = true
            },

            // Quality Settings
            ["quality"] = new() 
            { 
                Key = "quality", 
                Description = "Preferred audio quality for downloads", 
                Type = typeof(string),
                DefaultValue = "flac-max", 
                AllowedValues = new List<string> { "mp3-320", "flac-cd", "flac-hires", "flac-max" }, 
                Category = "Quality"
            },
            ["auto-quality-fallback"] = new() 
            { 
                Key = "auto-quality-fallback", 
                Description = "Automatically fallback to lower quality if preferred is unavailable", 
                Type = typeof(bool), 
                DefaultValue = true, 
                Category = "Quality"
            },

            // Download Settings  
            ["output-directory"] = new() 
            { 
                Key = "output-directory", 
                Description = "Directory where music will be downloaded", 
                Type = typeof(string), 
                DefaultValue = "./Downloads", 
                Category = "Downloads"
            },
            ["max-concurrent-downloads"] = new() 
            { 
                Key = "max-concurrent-downloads", 
                Description = "Maximum number of simultaneous downloads", 
                Type = typeof(int), 
                DefaultValue = 4, 
                Category = "Downloads"
            },
            ["create-artist-folders"] = new() 
            { 
                Key = "create-artist-folders", 
                Description = "Create subdirectories for each artist", 
                Type = typeof(bool), 
                DefaultValue = true, 
                Category = "Downloads"
            },
            ["create-album-folders"] = new() 
            { 
                Key = "create-album-folders", 
                Description = "Create subdirectories for each album", 
                Type = typeof(bool), 
                DefaultValue = true, 
                Category = "Downloads"
            },
            ["file-naming-pattern"] = new() 
            { 
                Key = "file-naming-pattern", 
                Description = "Pattern for naming downloaded files", 
                Type = typeof(string), 
                DefaultValue = "{track:00} - {title}", 
                Category = "Downloads"
            },

            // Search Settings
            ["search-result-limit"] = new() 
            { 
                Key = "search-result-limit", 
                Description = "Maximum number of search results to display", 
                Type = typeof(int), 
                DefaultValue = 20, 
                Category = "Search"
            },
            ["auto-resolve-exact-matches"] = new() 
            { 
                Key = "auto-resolve-exact-matches", 
                Description = "Automatically select exact matches without prompting", 
                Type = typeof(bool), 
                DefaultValue = true, 
                Category = "Search"
            },

            // Advanced
            ["api-timeout-seconds"] = new() 
            { 
                Key = "api-timeout-seconds", 
                Description = "Timeout for API requests in seconds", 
                Type = typeof(int), 
                DefaultValue = 30, 
                Category = "Advanced"
            },
            ["retry-attempts"] = new() 
            { 
                Key = "retry-attempts", 
                Description = "Number of retry attempts for failed operations", 
                Type = typeof(int), 
                DefaultValue = 3, 
                Category = "Advanced"
            },
            ["verbose-logging"] = new() 
            { 
                Key = "verbose-logging", 
                Description = "Enable detailed logging output", 
                Type = typeof(bool), 
                DefaultValue = false, 
                Category = "Advanced"
            },
            ["enable-local-cache"] = new() 
            { 
                Key = "enable-local-cache", 
                Description = "Skip downloads for files that already exist with adequate quality", 
                Type = typeof(bool), 
                DefaultValue = true, 
                Category = "Advanced"
            },

            // Lidarr Integration
            ["lidarr-url"] = new() 
            { 
                Key = "lidarr-url", 
                Description = "Lidarr server URL (e.g., http://localhost:8686)", 
                Type = typeof(string), 
                Category = "Lidarr Integration",
                IsRequired = false
            },
            ["lidarr-api-key"] = new() 
            { 
                Key = "lidarr-api-key", 
                Description = "Lidarr API key for authentication", 
                Type = typeof(string), 
                Category = "Lidarr Integration",
                IsRequired = false,
                IsSensitive = true
            },
            ["lidarr-timeout-seconds"] = new() 
            { 
                Key = "lidarr-timeout-seconds", 
                Description = "API request timeout for Lidarr in seconds", 
                Type = typeof(int), 
                DefaultValue = 30, 
                Category = "Lidarr Integration"
            },
            ["lidarr-default-export-format"] = new() 
            { 
                Key = "lidarr-default-export-format", 
                Description = "Default export format for wanted albums", 
                Type = typeof(string),
                DefaultValue = "json", 
                AllowedValues = new List<string> { "json", "txt", "csv" }, 
                Category = "Lidarr Integration"
            },
            ["lidarr-default-sort-order"] = new() 
            { 
                Key = "lidarr-default-sort-order", 
                Description = "Default sort order for exported albums", 
                Type = typeof(string),
                DefaultValue = "release_date_desc", 
                AllowedValues = new List<string> { "release_date_desc", "release_date_asc", "artist_name", "album_name", "track_count_desc", "track_count_asc", "album_type", "random" }, 
                Category = "Lidarr Integration"
            },
            ["lidarr-default-export-limit"] = new() 
            { 
                Key = "lidarr-default-export-limit", 
                Description = "Default limit for number of albums to export (0 = no limit)", 
                Type = typeof(int), 
                DefaultValue = 0, 
                Category = "Lidarr Integration"
            },
            ["lidarr-auto-download-after-export"] = new() 
            { 
                Key = "lidarr-auto-download-after-export", 
                Description = "Enable automatic download after export", 
                Type = typeof(bool), 
                DefaultValue = false, 
                Category = "Lidarr Integration"
            },
            ["lidarr-default-filter-mode"] = new() 
            { 
                Key = "lidarr-default-filter-mode", 
                Description = "Default filter mode for export operations", 
                Type = typeof(string),
                DefaultValue = "and", 
                AllowedValues = new List<string> { "and", "or" }, 
                Category = "Lidarr Integration"
            },
            ["lidarr-enable-pre-download-validation"] = new() 
            { 
                Key = "lidarr-enable-pre-download-validation", 
                Description = "Enable validation of albums before adding to queue", 
                Type = typeof(bool), 
                DefaultValue = true, 
                Category = "Lidarr Integration"
            },
            ["lidarr-default-album-types"] = new() 
            { 
                Key = "lidarr-default-album-types", 
                Description = "Default album types to include in exports (comma-separated)", 
                Type = typeof(string), 
                DefaultValue = "album", 
                Category = "Lidarr Integration"
            },
            ["lidarr-default-min-year"] = new() 
            { 
                Key = "lidarr-default-min-year", 
                Description = "Default minimum year filter (0 = no filter)", 
                Type = typeof(int), 
                DefaultValue = 0, 
                Category = "Lidarr Integration"
            },
            ["lidarr-default-max-year"] = new() 
            { 
                Key = "lidarr-default-max-year", 
                Description = "Default maximum year filter (0 = no filter)", 
                Type = typeof(int), 
                DefaultValue = 0, 
                Category = "Lidarr Integration"
            },
            ["lidarr-default-min-tracks"] = new() 
            { 
                Key = "lidarr-default-min-tracks", 
                Description = "Default minimum track count filter (0 = no filter)", 
                Type = typeof(int), 
                DefaultValue = 0, 
                Category = "Lidarr Integration"
            },
            ["lidarr-enable-export-caching"] = new() 
            { 
                Key = "lidarr-enable-export-caching", 
                Description = "Cache exported data locally to avoid re-querying Lidarr", 
                Type = typeof(bool), 
                DefaultValue = true, 
                Category = "Lidarr Integration"
            },
            ["lidarr-cache-expiry-hours"] = new() 
            { 
                Key = "lidarr-cache-expiry-hours", 
                Description = "Cache expiry time in hours", 
                Type = typeof(int), 
                DefaultValue = 24, 
                Category = "Lidarr Integration"
            },
            ["lidarr-generate-download-reports"] = new() 
            { 
                Key = "lidarr-generate-download-reports", 
                Description = "Generate reports after batch downloads", 
                Type = typeof(bool), 
                DefaultValue = true, 
                Category = "Lidarr Integration"
            },
            ["lidarr-default-report-format"] = new() 
            { 
                Key = "lidarr-default-report-format", 
                Description = "Default report format for download reports", 
                Type = typeof(string),
                DefaultValue = "html", 
                AllowedValues = new List<string> { "html", "text", "json" }, 
                Category = "Lidarr Integration"
            }
        };
    }

    private static string ToPascalCase(string kebabCase)
    {
        return string.Join("", kebabCase.Split('-').Select(word => 
            char.ToUpper(word[0]) + word.Substring(1).ToLower()));
    }

    private static string ToKebabCase(string pascalCase)
    {
        return string.Concat(pascalCase.Select((x, i) => i > 0 && char.IsUpper(x) ? "-" + x : x.ToString())).ToLower();
    }
}