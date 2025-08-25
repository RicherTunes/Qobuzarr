using System;
using System.Collections.Generic;
using System.ComponentModel;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Validation;
using NzbDrone.Common.Extensions;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Security;

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
            QueryOptimizationMode = (int)Qobuzarr.Indexers.QueryOptimizationMode.QueryIntelligence; // Default to Query Intelligence
            ConcurrencyMode = (int)Lidarr.Plugin.Qobuzarr.Indexers.ConcurrencyMode.Adaptive; // Initialize in constructor
            FixedConcurrencyLevel = 4;
            AdaptiveMinConcurrency = 1;
            AdaptiveMaxConcurrency = 8;
            AdaptiveTargetLatency = 1000;
            AdaptiveMaxLatency = 5000;
        }

        // Required by IIndexerSettings interface but not shown in UI
        public string BaseUrl { get; set; } = QobuzConstants.Api.BaseUrl;

        // === AUTHENTICATION SETTINGS ===
        [FieldDefinition(1, Label = "Authentication Method", Type = FieldType.Select, SelectOptions = typeof(AuthenticationMethod), Section = "Authentication", HelpText = "Choose how to authenticate with Qobuz. Email/Password is the standard method. Token authentication is for advanced users who have extracted their auth tokens.")]
        public int AuthMethod { get; set; }

        [FieldDefinition(2, Label = "Email Address", Type = FieldType.Textbox, Section = "Authentication", HelpText = "Your Qobuz account email address. Same email you use to log into qobuz.com. Visit https://www.qobuz.com/login to manage your account.")]
        public string Email { get; set; }

        [FieldDefinition(3, Label = "Password", Type = FieldType.Password, Privacy = PrivacyLevel.Password, Section = "Authentication", HelpText = "Your Qobuz account password. This will be securely hashed (MD5) before transmission to Qobuz servers.")]
        public string Password { get; set; }

        [FieldDefinition(4, Label = "User ID", Type = FieldType.Textbox, Section = "Authentication", Advanced = true, HelpText = "Your numeric Qobuz user ID. Only needed for token authentication. You can find this in your Qobuz web player session data.")]
        public string UserId { get; set; }

        [FieldDefinition(5, Label = "Authentication Token", Type = FieldType.Password, Privacy = PrivacyLevel.Password, Section = "Authentication", Advanced = true, HelpText = "Your Qobuz authentication token. This is an advanced option for users who have extracted their session token from the Qobuz web player.")]
        public string AuthToken { get; set; }

        [FieldDefinition(6, Label = "App ID", Type = FieldType.Textbox, Section = "Authentication", Advanced = true, HelpText = "Qobuz API Application ID. Leave empty for automatic detection from Qobuz web player. Only set this if you have custom API credentials.")]
        public string AppId { get; set; }

        [FieldDefinition(7, Label = "App Secret", Type = FieldType.Textbox, Section = "Authentication", Advanced = true, HelpText = "Qobuz API Application Secret. Must be provided together with App ID. Leave empty for automatic detection.")]
        public string AppSecret { get; set; }

        [FieldDefinition(8, Label = "Country/Region", Type = FieldType.Textbox, Section = "Authentication", HelpText = "Your country code (e.g., US, CA, GB, FR, DE, JP). This determines content availability and pricing. Must match your Qobuz account region.")]
        public string CountryCode { get; set; }

        // === SEARCH SETTINGS ===
        [FieldDefinition(10, Label = "Maximum Search Results", Type = FieldType.Number, Section = "Search", HelpText = "How many results to fetch per search query. Higher values may find more obscure releases but use more API calls. Range: 10-500, Default: 100")]
        public int SearchLimit { get; set; }

        [FieldDefinition(11, Label = "Include Singles & EPs", Type = FieldType.Checkbox, Section = "Search", HelpText = "Search for singles and EPs in addition to full albums. Useful for finding exclusive tracks not on albums.")]
        public bool IncludeSingles { get; set; }

        [FieldDefinition(12, Label = "Include Compilations", Type = FieldType.Checkbox, Section = "Search", HelpText = "Search for compilation and 'Various Artists' albums. Useful for soundtracks and greatest hits collections.")]
        public bool IncludeCompilations { get; set; }

        // === OPTIMIZATION SETTINGS ===
        [FieldDefinition(15, Label = "Query Optimization", Type = FieldType.Select, SelectOptions = typeof(QueryOptimizationMode), Section = "Performance", HelpText = "🧠 Reduces API calls by intelligently optimizing search queries. 'Query Intelligence' uses pattern analysis (saves ~35% API calls). 'ML Prediction' uses machine learning (saves ~49% API calls). Default: Query Intelligence")]
        public int QueryOptimizationMode { get; set; } = (int)Qobuzarr.Indexers.QueryOptimizationMode.QueryIntelligence;

        [FieldDefinition(16, Label = "ML Model Type", Type = FieldType.Select, SelectOptions = typeof(MLModelType), Section = "Performance", Advanced = true, HelpText = "⚠️ EXPERIMENTAL: ML model selection when using ML Prediction mode. 'Baseline' uses the pre-trained model included with the plugin. 'Personal' and 'Hybrid' require manual model training (not yet implemented).")]
        public int MLModelType { get; set; } = (int)Qobuzarr.Indexers.MLModelType.Baseline;

        // Legacy properties for backward compatibility
        [Obsolete("Use QueryOptimizationMode instead")]
        public bool EnableQueryIntelligence { get; set; }
        [Obsolete("Use QueryOptimizationMode instead")]
        public bool EnableMLPredictions { get; set; }

        // === API SETTINGS ===
        [FieldDefinition(20, Label = "API Rate Limit", Type = FieldType.Number, Section = "API", Advanced = true, HelpText = "Maximum API requests per minute. Qobuz may throttle or block if exceeded. Range: 1-300, Default: 60")]
        public int ApiRateLimit { get; set; }

        [FieldDefinition(21, Label = "Cache Duration (minutes)", Type = FieldType.Number, Section = "API", Advanced = true, HelpText = "How long to cache search results to avoid duplicate API calls. Set to 0 to disable caching. Range: 0-60, Default: 5")]
        public int SearchCacheDuration { get; set; }

        [FieldDefinition(22, Label = "Connection Timeout (seconds)", Type = FieldType.Number, Section = "API", Advanced = true, HelpText = "How long to wait for Qobuz API responses before timing out. Increase if you have a slow connection. Range: 5-300, Default: 30")]
        public int ConnectionTimeout { get; set; }

        [FieldDefinition(23, Label = "Pre-release Window (days)", Type = FieldType.Number, Section = "Search", Advanced = true, HelpText = "Include albums up to this many days before their official release date. Useful for catching early releases. Range: 0-30, Default: 0")]
        public int EarlyReleaseDayLimit { get; set; }

        // === CONCURRENCY SETTINGS ===
        [FieldDefinition(24, Label = "Concurrency Mode", Type = FieldType.Select, SelectOptions = typeof(ConcurrencyMode), Section = "Concurrency", Advanced = true, HelpText = "How to manage parallel API operations. 'Adaptive' automatically adjusts based on server response times (recommended). 'Fixed' uses a constant number of parallel operations.")]
        public int ConcurrencyMode { get; set; }

        [FieldDefinition(25, Label = "Fixed Concurrency Level", Type = FieldType.Number, Section = "Concurrency", Advanced = true, HelpText = "Number of parallel API operations when using Fixed mode. Higher = faster but may hit rate limits. Range: 1-16, Default: 4")]
        public int FixedConcurrencyLevel { get; set; } = 4;

        [FieldDefinition(26, Label = "Minimum Concurrency", Type = FieldType.Number, Section = "Concurrency", Advanced = true, HelpText = "[Adaptive Mode] Minimum parallel operations. System won't go below this even if API is slow. Range: 1-8, Default: 1")]
        public int AdaptiveMinConcurrency { get; set; } = 1;

        [FieldDefinition(27, Label = "Maximum Concurrency", Type = FieldType.Number, Section = "Concurrency", Advanced = true, HelpText = "[Adaptive Mode] Maximum parallel operations. System won't exceed this even if API is fast. Range: 2-16, Default: 8")]
        public int AdaptiveMaxConcurrency { get; set; } = 8;

        [FieldDefinition(28, Label = "Target Response Time (ms)", Type = FieldType.Number, Section = "Concurrency", Advanced = true, HelpText = "[Adaptive Mode] Ideal API response time. System increases concurrency when faster than this. Range: 500-5000ms, Default: 1000ms")]
        public int AdaptiveTargetLatency { get; set; } = 1000;

        [FieldDefinition(29, Label = "Maximum Response Time (ms)", Type = FieldType.Number, Section = "Concurrency", Advanced = true, HelpText = "[Adaptive Mode] Unacceptable API response time. System reduces concurrency when slower than this. Range: 1000-10000ms, Default: 5000ms")]
        public int AdaptiveMaxLatency { get; set; } = 5000;
        
        public int? EarlyReleaseLimit { get; set; }

        // === ADVANCED SETTINGS ===
        [FieldDefinition(30, Label = "Match Confidence Threshold", Type = FieldType.Number, Section = "Advanced", Advanced = true, HelpText = "Minimum confidence score (0-1) for accepting search results. Lower = more matches but potentially wrong albums. Higher = fewer but more accurate matches. Default: 0.8")]
        public double MetadataMatchConfidenceThreshold { get; set; } = 0.8;

        [FieldDefinition(31, Label = "Hybrid Search Threshold", Type = FieldType.Number, Section = "Advanced", Advanced = true, HelpText = "Confidence threshold (0-1) for activating hybrid search mode. When confidence is below this, additional search strategies are used. Default: 0.6")]
        public double HybridModeThreshold { get; set; } = 0.6;

        // === SUBSCRIPTION SETTINGS ===
        [FieldDefinition(32, Label = "Qobuz Subscription", Type = FieldType.Select, SelectOptions = typeof(QobuzSubscriptionTier), Section = "Subscription", HelpText = "Your Qobuz subscription level. This helps optimize quality selection - Studio Sublime users won't waste time trying Hi-Res, Premier users get Hi-Res priority. Select 'Unknown' to auto-detect.")]
        public int SubscriptionTier { get; set; } = (int)QobuzSubscriptionTier.Unknown;

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
        /// Check if query intelligence is enabled (for backward compatibility)
        /// </summary>
        public bool IsQueryIntelligenceEnabled()
        {
            // Support legacy EnableQueryIntelligence property
#pragma warning disable CS0618 // Type or member is obsolete
            if (EnableQueryIntelligence && QueryOptimizationMode == 0)
            {
                // Migrate from old property
                QueryOptimizationMode = (int)Qobuzarr.Indexers.QueryOptimizationMode.QueryIntelligence;
            }
#pragma warning restore CS0618 // Type or member is obsolete

            return QueryOptimizationMode >= (int)Qobuzarr.Indexers.QueryOptimizationMode.QueryIntelligence;
        }

        /// <summary>
        /// Check if ML predictions are enabled (for backward compatibility)
        /// </summary>
        public bool IsMLPredictionEnabled()
        {
            // Support legacy EnableMLPredictions property
#pragma warning disable CS0618 // Type or member is obsolete
            if (EnableMLPredictions && QueryOptimizationMode < (int)Qobuzarr.Indexers.QueryOptimizationMode.MLPrediction)
            {
                // Migrate from old property
                QueryOptimizationMode = (int)Qobuzarr.Indexers.QueryOptimizationMode.MLPrediction;
            }
#pragma warning restore CS0618 // Type or member is obsolete

            return QueryOptimizationMode == (int)Qobuzarr.Indexers.QueryOptimizationMode.MLPrediction;
        }

        /// <summary>
        /// Get App ID for API calls - optional, leave empty to automatically fetch from Qobuz web player.
        /// Falls back to environment variable if not set in settings.
        /// </summary>
        public string GetAppId()
        {
            // Use user-provided App ID if available
            if (!string.IsNullOrWhiteSpace(AppId))
            {
                try
                {
                    return InputSanitizer.SanitizeAppId(AppId);
                }
                catch
                {
                    // Invalid App ID, fall through to try environment variable
                }
            }
                
            // Try environment variable as fallback
            var envAppId = System.Environment.GetEnvironmentVariable(QobuzConstants.Authentication.AppIdEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(envAppId))
            {
                try
                {
                    return InputSanitizer.SanitizeAppId(envAppId);
                }
                catch
                {
                    // Invalid environment App ID
                }
            }
                
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
            try
            {
                if (!string.IsNullOrWhiteSpace(CountryCode))
                {
                    return InputSanitizer.SanitizeCountryCode(CountryCode);
                }
            }
            catch
            {
                // Invalid country code, use default
            }
                
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

    public enum QobuzSubscriptionTier
    {
        [Description("❓ Unknown - Detect automatically")]
        Unknown = 0,
        
        [Description("🆓 Free - 30-second samples only")]
        Free = 1,
        
        [Description("💿 Studio Sublime - CD Quality (16-bit/44.1kHz)")]
        Sublime = 2,
        
        [Description("🏆 Studio Premier - Hi-Res (up to 24-bit/192kHz)")]
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

            // Query optimization validation
            RuleFor(c => c.QueryOptimizationMode)
                .Must(mode => Enum.IsDefined(typeof(QueryOptimizationMode), mode))
                .WithMessage("Invalid query optimization mode selected");

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