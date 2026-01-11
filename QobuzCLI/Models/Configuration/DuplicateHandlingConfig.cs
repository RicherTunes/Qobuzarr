using Lidarr.Plugin.Common.Security;
using Newtonsoft.Json;

namespace QobuzCLI.Models.Configuration
{
    /// <summary>
    /// Configuration settings for handling duplicate files and quality upgrades.
    /// </summary>
    public class DuplicateHandlingConfig
    {
        [JsonProperty("enableDuplicateDetection")]
        public bool EnableDuplicateDetection { get; set; } = true;
        
        [JsonProperty("enableQualityUpgrades")]
        public bool EnableQualityUpgrades { get; set; } = true;
        
        [JsonProperty("minQualityDifferencePercent")]
        public double MinQualityDifferencePercent { get; set; } = 20.0;
        
        [JsonProperty("keepReplacedFiles")]
        public bool KeepReplacedFiles { get; set; } = false;
        
        [JsonProperty("replacedFilesSuffix")]
        public string ReplacedFilesSuffix { get; set; } = ".replaced";

        /// <summary>
        /// Validates that quality difference percentage is within reasonable bounds
        /// </summary>
        public void ValidateQualityDifference()
        {
            MinQualityDifferencePercent = Math.Max(0, Math.Min(100, MinQualityDifferencePercent));
        }

        /// <summary>
        /// Checks if the replaced files suffix is valid for file system use
        /// </summary>
        public bool IsValidReplacedFilesSuffix()
        {
            if (string.IsNullOrEmpty(ReplacedFilesSuffix))
                return false;

            // Valid if sanitization doesn't change the suffix (no invalid chars removed)
            return Sanitize.PathSegment(ReplacedFilesSuffix) == ReplacedFilesSuffix;
        }
    }
}