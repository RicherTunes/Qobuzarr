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

        // Register the QobuzApiClient as a singleton if not already registered by the host.
        // Consumers are expected to set the session on the client before calling indexer methods.
        services.AddSingleton<IQobuzApiClient>(sp =>
        {
            // When running in the full Lidarr host context, the API client will be
            // provided through the host's DI container. In the bridge/sandbox context
            // we create a no-op placeholder that requires explicit session configuration.
            throw new InvalidOperationException(
                "IQobuzApiClient must be provided by the host or configured externally. " +
                "In sandbox mode, register a concrete instance before building the container.");
        });
    }

    /// <inheritdoc />
    protected override async ValueTask<IIndexer?> CreateIndexerAsync(
        QobuzarrStreamingSettings settings,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var apiClient = services.GetRequiredService<IQobuzApiClient>();
        var statusReporter = services.GetRequiredService<IIndexerStatusReporter>();
        var logger = services.GetRequiredService<ILogger<QobuzIndexerAdapter>>();

        var adapter = new QobuzIndexerAdapter(apiClient, statusReporter, logger, settings);

        var validation = await adapter.InitializeAsync(cancellationToken).ConfigureAwait(false);

        if (!validation.IsValid)
        {
            var loggerPlugin = services.GetRequiredService<ILogger<QobuzarrStreamingPlugin>>();
            loggerPlugin.LogWarning(
                "QobuzIndexerAdapter initialization returned warnings/errors: {Errors}",
                string.Join("; ", validation.Errors));
        }

        return adapter;
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
