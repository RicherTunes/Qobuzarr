using System;
using System.Collections.Generic;
using System.Linq;

namespace Lidarr.Plugin.Qobuzarr.Models
{
    /// <summary>
    /// Result of quality detection for a track.
    /// </summary>
    public class QualityDetectionResult
    {
        public string TrackId { get; set; }
        public List<QualityFormat> AvailableQualities { get; set; } = new List<QualityFormat>();
        public QualityFormat HighestAvailableQuality { get; set; }
        public DateTime CheckedAt { get; set; }
        
        /// <summary>
        /// Gets the number of available qualities.
        /// </summary>
        public int QualityCount => AvailableQualities?.Count ?? 0;
        
        /// <summary>
        /// Checks if any qualities are available.
        /// </summary>
        public bool HasAvailableQualities => AvailableQualities?.Any() == true;
        
        /// <summary>
        /// Checks if Hi-Res quality is available.
        /// </summary>
        public bool HasHiResQuality => AvailableQualities?.Any(q => q.Id == 7 || q.Id == 27) == true;
        
        /// <summary>
        /// Checks if lossless quality is available.
        /// </summary>
        public bool HasLosslessQuality => AvailableQualities?.Any(q => q.IsLossless) == true;
        
        /// <summary>
        /// Gets the available quality IDs as a list.
        /// </summary>
        public List<int> GetQualityIds()
        {
            return AvailableQualities?.Select(q => q.Id).ToList() ?? new List<int>();
        }
        
        public override string ToString()
        {
            var qualityNames = AvailableQualities?.Select(q => q.Name) ?? Enumerable.Empty<string>();
            return $"QualityDetectionResult[TrackId={TrackId}, Qualities=[{string.Join(", ", qualityNames)}], CheckedAt={CheckedAt:yyyy-MM-dd HH:mm:ss}]";
        }
    }
}