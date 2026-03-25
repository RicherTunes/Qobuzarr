using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Abstractions.Contracts;
using Lidarr.Plugin.Abstractions.Manifest;
using Microsoft.Extensions.Logging;

namespace Lidarr.Plugin.Qobuzarr.Integration;

/// <summary>
/// Entry point for the Qobuzarr plugin under the Common library's IPlugin contract.
/// Enables PluginSandbox-based runtime testing and future host integration.
/// </summary>
public sealed class QobuzarrPlugin : IPlugin
{
    private IPluginContext? _context;

    public PluginManifest Manifest
    {
        get
        {
            try
            {
                string baseDir = AppContext.BaseDirectory;
                string manifestPath = Path.Combine(baseDir, "plugin.json");
                return PluginManifest.Load(manifestPath);
            }
            catch
            {
                // Fallback minimal manifest to satisfy hosts/tests if plugin.json is not adjacent
                return new PluginManifest
                {
                    Id = "qobuzarr",
                    Name = "Qobuzarr",
                    Version = "0.0.14",
                    ApiVersion = "1.x",
                    RequiredSettings = ["DownloadPath", "Email", "Password"]
                };
            }
        }
    }

    public ISettingsProvider SettingsProvider { get; }

    public QobuzarrPlugin()
    {
        SettingsProvider = new QobuzarrSettingsProvider();
    }

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        return ValueTask.CompletedTask;
    }

    public ValueTask<IIndexer?> CreateIndexerAsync(CancellationToken cancellationToken = default)
    {
        // Indexer creation requires full Lidarr host context; return null from sandbox
        return ValueTask.FromResult<IIndexer?>(null);
    }

    public ValueTask<IDownloadClient?> CreateDownloadClientAsync(CancellationToken cancellationToken = default)
    {
        // Download client creation requires full Lidarr host context; return null from sandbox
        return ValueTask.FromResult<IDownloadClient?>(null);
    }

    public ValueTask DisposeAsync()
    {
        _context = null;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Settings provider for Qobuzarr plugin. Exposes indexer and download settings
    /// through the Common library's ISettingsProvider contract.
    /// </summary>
    private sealed class QobuzarrSettingsProvider : ISettingsProvider
    {
        public IReadOnlyCollection<SettingDefinition> Describe()
        {
            return
            [
                new SettingDefinition
                {
                    Key = "Email",
                    DisplayName = "Email Address",
                    Description = "Qobuz account email address for authentication.",
                    DataType = SettingDataType.String,
                    IsRequired = true
                },
                new SettingDefinition
                {
                    Key = "Password",
                    DisplayName = "Password",
                    Description = "Qobuz account password.",
                    DataType = SettingDataType.Password,
                    IsRequired = true
                },
                new SettingDefinition
                {
                    Key = "DownloadPath",
                    DisplayName = "Download Path",
                    Description = "Root folder where downloaded music will be saved.",
                    DataType = SettingDataType.String,
                    IsRequired = true
                },
                new SettingDefinition
                {
                    Key = "PreferredQuality",
                    DisplayName = "Preferred Quality",
                    Description = "Audio quality preference (5=MP3-320, 6=FLAC-CD, 7=FLAC-Hi-Res, 27=FLAC-Max).",
                    DataType = SettingDataType.Integer,
                    DefaultValue = 6
                },
                new SettingDefinition
                {
                    Key = "CountryCode",
                    DisplayName = "Country Code",
                    Description = "Two-letter ISO country code (e.g., US, CA, GB).",
                    DataType = SettingDataType.String,
                    DefaultValue = "US"
                },
                new SettingDefinition
                {
                    Key = "SearchLimit",
                    DisplayName = "Search Limit",
                    Description = "Maximum results per search query (10-500).",
                    DataType = SettingDataType.Integer,
                    DefaultValue = 100
                }
            ];
        }

        public IReadOnlyDictionary<string, object?> GetDefaults()
        {
            return new Dictionary<string, object?>
            {
                ["Email"] = string.Empty,
                ["Password"] = string.Empty,
                ["DownloadPath"] = string.Empty,
                ["PreferredQuality"] = 6,
                ["CountryCode"] = "US",
                ["SearchLimit"] = 100
            };
        }

        public PluginValidationResult Validate(IDictionary<string, object?> settings)
        {
            var errors = new List<string>();

            string? email = GetString(settings, "Email");
            string? password = GetString(settings, "Password");
            string? downloadPath = GetString(settings, "DownloadPath");

            if (string.IsNullOrWhiteSpace(email))
                errors.Add("Email is required.");
            if (string.IsNullOrWhiteSpace(password))
                errors.Add("Password is required.");
            if (string.IsNullOrWhiteSpace(downloadPath))
                errors.Add("Download path is required.");

            return errors.Count == 0
                ? PluginValidationResult.Success()
                : PluginValidationResult.Failure(errors);
        }

        public PluginValidationResult Apply(IDictionary<string, object?> settings)
        {
            PluginValidationResult validation = Validate(settings);
            if (!validation.IsValid)
                return validation;

            // In a full implementation this would rebuild internal service providers.
            // For sandbox testing we just validate and accept.
            return PluginValidationResult.Success();
        }

        private static string? GetString(IDictionary<string, object?> map, string key)
        {
            if (!map.TryGetValue(key, out object? value) || value is null)
                return null;

            return value switch
            {
                string s => s,
                _ => value.ToString()
            };
        }
    }
}
