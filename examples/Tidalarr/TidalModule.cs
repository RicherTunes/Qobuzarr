using System.Collections.Generic;
using Lidarr.Plugin.Common.Services.Registration;
using Lidarr.Plugin.Tidalarr.Settings;
using Lidarr.Plugin.Tidalarr.Indexers;
using Lidarr.Plugin.Tidalarr.Download;

namespace Lidarr.Plugin.Tidalarr
{
    /// <summary>
    /// Tidalarr plugin module demonstrating shared library usage.
    /// Only ~50 lines needed vs ~200 lines in traditional plugin modules!
    /// </summary>
    public class TidalModule : StreamingPluginModule
    {
        public override string ServiceName => "Tidalarr";
        public override string Description => "High-quality music indexer and download client for Tidal streaming service";
        public override string Author => "YourName";

        /// <summary>
        /// Registers core Tidal-specific services.
        /// Shared library handles all the boilerplate DI setup.
        /// </summary>
        protected override void RegisterCoreServices()
        {
            // Register the indexer (auto-discovered by Lidarr)
            // TidalIndexer : HttpIndexerBase<TidalSettings> - automatically registered

            // Register the download client (auto-discovered by Lidarr)  
            // TidalDownloadClient : DownloadClientBase<TidalSettings> - automatically registered

            // Register settings validator
            GetSingleton<TidalSettingsValidator>(() => new TidalSettingsValidator());
        }

        /// <summary>
        /// Register Tidal-specific authentication services.
        /// </summary>
        protected override void RegisterAuthenticationServices()
        {
            // Would register TidalAuthenticationService : BaseStreamingAuthenticationService<TidalSession, TidalCredentials>
            // GetSingleton<ITidalAuthenticationService>(() => new TidalAuthenticationService());
        }

        /// <summary>
        /// Register Tidal-specific HTTP services.
        /// </summary>
        protected override void RegisterHttpServices()
        {
            // Register Tidal API client
            // GetSingleton<ITidalApiClient>(() => new TidalApiClient(_httpClient, _settings));
            
            // Register Tidal-specific cache implementation
            // GetSingleton<IStreamingResponseCache>(() => new TidalResponseCache());
        }

        /// <summary>
        /// Validates that Tidalarr-specific services are properly configured.
        /// </summary>
        protected override void ValidateCoreServices(List<string> errors, List<string> warnings)
        {
            // Validate that Tidal-specific requirements are met
            try
            {
                var settings = GetSingleton<TidalSettings>(() => new TidalSettings());
                if (!settings.IsValid(out string settingsError))
                {
                    errors.Add($"Tidal settings validation failed: {settingsError}");
                }

                if (string.IsNullOrEmpty(settings.TidalApiToken))
                {
                    warnings.Add("Tidal API token not configured - indexer will not function");
                }
            }
            catch (System.Exception ex)
            {
                errors.Add($"Failed to validate Tidal services: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets Tidal-specific required settings.
        /// </summary>
        protected override List<string> GetRequiredSettings()
        {
            var baseSettings = base.GetRequiredSettings();
            baseSettings.AddRange(new[]
            {
                "TidalApiToken",
                "TidalMarket",
                "SubscriptionTier"
            });
            return baseSettings;
        }

        /// <summary>
        /// Gets Tidal-specific supported features.
        /// </summary>
        protected override List<string> GetSupportedFeatures()
        {
            var baseFeatures = base.GetSupportedFeatures();
            baseFeatures.AddRange(new[]
            {
                "MQA Support",
                "Hi-Res Audio",
                "360 Reality Audio",
                "Explicit Content Filtering"
            });
            return baseFeatures;
        }
    }

    /// <summary>
    /// Validator for Tidal settings using FluentValidation patterns.
    /// </summary>
    public class TidalSettingsValidator
    {
        public ValidationResult Validate(TidalSettings settings)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            if (!settings.IsValid(out string baseError))
            {
                errors.Add(baseError);
            }

            // Tidal-specific validation
            if (string.IsNullOrWhiteSpace(settings.TidalApiToken))
            {
                errors.Add("Tidal API Token is required");
            }

            if (settings.TidalApiToken?.Length < 10)
            {
                warnings.Add("Tidal API Token appears to be too short");
            }

            if (settings.ApiRateLimit > 200)
            {
                warnings.Add("High API rate limit may cause throttling");
            }

            return new ValidationResult
            {
                IsValid = errors.Count == 0,
                Errors = errors,
                Warnings = warnings
            };
        }
    }
}

// Total Tidalarr module: ~50 lines vs ~150 lines traditional implementation
// Shared library provides: DI patterns, validation framework, service lifecycle
// Tidalarr provides: Service registration, Tidal-specific validation, feature definition