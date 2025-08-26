using System.ComponentModel;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Indexers;
using Lidarr.Plugin.Common.Base;
using Lidarr.Plugin.Common.Interfaces;

namespace Lidarr.Plugin.Tidalarr.Settings
{
    /// <summary>
    /// Settings for the Tidalarr plugin.
    /// Demonstrates how to extend BaseStreamingSettings with Tidal-specific configuration.
    /// Only ~50 lines of code needed thanks to shared library!
    /// </summary>
    public class TidalSettings : BaseStreamingSettings, IIndexerSettings
    {
        public TidalSettings()
        {
            // Set Tidal-specific defaults
            BaseUrl = "https://api.tidalhifi.com/v1";
            SearchLimit = 100;
            CountryCode = "US";
            ApiRateLimit = 100; // Tidal allows higher rate limits
        }

        // === TIDAL-SPECIFIC AUTHENTICATION ===
        
        [FieldDefinition(50, Label = "Tidal API Token", Type = FieldType.Password, Privacy = PrivacyLevel.Password, 
                        Section = "Authentication", HelpText = "Your Tidal API access token. Get this from https://developer.tidal.com")]
        public string TidalApiToken { get; set; }

        [FieldDefinition(51, Label = "Tidal Country Market", Type = FieldType.Textbox, 
                        Section = "Authentication", HelpText = "Your Tidal market/country code (e.g., US, GB, DE). Must match your Tidal subscription region.")]
        public string TidalMarket { get; set; } = "US";

        [FieldDefinition(52, Label = "Tidal Subscription Tier", Type = FieldType.Select, SelectOptions = typeof(TidalSubscriptionTier),
                        Section = "Authentication", HelpText = "Your Tidal subscription level. Determines available audio quality.")]
        public int SubscriptionTier { get; set; } = (int)TidalSubscriptionTier.HiFi;

        // === TIDAL-SPECIFIC SEARCH OPTIONS ===

        [FieldDefinition(60, Label = "Include MQA", Type = FieldType.Checkbox,
                        Section = "Search", HelpText = "Include MQA (Master Quality Authenticated) albums in search results. Requires Tidal HiFi Plus subscription.")]
        public bool IncludeMqa { get; set; } = true;

        [FieldDefinition(61, Label = "Include 360 Reality Audio", Type = FieldType.Checkbox,
                        Section = "Search", Advanced = true, HelpText = "Include Sony 360 Reality Audio albums. Requires compatible hardware.")]
        public bool Include360Audio { get; set; } = false;

        // === OVERRIDE BASE VALIDATION ===

        public override bool IsValid(out string errorMessage)
        {
            // Call base validation first
            if (!base.IsValid(out errorMessage))
                return false;

            // Add Tidal-specific validation
            if (string.IsNullOrWhiteSpace(TidalApiToken))
            {
                errorMessage = "Tidal API Token is required. Get one from https://developer.tidal.com";
                return false;
            }

            if (string.IsNullOrWhiteSpace(TidalMarket) || TidalMarket.Length != 2)
            {
                errorMessage = "Valid Tidal market code is required (e.g., 'US', 'GB')";
                return false;
            }

            return true;
        }

        public override object GetMaskedForLogging()
        {
            var baseLogging = base.GetMaskedForLogging();
            return new
            {
                baseLogging,
                TidalApiToken = string.IsNullOrEmpty(TidalApiToken) ? "[not set]" : "[MASKED]",
                TidalMarket,
                SubscriptionTier = (TidalSubscriptionTier)SubscriptionTier,
                IncludeMqa,
                Include360Audio
            };
        }
    }

    /// <summary>
    /// Tidal subscription tiers that determine available features and quality.
    /// </summary>
    public enum TidalSubscriptionTier
    {
        [Description("Free")]
        Free = 0,

        [Description("Premium")]  
        Premium = 1,

        [Description("HiFi")]
        HiFi = 2,

        [Description("HiFi Plus")]
        HiFiPlus = 3
    }
}