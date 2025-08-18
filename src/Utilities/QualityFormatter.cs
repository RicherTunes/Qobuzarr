using Lidarr.Plugin.Qobuzarr.Core;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    /// <summary>
    /// Provides human-readable formatting for Qobuz quality IDs
    /// This class maintains backward compatibility with int-based quality IDs
    /// while delegating to the enum-based implementation
    /// </summary>
    public static class QualityFormatter
    {
        /// <summary>
        /// Gets the full human-readable name for a quality ID
        /// </summary>
        public static string GetQualityName(int qualityId)
        {
            if (QobuzQuality.TryParse(qualityId, out var quality))
            {
                return quality.GetDisplayName();
            }
            return $"Quality {qualityId}";
        }

        /// <summary>
        /// Gets the short human-readable name for a quality ID
        /// </summary>
        public static string GetShortQualityName(int qualityId)
        {
            if (QobuzQuality.TryParse(qualityId, out var quality))
            {
                return quality.GetShortName();
            }
            return $"Q{qualityId}";
        }

        /// <summary>
        /// Formats a quality fallback message
        /// </summary>
        public static string FormatQualityFallback(int actualQuality, int preferredQuality)
        {
            if (QobuzQuality.TryParse(actualQuality, out var actual) && 
                QobuzQuality.TryParse(preferredQuality, out var preferred))
            {
                return actual.FormatFallbackMessage(preferred);
            }
            
            // Fallback for unknown quality IDs
            return $"Q{actualQuality} (requested Q{preferredQuality})";
        }
    }
}