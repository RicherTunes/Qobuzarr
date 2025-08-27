using System.Collections.Generic;
using System.Linq;

namespace Lidarr.Plugin.Qobuzarr.Models
{
    /// <summary>
    /// Result of batch stream information retrieval.
    /// </summary>
    public class BatchStreamResult
    {
        public QobuzQuality RequestedQuality { get; set; }
        public Dictionary<string, StreamInfo> TrackResults { get; set; } = new Dictionary<string, StreamInfo>();
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        
        /// <summary>
        /// Gets the total number of tracks processed.
        /// </summary>
        public int TotalCount => SuccessCount + FailureCount;
        
        /// <summary>
        /// Gets the success rate as a percentage.
        /// </summary>
        public double SuccessRate => TotalCount > 0 ? (double)SuccessCount / TotalCount : 0.0;
        
        /// <summary>
        /// Gets the success rate as a formatted percentage string.
        /// </summary>
        public string SuccessRatePercentage => $"{SuccessRate:P0}";
        
        /// <summary>
        /// Checks if all tracks were processed successfully.
        /// </summary>
        public bool AllSuccessful => FailureCount == 0 && SuccessCount > 0;
        
        /// <summary>
        /// Gets the list of successful track IDs.
        /// </summary>
        public List<string> GetSuccessfulTrackIds()
        {
            return TrackResults.Keys.ToList();
        }
        
        /// <summary>
        /// Gets stream info for a specific track.
        /// </summary>
        public StreamInfo GetStreamInfo(string trackId)
        {
            return TrackResults.TryGetValue(trackId, out var streamInfo) ? streamInfo : null;
        }
        
        /// <summary>
        /// Checks if a specific track has stream info.
        /// </summary>
        public bool HasStreamInfo(string trackId)
        {
            return TrackResults.ContainsKey(trackId);
        }
        
        public override string ToString()
        {
            return $"BatchStreamResult[Quality={RequestedQuality?.Name}, Success={SuccessCount}/{TotalCount} ({SuccessRatePercentage})]";
        }
    }
}