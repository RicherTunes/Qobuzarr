using System.Collections.Generic;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Defines the contract for mapping Lidarr quality profiles to Qobuz quality levels.
    /// Handles quality preference resolution and fallback logic for optimal download quality selection.
    /// </summary>
    public interface IQualityMappingService
    {
        /// <summary>
        /// Gets the preferred Qobuz quality for a given Lidarr quality profile.
        /// </summary>
        /// <param name="qualityProfile">The Lidarr quality profile to map.</param>
        /// <returns>The preferred Qobuz quality string (e.g., "flac-hires", "flac-cd", "mp3-320").</returns>
        string GetPreferredQobuzQuality(LidarrQualityProfile qualityProfile);

        /// <summary>
        /// Gets all supported Qobuz qualities for a given Lidarr quality profile, ordered by preference.
        /// </summary>
        /// <param name="qualityProfile">The Lidarr quality profile to map.</param>
        /// <returns>List of Qobuz quality strings ordered from most to least preferred.</returns>
        List<string> GetQualityFallbackChain(LidarrQualityProfile qualityProfile);

        /// <summary>
        /// Determines the best available Qobuz quality from a list of available qualities, 
        /// based on the Lidarr quality profile preferences.
        /// </summary>
        /// <param name="qualityProfile">The Lidarr quality profile to use for selection.</param>
        /// <param name="availableQualities">List of Qobuz qualities available for the track/album.</param>
        /// <returns>The best matching Qobuz quality, or null if no suitable quality is found.</returns>
        string SelectBestAvailableQuality(LidarrQualityProfile qualityProfile, List<string> availableQualities);

        /// <summary>
        /// Gets the default Qobuz quality when no specific quality profile is available.
        /// </summary>
        /// <returns>The default Qobuz quality string.</returns>
        string GetDefaultQobuzQuality();

        /// <summary>
        /// Validates if a Qobuz quality string is supported and recognized.
        /// </summary>
        /// <param name="qobuzQuality">The Qobuz quality string to validate.</param>
        /// <returns>True if the quality is valid and supported; false otherwise.</returns>
        bool IsValidQobuzQuality(string qobuzQuality);

        /// <summary>
        /// Gets all supported Qobuz quality formats with their descriptions.
        /// </summary>
        /// <returns>Dictionary mapping Qobuz quality strings to human-readable descriptions.</returns>
        Dictionary<string, string> GetSupportedQobuzQualities();

        /// <summary>
        /// Determines if a specific Qobuz quality meets the requirements of a Lidarr quality profile.
        /// </summary>
        /// <param name="qualityProfile">The Lidarr quality profile with requirements.</param>
        /// <param name="qobuzQuality">The Qobuz quality to evaluate.</param>
        /// <returns>True if the quality meets the profile requirements; false otherwise.</returns>
        bool DoesQualityMeetProfileRequirements(LidarrQualityProfile qualityProfile, string qobuzQuality);

        /// <summary>
        /// Gets quality recommendations based on album metadata and quality profile.
        /// Provides intelligent quality selection based on factors like release type, year, etc.
        /// </summary>
        /// <param name="album">The Lidarr album to analyze.</param>
        /// <param name="qualityProfile">The quality profile to apply.</param>
        /// <returns>Recommended quality selection strategy.</returns>
        QualityRecommendation GetQualityRecommendation(LidarrAlbum album, LidarrQualityProfile qualityProfile);
    }

    /// <summary>
    /// Represents a quality recommendation with primary choice and fallback options.
    /// </summary>
    public class QualityRecommendation
    {
        /// <summary>
        /// The primary recommended Qobuz quality.
        /// </summary>
        public string PrimaryQuality { get; set; }

        /// <summary>
        /// Alternative Qobuz qualities in order of preference.
        /// </summary>
        public List<string> FallbackQualities { get; set; } = new List<string>();

        /// <summary>
        /// Reason for the quality recommendation (for logging/debugging).
        /// </summary>
        public string Reason { get; set; }

        /// <summary>
        /// Whether lossless quality is strongly preferred for this recommendation.
        /// </summary>
        public bool PreferLossless { get; set; }

        /// <summary>
        /// Minimum acceptable quality level.
        /// </summary>
        public string MinimumQuality { get; set; }

        /// <summary>
        /// Gets all quality options (primary + fallbacks) in order.
        /// </summary>
        public List<string> GetAllQualityOptions()
        {
            var options = new List<string>();
            if (!string.IsNullOrEmpty(PrimaryQuality))
            {
                options.Add(PrimaryQuality);
            }
            options.AddRange(FallbackQualities);
            return options;
        }
    }
}