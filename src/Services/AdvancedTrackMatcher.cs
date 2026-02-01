using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Services.Matching;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Advanced track matching service that uses strategy pattern for complex edge cases
    /// </summary>
    public class AdvancedTrackMatcher
    {
        private readonly Logger _logger;
        private readonly MatchingStrategyCoordinator _coordinator;

        public AdvancedTrackMatcher(
            Logger logger,
            MatchingStrategyCoordinator coordinator)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        }

        /// <summary>
        /// Performs advanced track matching that handles split tracks and complex scenarios
        /// </summary>
        public AdvancedTrackMatchResult PerformAdvancedMatching(
            List<LidarrTrack> lidarrTracks,
            List<QobuzTrack> qobuzTracks)
        {
            var result = _coordinator.PerformAdvancedMatching(lidarrTracks, qobuzTracks);

            // Convert to legacy format for backward compatibility
            return new AdvancedTrackMatchResult
            {
                IsSuccessful = result.IsSuccessful,
                ErrorMessage = result.ErrorMessage,
                MatchingStrategy = result.MatchingStrategy,
                StandardMatches = result.StandardMatches,
                SplitTrackGroups = result.SplitTrackGroups,
                MergedTrackGroups = result.MergedTrackGroups,
                UnmatchedLidarrTracks = result.UnmatchedLidarrTracks,
                UnmatchedQobuzTracks = result.UnmatchedQobuzTracks,
                OverallMatchRate = result.OverallMatchRate,
                StandardMatchRate = result.StandardMatchRate,
                HasComplexMatching = result.HasComplexMatching
            };
        }

        // Implementation moved to focused matching strategy classes
    }

    /// <summary>
    /// Result of advanced track matching with detailed information about different match types
    /// Legacy class for backward compatibility - uses new strategy results internally
    /// </summary>
    public class AdvancedTrackMatchResult
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

        /// <summary>
        /// Determines if the matching result is good enough for optimization
        /// </summary>
        public bool IsSuitableForOptimization => OverallMatchRate >= 0.8 && IsSuccessful;
    }
}
