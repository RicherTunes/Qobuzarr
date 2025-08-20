using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Unicode-aware query builder that generates search variants to handle international artists and albums.
    /// This is the deterministic solution to missed albums due to special characters.
    /// </summary>
    public class UnicodeQueryBuilder : IUnicodeQueryBuilder
    {
        private readonly Logger _logger;
        private readonly UnicodeQueryStatistics _statistics = new();
        private readonly object _statsLock = new object();
        
        // Adaptive learning: Track variant success rates per artist pattern
        private readonly Dictionary<string, VariantSuccessRate> _learnedPatterns = new();
        private const int MaxLearnedPatterns = 1000;
        
        // Performance optimizations: Compiled regex patterns
        private static readonly Regex ParentheticalRegex = new Regex(@"\s*\([^)]*\)\s*", RegexOptions.Compiled);
        private static readonly Regex BracketRegex = new Regex(@"\s*\[[^\]]*\]\s*", RegexOptions.Compiled);
        private static readonly Regex MultipleSpacesRegex = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex AlphanumericOnlyRegex = new Regex(@"[^\w\s]", RegexOptions.Compiled);
        private static readonly Regex SpecialEditionRegex = new Regex(
            @"\s*\b(deluxe|special|anniversary|remaster|remastered|edition|expanded|collector|limited)\b\s*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex YearRemovalRegex = new Regex(
            @"\s*\b(cd\d+|disc\d+|vol\.?\s*\d+|volume\s*\d+)\b\s*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        
        // Memory management constants
        private const int MaxFailurePatterns = 50;
        private const int FailurePatternCleanupThreshold = 100;
        
        // Comprehensive character mapping for common diacritics and special characters
        private static readonly Dictionary<char, string[]> CharacterVariants = new()
        {
            // Latin extended
            ['á'] = new[] { "a", "á", "à", "â", "ä", "ã", "å" },
            ['é'] = new[] { "e", "é", "è", "ê", "ë" },
            ['í'] = new[] { "i", "í", "ì", "î", "ï" },
            ['ó'] = new[] { "o", "ó", "ò", "ô", "ö", "õ", "ø" },
            ['ú'] = new[] { "u", "ú", "ù", "û", "ü" },
            ['ñ'] = new[] { "n", "ñ", "ny" },
            ['ç'] = new[] { "c", "ç", "ch" },
            ['ß'] = new[] { "ss", "ß", "sz", "s" },
            
            // Nordic characters
            ['ø'] = new[] { "o", "ø", "oe" },
            ['å'] = new[] { "a", "å", "aa" },
            ['æ'] = new[] { "ae", "æ", "e" },
            
            // Eastern European
            ['ż'] = new[] { "z", "ż", "zh" },
            ['ł'] = new[] { "l", "ł", "w" },
            ['š'] = new[] { "s", "š", "sh" },
            ['č'] = new[] { "c", "č", "ch" },
            ['ř'] = new[] { "r", "ř", "rz" },
            
            // Icelandic
            ['þ'] = new[] { "th", "þ", "p" },
            ['ð'] = new[] { "d", "ð", "th" },
        };
        
        // Common artist name corrections that are known to cause search issues
        private static readonly Dictionary<string, string[]> KnownArtistCorrections = new()
        {
            ["sigur rós"] = new[] { "sigur ros", "sigurros", "sigur_ros" },
            ["björk"] = new[] { "bjork", "bjoerk" },
            ["mötley crüe"] = new[] { "motley crue", "motley_crue" },
            ["blue öyster cult"] = new[] { "blue oyster cult", "boc", "blue_oyster_cult" },
            ["hüsker dü"] = new[] { "husker du", "husker_du" },
            ["motörhead"] = new[] { "motorhead", "motor_head" },
            ["naïve"] = new[] { "naive" },
            ["café tacvba"] = new[] { "cafe tacvba", "cafe_tacvba" },
            ["sónar"] = new[] { "sonar" },
            ["jónsi"] = new[] { "jonsi" },
            ["ólafur arnalds"] = new[] { "olafur arnalds" },
            ["múm"] = new[] { "mum" },
            ["röyksopp"] = new[] { "royksopp" },
            ["trentemøller"] = new[] { "trentemoller" },
            ["μ-ziq"] = new[] { "mu-ziq", "muziq", "m-ziq" },
            ["mikis theodorakis"] = new[] { "mikis theodorakis" },
        };
        
        // Greek to Latin transliteration (critical for μ-Ziq and other Greek artists)
        private static readonly Dictionary<char, string> GreekToLatin = new()
        {
            ['α'] = "a", ['β'] = "b", ['γ'] = "g", ['δ'] = "d", ['ε'] = "e",
            ['ζ'] = "z", ['η'] = "i", ['θ'] = "th", ['ι'] = "i", ['κ'] = "k",
            ['λ'] = "l", ['μ'] = "m", ['ν'] = "n", ['ξ'] = "x", ['ο'] = "o",
            ['π'] = "p", ['ρ'] = "r", ['σ'] = "s", ['τ'] = "t", ['υ'] = "y",
            ['φ'] = "f", ['χ'] = "ch", ['ψ'] = "ps", ['ω'] = "o",
            // Uppercase
            ['Α'] = "A", ['Β'] = "B", ['Γ'] = "G", ['Δ'] = "D", ['Ε'] = "E",
            ['Ζ'] = "Z", ['Η'] = "I", ['Θ'] = "Th", ['Ι'] = "I", ['Κ'] = "K",
            ['Λ'] = "L", ['Μ'] = "M", ['Ν'] = "N", ['Ξ'] = "X", ['Ο'] = "O",
            ['Π'] = "P", ['Ρ'] = "R", ['Σ'] = "S", ['Τ'] = "T", ['Υ'] = "Y",
            ['Φ'] = "F", ['Χ'] = "Ch", ['Ψ'] = "Ps", ['Ω'] = "O"
        };
        
        // Cyrillic to Latin transliteration (ISO 9 standard + common variations)
        private static readonly Dictionary<char, string> CyrillicToLatin = new()
        {
            ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d",
            ['е'] = "e", ['ё'] = "yo", ['ж'] = "zh", ['з'] = "z", ['и'] = "i",
            ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n",
            ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t",
            ['у'] = "u", ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch",
            ['ш'] = "sh", ['щ'] = "shch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "",
            ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
            // Uppercase
            ['А'] = "A", ['Б'] = "B", ['В'] = "V", ['Г'] = "G", ['Д'] = "D",
            ['Е'] = "E", ['Ё'] = "Yo", ['Ж'] = "Zh", ['З'] = "Z", ['И'] = "I",
            ['Й'] = "Y", ['К'] = "K", ['Л'] = "L", ['М'] = "M", ['Н'] = "N",
            ['О'] = "O", ['П'] = "P", ['Р'] = "R", ['С'] = "S", ['Т'] = "T",
            ['У'] = "U", ['Ф'] = "F", ['Х'] = "Kh", ['Ц'] = "Ts", ['Ч'] = "Ch",
            ['Ш'] = "Sh", ['Щ'] = "Shch", ['Ъ'] = "", ['Ы'] = "Y", ['Ь'] = "",
            ['Э'] = "E", ['Ю'] = "Yu", ['Я'] = "Ya"
        };

        public UnicodeQueryBuilder(Logger logger)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _logger.Debug("Initialized Unicode query builder with comprehensive character mapping");
        }

        public List<string> GenerateQueryVariants(string artist, string album, int maxVariants = 6)
        {
            if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(album))
                return new List<string>();

            var variants = new HashSet<string>();
            var fullQuery = $"{artist.Trim()} {album.Trim()}";
            
            // 1. Original query (highest priority if ASCII-only)
            variants.Add(fullQuery);
            
            // 2. ASCII folding (remove diacritics: é→e, ñ→n, etc.)
            var asciiFolded = FoldToAscii(fullQuery);
            if (asciiFolded != fullQuery)
                variants.Add(asciiFolded);
            
            // 3. Known artist corrections (hand-curated problem cases)
            var correctedQuery = ApplyKnownCorrections(fullQuery);
            if (correctedQuery != fullQuery)
                variants.Add(correctedQuery);
            
            // 4. Greek transliteration (for artists like μ-Ziq)
            var greekTransliterated = TransliterateGreek(fullQuery);
            if (greekTransliterated != fullQuery)
                variants.Add(greekTransliterated);
            
            // 5. Cyrillic transliteration (for Russian/Eastern European artists)
            var cyrillicTransliterated = TransliterateCyrillic(fullQuery);
            if (cyrillicTransliterated != fullQuery)
                variants.Add(cyrillicTransliterated);
            
            // 6. Component searches (when combined query fails)
            variants.Add(FoldToAscii(artist.Trim()));
            variants.Add(FoldToAscii(album.Trim()));
            
            // 7. Remove special characters entirely (nuclear option)
            var alphanumeric = AlphanumericOnlyRegex.Replace(fullQuery, " ");
            alphanumeric = MultipleSpacesRegex.Replace(alphanumeric, " ").Trim();
            if (alphanumeric != fullQuery && !string.IsNullOrWhiteSpace(alphanumeric))
                variants.Add(alphanumeric);
                
            // Order by priority and limit
            var orderedVariants = OrderVariantsByPriority(variants.ToList(), fullQuery);
            var result = orderedVariants.Take(maxVariants).ToList();
            
            _logger.Trace($"Generated {result.Count} query variants for '{fullQuery}': {string.Join(", ", result.Select(v => $"'{v}'"))}");
            
            return result;
        }

        public List<string> GenerateArtistVariants(string artist, int maxVariants = 4)
        {
            if (string.IsNullOrWhiteSpace(artist))
                return new List<string>();

            var variants = new HashSet<string>
            {
                artist.Trim(),
                FoldToAscii(artist.Trim()),
                ApplyKnownCorrections(artist.Trim()),
                TransliterateGreek(artist.Trim()),
                TransliterateCyrillic(artist.Trim())
            };

            variants.RemoveWhere(string.IsNullOrWhiteSpace);
            return variants.Take(maxVariants).ToList();
        }

        public List<string> GenerateAlbumVariants(string album, int maxVariants = 4)
        {
            if (string.IsNullOrWhiteSpace(album))
                return new List<string>();

            var variants = new HashSet<string>
            {
                album.Trim(),
                FoldToAscii(album.Trim()),
                RemoveParentheticals(album.Trim()),
                RemoveSpecialEditionText(album.Trim())
            };

            variants.RemoveWhere(string.IsNullOrWhiteSpace);
            return variants.Take(maxVariants).ToList();
        }

        public bool RequiresUnicodeHandling(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return false;
                
            // Check for non-ASCII characters
            return query.Any(c => c > 127) || 
                   query.Any(c => CharacterVariants.ContainsKey(char.ToLowerInvariant(c))) ||
                   KnownArtistCorrections.Keys.Any(known => 
                       query.Contains(known, StringComparison.OrdinalIgnoreCase));
        }

        public UnicodeQueryStatistics GetPerformanceStatistics()
        {
            lock (_statsLock)
            {
                return new UnicodeQueryStatistics
                {
                    TotalQueries = _statistics.TotalQueries,
                    UnicodeQueries = _statistics.UnicodeQueries,
                    VariantStats = new Dictionary<string, VariantPerformance>(_statistics.VariantStats),
                    OverallSuccessRate = _statistics.OverallSuccessRate,
                    UnicodeSuccessRate = _statistics.UnicodeSuccessRate,
                    TopFailurePatterns = new Dictionary<string, int>(_statistics.TopFailurePatterns)
                };
            }
        }

        public void RecordVariantResult(string originalQuery, string variantUsed, bool wasSuccessful, int resultCount)
        {
            // FIXED: Complete thread safety - all statistics updates inside lock
            lock (_statsLock)
            {
                _statistics.TotalQueries++;
                
                if (RequiresUnicodeHandling(originalQuery))
                    _statistics.UnicodeQueries++;
                
                var variantType = DetermineVariantType(originalQuery, variantUsed);
                
                if (!_statistics.VariantStats.ContainsKey(variantType))
                {
                    _statistics.VariantStats[variantType] = new VariantPerformance();
                }
                
                var stats = _statistics.VariantStats[variantType];
                stats.TimesUsed++;
                
                if (wasSuccessful)
                {
                    stats.TimesSuccessful++;
                    stats.AverageResultCount = (stats.AverageResultCount * (stats.TimesSuccessful - 1) + resultCount) / stats.TimesSuccessful;
                }
                else
                {
                    // Track failure patterns with memory bounds
                    var failurePattern = ExtractFailurePattern(originalQuery);
                    _statistics.TopFailurePatterns[failurePattern] = 
                        _statistics.TopFailurePatterns.GetValueOrDefault(failurePattern, 0) + 1;
                    
                    // FIXED: Memory management - limit failure pattern storage
                    if (_statistics.TopFailurePatterns.Count > FailurePatternCleanupThreshold)
                    {
                        // Keep only top patterns by frequency
                        var topPatterns = _statistics.TopFailurePatterns
                            .OrderByDescending(kvp => kvp.Value)
                            .Take(MaxFailurePatterns)
                            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                        
                        _statistics.TopFailurePatterns.Clear();
                        foreach (var kvp in topPatterns)
                        {
                            _statistics.TopFailurePatterns[kvp.Key] = kvp.Value;
                        }
                        
                        _logger.Debug($"Cleaned up failure patterns: kept top {MaxFailurePatterns} of {FailurePatternCleanupThreshold}");
                    }
                    
                    // Update learned patterns for adaptive prioritization
                    UpdateLearnedPatterns(originalQuery, variantType, wasSuccessful, resultCount);
                }
                
                // Update overall success rates
                var totalSuccessful = _statistics.VariantStats.Values.Sum(v => v.TimesSuccessful);
                _statistics.OverallSuccessRate = totalSuccessful / (double)_statistics.TotalQueries;
                
                var unicodeSuccessful = _statistics.VariantStats.Values
                    .Where(v => v.GetType().Name.Contains("Unicode"))
                    .Sum(v => v.TimesSuccessful);
                _statistics.UnicodeSuccessRate = _statistics.UnicodeQueries > 0 
                    ? unicodeSuccessful / (double)_statistics.UnicodeQueries 
                    : 0.0;
            }
            
            _logger.Trace($"Recorded query result: '{variantUsed}' → {(wasSuccessful ? "SUCCESS" : "FAILED")} ({resultCount} results)");
        }

        /// <summary>
        /// Convert Unicode characters to ASCII equivalents by removing diacritics
        /// </summary>
        private string FoldToAscii(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            var sb = new StringBuilder();
            
            foreach (var c in input)
            {
                var lower = char.ToLowerInvariant(c);
                
                // Check our custom character variants first
                if (CharacterVariants.ContainsKey(lower))
                {
                    sb.Append(CharacterVariants[lower][0]); // Use first (ASCII) variant
                }
                else
                {
                    // Use .NET normalization for other characters
                    var normalized = c.ToString().Normalize(NormalizationForm.FormD);
                    var asciiChar = normalized.FirstOrDefault(ch => 
                        CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark);
                    
                    sb.Append(asciiChar != '\0' ? asciiChar : c);
                }
            }
            
            return sb.ToString();
        }

        /// <summary>
        /// Apply known corrections for problematic artist names
        /// </summary>
        private string ApplyKnownCorrections(string query)
        {
            var lowerQuery = query.ToLowerInvariant();
            
            foreach (var (original, corrections) in KnownArtistCorrections)
            {
                if (lowerQuery.Contains(original))
                {
                    // Use the first correction (most reliable)
                    var corrected = lowerQuery.Replace(original, corrections[0]);
                    
                    // Preserve original casing for the replacement
                    var originalWords = query.Split(' ');
                    var correctedWords = corrected.Split(' ');
                    
                    for (int i = 0; i < Math.Min(originalWords.Length, correctedWords.Length); i++)
                    {
                        if (originalWords[i].ToLowerInvariant() != correctedWords[i])
                        {
                            // Apply title case to the correction
                            correctedWords[i] = char.ToUpperInvariant(correctedWords[i][0]) + 
                                               correctedWords[i].Substring(1);
                        }
                    }
                    
                    return string.Join(" ", correctedWords);
                }
            }
            
            return query;
        }

        /// <summary>
        /// Transliterate Greek characters to Latin equivalents (optimized with StringBuilder)
        /// </summary>
        private string TransliterateGreek(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            var sb = new StringBuilder(input.Length * 2); // Pre-allocate for expansion (μ→"mu", ш→"sh")
            
            foreach (var c in input)
            {
                if (GreekToLatin.ContainsKey(c))
                {
                    sb.Append(GreekToLatin[c]);
                }
                else
                {
                    sb.Append(c);
                }
            }
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Transliterate Cyrillic characters to Latin equivalents (optimized with StringBuilder)
        /// </summary>
        private string TransliterateCyrillic(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            var sb = new StringBuilder(input.Length * 2); // Pre-allocate for expansion (μ→"mu", ш→"sh")
            
            foreach (var c in input)
            {
                if (CyrillicToLatin.ContainsKey(c))
                {
                    sb.Append(CyrillicToLatin[c]);
                }
                else
                {
                    sb.Append(c);
                }
            }
            
            return sb.ToString();
        }

        /// <summary>
        /// Remove text in parentheses and brackets (optimized with compiled regex)
        /// </summary>
        private string RemoveParentheticals(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;
                
            // Use compiled regex patterns for better performance
            var result = ParentheticalRegex.Replace(input, " ");
            result = BracketRegex.Replace(result, " ");
            
            // Clean up multiple spaces
            result = MultipleSpacesRegex.Replace(result, " ").Trim();
            
            return result;
        }

        /// <summary>
        /// Remove special edition text that often interferes with searches (optimized)
        /// </summary>
        private string RemoveSpecialEditionText(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;
                
            // Use compiled regex patterns for better performance
            var result = SpecialEditionRegex.Replace(input, " ");
            result = YearRemovalRegex.Replace(result, " ");
            
            // Clean up spaces
            result = MultipleSpacesRegex.Replace(result, " ").Trim();
            
            return result;
        }

        /// <summary>
        /// Order variants by success probability (adaptive learning + static rules)
        /// </summary>
        private List<string> OrderVariantsByPriority(List<string> variants, string originalQuery)
        {
            var prioritized = variants.ToList();
            var isOriginalAscii = originalQuery.All(c => c <= 127);
            
            // Apply adaptive learning if we have data for this artist pattern
            var artistPattern = ExtractArtistPattern(originalQuery);
            
            lock (_statsLock)
            {
                if (_learnedPatterns.ContainsKey(artistPattern))
                {
                    var learnedData = _learnedPatterns[artistPattern];
                    
                    // Sort variants by learned success rate, then by static priority
                    prioritized.Sort((a, b) =>
                    {
                        var aVariantType = DetermineVariantType(originalQuery, a);
                        var bVariantType = DetermineVariantType(originalQuery, b);
                        
                        var aLearnedRate = learnedData.VariantSuccessRates.GetValueOrDefault(aVariantType, 0.0);
                        var bLearnedRate = learnedData.VariantSuccessRates.GetValueOrDefault(bVariantType, 0.0);
                        
                        // If we have learned data, use it
                        if (aLearnedRate > 0 || bLearnedRate > 0)
                        {
                            return bLearnedRate.CompareTo(aLearnedRate); // Higher success rate first
                        }
                        
                        // Fallback to static priority
                        var aScore = GetVariantPriority(a, originalQuery, isOriginalAscii);
                        var bScore = GetVariantPriority(b, originalQuery, isOriginalAscii);
                        return aScore.CompareTo(bScore);
                    });
                    
                    _logger.Trace($"Applied learned prioritization for pattern '{artistPattern}'");
                    return prioritized;
                }
            }
            
            // No learned data, use static priority rules
            prioritized.Sort((a, b) =>
            {
                var aScore = GetVariantPriority(a, originalQuery, isOriginalAscii);
                var bScore = GetVariantPriority(b, originalQuery, isOriginalAscii);
                return aScore.CompareTo(bScore);
            });
            
            return prioritized;
        }

        /// <summary>
        /// Get priority score for variant ordering (lower = higher priority)
        /// </summary>
        private int GetVariantPriority(string variant, string originalQuery, bool isOriginalAscii)
        {
            // Exact match with original
            if (variant == originalQuery)
                return isOriginalAscii ? 0 : 2; // ASCII originals have highest priority
                
            // Known corrections (hand-curated, high success rate)
            if (KnownArtistCorrections.Values.Any(corrections => 
                corrections.Any(c => variant.Contains(c, StringComparison.OrdinalIgnoreCase))))
                return 1;
                
            // ASCII folded version
            if (variant == FoldToAscii(originalQuery))
                return isOriginalAscii ? 5 : 2; // Higher priority for Unicode originals
                
            // Full queries (artist + album)
            if (variant.Split(' ').Length > 1)
                return 3;
                
            // Component searches (single artist or album)
            return 4;
        }

        /// <summary>
        /// Determine the type of variant for statistics tracking
        /// </summary>
        private string DetermineVariantType(string originalQuery, string variantUsed)
        {
            if (variantUsed == originalQuery)
                return "Original";
            if (variantUsed == FoldToAscii(originalQuery))
                return "AsciiFolded";
            if (variantUsed == ApplyKnownCorrections(originalQuery))
                return "KnownCorrection";
            if (variantUsed == TransliterateGreek(originalQuery))
                return "GreekTransliterated";
            if (variantUsed == TransliterateCyrillic(originalQuery))
                return "CyrillicTransliterated";
            if (variantUsed.Split(' ').Length == 1)
                return "ComponentSearch";
            
            return "Other";
        }

        /// <summary>
        /// Extract failure pattern for analysis (what type of Unicode caused the failure)
        /// </summary>
        private string ExtractFailurePattern(string originalQuery)
        {
            if (!RequiresUnicodeHandling(originalQuery))
                return "ASCII";
                
            var patterns = new List<string>();
            
            if (originalQuery.Any(c => CyrillicToLatin.ContainsKey(c)))
                patterns.Add("Cyrillic");
            if (originalQuery.Any(c => GreekToLatin.ContainsKey(c)))
                patterns.Add("Greek");
            if (originalQuery.Any(c => CharacterVariants.ContainsKey(char.ToLowerInvariant(c))))
                patterns.Add("Diacritics");
            if (originalQuery.Any(c => c > 255))
                patterns.Add("HighUnicode");
            if (originalQuery.Contains("(") || originalQuery.Contains("["))
                patterns.Add("Parentheticals");
                
            return patterns.Any() ? string.Join("+", patterns) : "UnknownUnicode";
        }
        
        /// <summary>
        /// Extract artist pattern for adaptive learning (normalize to base form)
        /// </summary>
        private string ExtractArtistPattern(string fullQuery)
        {
            // Extract artist portion (before first album word) and normalize
            var words = fullQuery.Split(' ');
            if (words.Length < 2) return FoldToAscii(fullQuery).ToLowerInvariant();
            
            // For "Björk Homogenic", extract "bjork" as pattern
            var artistWords = words.Take(words.Length / 2); // Rough artist extraction
            return FoldToAscii(string.Join(" ", artistWords)).ToLowerInvariant();
        }
        
        /// <summary>
        /// Update learned patterns for adaptive query prioritization
        /// </summary>
        private void UpdateLearnedPatterns(string originalQuery, string variantType, bool wasSuccessful, int resultCount)
        {
            var artistPattern = ExtractArtistPattern(originalQuery);
            
            if (!_learnedPatterns.ContainsKey(artistPattern))
            {
                _learnedPatterns[artistPattern] = new VariantSuccessRate();
            }
            
            var pattern = _learnedPatterns[artistPattern];
            pattern.TotalAttempts++;
            
            if (!pattern.VariantSuccessRates.ContainsKey(variantType))
            {
                pattern.VariantSuccessRates[variantType] = 0.0;
                pattern.VariantAttempts[variantType] = 0;
            }
            
            pattern.VariantAttempts[variantType]++;
            
            if (wasSuccessful)
            {
                pattern.SuccessfulAttempts++;
                // Update success rate using exponential moving average
                var attempts = pattern.VariantAttempts[variantType];
                var currentRate = pattern.VariantSuccessRates[variantType];
                pattern.VariantSuccessRates[variantType] = (currentRate * (attempts - 1) + 1.0) / attempts;
            }
            
            // Memory management for learned patterns
            if (_learnedPatterns.Count > MaxLearnedPatterns)
            {
                // Remove least recently used patterns
                var oldestPatterns = _learnedPatterns
                    .Where(kvp => kvp.Value.TotalAttempts < 3) // Remove patterns with few data points
                    .Take(_learnedPatterns.Count - MaxLearnedPatterns + 100)
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var key in oldestPatterns)
                {
                    _learnedPatterns.Remove(key);
                }
                
                if (oldestPatterns.Any())
                {
                    _logger.Debug($"Cleaned up learned patterns: removed {oldestPatterns.Count} low-usage patterns");
                }
            }
        }
    }
    
    /// <summary>
    /// Tracks success rates for different variant types per artist pattern
    /// </summary>
    public class VariantSuccessRate
    {
        public int TotalAttempts { get; set; }
        public int SuccessfulAttempts { get; set; }
        public Dictionary<string, double> VariantSuccessRates { get; set; } = new();
        public Dictionary<string, int> VariantAttempts { get; set; } = new();
        public double OverallSuccessRate => TotalAttempts > 0 ? (double)SuccessfulAttempts / TotalAttempts : 0.0;
    }
}