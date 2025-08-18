using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Semantic classifier that identifies the role of each word in album titles
    /// Differentiates between core album content and removable edition markers
    /// </summary>
    public class AlbumComponentClassifier
    {
        /// <summary>
        /// Version descriptors that are PART of the album identity and should never be removed
        /// These represent different versions or recordings of albums that are distinct releases
        /// </summary>
        private static readonly HashSet<string> VersionDescriptors = new(StringComparer.OrdinalIgnoreCase)
        {
            // Performance/recording context (critical album identity)
            "Instrumental", "Acoustic", "Live", "Unplugged", "Demo", "Sessions",
            "Orchestra", "Symphony", "Quartet", "Ensemble", "Orchestral",
            "Concert", "Festival", "Tour", "Residency", "Studio",
            
            // Audio format variations (distinct from "remasters")
            "Radio", "Single", "Remix", "Mix", "Extended", "Club", "Dub",
            "Mono", "Stereo", "Binaural", "Surround", "Quadraphonic", "5.1", "7.1",
            
            // Language/vocal variations  
            "English", "Spanish", "Japanese", "French", "German", "Italian",
            "Portuguese", "Chinese", "Korean", "Karaoke", "Vocal", "Instrumental",
            
            // International live performance terms
            "Vivo", "Direkt", "Direct", "Concert", "Concierto", "Concerto",
            "En Vivo", "En Direct", "Ao Vivo", "Im Studio", "Nel Studio",
            
            // Critical modifiers that change the album's identity
            "Naked", "Raw", "Pure", "Original", "Alternate", "Alternative",
            "Complete", "Incomplete", "Unreleased", "Rarities", "B-Sides",
            
            // Performance/recording quality descriptors
            "Hi-Fi", "Lo-Fi", "High-Quality", "Audiophile", "24-Bit",
            "Analog", "Digital", "Vinyl", "CD", "Studio", "Rehearsal"
        };
        
        /// <summary>
        /// Edition markers that are ADDITIONS to the base album (can be safely removed for broader search)
        /// These typically indicate repackaging or remastering of existing content
        /// </summary>
        private static readonly HashSet<string> EditionMarkers = new(StringComparer.OrdinalIgnoreCase)
        {
            // Standard edition types
            "Deluxe", "Expanded", "Limited", "Special", "Collector", "Collectors", "Collector's",
            "Target", "Amazon", "iTunes", "Walmart", "Best Buy", "UK", "US", "EU",
            
            // Remastering/technical improvements
            "Remastered", "Remaster", "Mastered", "Enhanced", "Restored",
            "Digitally", "SACD", "DSD", "Audiophile",
            
            // Anniversary/commemorative
            "Anniversary", "Commemorative", "Legacy", "Heritage", "Tribute",
            
            // Packaging/presentation
            "Bonus", "Complete", "Ultimate", "Definitive", "Essential", "Platinum",
            "Gold", "Silver", "Diamond", "Box", "Boxed", "Set"
        };
        
        /// <summary>
        /// Metadata markers that provide context but aren't part of the search query
        /// </summary>
        private static readonly HashSet<string> MetadataMarkers = new(StringComparer.OrdinalIgnoreCase)
        {
            "Explicit", "Clean", "Censored", "Uncensored", "Parental", "Advisory",
            "CD", "LP", "EP", "Single", "Vinyl", "Cassette", "Digital", "Stream",
            "MP3", "FLAC", "WAV", "WEB", "WEBRip", "CDRip"
        };

        /// <summary>
        /// Patterns that indicate edition contexts where edition markers should be preserved
        /// </summary>
        private static readonly List<Regex> EditionContextPatterns = new()
        {
            // "Word Edition" or "Word Version"
            new Regex(@"\b\w+\s+(edition|version|release)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            
            // Content in parentheses/brackets (likely additional info)
            new Regex(@"[\(\[].*\b\w+\b.*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            
            // Preceded by dash at end (common edition format)
            new Regex(@"-\s*\w+\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            
            // Year followed by edition marker
            new Regex(@"\b\d{4}\s+\w+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        };

        /// <summary>
        /// Classifies each component (word/phrase) in an album title to determine its semantic role
        /// </summary>
        /// <param name="albumTitle">Full album title to analyze</param>
        /// <returns>Dictionary mapping each component to its semantic type</returns>
        public Dictionary<string, AlbumComponentType> ClassifyComponents(string albumTitle)
        {
            var components = new Dictionary<string, AlbumComponentType>();
            
            if (string.IsNullOrWhiteSpace(albumTitle))
                return components;
            
            var tokens = TokenizeAlbumTitle(albumTitle);
            
            foreach (var token in tokens)
            {
                var cleanToken = CleanToken(token);
                if (string.IsNullOrWhiteSpace(cleanToken))
                    continue;
                
                components[token] = ClassifyToken(cleanToken, albumTitle);
            }
            
            return components;
        }

        /// <summary>
        /// Determines which terms in an album title should be preserved during query cleaning
        /// </summary>
        /// <param name="albumTitle">Album title to analyze</param>
        /// <returns>List of terms that should never be removed</returns>
        public List<string> GetPreservedTerms(string albumTitle)
        {
            var components = ClassifyComponents(albumTitle);
            return components.Where(c => c.Value == AlbumComponentType.VersionDescriptor || 
                                       c.Value == AlbumComponentType.CoreTitle)
                           .Select(c => c.Key)
                           .ToList();
        }

        /// <summary>
        /// Determines if a term should be treated as removable edition information
        /// </summary>
        /// <param name="term">Term to evaluate</param>
        /// <param name="context">Full album title for context analysis</param>
        /// <returns>True if the term can be safely removed</returns>
        public bool IsRemovableEditionMarker(string term, string context)
        {
            var cleanTerm = CleanToken(term);
            
            // Never remove version descriptors
            if (VersionDescriptors.Contains(cleanTerm))
                return false;
            
            // Check if it's in edition context
            if (EditionMarkers.Contains(cleanTerm))
            {
                return IsInEditionContext(term, context);
            }
            
            return false;
        }

        /// <summary>
        /// Analyzes album title to determine the appropriate query cleaning strategy
        /// </summary>
        /// <param name="albumTitle">Album title to analyze</param>
        /// <returns>Recommended cleaning level</returns>
        public CleaningLevel RecommendCleaningLevel(string albumTitle)
        {
            var components = ClassifyComponents(albumTitle);
            
            var hasVersionDescriptor = components.Values.Any(c => c == AlbumComponentType.VersionDescriptor);
            var hasEditionMarker = components.Values.Any(c => c == AlbumComponentType.EditionMarker);
            var coreTermCount = components.Values.Count(c => c == AlbumComponentType.CoreTitle);
            
            // Albums with version descriptors need careful handling
            if (hasVersionDescriptor)
            {
                return CleaningLevel.Minimal;
            }
            
            // Albums with only edition markers can be cleaned more aggressively
            if (hasEditionMarker && coreTermCount > 2)
            {
                return CleaningLevel.Moderate;
            }
            
            // Simple albums without special markers
            if (!hasVersionDescriptor && !hasEditionMarker && coreTermCount <= 3)
            {
                return CleaningLevel.Aggressive;
            }
            
            return CleaningLevel.Moderate;
        }

        private AlbumComponentType ClassifyToken(string token, string fullTitle)
        {
            // Check version descriptors first (highest priority)
            if (VersionDescriptors.Contains(token))
            {
                return AlbumComponentType.VersionDescriptor;
            }
            
            // Check edition markers with context
            if (EditionMarkers.Contains(token) && IsInEditionContext(token, fullTitle))
            {
                return AlbumComponentType.EditionMarker;
            }
            
            // Check metadata markers
            if (MetadataMarkers.Contains(token))
            {
                return AlbumComponentType.Metadata;
            }
            
            // Check for year patterns
            if (IsYearPattern(token))
            {
                return AlbumComponentType.Metadata;
            }
            
            // Check for noise (common meaningless words)
            if (IsNoise(token))
            {
                return AlbumComponentType.Noise;
            }
            
            // Default to core title
            return AlbumComponentType.CoreTitle;
        }

        private bool IsInEditionContext(string token, string fullTitle)
        {
            // The token parameter is already cleaned, but we need to check against both
            // the cleaned version and possible original forms in the full title
            
            // Check if the cleaned token or its variants appear before "Edition", "Version", etc.
            // This handles cases like "Collector's Edition" -> cleaned token "Collectors"
            var tokenVariants = new[]
            {
                token,                          // Cleaned version (e.g., "Collectors")
                token + "'s",                   // Possessive form (e.g., "Collector's")
                token.TrimEnd('s') + "'s",      // Singular possessive (e.g., "Collector's" from "Collectors")
                token.TrimEnd('s')              // Singular form (e.g., "Collector" from "Collectors")
            };
            
            foreach (var variant in tokenVariants)
            {
                if (string.IsNullOrWhiteSpace(variant)) continue;
                
                // Check if followed by "Edition" or "Version"
                var editionPattern = $@"\b{Regex.Escape(variant)}\s+(edition|version|release)\b";
                if (Regex.IsMatch(fullTitle, editionPattern, RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }
            
            // Check if the token appears in any edition context pattern
            foreach (var pattern in EditionContextPatterns)
            {
                var matches = pattern.Matches(fullTitle);
                foreach (Match match in matches)
                {
                    // Check both the cleaned token and its variants
                    foreach (var variant in tokenVariants)
                    {
                        if (!string.IsNullOrWhiteSpace(variant) && 
                            match.Value.Contains(variant, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            
            return false;
        }

        private bool IsYearPattern(string token)
        {
            // Match 4-digit years (1900-2030)
            return Regex.IsMatch(token, @"^(19|20)[0-9]{2}$");
        }

        private bool IsNoise(string token)
        {
            var noiseWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by"
            };
            
            return noiseWords.Contains(token);
        }

        private string CleanToken(string token)
        {
            // For multi-word descriptors, don't clean - use as-is
            if (token.Contains(' ') || token.Contains('-'))
            {
                return token.Trim();
            }
            
            // Remove punctuation and extra whitespace for single words
            return Regex.Replace(token, @"[^\w\s]", "").Trim();
        }

        private IEnumerable<string> TokenizeAlbumTitle(string title)
        {
            var tokens = new List<string>();
            
            if (string.IsNullOrWhiteSpace(title))
                return tokens;
            
            // First, identify and preserve multi-word version descriptors
            var preservedPhrases = new Dictionary<string, string>();
            var multiWordDescriptors = new[]
            {
                "En Vivo", "En Direct", "Ao Vivo", "Im Studio", "Nel Studio",
                "Hi-Fi", "Lo-Fi", "24-Bit", "B-Sides", "High-Quality"
            };
            
            var workingTitle = title;
            int placeholderIndex = 0;
            
            foreach (var phrase in multiWordDescriptors)
            {
                if (workingTitle.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                {
                    var placeholder = $"__PLACEHOLDER_{placeholderIndex++}__";
                    preservedPhrases[placeholder] = phrase;
                    workingTitle = Regex.Replace(workingTitle, Regex.Escape(phrase), placeholder, RegexOptions.IgnoreCase);
                }
            }
            
            // Remove parentheses/brackets but keep their content
            var parentheticalPattern = @"[\(\[]([^\)\]]+)[\)\]]";
            var matches = Regex.Matches(workingTitle, parentheticalPattern);
            
            foreach (Match match in matches)
            {
                var contentInside = match.Groups[1].Value.Trim();
                workingTitle = workingTitle.Replace(match.Value, " " + contentInside + " ");
            }
            
            // Split on whitespace and commas (but preserve hyphens in meaningful terms)
            var allTokens = Regex.Split(workingTitle, @"[\s,]+")
                                .Where(t => !string.IsNullOrWhiteSpace(t))
                                .Select(t => t.Trim().Trim(new char[] { '.', ',', '!', '?' }))
                                .Where(t => !string.IsNullOrWhiteSpace(t))
                                .ToList();
            
            // Restore preserved phrases
            for (int i = 0; i < allTokens.Count; i++)
            {
                if (preservedPhrases.ContainsKey(allTokens[i]))
                {
                    allTokens[i] = preservedPhrases[allTokens[i]];
                }
            }
            
            // Add the preserved phrases directly
            tokens.AddRange(preservedPhrases.Values);
            tokens.AddRange(allTokens.Where(t => !preservedPhrases.ContainsKey(t)));
            
            return tokens.Distinct();
        }
    }

    /// <summary>
    /// Semantic classification of album title components
    /// </summary>
    public enum AlbumComponentType
    {
        /// <summary>Core album title content that should always be preserved</summary>
        CoreTitle,
        
        /// <summary>Version descriptors that are part of the album's identity (Instrumental, Live, etc.)</summary>
        VersionDescriptor,
        
        /// <summary>Edition markers that can be removed for broader search (Deluxe Edition, etc.)</summary>
        EditionMarker,
        
        /// <summary>Metadata that provides context but isn't part of search (years, formats, etc.)</summary>
        Metadata,
        
        /// <summary>Noise words that can be safely removed</summary>
        Noise
    }

    /// <summary>
    /// Levels of query cleaning aggressiveness
    /// </summary>
    public enum CleaningLevel
    {
        /// <summary>No cleaning - use exact query</summary>
        None,
        
        /// <summary>Minimal cleaning - only remove obvious noise, preserve all meaningful terms</summary>
        Minimal,
        
        /// <summary>Moderate cleaning - remove edition markers in context, preserve version descriptors</summary>
        Moderate,
        
        /// <summary>Aggressive cleaning - remove all non-essential terms for broadest search</summary>
        Aggressive
    }
}