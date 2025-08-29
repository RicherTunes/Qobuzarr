using Lidarr.Plugin.Common.Base;

namespace Lidarr.Plugin.Qobuzarr.Settings
{
    /// <summary>
    /// Qobuz-specific settings extending the base streaming settings
    /// Maps CLI configuration to plugin requirements
    /// </summary>
    public class QobuzSettings : BaseStreamingSettings
    {
        /// <summary>
        /// Qobuz quality preference (5=MP3-320, 6=FLAC-CD, 7=FLAC-Hi-Res, 27=FLAC-Max)
        /// </summary>
        public int Quality { get; set; } = 27; // Default to highest quality
        
        /// <summary>
        /// User ID for token-based authentication (alternative to email/password)
        /// </summary>
        public new string? UserId { get; set; }
        
        /// <summary>
        /// Authentication token (alternative to email/password)
        /// </summary>
        public string? Token { get; set; }
        
        /// <summary>
        /// Maximum concurrent downloads specific to Qobuz
        /// </summary>
        public int MaxConcurrentDownloads { get; set; } = 3;
        
        /// <summary>
        /// API timeout in milliseconds
        /// </summary>
        public int ApiTimeoutMs { get; set; } = 30000;
        
        /// <summary>
        /// Enable advanced logging for troubleshooting
        /// </summary>
        public bool VerboseLogging { get; set; } = false;
        
        /// <summary>
        /// Preferred audio format for downloads
        /// </summary>
        public string AudioFormat { get; set; } = "FLAC";
        
        /// <summary>
        /// Enable metadata tagging after download
        /// </summary>
        public bool EnableMetadataTagging { get; set; } = true;
        
        /// <summary>
        /// Custom output filename pattern
        /// </summary>
        public string FilenamePattern { get; set; } = "{artist} - {album} - {track} - {title}";
    }
}