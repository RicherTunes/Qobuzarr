using Newtonsoft.Json;

namespace QobuzCLI.Models.Configuration
{
    /// <summary>
    /// Configuration settings for audio quality preferences and fallback behavior.
    /// </summary>
    public class QualityConfig
    {
        [JsonProperty("quality")]
        public string Quality { get; set; } = "flac-max"; // mp3-320, flac-cd, flac-hires, flac-max
        
        [JsonProperty("autoQualityFallback")]
        public bool AutoQualityFallback { get; set; } = true;
        
        [JsonProperty("qualityFallbackOrder")]
        public List<string>? QualityFallbackOrder { get; set; } = DefaultQualityFallbackOrder;

        [JsonProperty("preferredFormats")]
        public List<string>? PreferredFormats { get; set; } = DefaultPreferredFormats;

        /// <summary>
        /// Gets the default quality fallback order
        /// </summary>
        public static List<string> DefaultQualityFallbackOrder => 
            new() { "flac-max", "flac-hires", "flac-cd", "mp3-320" };

        /// <summary>
        /// Gets the default preferred formats
        /// </summary>
        public static List<string> DefaultPreferredFormats => 
            new() { "FLAC", "ALAC", "WAV", "MP3" };

        /// <summary>
        /// Gets the quality ID for API requests based on quality string
        /// </summary>
        public int GetQualityId()
        {
            return Quality.ToLower() switch
            {
                "mp3-320" => 5,
                "flac-cd" => 6,
                "flac-hires" => 7,
                "flac-max" => 27,
                _ => 27 // Default to flac-max
            };
        }

        /// <summary>
        /// Validates that the quality setting is supported
        /// </summary>
        public bool IsValidQuality()
        {
            var validQualities = new[] { "mp3-320", "flac-cd", "flac-hires", "flac-max" };
            return validQualities.Contains(Quality.ToLower());
        }
    }
}