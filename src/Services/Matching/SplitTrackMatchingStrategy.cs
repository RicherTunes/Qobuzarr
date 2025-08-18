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
    /// Strategy for detecting and matching split tracks (1 Lidarr track → multiple Qobuz tracks)
    /// </summary>
    public class SplitTrackMatchingStrategy : ITrackMatchingStrategy
    {
        private readonly Logger _logger;
        
        // Split track detection patterns
        private static readonly string[] SplitIndicators = 
        {
            "part", "pt", "movement", "mvt", "section", "sec", 
            "act", "chapter", "intro", "outro", "prelude", "interlude"
        };

        public string StrategyName => "SplitTrack";

        public SplitTrackMatchingStrategy(Logger logger = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
        }

        public bool CanHandle(List<LidarrTrack> lidarrTracks, List<QobuzTrack> qobuzTracks)
        {
            // Can handle when Qobuz has more tracks than Lidarr (potential split scenario)
            return lidarrTracks?.Any() == true && 
                   qobuzTracks?.Any() == true && 
                   qobuzTracks.Count > lidarrTracks.Count;
        }

        public TrackMatchingResult PerformMatching(List<LidarrTrack> lidarrTracks, List<QobuzTrack> qobuzTracks)
        {
            if (!CanHandle(lidarrTracks, qobuzTracks))
            {
                return new TrackMatchingResult
                {
                    IsSuccessful = false,
                    ErrorMessage = "Split track matching not applicable for this track ratio"
                };
            }

            _logger.Debug("Split track matching: {0} Lidarr tracks vs {1} Qobuz tracks", 
                         lidarrTracks.Count, qobuzTracks.Count);

            var result = new TrackMatchingResult
            {
                IsSuccessful = true,
                MatchingStrategy = StrategyName
            };

            var availableLidarrTracks = new List<LidarrTrack>(lidarrTracks);
            var availableQobuzTracks = new List<QobuzTrack>(qobuzTracks);

            ProcessSplitTrackMatches(availableLidarrTracks, availableQobuzTracks, result);

            // Add remaining unmatched tracks
            result.UnmatchedLidarrTracks.AddRange(availableLidarrTracks);
            result.UnmatchedQobuzTracks.AddRange(availableQobuzTracks);

            // Calculate statistics
            CalculateMatchingStatistics(result, lidarrTracks.Count);

            _logger.Info("Split track matching complete: {0} split groups found", result.SplitTrackGroups.Count);

            return result;
        }

        private void ProcessSplitTrackMatches(
            List<LidarrTrack> lidarrTracks,
            List<QobuzTrack> qobuzTracks,
            TrackMatchingResult result)
        {
            var matchedLidarr = new HashSet<LidarrTrack>();
            var matchedQobuz = new HashSet<QobuzTrack>();

            foreach (var lidarrTrack in lidarrTracks.ToList())
            {
                var splitGroup = DetectSplitTracks(lidarrTrack, qobuzTracks.Where(q => !matchedQobuz.Contains(q)).ToList());
                
                if (splitGroup != null && splitGroup.Confidence >= 0.75)
                {
                    result.SplitTrackGroups.Add(splitGroup);
                    matchedLidarr.Add(lidarrTrack);
                    
                    foreach (var qobuzTrack in splitGroup.QobuzTracks)
                        matchedQobuz.Add(qobuzTrack);

                    _logger.Info("Split track detected: '{0}' → {1} parts ({2:P1} confidence)", 
                                lidarrTrack.Title, splitGroup.QobuzTracks.Count, splitGroup.Confidence);
                }
            }

            // Remove matched tracks
            foreach (var matched in matchedLidarr)
                lidarrTracks.Remove(matched);
            
            foreach (var matched in matchedQobuz)
                qobuzTracks.Remove(matched);
        }

        private SplitTrackGroup DetectSplitTracks(LidarrTrack lidarrTrack, List<QobuzTrack> qobuzTracks)
        {
            // Find consecutive tracks with similar titles to the Lidarr track
            var candidates = qobuzTracks
                .Where(q => TrackTitlesSimilar(lidarrTrack.Title, q.Title) || 
                           ContainsSplitIndicator(q.Title, lidarrTrack.Title))
                .OrderBy(q => q.TrackNumber)
                .ToList();

            if (candidates.Count < 2)
                return null;

            // Check if these tracks are consecutive
            var consecutiveGroups = FindConsecutiveTrackGroups(candidates);
            
            foreach (var group in consecutiveGroups)
            {
                if (group.Count >= 2)
                {
                    var confidence = CalculateSplitTrackConfidence(lidarrTrack, group);
                    
                    if (confidence >= 0.75)
                    {
                        return new SplitTrackGroup
                        {
                            LidarrTrack = lidarrTrack,
                            QobuzTracks = group,
                            Confidence = confidence,
                            SplitReason = DetermineSplitReason(group)
                        };
                    }
                }
            }

            return null;
        }

        private bool TrackTitlesSimilar(string title1, string title2)
        {
            var normalized1 = NormalizeTitle(title1);
            var normalized2 = NormalizeTitle(title2);

            // Check if one title contains the other
            if (normalized1.Contains(normalized2) || normalized2.Contains(normalized1))
                return true;

            // Check string similarity
            return StringSimilarity.Calculate(normalized1, normalized2) >= 0.7;
        }

        private bool ContainsSplitIndicator(string qobuzTitle, string lidarrTitle)
        {
            var normalizedQobuz = qobuzTitle.ToLowerInvariant();
            var normalizedLidarr = lidarrTitle.ToLowerInvariant();

            return SplitIndicators.Any(indicator => 
                normalizedQobuz.Contains(indicator) && 
                normalizedQobuz.Contains(normalizedLidarr.Split(' ')[0])); // Contains first word of original title
        }

        private List<List<QobuzTrack>> FindConsecutiveTrackGroups(List<QobuzTrack> tracks)
        {
            var groups = new List<List<QobuzTrack>>();
            var currentGroup = new List<QobuzTrack>();

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
                        currentGroup = new List<QobuzTrack> { tracks[i] };
                    }
                }
            }

            if (currentGroup.Count > 1)
                groups.Add(currentGroup);

            return groups;
        }

        private double CalculateSplitTrackConfidence(LidarrTrack lidarrTrack, List<QobuzTrack> qobuzTracks)
        {
            double confidence = 0.5; // Base confidence

            // Check duration consistency
            if (lidarrTrack.DurationMs > 0)
            {
                var totalQobuzDuration = qobuzTracks.Sum(q => q.Duration.TotalSeconds);
                var expectedDuration = lidarrTrack.Duration.TotalSeconds;
                var durationDiff = Math.Abs(totalQobuzDuration - expectedDuration);

                if (durationDiff <= 30) // Within 30 seconds
                    confidence += 0.3;
                else if (durationDiff <= 60) // Within 1 minute
                    confidence += 0.15;
            }

            // Check title similarity
            var avgSimilarity = qobuzTracks.Average(q => StringSimilarity.Calculate(lidarrTrack.Title, q.Title));
            confidence += avgSimilarity * 0.2;

            return Math.Min(1.0, confidence);
        }

        private string DetermineSplitReason(List<QobuzTrack> qobuzTracks)
        {
            var titles = qobuzTracks.Select(t => t.Title.ToLowerInvariant()).ToList();

            if (titles.Any(t => t.Contains("part") || t.Contains("pt")))
                return "MultiPart";
            if (titles.Any(t => t.Contains("movement") || t.Contains("mvt")))
                return "ClassicalMovement";
            if (titles.Any(t => t.Contains("intro") || t.Contains("outro")))
                return "IntroOutroSplit";

            return "Unknown";
        }

        private string NormalizeTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return "";

            return title.ToLowerInvariant()
                       .Replace("(", "")
                       .Replace(")", "")
                       .Replace("[", "")
                       .Replace("]", "")
                       .Trim();
        }

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
}