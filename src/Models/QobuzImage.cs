using Newtonsoft.Json;
using NzbDrone.Common.Extensions;

namespace Lidarr.Plugin.Qobuzarr.Models
{
    public class QobuzImage
    {
        [JsonProperty("small")]
        public string Small { get; set; }

        [JsonProperty("medium")]
        public string Medium { get; set; }

        [JsonProperty("large")]
        public string Large { get; set; }

        [JsonProperty("extralarge")]
        public string ExtraLarge { get; set; }

        [JsonProperty("mega")]
        public string Mega { get; set; }

        [JsonProperty("original")]
        public string Original { get; set; }

        /// <summary>
        /// Get the best quality image URL available
        /// </summary>
        public string GetBestQuality()
        {
            return Original ?? ExtraLarge ?? Mega ?? Large ?? Medium ?? Small;
        }

        /// <summary>
        /// Get image URL for specified minimum size
        /// </summary>
        public string GetByMinimumSize(int minSize)
        {
            if (minSize >= 1400 && Original.IsNotNullOrWhiteSpace())
                return Original;

            if (minSize >= 1200 && ExtraLarge.IsNotNullOrWhiteSpace())
                return ExtraLarge;

            if (minSize >= 800 && Mega.IsNotNullOrWhiteSpace())
                return Mega;

            if (minSize >= 600 && Large.IsNotNullOrWhiteSpace())
                return Large;

            if (minSize >= 300 && Medium.IsNotNullOrWhiteSpace())
                return Medium;

            return Small ?? Medium ?? Large ?? Mega ?? ExtraLarge ?? Original;
        }

        /// <summary>
        /// Check if any image URL is available
        /// </summary>
        public bool HasAnyImage()
        {
            return Small.IsNotNullOrWhiteSpace() ||
                   Medium.IsNotNullOrWhiteSpace() ||
                   Large.IsNotNullOrWhiteSpace() ||
                   ExtraLarge.IsNotNullOrWhiteSpace() ||
                   Mega.IsNotNullOrWhiteSpace() ||
                   Original.IsNotNullOrWhiteSpace();
        }
    }
}
