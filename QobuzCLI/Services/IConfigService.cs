using QobuzCLI.Models;
using QobuzCLI.Models.Configuration;

namespace QobuzCLI.Services;

public interface IConfigService
{
    // Modern Configuration API (QobuzConfiguration)
    Task<QobuzConfiguration> LoadConfigurationAsync();
    Task SaveConfigurationAsync(QobuzConfiguration configuration);

    // Legacy ConfigService API (QobuzConfig) - for backward compatibility
    Task<QobuzConfig> LoadConfigAsync();
    Task SaveConfigAsync(QobuzConfig config);
    Task<T> GetValueAsync<T>(string key, T defaultValue = default!);
    Task SetValueAsync<T>(string key, T value);
    Task<Dictionary<string, object>> GetAllValuesAsync();
    Task<List<Models.ConfigParameter>> GetParametersAsync();
    string GetConfigPath();

    // Legacy ConfigManager API compatibility
    Task<T> GetAsync<T>(string key, T? defaultValue = default);
    Task SetAsync<T>(string key, T value);
    Task<Dictionary<string, object>> GetAllAsync();
    Task SaveAsync();
    Task LoadAsync();
}

