using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Specialized detector for compilation albums and various artists scenarios
    /// Prevents systematic optimization failures for compilations, soundtracks, and mixed-artist albums
    /// </summary>
    /// <remarks>
    /// Critical Issue: Various Artists albums often fail optimization because:
    /// - Lidarr: Album Artist = "Various Artists", individual track artists
    /// - Qobuz: Album Artist = "First Artist" or different attribution pattern
    /// - Standard matching fails due to artist mismatch
    /// 
    /// This detector:
    /// 1. Identifies compilation patterns in both platforms
    /// 2. Applies specialized matching logic for various artists scenarios  
    /// 3. Enables optimization for entire category of albums that would otherwise fail
    /// 4. Handles soundtracks, tribute albums, genre compilations, DJ mixes
    /// </remarks>
    public class CompilationAlbumDetector
    {
        private readonly Logger _logger;

        // Various Artists identification patterns
        private static readonly string[] VariousArtistsPatterns =
        {
            "various artists", "v.a.", "va", "various", "compilation", "mixed",
            "sampler", "collection", "anthology", "tribute", "best of various"
        };

        // Soundtrack identification patterns
        private static readonly string[] SoundtrackPatterns =
        {
            "soundtrack", "ost", "original soundtrack", "motion picture soundtrack",
            "movie soundtrack", "film soundtrack", "tv soundtrack", "television soundtrack",
            "game soundtrack", "video game soundtrack", "original motion picture",
            "score", "theme", "themes"
        };

        // Compilation album title patterns
        private static readonly Regex[] CompilationTitleRegexes =
        {
            new(@"\b(?:greatest\s+hits?|best\s+of|the\s+very\s+best|ultimate\s+collection)\b", RegexOptions.IgnoreCase),
            new(@"\b(?:anthology|collection|compilation|sampler|mixed\s+by)\b", RegexOptions.IgnoreCase),
            new(@"\b(?:tribute\s+to|in\s+memory\s+of|covers?)\b", RegexOptions.IgnoreCase),
            new(@"\b(?:dj\s+mix|mixed\s+by|selected\s+by)\b", RegexOptions.IgnoreCase),
            new(@"\b(?:vol\.?\s*\d+|volume\s+\d+|part\s+\d+)\b", RegexOptions.IgnoreCase),
            new(@"\b(?:\d{4}\s*-\s*\d{4}|spanning\s+\d+\s+years)\b", RegexOptions.IgnoreCase) // Year spans
        };

        // Genre compilation patterns
        private static readonly string[] GenreCompilationPatterns =
        {
            "now that's what i call", "hits of", "chart hits", "radio hits",
            "dance hits", "rock anthems", "pop classics", "jazz standards",
            "country classics", "hip hop essentials", "electronic collection"
        };

        public CompilationAlbumDetector(Logger logger = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
        }

        /// <summary>
        /// Analyzes albums to determine if they are compilations and how they should be handled
        /// </summary>
        /// <param name="lidarrAlbum">Album from Lidarr with MusicBrainz metadata</param>
        /// <param name="qobuzAlbum">Album from Qobuz search results</param>
        /// <returns>Compilation analysis result with handling recommendations</returns>
        public CompilationAnalysisResult AnalyzeCompilationStatus(LidarrAlbum lidarrAlbum, QobuzAlbum qobuzAlbum)
        {
            if (lidarrAlbum == null || qobuzAlbum == null)
            {
                return new CompilationAnalysisResult
                {
                    IsCompilation = false,
                    ErrorMessage = "Missing album data for compilation analysis"
                };
            }

            _logger.Debug("🎭 COMPILATION ANALYSIS: '{0}' vs '{1}'", lidarrAlbum.Title, qobuzAlbum.Title);

            var result = new CompilationAnalysisResult
            {
                LidarrAlbum = lidarrAlbum,
                QobuzAlbum = qobuzAlbum
            };

            // Analyze both albums for compilation indicators
            var lidarrCompilation = AnalyzeSingleAlbumForCompilation(lidarrAlbum, "Lidarr");
            var qobuzCompilation = AnalyzeSingleAlbumForCompilation(qobuzAlbum, "Qobuz");

            // Determine overall compilation status
            DetermineCompilationStatus(result, lidarrCompilation, qobuzCompilation);

            // Analyze artist attribution patterns
            AnalyzeArtistAttributionPatterns(result);

            // Determine matching strategy
            DetermineOptimizationStrategy(result);

            _logger.Info("🎭 COMPILATION RESULT: {0} (Confidence: {1:P1}, Strategy: {2})", 
                        result.CompilationType, result.Confidence, result.RecommendedStrategy);

            return result;
        }

        /// <summary>
        /// Analyzes a single album for compilation indicators
        /// </summary>
        private SingleAlbumCompilationAnalysis AnalyzeSingleAlbumForCompilation(object album, string source)
        {
            var analysis = new SingleAlbumCompilationAnalysis { Source = source };

            string albumTitle = "";
            string albumArtist = "";
            List<object> tracks = new();
            
            // Extract album information based on type
            if (album is LidarrAlbum lidarrAlbum)
            {
                albumTitle = lidarrAlbum.Title;
                albumArtist = lidarrAlbum.ArtistName;
                tracks = lidarrAlbum.Tracks?.Cast<object>().ToList() ?? new List<object>();
            }
            else if (album is QobuzAlbum qobuzAlbum)
            {
                albumTitle = qobuzAlbum.Title;
                albumArtist = qobuzAlbum.GetArtistName();
                tracks = qobuzAlbum.GetTracks()?.Cast<object>().ToList() ?? new List<object>();
            }

            // Check for explicit various artists attribution
            analysis.HasVariousArtistsAttribution = CheckVariousArtistsAttribution(albumArtist);

            // Check compilation title patterns
            analysis.HasCompilationTitlePattern = CheckCompilationTitlePatterns(albumTitle);

            // Check for soundtrack indicators
            analysis.IsSoundtrack = CheckSoundtrackPatterns(albumTitle);

            // Analyze track artist diversity
            analysis.TrackArtistDiversity = CalculateTrackArtistDiversity(tracks);

            // Check for genre compilation patterns
            analysis.IsGenreCompilation = CheckGenreCompilationPatterns(albumTitle);

            // Calculate overall compilation confidence
            analysis.CompilationConfidence = CalculateCompilationConfidence(analysis);

            _logger.Debug("📊 {0} Analysis: VA={1}, Title={2}, Soundtrack={3}, Diversity={4:P1}, Confidence={5:P1}",
                         source, analysis.HasVariousArtistsAttribution, analysis.HasCompilationTitlePattern,
                         analysis.IsSoundtrack, analysis.TrackArtistDiversity, analysis.CompilationConfidence);

            return analysis;
        }

        /// <summary>
        /// Determines overall compilation status by combining analysis from both platforms
        /// </summary>
        private void DetermineCompilationStatus(
            CompilationAnalysisResult result,
            SingleAlbumCompilationAnalysis lidarrAnalysis,
            SingleAlbumCompilationAnalysis qobuzAnalysis)
        {
            result.LidarrAnalysis = lidarrAnalysis;
            result.QobuzAnalysis = qobuzAnalysis;

            // Determine if either platform indicates compilation
            var isLidarrCompilation = lidarrAnalysis.CompilationConfidence >= 0.7;
            var isQobuzCompilation = qobuzAnalysis.CompilationConfidence >= 0.7;

            if (isLidarrCompilation || isQobuzCompilation)
            {
                result.IsCompilation = true;
                result.Confidence = Math.Max(lidarrAnalysis.CompilationConfidence, qobuzAnalysis.CompilationConfidence);

                // Determine specific compilation type
                if (lidarrAnalysis.IsSoundtrack || qobuzAnalysis.IsSoundtrack)
                    result.CompilationType = "Soundtrack";
                else if (lidarrAnalysis.HasVariousArtistsAttribution || qobuzAnalysis.HasVariousArtistsAttribution)
                    result.CompilationType = "VariousArtists";
                else if (lidarrAnalysis.IsGenreCompilation || qobuzAnalysis.IsGenreCompilation)
                    result.CompilationType = "GenreCompilation";
                else if (lidarrAnalysis.TrackArtistDiversity > 0.6 || qobuzAnalysis.TrackArtistDiversity > 0.6)
                    result.CompilationType = "MixedArtists";
                else
                    result.CompilationType = "Compilation";
            }
            else
            {
                result.IsCompilation = false;
                result.CompilationType = "StandardAlbum";
                result.Confidence = Math.Min(1.0 - lidarrAnalysis.CompilationConfidence, 1.0 - qobuzAnalysis.CompilationConfidence);
            }
        }

        /// <summary>
        /// Analyzes how artists are attributed differently between platforms
        /// </summary>
        private void AnalyzeArtistAttributionPatterns(CompilationAnalysisResult result)
        {
            var patterns = new List<string>();

            // Check if one platform uses "Various Artists" while the other doesn't
            var lidarrHasVA = result.LidarrAnalysis.HasVariousArtistsAttribution;
            var qobuzHasVA = result.QobuzAnalysis.HasVariousArtistsAttribution;

            if (lidarrHasVA && !qobuzHasVA)
            {
                patterns.Add("Lidarr uses Various Artists, Qobuz attributes to specific artist");
                result.HasAttributionMismatch = true;
            }
            else if (!lidarrHasVA && qobuzHasVA)
            {
                patterns.Add("Qobuz uses Various Artists, Lidarr attributes to specific artist");
                result.HasAttributionMismatch = true;
            }

            // Check for track artist diversity differences
            var diversityDiff = Math.Abs(result.LidarrAnalysis.TrackArtistDiversity - result.QobuzAnalysis.TrackArtistDiversity);
            if (diversityDiff > 0.3)
            {
                patterns.Add($"Different track artist diversity patterns (diff: {diversityDiff:P1})");
                result.HasAttributionMismatch = true;
            }

            result.AttributionPatterns = patterns;
            
            if (result.HasAttributionMismatch)
            {
                _logger.Debug("🎭 ATTRIBUTION MISMATCH: {0}", string.Join("; ", patterns));
            }
        }

        /// <summary>
        /// Determines the optimal strategy for handling this compilation
        /// </summary>
        private void DetermineOptimizationStrategy(CompilationAnalysisResult result)
        {
            if (!result.IsCompilation)
            {
                result.RecommendedStrategy = "StandardOptimization";
                result.CanUseOptimization = true;
                return;
            }

            // For compilations, determine if optimization is safe
            if (result.HasAttributionMismatch)
            {
                if (result.Confidence >= 0.9)
                {
                    // High confidence compilation with attribution mismatch
                    result.RecommendedStrategy = "CompilationHybridOptimization";
                    result.CanUseOptimization = true;
                    result.OptimizationNotes = "Use track-level matching with compilation-aware logic";
                }
                else
                {
                    // Medium confidence - be conservative  
                    result.RecommendedStrategy = "FallbackToQobuz";
                    result.CanUseOptimization = false;
                    result.OptimizationNotes = "Attribution patterns too different, use Qobuz metadata for safety";
                }
            }
            else
            {
                // No attribution mismatch - safe to optimize
                result.RecommendedStrategy = "CompilationOptimization";
                result.CanUseOptimization = true;
                result.OptimizationNotes = "Compilation patterns consistent across platforms";
            }

            // Special handling for soundtracks
            if (result.CompilationType == "Soundtrack")
            {
                result.RecommendedStrategy = "SoundtrackOptimization";
                result.OptimizationNotes = "Apply soundtrack-specific matching rules";
            }
        }

        #region Analysis Helper Methods

        private bool CheckVariousArtistsAttribution(string albumArtist)
        {
            if (string.IsNullOrWhiteSpace(albumArtist))
                return false;

            var normalizedArtist = albumArtist.ToLowerInvariant().Trim();
            return VariousArtistsPatterns.Any(pattern => normalizedArtist.Contains(pattern));
        }

        private bool CheckCompilationTitlePatterns(string albumTitle)
        {
            if (string.IsNullOrWhiteSpace(albumTitle))
                return false;

            return CompilationTitleRegexes.Any(regex => regex.IsMatch(albumTitle));
        }

        private bool CheckSoundtrackPatterns(string albumTitle)
        {
            if (string.IsNullOrWhiteSpace(albumTitle))
                return false;

            var normalizedTitle = albumTitle.ToLowerInvariant();
            return SoundtrackPatterns.Any(pattern => normalizedTitle.Contains(pattern));
        }

        private bool CheckGenreCompilationPatterns(string albumTitle)
        {
            if (string.IsNullOrWhiteSpace(albumTitle))
                return false;

            var normalizedTitle = albumTitle.ToLowerInvariant();
            return GenreCompilationPatterns.Any(pattern => normalizedTitle.Contains(pattern));
        }

        private double CalculateTrackArtistDiversity(List<object> tracks)
        {
            if (!tracks.Any())
                return 0;

            var artists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var track in tracks)
            {
                string artistName = "";
                
                if (track is LidarrTrack lidarrTrack)
                    artistName = lidarrTrack.ArtistName;
                else if (track is QobuzTrack qobuzTrack)
                    artistName = qobuzTrack.GetPerformerName();

                if (!string.IsNullOrWhiteSpace(artistName))
                    artists.Add(artistName.Trim());
            }

            // Return ratio of unique artists to total tracks
            return Math.Min(1.0, (double)artists.Count / tracks.Count);
        }

        private double CalculateCompilationConfidence(SingleAlbumCompilationAnalysis analysis)
        {
            double confidence = 0;

            // Various Artists attribution (strong indicator)
            if (analysis.HasVariousArtistsAttribution)
                confidence += 0.4;

            // Compilation title patterns (medium indicator)
            if (analysis.HasCompilationTitlePattern)
                confidence += 0.3;

            // Soundtrack (strong indicator for specific type)
            if (analysis.IsSoundtrack)
                confidence += 0.4;

            // High track artist diversity (medium indicator)
            if (analysis.TrackArtistDiversity > 0.6)
                confidence += 0.25;
            else if (analysis.TrackArtistDiversity > 0.4)
                confidence += 0.15;

            // Genre compilation patterns (medium indicator)
            if (analysis.IsGenreCompilation)
                confidence += 0.3;

            return Math.Min(1.0, confidence);
        }

        #endregion
    }

    #region Result Classes

    /// <summary>
    /// Result of compilation album analysis
    /// </summary>
    public class CompilationAnalysisResult
    {
        public LidarrAlbum LidarrAlbum { get; set; }
        public QobuzAlbum QobuzAlbum { get; set; }
        
        public bool IsCompilation { get; set; }
        public string CompilationType { get; set; } = "StandardAlbum";
        public double Confidence { get; set; }
        
        public SingleAlbumCompilationAnalysis LidarrAnalysis { get; set; }
        public SingleAlbumCompilationAnalysis QobuzAnalysis { get; set; }
        
        public bool HasAttributionMismatch { get; set; }
        public List<string> AttributionPatterns { get; set; } = new();
        
        public string RecommendedStrategy { get; set; }
        public bool CanUseOptimization { get; set; }
        public string OptimizationNotes { get; set; }
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Creates specialized track matching parameters for compilation albums
        /// </summary>
        public CompilationMatchingParameters GetMatchingParameters()
        {
            return new CompilationMatchingParameters
            {
                UseTrackLevelMatching = IsCompilation && CanUseOptimization,
                IgnoreAlbumArtistMismatch = HasAttributionMismatch,
                RequireHighTrackSimilarity = CompilationType == "VariousArtists",
                AllowPartialMatching = CompilationType == "Soundtrack",
                MinimumMatchRate = CompilationType == "MixedArtists" ? 0.7 : 0.8
            };
        }
    }

    /// <summary>
    /// Analysis results for a single album
    /// </summary>
    public class SingleAlbumCompilationAnalysis
    {
        public string Source { get; set; }
        public bool HasVariousArtistsAttribution { get; set; }
        public bool HasCompilationTitlePattern { get; set; }
        public bool IsSoundtrack { get; set; }
        public bool IsGenreCompilation { get; set; }
        public double TrackArtistDiversity { get; set; }
        public double CompilationConfidence { get; set; }
    }

    /// <summary>
    /// Specialized matching parameters for compilation albums
    /// </summary>
    public class CompilationMatchingParameters
    {
        public bool UseTrackLevelMatching { get; set; }
        public bool IgnoreAlbumArtistMismatch { get; set; }
        public bool RequireHighTrackSimilarity { get; set; }
        public bool AllowPartialMatching { get; set; }
        public double MinimumMatchRate { get; set; } = 0.8;
    }

    #endregion
}