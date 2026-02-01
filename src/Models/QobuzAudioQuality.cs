namespace Lidarr.Plugin.Qobuzarr.Models
{
    /// <summary>
    /// Audio quality levels available from Qobuz
    /// </summary>
    public enum QobuzAudioQuality
    {
        /// <summary>
        /// MP3 320kbps
        /// </summary>
        MP3320 = 5,

        /// <summary>
        /// FLAC CD Quality (16-bit/44.1kHz)
        /// </summary>
        FLACLossless = 6,

        /// <summary>
        /// FLAC Hi-Res (24-bit/96kHz)
        /// </summary>
        FLACHiRes24Bit96kHz = 7,

        /// <summary>
        /// FLAC Hi-Res (24-bit/192kHz)
        /// </summary>
        FLACHiRes24Bit192Khz = 27
    }

    /// <summary>
    /// Extension methods for QobuzAudioQuality
    /// </summary>
    public static class QobuzAudioQualityExtensions
    {
        /// <summary>
        /// Get human-readable format description
        /// </summary>
        public static string GetFormatDescription(this QobuzAudioQuality quality)
        {
            return quality switch
            {
                QobuzAudioQuality.MP3320 => "MP3 320kbps",
                QobuzAudioQuality.FLACLossless => "FLAC Lossless",
                QobuzAudioQuality.FLACHiRes24Bit96kHz => "FLAC 24bit 96kHz",
                QobuzAudioQuality.FLACHiRes24Bit192Khz => "FLAC 24bit 192kHz",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Get codec name that matches Lidarr's quality detection patterns
        /// </summary>
        public static string GetCodec(this QobuzAudioQuality quality)
        {
            return quality switch
            {
                QobuzAudioQuality.MP3320 => "MP3 320",
                QobuzAudioQuality.FLACLossless => "FLAC",
                QobuzAudioQuality.FLACHiRes24Bit96kHz => "FLAC24bit",
                QobuzAudioQuality.FLACHiRes24Bit192Khz => "FLAC24bit",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Get container description that matches Lidarr's quality detection
        /// </summary>
        public static string GetContainer(this QobuzAudioQuality quality)
        {
            return quality switch
            {
                QobuzAudioQuality.MP3320 => "MP3 320kbps",
                QobuzAudioQuality.FLACLossless => "FLAC",
                QobuzAudioQuality.FLACHiRes24Bit96kHz => "FLAC 24-96",
                QobuzAudioQuality.FLACHiRes24Bit192Khz => "FLAC 24-192",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Get estimated bitrate for size calculation
        /// </summary>
        public static int GetEstimatedBitrate(this QobuzAudioQuality quality)
        {
            return quality switch
            {
                QobuzAudioQuality.MP3320 => 320000,
                QobuzAudioQuality.FLACLossless => 1411200,
                QobuzAudioQuality.FLACHiRes24Bit96kHz => 4608000,
                QobuzAudioQuality.FLACHiRes24Bit192Khz => 9216000,
                _ => 320000
            };
        }
    }
}
