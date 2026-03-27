using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Abstractions.Contracts;
using Lidarr.Plugin.Common.Extensions;
using Lidarr.Plugin.Common.Hosting;
using Lidarr.Plugin.Common.Services.Registration;
using Lidarr.Plugin.Qobuzarr.API;
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

        // IQobuzApiClient is NOT registered here — it requires the full Lidarr host DI
        // (NLog Logger, IHttpClient, ICacheManager). The bridge DI container cannot provide these.
        // CreateIndexerAsync returns null when the API client is unavailable.
        // Once host bridge integration is complete, the API client will be injectable.
    }

    /// <inheritdoc />
    protected override ValueTask<IIndexer?> CreateIndexerAsync(
        QobuzarrStreamingSettings settings,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        // IQobuzApiClient requires the full Lidarr host DI container (NLog, IHttpClient, ICacheManager).
        // In the bridge/sandbox context it is not available. Return null until host integration is wired.
        var apiClient = services.GetService<IQobuzApiClient>();
        if (apiClient is null)
        {
            var logger = services.GetRequiredService<ILogger<QobuzarrStreamingPlugin>>();
            logger.LogInformation("IQobuzApiClient not available in bridge context; indexer creation deferred to host integration.");
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
                Description = "Qobuz account email address for authentication.",
                DataType = SettingDataType.String,
                IsRequired = true
            },
            new()
            {
                Key = "Password",
                DisplayName = "Password",
                Description = "Qobuz account password.",
                DataType = SettingDataType.Password,
                IsRequired = true
            },
            new()
            {
                Key = "DownloadPath",
                DisplayName = "Download Path",
                Description = "Root folder where downloaded music will be saved.",
                DataType = SettingDataType.String,
                IsRequired = true
            },
            new()
            {
                Key = "PreferredQuality",
                DisplayName = "Preferred Quality",
                Description = "Audio quality preference (5=MP3-320, 6=FLAC-CD, 7=FLAC-Hi-Res, 27=FLAC-Max).",
                DataType = SettingDataType.Integer,
                DefaultValue = 6
            },
            new()
            {
                Key = "CountryCode",
                DisplayName = "Country Code",
                Description = "Two-letter ISO country code (e.g., US, CA, GB).",
                DataType = SettingDataType.String,
                DefaultValue = "US"
            },
            new()
            {
                Key = "SearchLimit",
                DisplayName = "Search Limit",
                Description = "Maximum results per search query (10-500).",
                DataType = SettingDataType.Integer,
                DefaultValue = 100
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
