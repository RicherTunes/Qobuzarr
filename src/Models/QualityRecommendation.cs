using System.Collections.Generic;

namespace Lidarr.Plugin.Qobuzarr.Models
{
    /// <summary>
    /// Represents a quality recommendation with primary quality and fallback options.
    /// </summary>
    public class QualityRecommendation
    {
        /// <summary>
        /// The primary recommended quality.
        /// </summary>
        public string PrimaryQuality { get; set; }

        /// <summary>
        /// Fallback quality options in order of preference.
        /// </summary>
        public List<string> FallbackQualities { get; set; } = new List<string>();

        /// <summary>
        /// Reason for the recommendation.
        /// </summary>
        public string Reason { get; set; }

        /// <summary>
        /// Confidence level of the recommendation (0.0 - 1.0).
        /// </summary>
        public double Confidence { get; set; } = 1.0;

        /// <summary>
        /// Whether lossless quality is preferred.
        /// </summary>
        public bool PreferLossless { get; set; }
    }
}