using QobuzCLI.Models;

namespace QobuzCLI.Services;

public interface IConfigService
{
    // Primary ConfigService API
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

