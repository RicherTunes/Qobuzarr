using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Live album title normalization service that handles venue information and date variations
    /// Prevents matching failures for live albums due to inconsistent venue/date formatting
    /// </summary>
    /// <remarks>
    /// Critical Issue: Live albums often fail matching due to:
    /// - Inconsistent venue formatting: "Live at Madison Square Garden" vs "Live from MSG"
    /// - Date format variations: "2023-12-31" vs "December 31, 2023" vs "New Year's Eve 2023"
    /// - Regional venue name differences: "Royal Albert Hall" vs "Albert Hall"
    /// - Recording context variations: "Live at", "Recorded at", "From", etc.
    /// 
    /// This normalizer:
    /// 1. Identifies live album patterns and extracts core album title
    /// 2. Normalizes venue names to canonical forms
    /// 3. Standardizes date formats for consistent comparison
    /// 4. Creates fuzzy matching patterns for venue variations
    /// 5. Enables optimization for live album catalogs that would otherwise fail
    /// </remarks>
    public class LiveAlbumNormalizer
    {
        private readonly Logger _logger;
        
        // Live album context indicators
        private static readonly string[] LiveContextMarkers =
        {
            "live", "concert", "recorded", "performance", "show", "tour",
            "festival", "session", "unplugged", "acoustic", "mtv", "bbc"
        };
        
        // Common venue prefixes that should be normalized
        private static readonly Dictionary<string, string[]> VenuePatterns = new()
        {
            // Theater and concert halls
            ["theater"] = new[] { "theatre", "theater", "playhouse", "opera house", "concert hall" },
            ["arena"] = new[] { "arena", "stadium", "dome", "center", "centre", "coliseum", "colosseum" },
            ["club"] = new[] { "club", "bar", "pub", "tavern", "lounge", "café", "cafe" },
            ["festival"] = new[] { "festival", "fest", "celebration", "gathering", "jamboree" },
            
            // Famous venue normalizations
            ["madison square garden"] = new[] { "msg", "madison square garden", "the garden" },
            ["royal albert hall"] = new[] { "albert hall", "royal albert hall", "rah" },
            ["wembley"] = new[] { "wembley stadium", "wembley arena", "wembley" },
            ["red rocks"] = new[] { "red rocks amphitheatre", "red rocks amphitheater", "red rocks" },
            ["hollywood bowl"] = new[] { "hollywood bowl", "the bowl" },
            ["carnegie hall"] = new[] { "carnegie hall", "carnegie" }
        };
        
        // Date format patterns for normalization (using compile-time generated regexes)
        private static readonly Regex[] DatePatterns =
        {
            LiveAlbumNormalizerRegexes.IsoDate(),
            LiveAlbumNormalizerRegexes.UsDate(),
            LiveAlbumNormalizerRegexes.MonthNameDate(),
            LiveAlbumNormalizerRegexes.YearRange(),
            LiveAlbumNormalizerRegexes.SimpleYear()
        };

        // Live album title patterns for extraction (using compile-time generated regexes)
        private static readonly Regex[] LiveAlbumPatterns =
        {
            LiveAlbumNormalizerRegexes.LiveAtVenueParentheses(),
            LiveAlbumNormalizerRegexes.LiveAtVenueDash(),
            LiveAlbumNormalizerRegexes.VenueColonTitle(),
            LiveAlbumNormalizerRegexes.TitleSuffixLive(),
            LiveAlbumNormalizerRegexes.SpecialSessionPrefix()
        };
        
        // Special live album markers that indicate recording context
        private static readonly Dictionary<string, string> SpecialLiveMarkers = new()
        {
            ["mtv unplugged"] = "MTV Unplugged",
            ["bbc session"] = "BBC Session", 
            ["bbc live"] = "BBC Session",
            ["live session"] = "Live Session",
            ["acoustic session"] = "Acoustic Session",
            ["radio session"] = "Radio Session",
            ["studio session"] = "Studio Session"
        };

        public LiveAlbumNormalizer(Logger logger = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
        }

        /// <summary>
        /// Normalizes live album titles for better matching by extracting core titles and standardizing venue/date info
        /// </summary>
        /// <param name="albumTitle">Original album title to normalize</param>
        /// <param name="options">Normalization options for different use cases</param>
        /// <returns>Normalized title result with extracted components</returns>
        public LiveAlbumNormalizationResult NormalizeLiveAlbum(string albumTitle, LiveAlbumNormalizationOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(albumTitle))
            {
                return new LiveAlbumNormalizationResult
                {
                    IsLiveAlbum = false,
                    NormalizedTitle = string.Empty,
                    OriginalTitle = albumTitle ?? string.Empty
                };
            }

            options ??= LiveAlbumNormalizationOptions.Default;
            
            _logger.Debug("🎤 LIVE ALBUM NORMALIZATION: '{0}'", albumTitle);

            var result = new LiveAlbumNormalizationResult
            {
                OriginalTitle = albumTitle,
                IsLiveAlbum = IsLiveAlbum(albumTitle)
            };

            if (!result.IsLiveAlbum && !options.ForceProcessing)
            {
                result.NormalizedTitle = albumTitle.Trim();
                return result;
            }

            try
            {
                // Extract live album components
                ExtractLiveAlbumComponents(albumTitle, result);
                
                // Normalize venue information
                if (!string.IsNullOrWhiteSpace(result.Venue) && options.NormalizeVenues)
                {
                    result.NormalizedVenue = NormalizeVenueName(result.Venue);
                }
                
                // Normalize date information
                if (!string.IsNullOrWhiteSpace(result.Date) && options.NormalizeDates)
                {
                    result.NormalizedDate = NormalizeDateString(result.Date);
                }
                
                // Create final normalized title
                result.NormalizedTitle = CreateNormalizedTitle(result, options);
                
                // Generate matching variations
                if (options.GenerateVariations)
                {
                    result.TitleVariations = GenerateTitleVariations(result);
                }

                _logger.Info("🎤 LIVE NORMALIZATION: '{0}' → '{1}' (Venue: {2}, Date: {3})", 
                            albumTitle, result.NormalizedTitle, result.NormalizedVenue ?? "None", result.NormalizedDate ?? "None");

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to normalize live album: {0}", albumTitle);
                result.NormalizedTitle = albumTitle;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Determines if an album title indicates it's a live recording
        /// </summary>
        /// <param name="albumTitle">Album title to analyze</param>
        /// <returns>True if the title contains live album indicators</returns>
        public bool IsLiveAlbum(string albumTitle)
        {
            if (string.IsNullOrWhiteSpace(albumTitle))
                return false;

            var normalizedTitle = albumTitle.ToLowerInvariant();

            // Check for explicit live markers
            var hasLiveMarker = LiveContextMarkers.Any(marker => normalizedTitle.Contains(marker));
            
            // Check for special session markers
            var hasSpecialMarker = SpecialLiveMarkers.Keys.Any(marker => normalizedTitle.Contains(marker));
            
            // Check for live album pattern matches
            var hasLivePattern = LiveAlbumPatterns.Any(pattern => pattern.IsMatch(albumTitle));
            
            return hasLiveMarker || hasSpecialMarker || hasLivePattern;
        }

        /// <summary>
        /// Calculates similarity between two live album titles with venue/date awareness
        /// </summary>
        /// <param name="title1">First title to compare</param>
        /// <param name="title2">Second title to compare</param>
        /// <param name="options">Normalization options</param>
        /// <returns>Similarity score from 0.0 to 1.0</returns>
        public double CalculateLiveAlbumSimilarity(
            string title1, 
            string title2, 
            LiveAlbumNormalizationOptions options = null)
        {
            if (string.IsNullOrEmpty(title1) && string.IsNullOrEmpty(title2))
                return 1.0;
                
            if (string.IsNullOrEmpty(title1) || string.IsNullOrEmpty(title2))
                return 0.0;

            options ??= LiveAlbumNormalizationOptions.Default;

            var normalized1 = NormalizeLiveAlbum(title1, options);
            var normalized2 = NormalizeLiveAlbum(title2, options);

            // Compare core album titles
            var coreSimilarity = CalculateStringSimilarity(normalized1.CoreAlbumTitle ?? normalized1.NormalizedTitle, 
                                                          normalized2.CoreAlbumTitle ?? normalized2.NormalizedTitle);

            // If both are live albums, factor in venue/date matching
            if (normalized1.IsLiveAlbum && normalized2.IsLiveAlbum)
            {
                var venueSimilarity = CalculateVenueSimilarity(normalized1.NormalizedVenue, normalized2.NormalizedVenue);
                var dateSimilarity = CalculateDateSimilarity(normalized1.NormalizedDate, normalized2.NormalizedDate);
                
                // Weight: 70% core title, 20% venue, 10% date
                return (coreSimilarity * 0.7) + (venueSimilarity * 0.2) + (dateSimilarity * 0.1);
            }

            return coreSimilarity;
        }

        #region Private Methods

        /// <summary>
        /// Extracts live album components using pattern matching
        /// </summary>
        private void ExtractLiveAlbumComponents(string albumTitle, LiveAlbumNormalizationResult result)
        {
            foreach (var pattern in LiveAlbumPatterns)
            {
                var match = pattern.Match(albumTitle);
                if (match.Success)
                {
                    switch (match.Groups.Count)
                    {
                        case 2: // Simple pattern: "Album Title Live"
                            result.CoreAlbumTitle = match.Groups[1].Value.Trim();
                            break;
                            
                        case 3: // "Album Title - Live at Venue" or "Live at Venue: Album Title"
                            if (pattern.ToString().StartsWith("^(?:live", StringComparison.OrdinalIgnoreCase))
                            {
                                // "Live at Venue: Album Title"
                                result.Venue = match.Groups[1].Value.Trim();
                                result.CoreAlbumTitle = match.Groups[2].Value.Trim();
                            }
                            else
                            {
                                // "Album Title - Live at Venue"
                                result.CoreAlbumTitle = match.Groups[1].Value.Trim();
                                result.Venue = match.Groups[2].Value.Trim();
                            }
                            break;
                            
                        case 4: // "Album Title (Live at Venue, Date)"
                            result.CoreAlbumTitle = match.Groups[1].Value.Trim();
                            result.Venue = match.Groups[2].Value.Trim();
                            result.Date = match.Groups[3]?.Value?.Trim();
                            break;
                    }
                    
                    result.PatternMatched = pattern.ToString();
                    break;
                }
            }

            // If no pattern matched but it's detected as live, extract manually
            if (string.IsNullOrWhiteSpace(result.CoreAlbumTitle))
            {
                result.CoreAlbumTitle = ExtractCoreTitle(albumTitle);
            }

            // Check for special live markers
            CheckSpecialLiveMarkers(albumTitle, result);
        }

        /// <summary>
        /// Extracts core title by removing live indicators
        /// </summary>
        private string ExtractCoreTitle(string albumTitle)
        {
            var title = albumTitle;

            // Remove common live patterns (using compile-time generated regexes)
            title = LiveAlbumNormalizerRegexes.LiveDashSuffix().Replace(title, "");
            title = LiveAlbumNormalizerRegexes.LiveParenthesesSuffix().Replace(title, "");
            title = LiveAlbumNormalizerRegexes.LiveWordSuffix().Replace(title, "");

            // Remove special session markers
            foreach (var marker in SpecialLiveMarkers.Keys)
            {
                title = Regex.Replace(title, $@"^{Regex.Escape(marker)}:\s*", "", RegexOptions.IgnoreCase);
                title = Regex.Replace(title, $@"\s*[-–—]\s*{Regex.Escape(marker)}.*$", "", RegexOptions.IgnoreCase);
            }

            return title.Trim();
        }

        /// <summary>
        /// Checks for special live album markers
        /// </summary>
        private void CheckSpecialLiveMarkers(string albumTitle, LiveAlbumNormalizationResult result)
        {
            var normalizedTitle = albumTitle.ToLowerInvariant();
            
            foreach (var marker in SpecialLiveMarkers)
            {
                if (normalizedTitle.Contains(marker.Key))
                {
                    result.SpecialContext = marker.Value;
                    result.IsSpecialSession = true;
                    break;
                }
            }
        }

        /// <summary>
        /// Normalizes venue names to canonical forms
        /// </summary>
        private string NormalizeVenueName(string venue)
        {
            if (string.IsNullOrWhiteSpace(venue))
                return venue;

            var normalized = venue.ToLowerInvariant().Trim();

            // Check for known venue patterns
            foreach (var pattern in VenuePatterns)
            {
                if (pattern.Value.Any(variation => normalized.Contains(variation)))
                {
                    return pattern.Key;
                }
            }

            // Generic venue type normalization (using compile-time generated regexes)
            normalized = LiveAlbumNormalizerRegexes.LeadingThe().Replace(normalized, "");
            normalized = LiveAlbumNormalizerRegexes.VenueTypeSuffix().Replace(normalized, " venue");

            return normalized.Trim();
        }

        /// <summary>
        /// Normalizes date strings to standard format
        /// </summary>
        private string NormalizeDateString(string date)
        {
            if (string.IsNullOrWhiteSpace(date))
                return date;

            foreach (var pattern in DatePatterns)
            {
                var match = pattern.Match(date);
                if (match.Success)
                {
                    // Try to extract year as the most important component
                    var groups = match.Groups.Cast<Group>().Skip(1).Where(g => g.Success).ToList();
                    
                    // Look for 4-digit year
                    var year = groups.FirstOrDefault(g => g.Value.Length == 4)?.Value;
                    if (!string.IsNullOrEmpty(year) && int.TryParse(year, out var yearInt) && yearInt >= 1950 && yearInt <= DateTime.Now.Year + 1)
                    {
                        return year;
                    }
                }
            }

            // If no standard pattern, try to extract year manually (using compile-time generated regex)
            var yearMatch = LiveAlbumNormalizerRegexes.ExtractYear().Match(date);
            if (yearMatch.Success)
            {
                return yearMatch.Value;
            }

            return date.Trim();
        }

        /// <summary>
        /// Creates normalized title based on extracted components
        /// </summary>
        private string CreateNormalizedTitle(LiveAlbumNormalizationResult result, LiveAlbumNormalizationOptions options)
        {
            var coreTitle = result.CoreAlbumTitle ?? result.OriginalTitle;

            if (!options.IncludeLiveContext || !result.IsLiveAlbum)
            {
                return coreTitle;
            }

            var components = new List<string> { coreTitle };

            if (result.IsSpecialSession && !string.IsNullOrWhiteSpace(result.SpecialContext))
            {
                components.Add($"({result.SpecialContext})");
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(result.NormalizedVenue) && options.IncludeVenue)
                {
                    components.Add($"(Live at {result.NormalizedVenue})");
                }
                else if (result.IsLiveAlbum)
                {
                    components.Add("(Live)");
                }
            }

            return string.Join(" ", components);
        }

        /// <summary>
        /// Generates title variations for fuzzy matching
        /// </summary>
        private List<string> GenerateTitleVariations(LiveAlbumNormalizationResult result)
        {
            var variations = new List<string>();

            // Add core title
            if (!string.IsNullOrWhiteSpace(result.CoreAlbumTitle))
            {
                variations.Add(result.CoreAlbumTitle);
            }

            // Add normalized title
            variations.Add(result.NormalizedTitle);

            // Add variations with different live markers
            if (result.IsLiveAlbum && !string.IsNullOrWhiteSpace(result.CoreAlbumTitle))
            {
                variations.Add($"{result.CoreAlbumTitle} (Live)");
                variations.Add($"{result.CoreAlbumTitle} - Live");
                variations.Add($"Live: {result.CoreAlbumTitle}");
                
                if (!string.IsNullOrWhiteSpace(result.NormalizedVenue))
                {
                    variations.Add($"{result.CoreAlbumTitle} (Live at {result.NormalizedVenue})");
                }
            }

            return variations.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Calculates similarity between venue names
        /// </summary>
        private double CalculateVenueSimilarity(string venue1, string venue2)
        {
            if (string.IsNullOrEmpty(venue1) && string.IsNullOrEmpty(venue2))
                return 1.0;
                
            if (string.IsNullOrEmpty(venue1) || string.IsNullOrEmpty(venue2))
                return 0.5; // Partial penalty for missing venue info

            if (venue1.Equals(venue2, StringComparison.OrdinalIgnoreCase))
                return 1.0;

            return CalculateStringSimilarity(venue1, venue2);
        }

        /// <summary>
        /// Calculates similarity between date strings
        /// </summary>
        private double CalculateDateSimilarity(string date1, string date2)
        {
            if (string.IsNullOrEmpty(date1) && string.IsNullOrEmpty(date2))
                return 1.0;
                
            if (string.IsNullOrEmpty(date1) || string.IsNullOrEmpty(date2))
                return 0.8; // Less penalty for missing date info

            if (date1.Equals(date2, StringComparison.OrdinalIgnoreCase))
                return 1.0;

            // If both are years, compare as numbers
            if (int.TryParse(date1, out var year1) && int.TryParse(date2, out var year2))
            {
                var yearDiff = Math.Abs(year1 - year2);
                return yearDiff == 0 ? 1.0 : yearDiff <= 1 ? 0.9 : yearDiff <= 2 ? 0.7 : 0.3;
            }

            return CalculateStringSimilarity(date1, date2);
        }

        /// <summary>
        /// Calculates string similarity using Levenshtein distance
        /// </summary>
        private double CalculateStringSimilarity(string s1, string s2)
        {
            return CommonStringSimilarity.Calculate(s1, s2);
        }


        #endregion
    }

    #region Configuration and Result Classes

    /// <summary>
    /// Options for live album normalization behavior
    /// </summary>
    public class LiveAlbumNormalizationOptions
    {
        public bool NormalizeVenues { get; set; } = true;
        public bool NormalizeDates { get; set; } = true;
        public bool IncludeLiveContext { get; set; } = true;
        public bool IncludeVenue { get; set; } = false;
        public bool GenerateVariations { get; set; } = true;
        public bool ForceProcessing { get; set; } = false;

        public static LiveAlbumNormalizationOptions Default => new();
        
        public static LiveAlbumNormalizationOptions CoreTitleOnly => new()
        {
            NormalizeVenues = true,
            NormalizeDates = true,
            IncludeLiveContext = false,
            IncludeVenue = false,
            GenerateVariations = false,
            ForceProcessing = false
        };

        public static LiveAlbumNormalizationOptions FullContext => new()
        {
            NormalizeVenues = true,
            NormalizeDates = true,
            IncludeLiveContext = true,
            IncludeVenue = true,
            GenerateVariations = true,
            ForceProcessing = true
        };
    }

    /// <summary>
    /// Result of live album normalization with extracted components
    /// </summary>
    public class LiveAlbumNormalizationResult
    {
        public string OriginalTitle { get; set; }
        public string NormalizedTitle { get; set; }
        public string CoreAlbumTitle { get; set; }
        public string Venue { get; set; }
        public string NormalizedVenue { get; set; }
        public string Date { get; set; }
        public string NormalizedDate { get; set; }
        public bool IsLiveAlbum { get; set; }
        public bool IsSpecialSession { get; set; }
        public string SpecialContext { get; set; }
        public string PatternMatched { get; set; }
        public List<string> TitleVariations { get; set; } = new();
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Gets the best title for matching based on context
        /// </summary>
        public string GetBestMatchingTitle()
        {
            return !string.IsNullOrWhiteSpace(CoreAlbumTitle) ? CoreAlbumTitle : NormalizedTitle;
        }

        /// <summary>
        /// Determines if this live album can be matched against another title
        /// </summary>
        public bool CanMatchAgainst(string otherTitle)
        {
            if (string.IsNullOrWhiteSpace(otherTitle))
                return false;

            var normalizedOther = otherTitle.ToLowerInvariant();
            
            // Check against all variations
            return TitleVariations.Any(variation => 
                normalizedOther.Contains(variation.ToLowerInvariant()) ||
                variation.ToLowerInvariant().Contains(normalizedOther));
        }
    }

    #endregion
}
