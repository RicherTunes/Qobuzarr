using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Intelligent release mapping service that validates compatibility between Lidarr and Qobuz releases
    /// Handles edge cases like different editions, remasters, regional variations, and track count mismatches
    /// </summary>
    /// <remarks>
    /// Key features:
    /// - Comprehensive validation to prevent metadata mismatches
    /// - Edition detection for deluxe/expanded/remastered releases
    /// - Track matching with fuzzy logic and duration validation
    /// - Hybrid approach decision logic for bonus content
    /// - Conservative fallback to prevent data corruption
    /// 
    /// Safety priorities:
    /// 1. Never use mismatched metadata that could corrupt downloads
    /// 2. Detect incompatible releases early (different editions, remasters)
    /// 3. Provide clear logging for troubleshooting matching failures
    /// 4. Default to Qobuz metadata when uncertain for data integrity
    /// </remarks>
    public class IntelligentReleaseMapper
    {
        private readonly Logger _logger;
        private readonly QobuzTrackDownloader _trackDownloader;
        
        // Edition indicators that suggest different releases
        private static readonly string[] EditionIndicators = 
        {
            "deluxe", "expanded", "remaster", "remastered", "anniversary", "special", 
            "collector", "collectors", "limited", "extended", "director's cut", "directors cut",
            "bonus", "rare", "unreleased", "complete", "comprehensive", "ultimate",
            "super deluxe", "platinum", "gold", "silver", "commemorative", "legacy"
        };
        
        // Time tolerance thresholds for matching validation
        private const int MAX_TRACK_COUNT_VARIANCE = 2; // Allow 2 tracks difference for bonus content
        private const int MAX_ALBUM_DURATION_VARIANCE_MINUTES = 5; // Allow 5 minute total difference
        private const int MAX_TRACK_DURATION_VARIANCE_SECONDS = 10; // Allow 10 second track difference
        private const double MIN_CORE_TRACK_MATCH_RATE = 0.8; // Require 80% core tracks to match
        private const double MIN_TRACK_TITLE_SIMILARITY = 0.85; // Require 85% title similarity
        private const int MAX_RELEASE_YEAR_VARIANCE = 5; // Allow 5 year difference for remasters

        public IntelligentReleaseMapper(Logger logger, QobuzTrackDownloader trackDownloader)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _trackDownloader = trackDownloader ?? throw new ArgumentNullException(nameof(trackDownloader));
        }

        /// <summary>
        /// Analyzes compatibility between Lidarr and Qobuz releases and determines optimal metadata strategy
        /// </summary>
        /// <param name="lidarrAlbum">Album data from Lidarr with MusicBrainz metadata</param>
        /// <param name="qobuzAlbum">Album data from Qobuz search results</param>
        /// <returns>Match result indicating compatibility and recommended approach</returns>
        public ReleaseMatchResult ValidateReleaseMatch(LidarrAlbum lidarrAlbum, QobuzAlbum qobuzAlbum)
        {
            if (lidarrAlbum == null || qobuzAlbum == null)
            {
                return ReleaseMatchResult.Incompatible("Missing album data - cannot perform validation");
            }

            _logger.Debug("Validating release match: Lidarr '{0}' vs Qobuz '{1}'", 
                         lidarrAlbum.Title, qobuzAlbum.Title);

            var issues = new List<string>();
            var warnings = new List<string>();

            // Critical validation checks that prevent optimization
            var criticalIssue = ValidateCriticalCompatibility(lidarrAlbum, qobuzAlbum);
            if (criticalIssue != null)
            {
                _logger.Warn("🚫 CRITICAL COMPATIBILITY ISSUE: {0}", criticalIssue);
                return ReleaseMatchResult.Incompatible(criticalIssue);
            }

            // Check for edition differences that require hybrid approach
            var editionAnalysis = AnalyzeEditionDifferences(lidarrAlbum, qobuzAlbum);
            if (editionAnalysis.HasSignificantDifferences)
            {
                _logger.Info("📀 EDITION DIFFERENCE DETECTED: {0}", editionAnalysis.Reason);
                return ReleaseMatchResult.RequiresHybrid(editionAnalysis.Reason, editionAnalysis.TrackMismatches);
            }

            // Validate track matching quality
            var trackMatchResult = ValidateTrackMatching(lidarrAlbum, qobuzAlbum);
            if (!trackMatchResult.IsAcceptable)
            {
                _logger.Warn("🎵 POOR TRACK MATCHING: {0}", trackMatchResult.Reason);
                return ReleaseMatchResult.Incompatible(trackMatchResult.Reason);
            }

            // Check for minor issues that suggest caution but allow optimization
            var minorIssues = ValidateMinorCompatibilityIssues(lidarrAlbum, qobuzAlbum);
            if (minorIssues.Any())
            {
                warnings.AddRange(minorIssues);
                _logger.Debug("⚠️ MINOR COMPATIBILITY WARNINGS: {0}", string.Join(", ", minorIssues));
            }

            // Determine final result
            var result = warnings.Any() 
                ? ReleaseMatchResult.Compatible("Validated with minor warnings: " + string.Join(", ", warnings))
                : ReleaseMatchResult.Compatible("Perfect release match validated");

            result.TrackMatchResult = trackMatchResult;
            result.EditionAnalysis = editionAnalysis;

            _logger.Info("✅ RELEASE VALIDATION COMPLETE: {0}", result.Reason);
            return result;
        }

        /// <summary>
        /// Performs critical compatibility checks that would prevent safe metadata optimization
        /// </summary>
        private string ValidateCriticalCompatibility(LidarrAlbum lidarrAlbum, QobuzAlbum qobuzAlbum)
        {
            // Check 1: Extreme track count differences (indicates completely different releases)
            var trackCountDiff = Math.Abs(lidarrAlbum.TrackCount - qobuzAlbum.TracksCount);
            if (trackCountDiff > MAX_TRACK_COUNT_VARIANCE + 3) // Allow bonus content + some tolerance
            {
                return $"Track count difference too large: {lidarrAlbum.TrackCount} vs {qobuzAlbum.TracksCount} (difference: {trackCountDiff})";
            }

            // Check 2: Major album duration differences (indicates different mastering/versions)
            var durationDiff = CalculateAlbumDurationDifference(lidarrAlbum, qobuzAlbum);
            if (durationDiff > TimeSpan.FromMinutes(MAX_ALBUM_DURATION_VARIANCE_MINUTES + 5)) // Extra tolerance for critical check
            {
                return $"Duration difference too large: {durationDiff.TotalMinutes:F1} minutes (limit: {MAX_ALBUM_DURATION_VARIANCE_MINUTES + 5} minutes)";
            }

            // Check 3: Artist name mismatch (indicates wrong album entirely)
            if (!AreArtistNamesCompatible(lidarrAlbum.ArtistName, qobuzAlbum.GetArtistName()))
            {
                return $"Artist name mismatch: '{lidarrAlbum.ArtistName}' vs '{qobuzAlbum.GetArtistName()}'";
            }

            // Check 4: Album title completely different (indicates wrong match)
            var titleSimilarity = CalculateStringSimilarity(
                NormalizeTitle(lidarrAlbum.Title), 
                NormalizeTitle(qobuzAlbum.Title));
            
            if (titleSimilarity < 0.5) // Very low threshold for critical check
            {
                return $"Album titles too different: '{lidarrAlbum.Title}' vs '{qobuzAlbum.Title}' (similarity: {titleSimilarity:P1})";
            }

            return null; // No critical issues found
        }

        /// <summary>
        /// Analyzes differences between releases that might indicate different editions
        /// </summary>
        private EditionAnalysis AnalyzeEditionDifferences(LidarrAlbum lidarrAlbum, QobuzAlbum qobuzAlbum)
        {
            var analysis = new EditionAnalysis();
            var reasons = new List<string>();

            // Check for edition indicators in titles
            var lidarrHasEdition = HasEditionIndicators(lidarrAlbum.Title);
            var qobuzHasEdition = HasEditionIndicators(qobuzAlbum.Title);

            if (qobuzHasEdition && !lidarrHasEdition)
            {
                reasons.Add($"Qobuz appears to be special edition: '{qobuzAlbum.Title}'");
                analysis.QobuzIsSpecialEdition = true;
            }
            else if (lidarrHasEdition && !qobuzHasEdition)
            {
                reasons.Add($"Lidarr appears to be special edition: '{lidarrAlbum.Title}'");
                analysis.LidarrIsSpecialEdition = true;
            }

            // Check track count differences that suggest bonus content
            if (qobuzAlbum.TracksCount > lidarrAlbum.TrackCount)
            {
                var extraTracks = qobuzAlbum.TracksCount - lidarrAlbum.TrackCount;
                reasons.Add($"Qobuz has {extraTracks} additional tracks (likely bonus content)");
                analysis.QobuzHasBonusContent = true;
                analysis.ExtraTrackCount = extraTracks;
            }

            // Check release year variance (suggests remaster)
            var yearDiff = Math.Abs((lidarrAlbum.ReleaseYear ?? 0) - (qobuzAlbum.ReleaseDate.Year));
            if (yearDiff > MAX_RELEASE_YEAR_VARIANCE)
            {
                reasons.Add($"Release year variance: {lidarrAlbum.ReleaseYear} vs {qobuzAlbum.ReleaseDate.Year} (difference: {yearDiff} years)");
                analysis.IsPossibleRemaster = true;
            }

            // Analyze track mismatches for hybrid approach planning
            analysis.TrackMismatches = IdentifyTrackMismatches(lidarrAlbum, qobuzAlbum);

            analysis.HasSignificantDifferences = reasons.Any();
            analysis.Reason = string.Join("; ", reasons);

            return analysis;
        }

        /// <summary>
        /// Validates the quality of track matching between releases
        /// </summary>
        private TrackMatchResult ValidateTrackMatching(LidarrAlbum lidarrAlbum, QobuzAlbum qobuzAlbum)
        {
            var successfulMatches = 0;
            var totalTracks = Math.Min(lidarrAlbum.TrackCount, qobuzAlbum.TracksCount); // Only consider overlapping tracks
            var matchDetails = new List<TrackMatchDetail>();

            foreach (var lidarrTrack in lidarrAlbum.Tracks.Take(totalTracks))
            {
                var bestMatch = FindBestQobuzTrackMatch(lidarrTrack, qobuzAlbum.GetTracks());
                if (bestMatch != null)
                {
                    var similarity = CalculateStringSimilarity(lidarrTrack.Title, bestMatch.Title);
                    var durationDiff = Math.Abs(lidarrTrack.Duration.TotalSeconds - bestMatch.Duration.TotalSeconds);
                    
                    var matchDetail = new TrackMatchDetail
                    {
                        LidarrTrack = lidarrTrack,
                        QobuzTrack = bestMatch,
                        TitleSimilarity = similarity,
                        DurationDifferenceSeconds = durationDiff,
                        IsAcceptableMatch = similarity >= MIN_TRACK_TITLE_SIMILARITY && durationDiff <= MAX_TRACK_DURATION_VARIANCE_SECONDS
                    };
                    
                    matchDetails.Add(matchDetail);
                    
                    if (matchDetail.IsAcceptableMatch)
                    {
                        successfulMatches++;
                    }
                }
            }

            var matchRate = totalTracks > 0 ? (double)successfulMatches / totalTracks : 0;
            var isAcceptable = matchRate >= MIN_CORE_TRACK_MATCH_RATE;

            var reason = isAcceptable 
                ? $"{successfulMatches}/{totalTracks} tracks matched successfully ({matchRate:P1})"
                : $"Only {successfulMatches}/{totalTracks} tracks matched ({matchRate:P1}), below {MIN_CORE_TRACK_MATCH_RATE:P1} threshold";

            return new TrackMatchResult
            {
                MatchRate = matchRate,
                SuccessfulMatches = successfulMatches,
                TotalTracksAnalyzed = totalTracks,
                IsAcceptable = isAcceptable,
                Reason = reason,
                MatchDetails = matchDetails
            };
        }

        /// <summary>
        /// Checks for minor compatibility issues that don't prevent optimization but should be logged
        /// </summary>
        private List<string> ValidateMinorCompatibilityIssues(LidarrAlbum lidarrAlbum, QobuzAlbum qobuzAlbum)
        {
            var issues = new List<string>();

            // Minor track count differences
            var trackCountDiff = Math.Abs(lidarrAlbum.TrackCount - qobuzAlbum.TracksCount);
            if (trackCountDiff > 0 && trackCountDiff <= MAX_TRACK_COUNT_VARIANCE)
            {
                issues.Add($"{trackCountDiff} track count difference (within tolerance)");
            }

            // Minor duration differences
            var durationDiff = CalculateAlbumDurationDifference(lidarrAlbum, qobuzAlbum);
            if (durationDiff > TimeSpan.FromMinutes(1) && durationDiff <= TimeSpan.FromMinutes(MAX_ALBUM_DURATION_VARIANCE_MINUTES))
            {
                issues.Add($"{durationDiff.TotalMinutes:F1} minute duration difference (within tolerance)");
            }

            // Genre differences (informational)
            if (lidarrAlbum.Genres?.Any() == true && qobuzAlbum.GenresList?.Any() == true)
            {
                var sharedGenres = lidarrAlbum.Genres.Intersect(qobuzAlbum.GenresList, StringComparer.OrdinalIgnoreCase).Count();
                var totalUniqueGenres = lidarrAlbum.Genres.Union(qobuzAlbum.GenresList, StringComparer.OrdinalIgnoreCase).Count();
                
                if (totalUniqueGenres > 0 && (double)sharedGenres / totalUniqueGenres < 0.5)
                {
                    issues.Add($"Genre differences detected (informational)");
                }
            }

            return issues;
        }

        /// <summary>
        /// Identifies specific track mismatches for hybrid approach planning
        /// </summary>
        private List<TrackMismatch> IdentifyTrackMismatches(LidarrAlbum lidarrAlbum, QobuzAlbum qobuzAlbum)
        {
            var mismatches = new List<TrackMismatch>();
            var qobuzTracksUsed = new HashSet<int>();

            // Find Lidarr tracks that don't match well with Qobuz tracks
            foreach (var lidarrTrack in lidarrAlbum.Tracks)
            {
                var bestMatch = FindBestQobuzTrackMatch(lidarrTrack, qobuzAlbum.GetTracks().Where(t => !qobuzTracksUsed.Contains(t.TrackNumber)));
                
                if (bestMatch == null || CalculateStringSimilarity(lidarrTrack.Title, bestMatch.Title) < MIN_TRACK_TITLE_SIMILARITY)
                {
                    mismatches.Add(new TrackMismatch
                    {
                        LidarrTrack = lidarrTrack,
                        QobuzTrack = bestMatch,
                        MismatchType = bestMatch == null ? "NoMatch" : "PoorMatch",
                        Reason = bestMatch == null ? "No corresponding track found" : "Track titles too different"
                    });
                }
                else
                {
                    qobuzTracksUsed.Add(bestMatch.TrackNumber);
                }
            }

            // Find Qobuz tracks that don't match with Lidarr (likely bonus content)
            foreach (var qobuzTrack in qobuzAlbum.GetTracks().Where(t => !qobuzTracksUsed.Contains(t.TrackNumber)))
            {
                mismatches.Add(new TrackMismatch
                {
                    QobuzTrack = qobuzTrack,
                    MismatchType = "BonusContent",
                    Reason = "Track exists in Qobuz but not in Lidarr (likely bonus content)"
                });
            }

            return mismatches;
        }

        /// <summary>
        /// Finds the best matching Qobuz track for a given Lidarr track
        /// </summary>
        private QobuzTrack FindBestQobuzTrackMatch(LidarrTrack lidarrTrack, IEnumerable<QobuzTrack> qobuzTracks)
        {
            QobuzTrack? bestMatch = null;
            double bestScore = 0;

            foreach (var qobuzTrack in qobuzTracks)
            {
                var score = CalculateTrackMatchScore(lidarrTrack, qobuzTrack);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = qobuzTrack;
                }
            }

            return bestScore >= MIN_TRACK_TITLE_SIMILARITY ? bestMatch : null;
        }

        /// <summary>
        /// Calculates a comprehensive match score between Lidarr and Qobuz tracks
        /// </summary>
        private double CalculateTrackMatchScore(LidarrTrack lidarrTrack, QobuzTrack qobuzTrack)
        {
            double score = 0;

            // Title similarity (most important - 70% weight)
            var titleSimilarity = CalculateStringSimilarity(lidarrTrack.Title, qobuzTrack.Title);
            score += titleSimilarity * 0.7;

            // Track number match (20% weight)
            if (lidarrTrack.TrackNumber == qobuzTrack.TrackNumber)
            {
                score += 0.2;
            }

            // Duration similarity (10% weight)
            if (lidarrTrack.Duration != TimeSpan.Zero)
            {
                var durationDiff = Math.Abs(lidarrTrack.Duration.TotalSeconds - qobuzTrack.Duration.TotalSeconds);
                if (durationDiff <= MAX_TRACK_DURATION_VARIANCE_SECONDS)
                {
                    var durationScore = 1.0 - (durationDiff / MAX_TRACK_DURATION_VARIANCE_SECONDS);
                    score += durationScore * 0.1;
                }
            }

            return score;
        }

        /// <summary>
        /// Calculates total album duration difference between releases
        /// </summary>
        private TimeSpan CalculateAlbumDurationDifference(LidarrAlbum lidarrAlbum, QobuzAlbum qobuzAlbum)
        {
            var lidarrDuration = lidarrAlbum.Tracks?.Sum(t => t.Duration.TotalSeconds) ?? 0;
            var qobuzDuration = qobuzAlbum.GetTracks()?.Sum(t => t.Duration.TotalSeconds) ?? 0;
            
            return TimeSpan.FromSeconds(Math.Abs(lidarrDuration - qobuzDuration));
        }

        /// <summary>
        /// Checks if artist names are compatible allowing for common variations
        /// </summary>
        private bool AreArtistNamesCompatible(string lidarrArtist, string qobuzArtist)
        {
            if (string.IsNullOrWhiteSpace(lidarrArtist) || string.IsNullOrWhiteSpace(qobuzArtist))
                return false;

            var normalizedLidarr = NormalizeArtistName(lidarrArtist);
            var normalizedQobuz = NormalizeArtistName(qobuzArtist);

            // Check exact match after normalization
            if (normalizedLidarr.Equals(normalizedQobuz, StringComparison.OrdinalIgnoreCase))
                return true;

            // Check similarity score
            var similarity = CalculateStringSimilarity(normalizedLidarr, normalizedQobuz);
            return similarity >= 0.8; // 80% similarity threshold for artist names
        }

        /// <summary>
        /// Normalizes artist names for comparison by removing common variations
        /// </summary>
        private string NormalizeArtistName(string artistName)
        {
            if (string.IsNullOrWhiteSpace(artistName))
                return "";

            var normalized = artistName.Trim();
            
            // Remove "The" prefix variations
            normalized = Regex.Replace(normalized, @"^(The\s+)", "", RegexOptions.IgnoreCase);
            
            // Normalize punctuation and spacing
            normalized = Regex.Replace(normalized, @"[^\w\s]", "");
            normalized = Regex.Replace(normalized, @"\s+", " ");
            
            return normalized.Trim();
        }

        /// <summary>
        /// Normalizes album titles for comparison by removing edition indicators and common variations
        /// </summary>
        private string NormalizeTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return "";

            var normalized = title.Trim().ToLowerInvariant();
            
            // Remove edition indicators
            foreach (var indicator in EditionIndicators)
            {
                normalized = Regex.Replace(normalized, @$"\b{Regex.Escape(indicator)}\b", "", RegexOptions.IgnoreCase);
            }
            
            // Remove year patterns
            normalized = Regex.Replace(normalized, @"\s*[\(\[]?\d{4}[\)\]]?\s*", "");
            
            // Remove extra punctuation and whitespace
            normalized = Regex.Replace(normalized, @"[^\w\s]", "");
            normalized = Regex.Replace(normalized, @"\s+", " ");
            
            return normalized.Trim();
        }

        /// <summary>
        /// Checks if a title contains edition indicators
        /// </summary>
        private bool HasEditionIndicators(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return false;

            return EditionIndicators.Any(indicator => 
                title.Contains(indicator, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Calculates string similarity using Levenshtein distance normalized by length
        /// </summary>
        private double CalculateStringSimilarity(string s1, string s2)
        {
            return StringSimilarity.Calculate(s1, s2);
        }

    }

    #region Result Classes

    /// <summary>
    /// Result of release matching analysis with compatibility determination
    /// </summary>
    public class ReleaseMatchResult
    {
        /// <summary>
        /// Whether the releases are compatible for metadata optimization
        /// </summary>
        public bool IsCompatible { get; set; }

        /// <summary>
        /// Whether a hybrid approach is recommended (use both Lidarr and Qobuz metadata)
        /// </summary>
        public bool RequiresHybridApproach { get; set; }

        /// <summary>
        /// Explanation of the match result
        /// </summary>
        public string Reason { get; set; }

        /// <summary>
        /// Confidence score for the match (0.0 to 1.0)
        /// </summary>
        public double MatchConfidence { get; set; }

        /// <summary>
        /// Detailed track matching results
        /// </summary>
        public TrackMatchResult TrackMatchResult { get; set; }

        /// <summary>
        /// Edition analysis results
        /// </summary>
        public EditionAnalysis EditionAnalysis { get; set; }

        /// <summary>
        /// Identified track mismatches for hybrid approach planning
        /// </summary>
        public List<TrackMismatch> TrackMismatches { get; set; } = new();

        public static ReleaseMatchResult Compatible(string reason = null) => new() 
        { 
            IsCompatible = true, 
            Reason = reason ?? "Releases are compatible",
            MatchConfidence = 0.9
        };

        public static ReleaseMatchResult Incompatible(string reason) => new() 
        { 
            IsCompatible = false, 
            Reason = reason,
            MatchConfidence = 0.0
        };

        public static ReleaseMatchResult RequiresHybrid(string reason, List<TrackMismatch> trackMismatches = null) => new() 
        { 
            IsCompatible = true, 
            RequiresHybridApproach = true, 
            Reason = reason,
            TrackMismatches = trackMismatches ?? new(),
            MatchConfidence = 0.7
        };
    }

    /// <summary>
    /// Analysis of edition differences between releases
    /// </summary>
    public class EditionAnalysis
    {
        public bool HasSignificantDifferences { get; set; }
        public string Reason { get; set; }
        public bool QobuzIsSpecialEdition { get; set; }
        public bool LidarrIsSpecialEdition { get; set; }
        public bool QobuzHasBonusContent { get; set; }
        public int ExtraTrackCount { get; set; }
        public bool IsPossibleRemaster { get; set; }
        public List<TrackMismatch> TrackMismatches { get; set; } = new();
    }

    /// <summary>
    /// Result of track matching analysis
    /// </summary>
    public class TrackMatchResult
    {
        public double MatchRate { get; set; }
        public int SuccessfulMatches { get; set; }
        public int TotalTracksAnalyzed { get; set; }
        public bool IsAcceptable { get; set; }
        public string Reason { get; set; }
        public List<TrackMatchDetail> MatchDetails { get; set; } = new();
    }

    /// <summary>
    /// Detailed information about a track match attempt
    /// </summary>
    public class TrackMatchDetail
    {
        public LidarrTrack LidarrTrack { get; set; }
        public QobuzTrack QobuzTrack { get; set; }
        public double TitleSimilarity { get; set; }
        public double DurationDifferenceSeconds { get; set; }
        public bool IsAcceptableMatch { get; set; }
    }

    /// <summary>
    /// Information about a track mismatch for hybrid approach planning
    /// </summary>
    public class TrackMismatch
    {
        public LidarrTrack LidarrTrack { get; set; }
        public QobuzTrack QobuzTrack { get; set; }
        public string MismatchType { get; set; } // NoMatch, PoorMatch, BonusContent
        public string Reason { get; set; }
    }

    #endregion
}