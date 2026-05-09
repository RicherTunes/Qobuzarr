using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Services.Matching;

namespace Lidarr.Plugin.Qobuzarr.Services.Matching
{
    /// <summary>
    /// Coordinator that orchestrates different track matching strategies for complex scenarios
    /// </summary>
    public class MatchingStrategyCoordinator
    {
        private readonly Logger _logger;
        private readonly List<ITrackMatchingStrategy> _strategies;

        // Various artists patterns
        private static readonly string[] VariousArtistsPatterns =
        {
            "various artists", "va", "compilation", "soundtrack", "ost",
            "mixed", "sampler", "collection", "anthology"
        };

        public MatchingStrategyCoordinator(
            Logger logger,
            IEnumerable<ITrackMatchingStrategy> strategies)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _strategies = strategies?.ToList() ?? throw new ArgumentNullException(nameof(strategies));
        }

        /// <summary>
        /// Performs advanced track matching that handles split tracks and complex scenarios
        /// </summary>
        public TrackMatchingResult PerformAdvancedMatching(
            List<LidarrTrack> lidarrTracks,
            List<QobuzTrack> qobuzTracks)
        {
            if (!lidarrTracks?.Any() == true || !qobuzTracks?.Any() == true)
            {
                return new TrackMatchingResult
                {
                    IsSuccessful = false,
                    ErrorMessage = "No tracks provided for matching"
                };
            }

            _logger.Debug("Advanced track matching: {0} Lidarr tracks vs {1} Qobuz tracks",
                         lidarrTracks.Count, qobuzTracks.Count);

            var finalResult = new TrackMatchingResult
            {
                IsSuccessful = true,
                MatchingStrategy = DetermineOptimalMatchingStrategy(lidarrTracks, qobuzTracks)
            };

            var availableLidarrTracks = new List<LidarrTrack>(lidarrTracks);
            var availableQobuzTracks = new List<QobuzTrack>(qobuzTracks);

            // Phase 1: Try standard 1:1 track matching first
            var standardStrategy = _strategies.FirstOrDefault(s => s.StrategyName == "Standard");
            if (standardStrategy?.CanHandle(availableLidarrTracks, availableQobuzTracks) == true)
            {
                var standardResult = standardStrategy.PerformMatching(availableLidarrTracks, availableQobuzTracks);
                MergeResults(finalResult, standardResult);

                // Remove matched tracks
                RemoveMatchedTracks(availableLidarrTracks, availableQobuzTracks, standardResult);
            }

            // Phase 2: Try split track detection if tracks remain
            if (availableLidarrTracks.Any() && availableQobuzTracks.Any())
            {
                var splitStrategy = _strategies.FirstOrDefault(s => s.StrategyName == "SplitTrack");
                if (splitStrategy?.CanHandle(availableLidarrTracks, availableQobuzTracks) == true)
                {
                    var splitResult = splitStrategy.PerformMatching(availableLidarrTracks, availableQobuzTracks);
                    MergeResults(finalResult, splitResult);

                    // Remove matched tracks
                    RemoveMatchedTracks(availableLidarrTracks, availableQobuzTracks, splitResult);
                }
            }

            // Phase 3: Try merged track detection if tracks remain
            if (availableLidarrTracks.Any() && availableQobuzTracks.Any())
            {
                var mergedStrategy = _strategies.FirstOrDefault(s => s.StrategyName == "MergedTrack");
                if (mergedStrategy?.CanHandle(availableLidarrTracks, availableQobuzTracks) == true)
                {
                    var mergedResult = mergedStrategy.PerformMatching(availableLidarrTracks, availableQobuzTracks);
                    MergeResults(finalResult, mergedResult);

                    // Remove matched tracks
                    RemoveMatchedTracks(availableLidarrTracks, availableQobuzTracks, mergedResult);
                }
            }

            // Phase 4: Add remaining unmatched tracks
            finalResult.UnmatchedLidarrTracks.AddRange(availableLidarrTracks);
            finalResult.UnmatchedQobuzTracks.AddRange(availableQobuzTracks);

            // Calculate final statistics
            CalculateFinalStatistics(finalResult, lidarrTracks.Count);

            _logger.Info("Advanced matching complete: {0:P1} match rate, {1} split groups, {2} merged groups",
                        finalResult.OverallMatchRate, finalResult.SplitTrackGroups.Count, finalResult.MergedTrackGroups.Count);

            return finalResult;
        }

        /// <summary>
        /// Determines the optimal matching strategy based on track count differences and patterns
        /// </summary>
        private string DetermineOptimalMatchingStrategy(List<LidarrTrack> lidarrTracks, List<QobuzTrack> qobuzTracks)
        {
            var lidarrCount = lidarrTracks.Count;
            var qobuzCount = qobuzTracks.Count;
            var countDiff = Math.Abs(lidarrCount - qobuzCount);

            if (countDiff == 0)
                return "StandardMatching";

            if (qobuzCount > lidarrCount && countDiff <= lidarrCount * 0.5)
                return "SplitTrackProbable";

            if (lidarrCount > qobuzCount && countDiff <= qobuzCount * 0.5)
                return "MergedTrackProbable";

            // Check for various artists patterns
            var hasVariousArtists = VariousArtistsPatterns.Any(pattern =>
                lidarrTracks.Any(t => t.ArtistName?.ContainsIgnoreCase(pattern) == true) ||
                qobuzTracks.Any(t => t.GetPerformerName()?.ContainsIgnoreCase(pattern) == true));

            if (hasVariousArtists)
                return "VariousArtistsCompilation";

            return "ComplexMatching";
        }

        private void MergeResults(TrackMatchingResult finalResult, TrackMatchingResult partialResult)
        {
            if (partialResult == null) return;

            finalResult.StandardMatches.AddRange(partialResult.StandardMatches);
            finalResult.SplitTrackGroups.AddRange(partialResult.SplitTrackGroups);
            finalResult.MergedTrackGroups.AddRange(partialResult.MergedTrackGroups);

            // Update strategy to reflect combined approach
            if (finalResult.MatchingStrategy != partialResult.MatchingStrategy)
            {
                finalResult.MatchingStrategy = "Combined";
            }
        }

        private void RemoveMatchedTracks(
            List<LidarrTrack> availableLidarrTracks,
            List<QobuzTrack> availableQobuzTracks,
            TrackMatchingResult result)
        {
            // Remove standard matches
            foreach (var match in result.StandardMatches)
            {
                availableLidarrTracks.Remove(match.LidarrTrack);
                availableQobuzTracks.Remove(match.QobuzTrack);
            }

            // Remove split track matches
            foreach (var splitGroup in result.SplitTrackGroups)
            {
                availableLidarrTracks.Remove(splitGroup.LidarrTrack);
                foreach (var qobuzTrack in splitGroup.QobuzTracks)
                {
                    availableQobuzTracks.Remove(qobuzTrack);
                }
            }

            // Remove merged track matches
            foreach (var mergedGroup in result.MergedTrackGroups)
            {
                availableQobuzTracks.Remove(mergedGroup.QobuzTrack);
                foreach (var lidarrTrack in mergedGroup.LidarrTracks)
                {
                    availableLidarrTracks.Remove(lidarrTrack);
                }
            }
        }

        private void CalculateFinalStatistics(TrackMatchingResult result, int totalLidarrTracks)
        {
            var matchedLidarrTracks = result.StandardMatches.Count +
                                     result.SplitTrackGroups.Count +
                                     result.MergedTrackGroups.Sum(g => g.LidarrTracks.Count);

            result.OverallMatchRate = totalLidarrTracks > 0 ? (double)matchedLidarrTracks / totalLidarrTracks : 0;
            result.StandardMatchRate = totalLidarrTracks > 0 ? (double)result.StandardMatches.Count / totalLidarrTracks : 0;
            result.HasComplexMatching = result.SplitTrackGroups.Any() || result.MergedTrackGroups.Any();
        }

        /// <summary>
        /// Gets available strategy names
        /// </summary>
        public IReadOnlyList<string> GetAvailableStrategies()
        {
            // Wave 82 polish: List<T> already implements IReadOnlyList<T>, so the
            // extra `.AsReadOnly()` wrapper allocation is wasted. Returning the
            // list directly is just as immutable from the caller's perspective.
            return _strategies.Select(s => s.StrategyName).ToList();
        }
    }
}
