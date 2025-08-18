using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Services.Matching;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services.Matching
{
    /// <summary>
    /// Strategy for standard 1:1 track matching using title, duration, and position similarity
    /// </summary>
    public class StandardTrackMatchingStrategy : ITrackMatchingStrategy
    {
        private readonly Logger _logger;

        // Featured artist patterns
        private static readonly Regex[] FeaturedArtistRegexes =
        {
            new(@"\s+(?:ft\.?|feat\.?|featuring)\s+(.+)$", RegexOptions.IgnoreCase),
            new(@"\s+(?:with|&)\s+(.+)$", RegexOptions.IgnoreCase),
            new(@"\s*\((?:ft\.?|feat\.?|featuring)\s+(.+)\)$", RegexOptions.IgnoreCase)
        };
        
        // Live album venue patterns  
        private static readonly Regex LiveVenueRegex = new(
            @"\s*-?\s*(?:live\s+(?:at|from|in)?|recorded\s+(?:at|in)?)\s+[^,]+(?:,\s*[\d/\-\s]+)?$", 
            RegexOptions.IgnoreCase);

        public string StrategyName => "Standard";

        public StandardTrackMatchingStrategy(Logger logger = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
        }

        public bool CanHandle(List<LidarrTrack> lidarrTracks, List<QobuzTrack> qobuzTracks)
        {
            // Standard matching can always be attempted
            return lidarrTracks?.Any() == true && qobuzTracks?.Any() == true;
        }

        public TrackMatchingResult PerformMatching(List<LidarrTrack> lidarrTracks, List<QobuzTrack> qobuzTracks)
        {
            if (!lidarrTracks?.Any() == true || !qobuzTracks?.Any() == true)
            {
                return new TrackMatchingResult
                {
                    IsSuccessful = false,
                    ErrorMessage = "No tracks provided for matching"
                };
            }

            _logger.Debug("Standard track matching: {0} Lidarr tracks vs {1} Qobuz tracks", 
                         lidarrTracks.Count, qobuzTracks.Count);

            var result = new TrackMatchingResult
            {
                IsSuccessful = true,
                MatchingStrategy = StrategyName
            };

            var availableLidarrTracks = new List<LidarrTrack>(lidarrTracks);
            var availableQobuzTracks = new List<QobuzTrack>(qobuzTracks);

            ProcessStandardMatches(availableLidarrTracks, availableQobuzTracks, result);

            // Add remaining unmatched tracks
            result.UnmatchedLidarrTracks.AddRange(availableLidarrTracks);
            result.UnmatchedQobuzTracks.AddRange(availableQobuzTracks);

            // Calculate statistics
            CalculateMatchingStatistics(result, lidarrTracks.Count);

            _logger.Info("Standard matching complete: {0:P1} match rate", result.OverallMatchRate);

            return result;
        }

        private void ProcessStandardMatches(
            List<LidarrTrack> lidarrTracks,
            List<QobuzTrack> qobuzTracks,
            TrackMatchingResult result)
        {
            var matchedLidarr = new HashSet<LidarrTrack>();
            var matchedQobuz = new HashSet<QobuzTrack>();

            foreach (var lidarrTrack in lidarrTracks.ToList())
            {
                var bestMatch = FindBestStandardMatch(lidarrTrack, qobuzTracks.Where(q => !matchedQobuz.Contains(q)));
                
                if (bestMatch.Match != null && bestMatch.Confidence >= 0.85) // High confidence for standard matches
                {
                    result.StandardMatches.Add(new StandardTrackMatch
                    {
                        LidarrTrack = lidarrTrack,
                        QobuzTrack = bestMatch.Match,
                        MatchConfidence = bestMatch.Confidence,
                        MatchType = bestMatch.MatchType
                    });

                    matchedLidarr.Add(lidarrTrack);
                    matchedQobuz.Add(bestMatch.Match);

                    _logger.Debug("Standard match: '{0}' → '{1}' ({2:P1} confidence)", 
                                 lidarrTrack.Title, bestMatch.Match.Title, bestMatch.Confidence);
                }
            }

            // Remove matched tracks from available lists
            foreach (var matched in matchedLidarr)
                lidarrTracks.Remove(matched);
            
            foreach (var matched in matchedQobuz)
                qobuzTracks.Remove(matched);
        }

        private (QobuzTrack Match, double Confidence, string MatchType) FindBestStandardMatch(
            LidarrTrack lidarrTrack, 
            IEnumerable<QobuzTrack> qobuzTracks)
        {
            QobuzTrack? bestMatch = null;
            double bestScore = 0;
            string matchType = "None";

            foreach (var qobuzTrack in qobuzTracks)
            {
                // Normalize titles for comparison
                var normalizedLidarr = NormalizeTrackTitle(lidarrTrack.Title);
                var normalizedQobuz = NormalizeTrackTitle(qobuzTrack.Title);

                double score = 0;
                string currentMatchType = "None";

                // Exact normalized title match (highest priority)
                if (normalizedLidarr.Equals(normalizedQobuz, StringComparison.OrdinalIgnoreCase))
                {
                    score = 0.95;
                    currentMatchType = "ExactTitle";
                }
                // High similarity with track number match
                else if (lidarrTrack.TrackNumber == qobuzTrack.TrackNumber)
                {
                    var titleSimilarity = StringSimilarity.Calculate(normalizedLidarr, normalizedQobuz);
                    if (titleSimilarity >= 0.8)
                    {
                        score = 0.9 * titleSimilarity;
                        currentMatchType = "TitleAndNumber";
                    }
                }
                // Duration-based matching for instrumental tracks
                else if (lidarrTrack.DurationMs > 0)
                {
                    var durationDiff = Math.Abs(lidarrTrack.Duration.TotalSeconds - qobuzTrack.Duration.TotalSeconds);
                    var titleSimilarity = StringSimilarity.Calculate(normalizedLidarr, normalizedQobuz);
                    
                    if (durationDiff <= 15 && titleSimilarity >= 0.7) // 15 second tolerance
                    {
                        score = 0.85 * titleSimilarity * (1 - (durationDiff / 60)); // Reduce score for duration differences
                        currentMatchType = "TitleAndDuration";
                    }
                }
                // Fuzzy title matching
                else
                {
                    var titleSimilarity = StringSimilarity.Calculate(normalizedLidarr, normalizedQobuz);
                    if (titleSimilarity >= 0.8)
                    {
                        score = 0.8 * titleSimilarity;
                        currentMatchType = "FuzzyTitle";
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = qobuzTrack;
                    matchType = currentMatchType;
                }
            }

            return (bestMatch!, bestScore, matchType);
        }

        private string NormalizeTrackTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return "";

            var normalized = title.Trim();

            // Remove featured artist variations
            foreach (var regex in FeaturedArtistRegexes)
            {
                normalized = regex.Replace(normalized, "");
            }

            // Remove live venue information
            normalized = LiveVenueRegex.Replace(normalized, "");

            // Remove common punctuation and extra spaces
            normalized = Regex.Replace(normalized, @"[^\w\s]", " ");
            normalized = Regex.Replace(normalized, @"\s+", " ");

            // Remove common prefixes/suffixes
            normalized = Regex.Replace(normalized, @"\b(the|a|an)\s+", "", RegexOptions.IgnoreCase);

            return normalized.Trim().ToLowerInvariant();
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