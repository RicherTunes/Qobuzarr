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

        // ===== AUTHENTICATION SECTION =====
        [FieldDefinition(1, Label = "Authentication Method", Type = FieldType.Select, SelectOptions = typeof(AuthenticationMethod), HelpText = "How do you want to login to Qobuz?", Section = "Authentication")]
        public int AuthMethod { get; set; }

        [FieldDefinition(2, Label = "Email", Type = FieldType.Textbox, HelpText = "Your Qobuz account email address (for Email & Password auth)", Section = "Authentication")]
        public string Email { get; set; }

        [FieldDefinition(3, Label = "Password", Type = FieldType.Password, Privacy = PrivacyLevel.Password, HelpText = "Your Qobuz account password (for Email & Password auth)", Section = "Authentication")]
        public string Password { get; set; }

        [FieldDefinition(4, Label = "User ID", Type = FieldType.Textbox, HelpText = "Your Qobuz user ID - found in account settings (for Token auth)", Section = "Authentication")]
        public string UserId { get; set; }

        [FieldDefinition(5, Label = "Auth Token", Type = FieldType.Password, Privacy = PrivacyLevel.Password, HelpText = "Your Qobuz authentication token (for Token auth)", Section = "Authentication")]
        public string AuthToken { get; set; }

        // API Credentials - Advanced section with better explanation
        [FieldDefinition(6, Label = "Custom App ID", Type = FieldType.Textbox, Advanced = true, Section = "Advanced", HelpText = "⚡ Auto-detected from Qobuz. Only override if experiencing issues (leave empty for automatic)")]
        public string AppId { get; set; }

        [FieldDefinition(7, Label = "Custom App Secret", Type = FieldType.Textbox, Advanced = true, Section = "Advanced", HelpText = "⚡ Auto-detected from Qobuz. Must be provided with App ID (leave empty for automatic)")]
        public string AppSecret { get; set; }

        // ===== BASIC SETTINGS SECTION =====
        [FieldDefinition(8, Label = "Region", Type = FieldType.Textbox, Section = "Basic Settings", HelpText = "Your country code (US, CA, GB, FR, DE, etc.) - affects available content and pricing")]
        public string CountryCode { get; set; }

        [FieldDefinition(9, Label = "Subscription", Type = FieldType.Select, SelectOptions = typeof(QobuzSubscriptionTier), Section = "Basic Settings", HelpText = "Your Qobuz subscription level - helps optimize quality selection")]
        public int SubscriptionTier { get; set; } = (int)QobuzSubscriptionTier.Unknown;

        // ===== SEARCH & CONTENT SECTION =====
        [FieldDefinition(10, Label = "Results Per Search", Type = FieldType.Number, Section = "Search & Content", HelpText = "How many results to show per search (10-500, default: 100)")]
        public int SearchLimit { get; set; }

        [FieldDefinition(11, Label = "Include Singles", Type = FieldType.Checkbox, Section = "Search & Content", HelpText = "Show single releases in search results")]
        public bool IncludeSingles { get; set; }

        [FieldDefinition(12, Label = "Include Compilations", Type = FieldType.Checkbox, Section = "Search & Content", HelpText = "Show compilation albums in search results")]
        public bool IncludeCompilations { get; set; }

        [FieldDefinition(13, Label = "Early Releases", Type = FieldType.Number, Section = "Search & Content", HelpText = "Show albums this many days before official release (0 = disabled)")]
        public int EarlyReleaseDayLimit { get; set; }

        // ===== PERFORMANCE SECTION =====
        [FieldDefinition(15, Label = "Smart Search", Type = FieldType.Checkbox, Section = "Performance", HelpText = "🧠 Reduces API calls by ~50% by intelligently optimizing searches. Highly recommended!")]
        public bool EnableQueryIntelligence { get; set; }

        [FieldDefinition(16, Label = "ML Optimization", Type = FieldType.Checkbox, Section = "Performance", Advanced = true, HelpText = "🤖 Use AI to further optimize searches (requires Smart Search enabled)")]
        public bool EnableMLPredictions { get; set; }

        [FieldDefinition(17, Label = "ML Model", Type = FieldType.Select, SelectOptions = typeof(MLModelType), Section = "Performance", Advanced = true, HelpText = "AI model type to use for optimization")]
        public int MLModelType { get; set; } = (int)Qobuzarr.Indexers.MLModelType.Baseline;

        [FieldDefinition(20, Label = "Cache Duration", Type = FieldType.Number, Section = "Performance", HelpText = "Keep search results cached for this many minutes (0-60, default: 5)")]
        public int SearchCacheDuration { get; set; }

        // ===== ADVANCED SECTION =====
        [FieldDefinition(21, Label = "API Rate Limit", Type = FieldType.Number, Section = "Advanced", Advanced = true, HelpText = "Maximum API requests per minute (1-300, default: 60)")]
        public int ApiRateLimit { get; set; }

        [FieldDefinition(22, Label = "Connection Timeout", Type = FieldType.Number, Section = "Advanced", Advanced = true, HelpText = "Seconds to wait for API response (5-300, default: 30)")]
        public int ConnectionTimeout { get; set; }

        [FieldDefinition(24, Label = "Download Speed", Type = FieldType.Select, SelectOptions = typeof(ConcurrencyMode), Section = "Performance", HelpText = "How many searches to run simultaneously")]
        public int ConcurrencyMode { get; set; }

        [FieldDefinition(25, Label = "Fixed Speed Level", Type = FieldType.Number, Section = "Performance", Advanced = true, HelpText = "Simultaneous operations when using Fixed mode (1-16, default: 4)")]
        public int FixedConcurrencyLevel { get; set; } = 4;

        [FieldDefinition(26, Label = "Min Simultaneous", Type = FieldType.Number, Section = "Performance", Advanced = true, HelpText = "Minimum simultaneous operations for Automatic mode (1-8, default: 1)")]
        public int AdaptiveMinConcurrency { get; set; } = 1;

        [FieldDefinition(27, Label = "Max Simultaneous", Type = FieldType.Number, Section = "Performance", Advanced = true, HelpText = "Maximum simultaneous operations for Automatic mode (2-16, default: 8)")]
        public int AdaptiveMaxConcurrency { get; set; } = 8;

        [FieldDefinition(28, Label = "Target Speed (ms)", Type = FieldType.Number, Section = "Performance", Advanced = true, HelpText = "Target response time for Automatic mode (500-5000ms, default: 1000)")]
        public int AdaptiveTargetLatency { get; set; } = 1000;

        [FieldDefinition(29, Label = "Max Latency (ms)", Type = FieldType.Number, Section = "Performance", Advanced = true, HelpText = "Reduce speed if slower than this in Automatic mode (1000-10000ms, default: 5000)")]
        public int AdaptiveMaxLatency { get; set; } = 5000;
        
        // Property for backward compatibility
        public int? EarlyReleaseLimit { get => EarlyReleaseDayLimit; set => EarlyReleaseDayLimit = value ?? 0; }

        [FieldDefinition(30, Label = "Match Confidence", Type = FieldType.Number, Section = "Advanced", Advanced = true, HelpText = "How strict to be when matching albums (0.0-1.0, default: 0.8)")]
        public double MetadataMatchConfidenceThreshold { get; set; } = 0.8;

        [FieldDefinition(31, Label = "Hybrid Threshold", Type = FieldType.Number, Section = "Advanced", Advanced = true, HelpText = "When to use hybrid matching mode (0.0-1.0, default: 0.6)")]
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
        [Description("📧 Email & Password - Use your Qobuz login credentials")]
        Email = 0,
        
        [Description("🔑 User ID & Token - Use API tokens from account settings")]
        Token = 1
    }

    public enum ConcurrencyMode
    {
        [Description("🤖 Automatic - Adjusts speed based on server response")]
        Adaptive = 0,
        
        [Description("🔧 Fixed - Constant speed regardless of conditions")]
        Fixed = 1,
        
        [Description("👨‍💻 Manual - Custom control (advanced users)")]
        Manual = 2
    }

    public enum QobuzSubscriptionTier
    {
        [Description("Let Qobuzarr detect automatically")]
        Unknown = 0,
        
        [Description("Free - 30-second previews only")]
        Free = 1,
        
        [Description("Studio/Sublime - CD Quality")]
        Sublime = 2,
        
        [Description("Studio Premier - Hi-Res Audio")]
        Premier = 3
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