using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Implementation of quality mapping service that maps Lidarr quality profiles to Qobuz quality levels.
    /// Provides intelligent quality selection with fallback logic and preference-based recommendations.
    /// </summary>
    public class QualityMappingService : IQualityMappingService
    {
        private readonly Logger _logger;

        // Qobuz quality hierarchy (highest to lowest quality)
        private static readonly List<string> QobuzQualityHierarchy = new List<string>
        {
            "flac-hires",   // Hi-Res lossless (up to 24-bit/192kHz)
            "flac-cd",      // CD quality lossless (16-bit/44.1kHz)
            "mp3-320"       // High quality lossy (320kbps MP3)
        };

        private static readonly Dictionary<string, string> QobuzQualityDescriptions = new Dictionary<string, string>
        {
            ["flac-hires"] = "Hi-Res FLAC (up to 24-bit/192kHz)",
            ["flac-cd"] = "CD Quality FLAC (16-bit/44.1kHz)",
            ["mp3-320"] = "High Quality MP3 (320kbps)"
        };

        // Quality mapping based on common Lidarr quality profile patterns
        private static readonly Dictionary<string, string> CommonQualityMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Hi-Res patterns
            ["Hi-Res"] = "flac-hires",
            ["HiRes"] = "flac-hires",
            ["High Resolution"] = "flac-hires",
            ["24bit"] = "flac-hires",
            ["24-bit"] = "flac-hires",
            ["192khz"] = "flac-hires",
            ["96khz"] = "flac-hires",
            ["DXD"] = "flac-hires",
            ["DSD"] = "flac-hires",

            // CD Quality patterns
            ["Lossless"] = "flac-cd",
            ["FLAC"] = "flac-cd",
            ["CD"] = "flac-cd",
            ["16bit"] = "flac-cd",
            ["16-bit"] = "flac-cd",
            ["44.1khz"] = "flac-cd",
            ["44khz"] = "flac-cd",

            // Lossy patterns
            ["MP3"] = "mp3-320",
            ["320"] = "mp3-320",
            ["Lossy"] = "mp3-320",
            ["Standard"] = "mp3-320"
        };

        public QualityMappingService(Logger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Gets the preferred Qobuz quality for a given Lidarr quality profile.
        /// </summary>
        public string GetPreferredQobuzQuality(LidarrQualityProfile qualityProfile)
        {
            if (qualityProfile == null)
            {
                _logger.Debug("No quality profile provided, using default quality");
                return GetDefaultQobuzQuality();
            }

            _logger.Debug("Mapping quality profile '{0}' (ID: {1}) to Qobuz quality", qualityProfile.Name, qualityProfile.Id);

            // First, try mapping based on profile name
            var nameBasedQuality = MapProfileNameToQuality(qualityProfile.Name);
            if (!string.IsNullOrEmpty(nameBasedQuality))
            {
                _logger.Debug("Mapped profile '{0}' to '{1}' based on name", qualityProfile.Name, nameBasedQuality);
                return nameBasedQuality;
            }

            // If name mapping fails, analyze the quality items
            var preferredQuality = qualityProfile.GetPreferredQuality();
            if (preferredQuality != null)
            {
                var mappedQuality = preferredQuality.ToQobuzQuality();
                _logger.Debug("Mapped preferred quality '{0}' to '{1}'", preferredQuality.Name, mappedQuality);
                return mappedQuality;
            }

            // Fallback to analyzing all allowed qualities
            var allowedQualities = qualityProfile.GetAllowedQualities();
            if (allowedQualities.Any())
            {
                var bestQuality = allowedQualities.First(); // Already ordered by preference
                var mappedQuality = bestQuality.ToQobuzQuality();
                _logger.Debug("Mapped best allowed quality '{0}' to '{1}'", bestQuality.Name, mappedQuality);
                return mappedQuality;
            }

            _logger.Warn("Could not determine quality mapping for profile '{0}', using default", qualityProfile.Name);
            return GetDefaultQobuzQuality();
        }

        /// <summary>
        /// Gets all supported Qobuz qualities for a given Lidarr quality profile, ordered by preference.
        /// </summary>
        public List<string> GetQualityFallbackChain(LidarrQualityProfile qualityProfile)
        {
            if (qualityProfile == null)
            {
                return new List<string> { GetDefaultQobuzQuality(), "mp3-320" };
            }

            var allowedQualities = qualityProfile.GetAllowedQualities();
            var qobuzQualities = allowedQualities.Select(q => q.ToQobuzQuality()).Distinct().ToList();

            // Ensure qualities are in the correct hierarchy order
            var orderedQualities = QobuzQualityHierarchy.Where(q => qobuzQualities.Contains(q)).ToList();

            if (!orderedQualities.Any())
            {
                _logger.Warn("No valid Qobuz qualities found for profile '{0}', using defaults", qualityProfile.Name);
                return new List<string> { GetDefaultQobuzQuality(), "mp3-320" };
            }

            _logger.Debug("Quality fallback chain for profile '{0}': {1}", qualityProfile.Name, string.Join(" -> ", orderedQualities));
            return orderedQualities;
        }

        /// <summary>
        /// Determines the best available Qobuz quality from a list of available qualities.
        /// </summary>
        public string SelectBestAvailableQuality(LidarrQualityProfile qualityProfile, List<string> availableQualities)
        {
            if (availableQualities == null || !availableQualities.Any())
            {
                _logger.Debug("No available qualities provided");
                return null;
            }

            var preferredQualities = GetQualityFallbackChain(qualityProfile);
            
            // Find the first preferred quality that's available
            foreach (var preferredQuality in preferredQualities)
            {
                if (availableQualities.Contains(preferredQuality, StringComparer.OrdinalIgnoreCase))
                {
                    _logger.Debug("Selected quality '{0}' from available options: {1}", preferredQuality, string.Join(", ", availableQualities));
                    return preferredQuality;
                }
            }

            // If no preferred quality is available, select the best from what's available
            var bestAvailable = QobuzQualityHierarchy.FirstOrDefault(q => availableQualities.Contains(q, StringComparer.OrdinalIgnoreCase));
            if (bestAvailable != null)
            {
                _logger.Debug("No preferred quality available, selected best option '{0}' from: {1}", bestAvailable, string.Join(", ", availableQualities));
                return bestAvailable;
            }

            _logger.Warn("No recognizable Qobuz qualities in available list: {0}", string.Join(", ", availableQualities));
            return availableQualities.FirstOrDefault();
        }

        /// <summary>
        /// Gets the default Qobuz quality when no specific quality profile is available.
        /// </summary>
        public string GetDefaultQobuzQuality()
        {
            return "flac-cd"; // Default to CD quality lossless
        }

        /// <summary>
        /// Validates if a Qobuz quality string is supported and recognized.
        /// </summary>
        public bool IsValidQobuzQuality(string qobuzQuality)
        {
            return !string.IsNullOrEmpty(qobuzQuality) && QobuzQualityHierarchy.Contains(qobuzQuality, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets all supported Qobuz quality formats with their descriptions.
        /// </summary>
        public Dictionary<string, string> GetSupportedQobuzQualities()
        {
            return new Dictionary<string, string>(QobuzQualityDescriptions);
        }

        /// <summary>
        /// Determines if a specific Qobuz quality meets the requirements of a Lidarr quality profile.
        /// </summary>
        public bool DoesQualityMeetProfileRequirements(LidarrQualityProfile qualityProfile, string qobuzQuality)
        {
            if (qualityProfile == null || string.IsNullOrEmpty(qobuzQuality))
            {
                return false;
            }

            var allowedQualities = GetQualityFallbackChain(qualityProfile);
            return allowedQualities.Contains(qobuzQuality, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets quality recommendations based on album metadata and quality profile.
        /// </summary>
        public QualityRecommendation GetQualityRecommendation(LidarrAlbum album, LidarrQualityProfile qualityProfile)
        {
            var recommendation = new QualityRecommendation();

            if (qualityProfile == null)
            {
                recommendation.PrimaryQuality = GetDefaultQobuzQuality();
                recommendation.FallbackQualities = new List<string> { "mp3-320" };
                recommendation.Reason = "No quality profile available, using default";
                recommendation.PreferLossless = true;
                recommendation.MinimumQuality = "mp3-320";
                return recommendation;
            }

            // Get the primary quality recommendation
            recommendation.PrimaryQuality = GetPreferredQobuzQuality(qualityProfile);
            recommendation.FallbackQualities = GetQualityFallbackChain(qualityProfile)
                .Where(q => q != recommendation.PrimaryQuality)
                .ToList();

            // Determine preferences based on profile and album characteristics
            recommendation.PreferLossless = qualityProfile.PrefersLossless();
            recommendation.MinimumQuality = GetMinimumAcceptableQuality(qualityProfile);

            // Build reasoning
            var reasonParts = new List<string>();
            reasonParts.Add($"Profile: {qualityProfile.Name}");
            
            if (album?.AlbumType != null)
            {
                reasonParts.Add($"Type: {album.AlbumType}");
            }

            if (album?.ReleaseDate.HasValue == true)
            {
                var releaseYear = album.ReleaseDate.Value.Year;
                if (releaseYear >= 2010)
                {
                    reasonParts.Add("Modern release (likely has hi-res)");
                }
                else if (releaseYear < 1990)
                {
                    reasonParts.Add("Vintage release (may prefer remastered)");
                }
            }

            recommendation.Reason = string.Join(", ", reasonParts);

            _logger.Debug("Quality recommendation for album '{0}': Primary={1}, Fallbacks=[{2}], Reason: {3}",
                album?.GetFullTitle() ?? "Unknown",
                recommendation.PrimaryQuality,
                string.Join(", ", recommendation.FallbackQualities),
                recommendation.Reason);

            return recommendation;
        }

        /// <summary>
        /// Maps a quality profile name to a Qobuz quality using common patterns.
        /// </summary>
        private string MapProfileNameToQuality(string profileName)
        {
            if (string.IsNullOrEmpty(profileName))
            {
                return null;
            }

            // Check exact matches first
            if (CommonQualityMappings.TryGetValue(profileName, out var exactMatch))
            {
                return exactMatch;
            }

            // Check partial matches
            foreach (var mapping in CommonQualityMappings)
            {
                if (profileName.Contains(mapping.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return mapping.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// Determines the minimum acceptable quality for a quality profile.
        /// </summary>
        private string GetMinimumAcceptableQuality(LidarrQualityProfile qualityProfile)
        {
            var allowedQualities = GetQualityFallbackChain(qualityProfile);
            return allowedQualities.LastOrDefault() ?? "mp3-320";
        }
    }
}