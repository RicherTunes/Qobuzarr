using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Abstractions.Contracts;
using Lidarr.Plugin.Common.Extensions;
using Lidarr.Plugin.Common.Hosting;
using Lidarr.Plugin.Common.Services.Registration;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Integration.Bridge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lidarr.Plugin.Qobuzarr.Integration;

/// <summary>
/// Bridge-based streaming plugin entry point for Qobuzarr.
/// Extends <see cref="StreamingPlugin{TModule, TSettings}"/> which handles manifest loading,
/// DI bootstrapping, and the ISettingsProvider bridge automatically.
/// </summary>
public sealed class QobuzarrStreamingPlugin : StreamingPlugin<QobuzarrStreamingModule, QobuzarrStreamingSettings>
{
    /// <inheritdoc />
    protected override void ConfigureServices(IServiceCollection services, IPluginContext context, QobuzarrStreamingSettings settings)
    {
        // Register bridge defaults (auth failure handler, status reporter, rate limit reporter).
        // AddBridgeDefaults uses TryAdd, so custom registrations added before this call take precedence.
        services.AddBridgeDefaults();

        // Register the bridge-compatible API client backed by System.Net.Http.HttpClient.
        // This replaces the full QobuzApiClient which requires NLog, IHttpClient, ICacheManager from the host.
        services.AddSingleton<HttpClient>(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        services.AddSingleton<IQobuzApiClient, BridgeQobuzApiClient>();
    }

    /// <inheritdoc />
    protected override ValueTask<IIndexer?> CreateIndexerAsync(
        QobuzarrStreamingSettings settings,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var apiClient = services.GetService<IQobuzApiClient>();
        if (apiClient is null)
        {
            var pluginLogger = services.GetRequiredService<ILogger<QobuzarrStreamingPlugin>>();
            pluginLogger.LogWarning("IQobuzApiClient not available; indexer creation skipped.");
            return ValueTask.FromResult<IIndexer?>(null);
        }

        var statusReporter = services.GetRequiredService<IIndexerStatusReporter>();
        var adapterLogger = services.GetRequiredService<ILogger<QobuzIndexerAdapter>>();
        return ValueTask.FromResult<IIndexer?>(new QobuzIndexerAdapter(apiClient, statusReporter, adapterLogger, settings));
    }

    /// <inheritdoc />
    protected override ValueTask<IDownloadClient?> CreateDownloadClientAsync(
        QobuzarrStreamingSettings settings,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        // Download client adaptation is deferred to a future bridge slice.
        return ValueTask.FromResult<IDownloadClient?>(null);
    }

    /// <inheritdoc />
    protected override IEnumerable<SettingDefinition> DescribeSettings()
    {
        return new SettingDefinition[]
        {
            new()
            {
                Key = "Email",
                DisplayName = "Email Address",
                Description = "Qobuz account email address (same email you use to sign in at qobuz.com). A paid subscription is required — Studio Premier or Sublime tier for streaming, no free tier.",
                DataType = SettingDataType.String,
                IsRequired = true
            },
            new()
            {
                Key = "Password",
                DisplayName = "Password",
                Description = "Your Qobuz account password — the same one you use to log in at qobuz.com. NOT an API key or app secret.",
                DataType = SettingDataType.Password,
                IsRequired = true
            },
            new()
            {
                Key = "DownloadPath",
                DisplayName = "Download Path",
                Description = "Root folder where downloaded music will be saved. Must be writable by the Lidarr process. Examples: /downloads/qobuz (Docker), C:\\Music\\Qobuz (Windows), /Volumes/Music/Qobuz (macOS). Lidarr organizes files into Artist/Album subfolders under this root.",
                DataType = SettingDataType.String,
                IsRequired = true
            },
            new()
            {
                Key = "PreferredQuality",
                DisplayName = "Preferred Quality",
                Description = "Preferred audio quality. Codes: 5=MP3-320kbps, 6=CD-quality FLAC (16-bit/44.1kHz), 7=Hi-Res FLAC (24-bit/96kHz), 27=Max FLAC (24-bit/192kHz). Your Qobuz subscription tier may not include all qualities — the plugin falls back to the highest you have access to. Default 6 (CD-quality FLAC) works on every paid tier that includes lossless.",
                DataType = SettingDataType.Integer,
                DefaultValue = 6
            },
            new()
            {
                Key = "CountryCode",
                DisplayName = "Country Code",
                Description = "Two-letter ISO country code matching your Qobuz account region (e.g., US, CA, GB, DE, FR, JP). This determines which catalog you see — set this WRONG and you'll get tracks unavailable in your region. Most users should use the country code Qobuz showed during signup.",
                DataType = SettingDataType.String,
                DefaultValue = "US"
            },
            new()
            {
                Key = "SearchLimit",
                DisplayName = "Search Limit",
                Description = "Maximum results returned per search query. Range 10-500, default 100. Higher values give Lidarr more candidates to choose from but slow each search. Lower values speed up first-result-back but may miss matches on common-named albums.",
                DataType = SettingDataType.Integer,
                DefaultValue = 100
            },
            new()
            {
                Key = "AppId",
                DisplayName = "App ID",
                Description = "Qobuz API App ID. Optional -- falls back to QOBUZ_APP_ID environment variable or built-in default.",
                DataType = SettingDataType.String,
                IsRequired = false
            },
            new()
            {
                Key = "AppSecret",
                DisplayName = "App Secret",
                Description = "Qobuz API App Secret. Optional -- falls back to QOBUZ_APP_SECRET environment variable. Required for streaming URLs.",
                DataType = SettingDataType.Password,
                IsRequired = false
            }
        };
    }

    /// <inheritdoc />
    protected override void ConfigureDefaults(QobuzarrStreamingSettings settings)
    {
        settings.Email = string.Empty;
        settings.Password = string.Empty;
        settings.DownloadPath = string.Empty;
        settings.PreferredQuality = 6;
        settings.CountryCode = "US";
        settings.SearchLimit = 100;
        settings.AppId = string.Empty;
        settings.AppSecret = string.Empty;
    }

    /// <inheritdoc />
    protected override PluginValidationResult ValidateSettings(QobuzarrStreamingSettings settings)
    {
        if (settings.IsValid(out var errorMessage))
        {
            return PluginValidationResult.Success();
        }

        return PluginValidationResult.Failure(new[] { errorMessage });
    }
}
