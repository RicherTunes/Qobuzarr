using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Services.Matching;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services.Matching
{
    /// <summary>
    /// Strategy for detecting and matching merged tracks (multiple Lidarr tracks → 1 Qobuz track)
    /// </summary>
    public class MergedTrackMatchingStrategy : ITrackMatchingStrategy
    {
        private readonly Logger _logger;

        public string StrategyName => "MergedTrack";

        public MergedTrackMatchingStrategy(Logger logger = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
        }

        public bool CanHandle(List<LidarrTrack> lidarrTracks, List<QobuzTrack> qobuzTracks)
        {
            // Can handle when Lidarr has more tracks than Qobuz (potential merge scenario)
            return lidarrTracks?.Any() == true && 
                   qobuzTracks?.Any() == true && 
                   lidarrTracks.Count > qobuzTracks.Count;
        }

        public TrackMatchingResult PerformMatching(List<LidarrTrack> lidarrTracks, List<QobuzTrack> qobuzTracks)
        {
            if (!CanHandle(lidarrTracks, qobuzTracks))
            {
                return new TrackMatchingResult
                {
                    IsSuccessful = false,
                    ErrorMessage = "Merged track matching not applicable for this track ratio"
                };
            }

            _logger.Debug("Merged track matching: {0} Lidarr tracks vs {1} Qobuz tracks", 
                         lidarrTracks.Count, qobuzTracks.Count);

            var result = new TrackMatchingResult
            {
                IsSuccessful = true,
                MatchingStrategy = StrategyName
            };

            var availableLidarrTracks = new List<LidarrTrack>(lidarrTracks);
            var availableQobuzTracks = new List<QobuzTrack>(qobuzTracks);

            ProcessMergedTrackMatches(availableLidarrTracks, availableQobuzTracks, result);

            // Add remaining unmatched tracks
            result.UnmatchedLidarrTracks.AddRange(availableLidarrTracks);
            result.UnmatchedQobuzTracks.AddRange(availableQobuzTracks);

            // Calculate statistics
            CalculateMatchingStatistics(result, lidarrTracks.Count);

            _logger.Info("Merged track matching complete: {0} merged groups found", result.MergedTrackGroups.Count);

            return result;
        }

        private void ProcessMergedTrackMatches(
            List<LidarrTrack> lidarrTracks,
            List<QobuzTrack> qobuzTracks,
            TrackMatchingResult result)
        {
            var matchedLidarr = new HashSet<LidarrTrack>();
            var matchedQobuz = new HashSet<QobuzTrack>();

            foreach (var qobuzTrack in qobuzTracks.ToList())
            {
                var mergedGroup = DetectMergedTracks(lidarrTracks.Where(l => !matchedLidarr.Contains(l)).ToList(), qobuzTrack);
                
                if (mergedGroup != null && mergedGroup.Confidence >= 0.75)
                {
                    result.MergedTrackGroups.Add(mergedGroup);
                    matchedQobuz.Add(qobuzTrack);
                    
                    foreach (var lidarrTrack in mergedGroup.LidarrTracks)
                        matchedLidarr.Add(lidarrTrack);

                    _logger.Info("Merged track detected: {0} tracks → '{1}' ({2:P1} confidence)", 
                                mergedGroup.LidarrTracks.Count, qobuzTrack.Title, mergedGroup.Confidence);
                }
            }

            // Remove matched tracks
            foreach (var matched in matchedLidarr)
                lidarrTracks.Remove(matched);
            
            foreach (var matched in matchedQobuz)
                qobuzTracks.Remove(matched);
        }

        private MergedTrackGroup DetectMergedTracks(List<LidarrTrack> lidarrTracks, QobuzTrack qobuzTrack)
        {
            // Find consecutive Lidarr tracks that might be merged in Qobuz
            var candidates = lidarrTracks
                .Where(l => TrackTitlesSimilar(l.Title, qobuzTrack.Title) ||
                           qobuzTrack.Title.ContainsIgnoreCase(l.Title))
                .OrderBy(l => l.TrackNumber)
                .ToList();

            if (candidates.Count < 2)
                return null;

            var consecutiveGroups = FindConsecutiveLidarrGroups(candidates);
            
            foreach (var group in consecutiveGroups)
            {
                if (group.Count >= 2)
                {
                    var confidence = CalculateMergedTrackConfidence(group, qobuzTrack);
                    
                    if (confidence >= 0.75)
                    {
                        return new MergedTrackGroup
                        {
                            LidarrTracks = group,
                            QobuzTrack = qobuzTrack,
                            Confidence = confidence,
                            MergeReason = DetermineMergeReason(group, qobuzTrack)
                        };
                    }
                }
            }

            return null;
        }

        private bool TrackTitlesSimilar(string title1, string title2)
        {
            var normalized1 = TitleNormalizer.Normalize(title1);
            var normalized2 = TitleNormalizer.Normalize(title2);

            // Check if one title contains the other
            if (normalized1.Contains(normalized2) || normalized2.Contains(normalized1))
                return true;

            // Check string similarity
            return CommonStringSimilarity.Calculate(normalized1, normalized2) >= 0.7;
        }

        private List<List<LidarrTrack>> FindConsecutiveLidarrGroups(List<LidarrTrack> tracks)
        {
            var groups = new List<List<LidarrTrack>>();
            var currentGroup = new List<LidarrTrack>();

            for (int i = 0; i < tracks.Count; i++)
            {
                if (currentGroup.Count == 0)
                {
                    currentGroup.Add(tracks[i]);
                }
                else
                {
                    var lastTrack = currentGroup.Last();
                    if (tracks[i].TrackNumber == lastTrack.TrackNumber + 1)
                    {
                        currentGroup.Add(tracks[i]);
                    }
                    else
                    {
                        if (currentGroup.Count > 1)
                            groups.Add(currentGroup);
                        currentGroup = new List<LidarrTrack> { tracks[i] };
                    }
                }
            }

            if (currentGroup.Count > 1)
                groups.Add(currentGroup);

            return groups;
        }

        private double CalculateMergedTrackConfidence(List<LidarrTrack> lidarrTracks, QobuzTrack qobuzTrack)
        {
            double confidence = 0.5; // Base confidence

            // Check duration consistency  
            var totalLidarrDuration = lidarrTracks.Sum(l => l.Duration.TotalSeconds);
            var expectedDuration = qobuzTrack.Duration.TotalSeconds;
            var durationDiff = Math.Abs(totalLidarrDuration - expectedDuration);

            if (durationDiff <= 30)
                confidence += 0.3;
            else if (durationDiff <= 60)
                confidence += 0.15;

            // Check if Qobuz title contains elements from multiple Lidarr titles
            var titleMatches = lidarrTracks.Count(l => qobuzTrack.Title.ContainsIgnoreCase(l.Title));
            confidence += (double)titleMatches / lidarrTracks.Count * 0.2;

            return Math.Min(1.0, confidence);
        }

        private string DetermineMergeReason(List<LidarrTrack> lidarrTracks, QobuzTrack qobuzTrack)
        {
            var lidarrTitles = string.Join(" ", lidarrTracks.Select(t => t.Title)).ToLowerInvariant();
            var qobuzTitle = qobuzTrack.Title.ToLowerInvariant();

            if (lidarrTitles.Contains("intro") && lidarrTitles.Contains("outro"))
                return "IntroOutroMerged";
            if (lidarrTitles.Contains("movement") || lidarrTitles.Contains("mvt"))
                return "ClassicalMovementMerged";

            return "ContinuousComposition";
        }

        // Title normalization now centralized in Utilities.TitleNormalizer

        private void CalculateMatchingStatistics(TrackMatchingResult result, int totalLidarrTracks)
        {
            var matchedLidarrTracks = result.StandardMatches.Count + 
                                     result.SplitTrackGroups.Count + 
                                     result.MergedTrackGroups.Sum(g => g.LidarrTracks.Count);

            result.OverallMatchRate = totalLidarrTracks > 0 ? (double)matchedLidarrTracks / totalLidarrTracks : 0;
            result.StandardMatchRate = totalLidarrTracks > 0 ? (double)result.StandardMatches.Count / totalLidarrTracks : 0;
            result.HasComplexMatching = result.SplitTrackGroups.Any() || result.MergedTrackGroups.Any();
        }
    }

    /// <summary>
    /// Extension methods for string operations
    /// </summary>
    public static class StringExtensions
    {
        public static bool ContainsIgnoreCase(this string source, string value)
        {
            return source?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false;
        }
    }
}
