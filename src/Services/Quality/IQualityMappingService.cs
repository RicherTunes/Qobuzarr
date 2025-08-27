using System.Collections.Generic;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;

namespace Lidarr.Plugin.Qobuzarr.Services.Quality
{
    /// <summary>
    /// Service interface for mapping between Lidarr and Qobuz quality profiles.
    /// Extracted from QobuzQualityManager to follow Single Responsibility Principle.
    /// </summary>
    public interface IQualityMappingService
    {
        /// <summary>
        /// Maps a Lidarr quality profile to Qobuz quality.
        /// </summary>
        QobuzQuality MapLidarrQuality(LidarrQualityProfile profile);

        /// <summary>
        /// Maps individual Lidarr quality to Qobuz quality.
        /// </summary>
        QobuzQuality MapLidarrQuality(LidarrQuality quality);

        /// <summary>
        /// Gets the quality fallback chain for a given preferred quality.
        /// </summary>
        List<QobuzQuality> GetQualityFallbackChain(QobuzQuality preferred);

        /// <summary>
        /// Gets the default quality when no preference is specified.
        /// </summary>
        QobuzQuality GetDefaultQuality();

        /// <summary>
        /// Gets all available Qobuz quality formats.
        /// </summary>
        Dictionary<int, Models.QualityFormat> GetQualityFormats();
    }
}