using System.Collections.Generic;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;

namespace Lidarr.Plugin.Qobuzarr.Services.Matching
{
    /// <summary>
    /// Strategy interface for different track matching approaches
    /// </summary>
    public interface ITrackMatchingStrategy
    {
        /// <summary>
        /// Strategy name for logging and diagnostics
        /// </summary>
        string StrategyName { get; }

        /// <summary>
        /// Determines if this strategy can handle the given track lists
        /// </summary>
        bool CanHandle(List<LidarrTrack> lidarrTracks, List<QobuzTrack> qobuzTracks);

        /// <summary>
        /// Performs track matching using this strategy
        /// </summary>
        TrackMatchingResult PerformMatching(List<LidarrTrack> lidarrTracks, List<QobuzTrack> qobuzTracks);
    }

    /// <summary>
    /// Result of track matching operation
    /// </summary>
    public class TrackMatchingResult
    {
        public bool IsSuccessful { get; set; }
        public string ErrorMessage { get; set; }
        public string MatchingStrategy { get; set; }
        public List<StandardTrackMatch> StandardMatches { get; set; } = new();
        public List<SplitTrackGroup> SplitTrackGroups { get; set; } = new();
        public List<MergedTrackGroup> MergedTrackGroups { get; set; } = new();
        public List<LidarrTrack> UnmatchedLidarrTracks { get; set; } = new();
        public List<QobuzTrack> UnmatchedQobuzTracks { get; set; } = new();
        public double OverallMatchRate { get; set; }
        public double StandardMatchRate { get; set; }
        public bool HasComplexMatching { get; set; }
        public bool IsSuitableForOptimization => OverallMatchRate >= 0.8 && IsSuccessful;
    }

    /// <summary>
    /// Standard 1:1 track match
    /// </summary>
    public class StandardTrackMatch
    {
        public LidarrTrack LidarrTrack { get; set; }
        public QobuzTrack QobuzTrack { get; set; }
        public double MatchConfidence { get; set; }
        public string MatchType { get; set; }
    }

    /// <summary>
    /// Group representing one Lidarr track split into multiple Qobuz tracks
    /// </summary>
    public class SplitTrackGroup
    {
        public LidarrTrack LidarrTrack { get; set; }
        public List<QobuzTrack> QobuzTracks { get; set; } = new();
        public double Confidence { get; set; }
        public string SplitReason { get; set; }
    }

    /// <summary>
    /// Group representing multiple Lidarr tracks merged into one Qobuz track
    /// </summary>
    public class MergedTrackGroup
    {
        public List<LidarrTrack> LidarrTracks { get; set; } = new();
        public QobuzTrack QobuzTrack { get; set; }
        public double Confidence { get; set; }
        public string MergeReason { get; set; }
    }
}
