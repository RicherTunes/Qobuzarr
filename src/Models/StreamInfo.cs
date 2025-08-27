using System;

namespace Lidarr.Plugin.Qobuzarr.Models
{
    /// <summary>
    /// Contains streaming information for a track.
    /// </summary>
    public class StreamInfo
    {
        public string Url { get; set; }
        public int QualityId { get; set; }
        public string TrackId { get; set; }
        public DateTime ExpiresAt { get; set; }
        
        /// <summary>
        /// Checks if the stream URL is still valid.
        /// </summary>
        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
        
        /// <summary>
        /// Checks if the stream URL is valid and not empty.
        /// </summary>
        public bool HasValidUrl => !string.IsNullOrWhiteSpace(Url);
        
        /// <summary>
        /// Gets the remaining time until the stream expires.
        /// </summary>
        public TimeSpan TimeUntilExpiration => ExpiresAt - DateTime.UtcNow;
        
        public override string ToString()
        {
            return $"StreamInfo[TrackId={TrackId}, QualityId={QualityId}, ExpiresAt={ExpiresAt:HH:mm:ss}]";
        }
    }
}