namespace Lidarr.Plugin.Qobuzarr.Core
{
    /// <summary>
    /// Qobuz streaming quality format identifiers
    /// 
    /// API BEHAVIOR NOTES:
    /// - format_id 27 (192kHz) has very limited availability - most tracks don't have this quality
    /// - format_id 7 (96kHz) only works if the track has Hi-Res available
    /// - format_id 6 and 5 generally work for all available tracks
    /// - With "download" intent, API does NOT fall back to lower quality
    /// - With "stream" intent, API may fall back to lower quality automatically
    /// </summary>
    public enum QobuzQualityId
    {
        /// <summary>
        /// MP3 320kbps - Lossy compression
        /// Always works if track is available
        /// </summary>
        Mp3_320 = 5,

        /// <summary>
        /// FLAC CD Quality - 16-bit/44.1kHz lossless
        /// Always works if track is available
        /// </summary>
        Flac_CD = 6,

        /// <summary>
        /// FLAC Hi-Res - 24-bit up to 96kHz lossless
        /// Covers: 24/48, 24/88.2, 24/96
        /// Only works if Hi-Res version exists for the track
        /// </summary>
        Flac_HiRes_96 = 7,

        /// <summary>
        /// FLAC Hi-Res - 24-bit/192kHz lossless
        /// Covers: 24/176.4, 24/192
        /// NOTE: Very limited availability in Qobuz catalog
        /// Most tracks will fall back to lower qualities
        /// </summary>
        Flac_HiRes_192 = 27
    }

    /// <summary>
    /// Extension methods for QobuzQualityId enum
    /// </summary>
    public static class QobuzQualityExtensions
    {
        /// <summary>
        /// Gets a human-readable display name for the quality
        /// </summary>
        public static string GetDisplayName(this QobuzQualityId quality)
        {
            return quality switch
            {
                QobuzQualityId.Mp3_320 => "MP3 320kbps",
                QobuzQualityId.Flac_CD => "FLAC CD (16-bit/44.1kHz)",
                QobuzQualityId.Flac_HiRes_96 => "FLAC Hi-Res (24-bit/96kHz)",
                QobuzQualityId.Flac_HiRes_192 => "FLAC Hi-Res (24-bit/192kHz)",
                _ => $"Quality {(int)quality}"
            };
        }

        /// <summary>
        /// Gets a short display name for compact logging
        /// </summary>
        public static string GetShortName(this QobuzQualityId quality)
        {
            return quality switch
            {
                QobuzQualityId.Mp3_320 => "MP3 320",
                QobuzQualityId.Flac_CD => "FLAC CD",
                QobuzQualityId.Flac_HiRes_96 => "FLAC 24/96",
                QobuzQualityId.Flac_HiRes_192 => "FLAC 24/192",
                _ => $"Q{(int)quality}"
            };
        }

        /// <summary>
        /// Determines if this quality is lossless
        /// </summary>
        public static bool IsLossless(this QobuzQualityId quality)
        {
            return quality != QobuzQualityId.Mp3_320;
        }

        /// <summary>
        /// Determines if this quality is Hi-Res
        /// </summary>
        public static bool IsHiRes(this QobuzQualityId quality)
        {
            return quality == QobuzQualityId.Flac_HiRes_96 || quality == QobuzQualityId.Flac_HiRes_192;
        }

        /// <summary>
        /// Gets the bitrate in kbps (approximate for lossless formats)
        /// </summary>
        public static int GetBitrate(this QobuzQualityId quality)
        {
            return quality switch
            {
                QobuzQualityId.Mp3_320 => 320,
                QobuzQualityId.Flac_CD => 1411,  // CD quality bitrate
                QobuzQualityId.Flac_HiRes_96 => 4608,  // Approximate for 24/96
                QobuzQualityId.Flac_HiRes_192 => 9216,  // Approximate for 24/192
                _ => 0
            };
        }

        /// <summary>
        /// Gets quality priority for fallback ordering (higher is better)
        /// </summary>
        public static int GetPriority(this QobuzQualityId quality)
        {
            return quality switch
            {
                QobuzQualityId.Mp3_320 => 1,
                QobuzQualityId.Flac_CD => 2,
                QobuzQualityId.Flac_HiRes_96 => 3,
                QobuzQualityId.Flac_HiRes_192 => 4,
                _ => 0
            };
        }

        /// <summary>
        /// Formats a quality fallback message for logging
        /// </summary>
        public static string FormatFallbackMessage(this QobuzQualityId actualQuality, QobuzQualityId preferredQuality)
        {
            if (actualQuality == preferredQuality)
                return actualQuality.GetShortName();

            var actual = actualQuality.GetShortName();
            var preferred = preferredQuality.GetShortName();
            
            // Provide context-aware messages
            if (actualQuality == QobuzQualityId.Flac_CD && preferredQuality.IsHiRes())
                return $"CD quality ({actual}) - Hi-Res not available";
            else if (actualQuality == QobuzQualityId.Mp3_320)
                return $"MP3 only available (requested {preferred})";
            else if (actualQuality.IsHiRes() && preferredQuality.IsHiRes())
                return $"{actual} (requested {preferred})";
            else
                return $"{actual} (requested {preferred})";
        }
    }

    /// <summary>
    /// Helper class for working with Qobuz quality IDs
    /// </summary>
    public static class QobuzQuality
    {
        /// <summary>
        /// Default quality fallback order from highest to lowest
        /// </summary>
        public static readonly QobuzQualityId[] FallbackOrder = 
        {
            QobuzQualityId.Flac_HiRes_192,
            QobuzQualityId.Flac_HiRes_96,
            QobuzQualityId.Flac_CD,
            QobuzQualityId.Mp3_320
        };

        /// <summary>
        /// Tries to parse an integer to QobuzQualityId
        /// </summary>
        public static bool TryParse(int value, out QobuzQualityId quality)
        {
            if (System.Enum.IsDefined(typeof(QobuzQualityId), value))
            {
                quality = (QobuzQualityId)value;
                return true;
            }
            quality = default;
            return false;
        }

        /// <summary>
        /// Gets the next lower quality in the fallback chain
        /// </summary>
        public static QobuzQualityId? GetNextLowerQuality(QobuzQualityId quality)
        {
            return quality switch
            {
                QobuzQualityId.Flac_HiRes_192 => QobuzQualityId.Flac_HiRes_96,
                QobuzQualityId.Flac_HiRes_96 => QobuzQualityId.Flac_CD,
                QobuzQualityId.Flac_CD => QobuzQualityId.Mp3_320,
                QobuzQualityId.Mp3_320 => null,
                _ => null
            };
        }
    }
}