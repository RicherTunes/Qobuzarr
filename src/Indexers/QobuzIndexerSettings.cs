using System;
using System.Collections.Generic;
using System.ComponentModel;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Validation;
using NzbDrone.Common.Extensions;
using Lidarr.Plugin.Qobuzarr.Configuration;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    public class QobuzIndexerSettings : IIndexerSettings
    {
        private static readonly QobuzIndexerSettingsValidator Validator = new QobuzIndexerSettingsValidator();

        public QobuzIndexerSettings()
        {
            BaseUrl = QobuzConstants.Api.BaseUrl;
            SearchLimit = 100;
            ConnectionTimeout = 30;
            ApiRateLimit = 60;
            SearchCacheDuration = 5;
            // App ID and Secret are optional - will be fetched automatically from Qobuz web player when empty
            AppId = string.Empty;
            AppSecret = string.Empty;
            CountryCode = "US";
            EnableQueryIntelligence = true; // Default to enabled for API savings
            ConcurrencyMode = (int)Lidarr.Plugin.Qobuzarr.Indexers.ConcurrencyMode.Adaptive; // Initialize in constructor
            FixedConcurrencyLevel = 4;
            AdaptiveMinConcurrency = 1;
            AdaptiveMaxConcurrency = 8;
            AdaptiveTargetLatency = 1000;
            AdaptiveMaxLatency = 5000;
        }

        // Required by IIndexerSettings interface but not shown in UI
        public string BaseUrl { get; set; } = QobuzConstants.Api.BaseUrl;

        [FieldDefinition(1, Label = "Authentication Method", Type = FieldType.Select, SelectOptions = typeof(AuthenticationMethod), HelpText = "Choose authentication method")]
        public int AuthMethod { get; set; }

        [FieldDefinition(2, Label = "Email", Type = FieldType.Textbox, HelpText = "Your Qobuz email address (for email authentication)")]
        public string Email { get; set; }

        [FieldDefinition(3, Label = "Password", Type = FieldType.Password, Privacy = PrivacyLevel.Password, HelpText = "Your Qobuz password (will be MD5 hashed)")]
        public string Password { get; set; }

        [FieldDefinition(4, Label = "User ID", Type = FieldType.Textbox, HelpText = "Your Qobuz user ID (for token authentication)")]
        public string UserId { get; set; }

        [FieldDefinition(5, Label = "Auth Token", Type = FieldType.Password, Privacy = PrivacyLevel.Password, HelpText = "Your Qobuz authentication token")]
        public string AuthToken { get; set; }

        [FieldDefinition(6, Label = "App ID", Type = FieldType.Textbox, HelpText = "Qobuz App ID (optional). Leave empty to automatically fetch current credentials from Qobuz web player. Only provide if you have specific credentials to use.")]
        public string AppId { get; set; }

        [FieldDefinition(7, Label = "App Secret", Type = FieldType.Textbox, HelpText = "Qobuz App Secret (optional). Must match the App ID when provided. Leave empty to automatically fetch current credentials from Qobuz web player.")]
        public string AppSecret { get; set; }

        [FieldDefinition(8, Label = "Country Code", Type = FieldType.Textbox, HelpText = "Two-letter country code for regional content and pricing (e.g., US, CA, GB, FR, DE). Default: US")]
        public string CountryCode { get; set; }

        [FieldDefinition(10, Label = "Search Result Limit", Type = FieldType.Number, HelpText = "Maximum number of search results to return (10-500)")]
        public int SearchLimit { get; set; }

        [FieldDefinition(11, Label = "Include Singles", Type = FieldType.Checkbox, HelpText = "Include single releases in search results")]
        public bool IncludeSingles { get; set; }

        [FieldDefinition(12, Label = "Include Compilations", Type = FieldType.Checkbox, HelpText = "Include compilation albums in search results")]
        public bool IncludeCompilations { get; set; }

        [FieldDefinition(15, Label = "Enable Query Intelligence", Type = FieldType.Checkbox, HelpText = "🧠 Smart search optimization that analyzes artist/album complexity to reduce unnecessary API calls. Simple searches (like 'Taylor Swift - 1989') use 1 query instead of 3, while complex searches (compilations, special characters) preserve all queries for accuracy. Typically saves ~50% of API calls with minimal quality impact.")]
        public bool EnableQueryIntelligence { get; set; }

        [FieldDefinition(16, Label = "Enable ML Predictions (Experimental)", Type = FieldType.Checkbox, Advanced = true, HelpText = "🤖 Uses machine learning to intelligently predict the best search strategy for each artist/album combination. The system learns from successful searches and adapts over time to reduce API calls even further while maintaining accuracy. Requires 'Query Intelligence' to be enabled. Note: Initial performance may vary as the model learns your catalog patterns.")]
        public bool EnableMLPredictions { get; set; }

        [FieldDefinition(20, Label = "API Rate Limit", Type = FieldType.Number, Advanced = true, HelpText = "API requests per minute (default: 60)")]
        public int ApiRateLimit { get; set; }

        [FieldDefinition(21, Label = "Search Cache Duration", Type = FieldType.Number, Advanced = true, HelpText = "Cache search results for this many minutes (default: 5)")]
        public int SearchCacheDuration { get; set; }

        [FieldDefinition(22, Label = "Connection Timeout", Type = FieldType.Number, Advanced = true, HelpText = "Connection timeout in seconds (default: 30)")]
        public int ConnectionTimeout { get; set; }

        [FieldDefinition(23, Label = "Early Release Limit", Type = FieldType.Number, Advanced = true, HelpText = "Number of days before official release date to include early releases (default: 0)")]
        public int EarlyReleaseDayLimit { get; set; }

        [FieldDefinition(24, Label = "Concurrency Mode", Type = FieldType.Select, SelectOptions = typeof(ConcurrencyMode), Advanced = true, HelpText = "🎯 How to manage concurrent operations: 'Adaptive' automatically optimizes based on API performance (recommended), 'Fixed' uses constant concurrency, 'Manual' for advanced custom control")]
        public int ConcurrencyMode { get; set; }

        [FieldDefinition(25, Label = "[Fixed/Manual] Concurrency Level", Type = FieldType.Number, Advanced = true, HelpText = "Number of concurrent operations when using Fixed or Manual mode (1-16, default: 4). Ignored in Adaptive mode.")]
        public int FixedConcurrencyLevel { get; set; } = 4;

        [FieldDefinition(26, Label = "[Adaptive] Min Concurrency", Type = FieldType.Number, Advanced = true, HelpText = "🤖 Adaptive Mode: Minimum concurrent operations (1-8, default: 1). The system will never go below this limit.")]
        public int AdaptiveMinConcurrency { get; set; } = 1;

        [FieldDefinition(27, Label = "[Adaptive] Max Concurrency", Type = FieldType.Number, Advanced = true, HelpText = "🤖 Adaptive Mode: Maximum concurrent operations (2-16, default: 8). The system will never exceed this limit.")]
        public int AdaptiveMaxConcurrency { get; set; } = 8;

        [FieldDefinition(28, Label = "[Adaptive] Target Latency (ms)", Type = FieldType.Number, Advanced = true, HelpText = "🤖 Adaptive Mode: Target API response time (500-5000ms, default: 1000ms). System increases concurrency when latency is below this.")]
        public int AdaptiveTargetLatency { get; set; } = 1000;

        [FieldDefinition(29, Label = "[Adaptive] Max Latency (ms)", Type = FieldType.Number, Advanced = true, HelpText = "🤖 Adaptive Mode: Maximum acceptable latency (1000-10000ms, default: 5000ms). System reduces concurrency when exceeded.")]
        public int AdaptiveMaxLatency { get; set; } = 5000;
        
        public int? EarlyReleaseLimit { get; set; }

        [FieldDefinition(30, Label = "Metadata Match Confidence Threshold", Type = FieldType.Number, Advanced = true, HelpText = "Minimum confidence score for metadata matching (0.0-1.0, default: 0.8)")]
        public double MetadataMatchConfidenceThreshold { get; set; } = 0.8;

        [FieldDefinition(31, Label = "Hybrid Mode Threshold", Type = FieldType.Number, Advanced = true, HelpText = "Threshold for hybrid metadata mode activation (0.0-1.0, default: 0.6)")]
        public double HybridModeThreshold { get; set; } = 0.6;

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }

        /// <summary>
        /// Check if using email authentication
        /// </summary>
        public bool IsEmailAuth()
        {
            return AuthMethod == (int)AuthenticationMethod.Email && 
                   Email.IsNotNullOrWhiteSpace() && 
                   Password.IsNotNullOrWhiteSpace();
        }

        /// <summary>
        /// Check if using token authentication
        /// </summary>
        public bool IsTokenAuth()
        {
            return AuthMethod == (int)AuthenticationMethod.Token && 
                   UserId.IsNotNullOrWhiteSpace() && 
                   AuthToken.IsNotNullOrWhiteSpace();
        }

        /// <summary>
        /// Get App ID for API calls - optional, leave empty to automatically fetch from Qobuz web player.
        /// Falls back to environment variable if not set in settings.
        /// </summary>
        public string GetAppId()
        {
            // Use user-provided App ID if available
            if (!string.IsNullOrWhiteSpace(AppId))
                return AppId;
                
            // Try environment variable as fallback
            var envAppId = System.Environment.GetEnvironmentVariable(QobuzConstants.Authentication.AppIdEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(envAppId))
                return envAppId;
                
            // No valid credentials available
            return string.Empty;
        }

        /// <summary>
        /// Get App Secret for API calls - optional, leave empty to automatically fetch from Qobuz web player.
        /// Falls back to environment variable if not set in settings.
        /// </summary>
        public string GetAppSecret()
        {
            // Use user-provided App Secret if available
            if (!string.IsNullOrWhiteSpace(AppSecret))
                return AppSecret;
                
            // Try environment variable as fallback
            var envAppSecret = System.Environment.GetEnvironmentVariable(QobuzConstants.Authentication.AppSecretEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(envAppSecret))
                return envAppSecret;
                
            // No valid credentials available
            return string.Empty;
        }

        /// <summary>
        /// Get Country Code for API calls - validates and defaults to US if invalid
        /// </summary>
        public string GetCountryCode()
        {
            if (!string.IsNullOrWhiteSpace(CountryCode) && CountryCode.Length == 2)
                return CountryCode.ToUpperInvariant();
                
            return "US"; // Default fallback
        }

        /// <summary>
        /// Get the effective concurrency level based on the current mode
        /// </summary>
        public int GetEffectiveConcurrency()
        {
            return ConcurrencyMode switch
            {
                (int)Lidarr.Plugin.Qobuzarr.Indexers.ConcurrencyMode.Fixed => FixedConcurrencyLevel,
                (int)Lidarr.Plugin.Qobuzarr.Indexers.ConcurrencyMode.Manual => FixedConcurrencyLevel, // Use fixed value for manual mode
                _ => Math.Min(Environment.ProcessorCount / 2, AdaptiveMaxConcurrency) // Adaptive default
            };
        }

        /// <summary>
        /// Check if adaptive concurrency is enabled
        /// </summary>
        public bool IsAdaptiveConcurrencyEnabled()
        {
            return ConcurrencyMode == (int)Lidarr.Plugin.Qobuzarr.Indexers.ConcurrencyMode.Adaptive;
        }

    }

    public enum AuthenticationMethod
    {
        [Description("Email & Password")]
        Email = 0,
        
        [Description("User ID & Token")]
        Token = 1
    }

    public enum ConcurrencyMode
    {
        [Description("🤖 Adaptive (Recommended) - Automatically optimizes based on API performance")]
        Adaptive = 0,
        
        [Description("🔧 Fixed - Uses constant concurrency level")]
        Fixed = 1,
        
        [Description("👨‍💻 Manual (Advanced) - For custom implementations")]
        Manual = 2
    }


    public class QobuzIndexerSettingsValidator : AbstractValidator<QobuzIndexerSettings>
    {
        public QobuzIndexerSettingsValidator()
        {
            RuleFor(c => c.SearchLimit)
                .GreaterThanOrEqualTo(10)
                .LessThanOrEqualTo(500)
                .WithMessage("Search limit must be between 10 and 500");

            RuleFor(c => c.ConnectionTimeout)
                .GreaterThanOrEqualTo(5)
                .LessThanOrEqualTo(300)
                .WithMessage("Connection timeout must be between 5 and 300 seconds");

            RuleFor(c => c.ApiRateLimit)
                .GreaterThanOrEqualTo(1)
                .LessThanOrEqualTo(300)
                .WithMessage("API rate limit must be between 1 and 300 requests per minute");

            RuleFor(c => c.SearchCacheDuration)
                .GreaterThanOrEqualTo(0)
                .LessThanOrEqualTo(60)
                .WithMessage("Search cache duration must be between 0 and 60 minutes");

            RuleFor(c => c.CountryCode)
                .Length(2)
                .Matches(@"^[A-Z]{2}$")
                .WithMessage("Country code must be a valid 2-letter ISO country code (e.g., US, CA, GB)")
                .When(c => !string.IsNullOrWhiteSpace(c.CountryCode));

            // Note: App ID and App Secret validation is handled at runtime by the API client
            // to avoid disabling the indexer due to configuration mismatches

            // Email authentication validation
            When(c => c.AuthMethod == (int)AuthenticationMethod.Email, () =>
            {
                RuleFor(c => c.Email)
                    .NotEmpty()
                    .EmailAddress()
                    .WithMessage("Valid email address is required for email authentication");

                RuleFor(c => c.Password)
                    .NotEmpty()
                    .WithMessage("Password is required for email authentication");
            });

            // Token authentication validation
            When(c => c.AuthMethod == (int)AuthenticationMethod.Token, () =>
            {
                RuleFor(c => c.UserId)
                    .NotEmpty()
                    .WithMessage("User ID is required for token authentication");

                RuleFor(c => c.AuthToken)
                    .NotEmpty()
                    .WithMessage("Auth token is required for token authentication");
            });

            // At least one authentication method must be configured
            RuleFor(c => c)
                .Must(c => c.IsEmailAuth() || c.IsTokenAuth())
                .WithMessage("Either email/password or user ID/token authentication must be configured");

            // App ID and Secret validation - both must be provided together if custom credentials are used
            When(c => !string.IsNullOrWhiteSpace(c.AppId) && !string.IsNullOrWhiteSpace(c.AppSecret), () =>
            {
                RuleFor(c => c.AppId)
                    .Matches(@"^\d+$")
                    .WithMessage("App ID must be numeric when provided");
            });

            // Partial credential validation - warn if only one is provided
            When(c => !string.IsNullOrWhiteSpace(c.AppId) && string.IsNullOrWhiteSpace(c.AppSecret), () =>
            {
                RuleFor(c => c.AppSecret)
                    .NotEmpty()
                    .WithMessage("App Secret is required when App ID is provided. Leave both empty to use automatic defaults.");
            });

            When(c => string.IsNullOrWhiteSpace(c.AppId) && !string.IsNullOrWhiteSpace(c.AppSecret), () =>
            {
                RuleFor(c => c.AppId)
                    .NotEmpty()
                    .WithMessage("App ID is required when App Secret is provided. Leave both empty to use automatic defaults.");
            });

            // ML Predictions requires Query Intelligence to be enabled
            When(c => c.EnableMLPredictions, () =>
            {
                RuleFor(c => c.EnableQueryIntelligence)
                    .Equal(true)
                    .WithMessage("ML Predictions requires Query Intelligence to be enabled. Enable 'Query Intelligence' first.");
            });

            // Concurrency validation
            RuleFor(c => c.FixedConcurrencyLevel)
                .InclusiveBetween(1, 16)
                .WithMessage("Fixed concurrency level must be between 1 and 16");

            RuleFor(c => c.AdaptiveMinConcurrency)
                .InclusiveBetween(1, 8)
                .WithMessage("Adaptive minimum concurrency must be between 1 and 8");

            RuleFor(c => c.AdaptiveMaxConcurrency)
                .InclusiveBetween(2, 16)
                .WithMessage("Adaptive maximum concurrency must be between 2 and 16");

            // Adaptive max must be greater than min
            RuleFor(c => c.AdaptiveMaxConcurrency)
                .GreaterThan(c => c.AdaptiveMinConcurrency)
                .WithMessage("Adaptive maximum concurrency must be greater than minimum concurrency");

            RuleFor(c => c.AdaptiveTargetLatency)
                .InclusiveBetween(500, 5000)
                .WithMessage("Target latency must be between 500 and 5000 milliseconds");

            RuleFor(c => c.AdaptiveMaxLatency)
                .InclusiveBetween(1000, 10000)
                .WithMessage("Maximum latency threshold must be between 1000 and 10000 milliseconds");

            // Max latency must be greater than target latency
            RuleFor(c => c.AdaptiveMaxLatency)
                .GreaterThan(c => c.AdaptiveTargetLatency)
                .WithMessage("Maximum latency threshold must be greater than target latency");

        }
    }
}