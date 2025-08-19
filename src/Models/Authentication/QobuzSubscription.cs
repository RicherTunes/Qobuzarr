using System;
using Newtonsoft.Json;

namespace Lidarr.Plugin.Qobuzarr.Models.Authentication
{
    public class QobuzSubscription
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("is_hires")]
        public bool IsHiRes { get; set; }

        [JsonProperty("max_sample_rate")]
        public int MaxSampleRate { get; set; }

        [JsonProperty("max_bit_depth")]
        public int MaxBitDepth { get; set; }

        [JsonProperty("can_stream")]
        public bool CanStream { get; set; }

        [JsonProperty("can_download")]
        public bool CanDownload { get; set; }

        [JsonProperty("subscription_type")]
        public string SubscriptionType { get; set; }

        [JsonProperty("expiry_date")]
        public DateTime? ExpiryDate { get; set; }

        /// <summary>
        /// Check if the subscription supports the specified quality
        /// </summary>
        public bool SupportsQuality(int formatId)
        {
            return formatId switch
            {
                5 => true, // MP3 320 - supported by all tiers
                6 => true, // FLAC 16/44.1 - supported by all tiers
                7 => IsHiRes && MaxSampleRate >= 96000, // FLAC 24/96
                27 => IsHiRes && MaxSampleRate >= 192000, // FLAC 24/192
                _ => false
            };
        }

        /// <summary>
        /// Get the maximum supported format ID
        /// </summary>
        public int GetMaxFormatId()
        {
            if (IsHiRes && MaxSampleRate >= 192000)
                return 27; // FLAC 24/192

            if (IsHiRes && MaxSampleRate >= 96000)
                return 7; // FLAC 24/96

            return 6; // FLAC 16/44.1 (CD Quality)
        }

        /// <summary>
        /// Get a human-readable description of the subscription tier
        /// </summary>
        public string GetTierDescription()
        {
            if (IsHiRes && MaxSampleRate >= 192000)
                return "Sublime (Hi-Res up to 24-bit/192kHz)";

            if (IsHiRes && MaxSampleRate >= 96000)
                return "Studio Premier (Hi-Res up to 24-bit/96kHz)";

            return "Studio (CD Quality)";
        }
    }
}