using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NLog;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Unicode normalization service for international artist and album matching
    /// Handles complex character encoding issues that cause matching failures for non-English content
    /// </summary>
    /// <remarks>
    /// Critical Issue: International artists often fail matching due to:
    /// - Different Unicode encodings (UTF-8 vs EUC-KR vs GB2312)
    /// - Composed vs decomposed character forms (é vs e+´)  
    /// - Romanization variations (블랙핑크 vs BLACKPINK vs BlackPink)
    /// - Right-to-left text with directional markers
    /// - Mixed script content (Latin + Cyrillic + CJK)
    /// 
    /// This normalizer:
    /// 1. Standardizes Unicode forms for consistent comparison
    /// 2. Handles romanization and transliteration variants
    /// 3. Manages directional text and mixed scripts
    /// 4. Provides fuzzy matching for international content
    /// 5. Enables optimization for global music catalogs
    /// </remarks>
    public class UnicodeNormalizer
    {
        private readonly Logger _logger;
        
        // Common romanization mappings for major languages
        private static readonly Dictionary<string, Dictionary<string, string>> RomanizationMaps = new()
        {
            // Korean romanization variants
            ["korean"] = new Dictionary<string, string>
            {
                ["ㅂ"] = "b", ["ㅍ"] = "p", ["ㅃ"] = "bb",
                ["ㄱ"] = "g", ["ㅋ"] = "k", ["ㄲ"] = "gg",
                ["ㄷ"] = "d", ["ㅌ"] = "t", ["ㄸ"] = "dd",
                ["ㅅ"] = "s", ["ㅆ"] = "ss", ["ㅈ"] = "j",
                ["ㅊ"] = "ch", ["ㅉ"] = "jj", ["ㅎ"] = "h"
            },
            
            // Japanese romanization variants  
            ["japanese"] = new Dictionary<string, string>
            {
                ["あ"] = "a", ["か"] = "ka", ["さ"] = "sa", ["た"] = "ta",
                ["な"] = "na", ["は"] = "ha", ["ま"] = "ma", ["や"] = "ya",
                ["ら"] = "ra", ["わ"] = "wa", ["を"] = "wo", ["ん"] = "n"
            },
            
            // Chinese pinyin variants
            ["chinese"] = new Dictionary<string, string>
            {
                ["zh"] = "z", ["ch"] = "c", ["sh"] = "s",
                ["ü"] = "u", ["ê"] = "e"
            }
        };

        // Diacritic removal mappings for major European languages
        private static readonly Dictionary<char, char> DiacriticMappings = new()
        {
            // Latin extended
            ['á'] = 'a', ['à'] = 'a', ['ä'] = 'a', ['â'] = 'a', ['ã'] = 'a', ['å'] = 'a',
            ['é'] = 'e', ['è'] = 'e', ['ë'] = 'e', ['ê'] = 'e', ['ė'] = 'e', ['ę'] = 'e',
            ['í'] = 'i', ['ì'] = 'i', ['ï'] = 'i', ['î'] = 'i', ['į'] = 'i',
            ['ó'] = 'o', ['ò'] = 'o', ['ö'] = 'o', ['ô'] = 'o', ['õ'] = 'o', ['ø'] = 'o',
            ['ú'] = 'u', ['ù'] = 'u', ['ü'] = 'u', ['û'] = 'u', ['ū'] = 'u', ['ų'] = 'u',
            ['ý'] = 'y', ['ÿ'] = 'y',
            ['ñ'] = 'n', ['ç'] = 'c', ['ğ'] = 'g', ['ş'] = 's', ['ı'] = 'i',
            ['ć'] = 'c', ['č'] = 'c', ['ď'] = 'd', ['ľ'] = 'l', ['ł'] = 'l',
            ['ń'] = 'n', ['ř'] = 'r', ['ś'] = 's', ['š'] = 's', ['ť'] = 't',
            ['ź'] = 'z', ['ž'] = 'z', ['ż'] = 'z',
            
            // Cyrillic to Latin approximations (common cases)
            ['а'] = 'a', ['е'] = 'e', ['и'] = 'i', ['о'] = 'o', ['у'] = 'u', ['ы'] = 'y'
        };

        // Common artist name variations for major international artists
        private static readonly Dictionary<string, string[]> KnownVariations = new()
        {
            // Korean artists
            ["blackpink"] = new[] { "블랙핑크", "BLACKPINK", "BlackPink", "Black Pink" },
            ["bts"] = new[] { "방탄소년단", "BTS", "Bangtan Boys", "Bangtan Sonyeondan" },
            ["bigbang"] = new[] { "빅뱅", "BIGBANG", "Big Bang" },
            
            // Japanese artists  
            ["babymetal"] = new[] { "ベビーメタル", "BABYMETAL", "Baby Metal" },
            ["xjapan"] = new[] { "エックス・ジャパン", "X JAPAN", "X-Japan" },
            
            // Chinese artists
            ["jackson wang"] = new[] { "王嘉尔", "Jackson Wang", "Jackson Wang 王嘉尔" },
            ["jay chou"] = new[] { "周杰倫", "Jay Chou", "Jay Chow", "Zhou Jielun" },
            
            // European artists with diacritics
            ["bjork"] = new[] { "Björk", "Bjork", "Björk" },
            ["mylene farmer"] = new[] { "Mylène Farmer", "Mylene Farmer" },
            ["rammstein"] = new[] { "Rammstein", "RAMMSTEIN" }
        };

        public UnicodeNormalizer(Logger logger = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
        }

        /// <summary>
        /// Normalizes text for international matching with comprehensive Unicode handling
        /// </summary>
        /// <param name="input">Text to normalize</param>
        /// <param name="options">Normalization options for specific use cases</param>
        /// <returns>Normalized text optimized for matching</returns>
        public string NormalizeForMatching(string input, UnicodeNormalizationOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            options ??= UnicodeNormalizationOptions.Default;
            var normalized = input.Trim();

            try
            {
                _logger.Debug("🌍 UNICODE NORMALIZATION: '{0}' (options: {1})", input, options.ToString());

                // Step 1: Unicode form normalization
                normalized = NormalizeUnicodeForm(normalized, options);

                // Step 2: Remove directional marks for RTL languages
                if (options.RemoveDirectionalMarks)
                    normalized = RemoveDirectionalMarks(normalized);

                // Step 3: Handle diacritics and accents
                if (options.RemoveDiacritics)
                    normalized = RemoveDiacritics(normalized);

                // Step 4: Case normalization
                if (options.NormalizeCase)
                    normalized = normalized.ToLowerInvariant();

                // Step 5: Romanization handling
                if (options.HandleRomanization)
                    normalized = HandleRomanizationVariants(normalized);

                // Step 6: Whitespace and punctuation cleanup
                if (options.NormalizePunctuation)
                    normalized = NormalizePunctuationAndWhitespace(normalized);

                // Step 7: Apply known artist variations
                if (options.UseKnownVariations)
                    normalized = ApplyKnownVariations(normalized);

                var result = normalized.Trim();
                
                if (result != input)
                {
                    _logger.Debug("🌍 NORMALIZED: '{0}' → '{1}'", input, result);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to normalize Unicode text: {0}", input);
                return input; // Return original on failure
            }
        }

        /// <summary>
        /// Calculates similarity between two internationalized strings with Unicode awareness
        /// </summary>
        /// <param name="text1">First string to compare</param>
        /// <param name="text2">Second string to compare</param>
        /// <param name="options">Normalization options for comparison</param>
        /// <returns>Similarity score from 0.0 to 1.0</returns>
        public double CalculateInternationalSimilarity(
            string text1, 
            string text2, 
            UnicodeNormalizationOptions options = null)
        {
            if (string.IsNullOrEmpty(text1) && string.IsNullOrEmpty(text2))
                return 1.0;
                
            if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2))
                return 0.0;

            options ??= UnicodeNormalizationOptions.Default;

            // Normalize both strings
            var normalized1 = NormalizeForMatching(text1, options);
            var normalized2 = NormalizeForMatching(text2, options);

            // Exact match after normalization
            if (normalized1.Equals(normalized2, StringComparison.OrdinalIgnoreCase))
                return 1.0;

            // Check for known variations
            if (options.UseKnownVariations)
            {
                var similarity = CheckKnownVariationsSimilarity(normalized1, normalized2);
                if (similarity >= 0.9)
                    return similarity;
            }

            // Check for substring matches (common in international contexts)
            if (normalized1.Contains(normalized2) || normalized2.Contains(normalized1))
            {
                var longer = normalized1.Length > normalized2.Length ? normalized1 : normalized2;
                var shorter = normalized1.Length <= normalized2.Length ? normalized1 : normalized2;
                return (double)shorter.Length / longer.Length;
            }

            // Fallback to edit distance for final comparison
            return CalculateEditDistanceSimilarity(normalized1, normalized2);
        }

        /// <summary>
        /// Detects the primary script/language of the text for targeted normalization
        /// </summary>
        /// <param name="text">Text to analyze</param>
        /// <returns>Detected script information</returns>
        public ScriptDetectionResult DetectScript(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new ScriptDetectionResult { PrimaryScript = "Unknown" };

            var scriptCounts = new Dictionary<string, int>();
            var totalChars = 0;

            foreach (var ch in text)
            {
                if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch))
                    continue;

                totalChars++;
                var script = GetCharacterScript(ch);
                scriptCounts[script] = scriptCounts.GetValueOrDefault(script, 0) + 1;
            }

            if (scriptCounts.Count == 0)
                return new ScriptDetectionResult { PrimaryScript = "Latin" };

            var primaryScript = scriptCounts.OrderByDescending(kvp => kvp.Value).First();
            var confidence = totalChars > 0 ? (double)primaryScript.Value / totalChars : 0;

            return new ScriptDetectionResult
            {
                PrimaryScript = primaryScript.Key,
                Confidence = confidence,
                ScriptDistribution = scriptCounts,
                IsMixedScript = scriptCounts.Count > 1 && confidence < 0.8
            };
        }

        #region Private Normalization Methods

        /// <summary>
        /// Normalizes Unicode form to ensure consistent character representation
        /// </summary>
        private string NormalizeUnicodeForm(string text, UnicodeNormalizationOptions options)
        {
            // First normalize to decomposed form, then to composed form for consistency
            var normalized = text.Normalize(NormalizationForm.FormD);
            return normalized.Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        /// Removes Unicode directional marks that can interfere with matching
        /// </summary>
        private string RemoveDirectionalMarks(string text)
        {
            // Remove common directional marks
            var directionalMarks = new char[]
            {
                '\u200E', // Left-to-Right Mark
                '\u200F', // Right-to-Left Mark  
                '\u202A', // Left-to-Right Embedding
                '\u202B', // Right-to-Left Embedding
                '\u202C', // Pop Directional Formatting
                '\u202D', // Left-to-Right Override
                '\u202E', // Right-to-Left Override
                '\u2066', // Left-to-Right Isolate
                '\u2067', // Right-to-Left Isolate
                '\u2068', // First Strong Isolate
                '\u2069'  // Pop Directional Isolate
            };

            var result = text;
            foreach (var mark in directionalMarks)
            {
                result = result.Replace(mark.ToString(), "");
            }

            return result;
        }

        /// <summary>
        /// Removes or replaces diacritical marks for better matching
        /// </summary>
        private string RemoveDiacritics(string text)
        {
            var sb = new StringBuilder();

            foreach (var ch in text)
            {
                if (DiacriticMappings.TryGetValue(ch, out var replacement))
                {
                    sb.Append(replacement);
                }
                else
                {
                    // Use Unicode category to identify and remove other diacritics
                    if (char.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                        continue; // Skip combining diacritical marks
                    
                    sb.Append(ch);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Handles romanization variants for CJK languages
        /// </summary>
        private string HandleRomanizationVariants(string text)
        {
            var result = text;

            // Apply romanization mappings for detected scripts
            var script = DetectScript(text);
            
            if (script.PrimaryScript == "Hangul" && RomanizationMaps.TryGetValue("korean", out var koreanMap))
            {
                foreach (var mapping in koreanMap)
                {
                    result = result.Replace(mapping.Key, mapping.Value);
                }
            }
            else if (script.PrimaryScript == "Hiragana" && RomanizationMaps.TryGetValue("japanese", out var japaneseMap))
            {
                foreach (var mapping in japaneseMap)
                {
                    result = result.Replace(mapping.Key, mapping.Value);
                }
            }

            return result;
        }

        /// <summary>
        /// Normalizes punctuation and whitespace for consistent matching
        /// </summary>
        private string NormalizePunctuationAndWhitespace(string text)
        {
            // Replace various quotation marks with standard ones
            var normalized = text
                .Replace("\u201C", "\"").Replace("\u201D", "\"")  // Smart quotes
                .Replace("\u2018", "'").Replace("\u2019", "'")     // Smart apostrophes  
                .Replace("\u2014", "-").Replace("\u2013", "-")     // Em/en dashes
                .Replace("\u2026", "...");                         // Ellipsis

            // Normalize whitespace
            normalized = Regex.Replace(normalized, @"\s+", " ");
            
            // Remove common punctuation that doesn't affect meaning
            normalized = Regex.Replace(normalized, @"[^\w\s]", " ");
            
            return Regex.Replace(normalized, @"\s+", " ").Trim();
        }

        /// <summary>
        /// Applies known artist name variations for common international artists
        /// </summary>
        private string ApplyKnownVariations(string text)
        {
            var normalized = text.ToLowerInvariant();

            foreach (var variation in KnownVariations)
            {
                if (variation.Value.Any(v => v.ToLowerInvariant() == normalized))
                {
                    _logger.Debug("🎭 KNOWN VARIATION: '{0}' → '{1}'", text, variation.Key);
                    return variation.Key;
                }
            }

            return text;
        }

        /// <summary>
        /// Checks similarity using known variations database
        /// </summary>
        private double CheckKnownVariationsSimilarity(string text1, string text2)
        {
            var norm1 = text1.ToLowerInvariant();
            var norm2 = text2.ToLowerInvariant();

            foreach (var variations in KnownVariations.Values)
            {
                var variations1 = variations.Select(v => v.ToLowerInvariant()).ToArray();
                
                var has1 = variations1.Contains(norm1);
                var has2 = variations1.Contains(norm2);
                
                if (has1 && has2)
                    return 1.0; // Perfect match via known variations
            }

            return 0.0;
        }

        /// <summary>
        /// Determines the script category for a character
        /// </summary>
        private string GetCharacterScript(char ch)
        {
            var category = char.GetUnicodeCategory(ch);
            
            // CJK ranges
            if (ch >= 0x4E00 && ch <= 0x9FFF) return "CJK";
            if (ch >= 0x3400 && ch <= 0x4DBF) return "CJK";
            if (ch >= 0x20000 && ch <= 0x2A6DF) return "CJK";
            
            // Korean
            if (ch >= 0xAC00 && ch <= 0xD7AF) return "Hangul";
            if (ch >= 0x1100 && ch <= 0x11FF) return "Hangul";
            if (ch >= 0x3130 && ch <= 0x318F) return "Hangul";
            
            // Japanese
            if (ch >= 0x3040 && ch <= 0x309F) return "Hiragana";
            if (ch >= 0x30A0 && ch <= 0x30FF) return "Katakana";
            
            // Arabic
            if (ch >= 0x0600 && ch <= 0x06FF) return "Arabic";
            if (ch >= 0x0750 && ch <= 0x077F) return "Arabic";
            
            // Hebrew
            if (ch >= 0x0590 && ch <= 0x05FF) return "Hebrew";
            
            // Cyrillic
            if (ch >= 0x0400 && ch <= 0x04FF) return "Cyrillic";
            
            // Thai
            if (ch >= 0x0E00 && ch <= 0x0E7F) return "Thai";
            
            // Default to Latin for basic ASCII and Latin extended
            return "Latin";
        }

        /// <summary>
        /// Calculates similarity using edit distance with Unicode awareness
        /// </summary>
        private double CalculateEditDistanceSimilarity(string s1, string s2)
        {
            return StringSimilarity.Calculate(s1, s2);
        }


        #endregion
    }

    #region Configuration and Result Classes

    /// <summary>
    /// Options for Unicode normalization behavior
    /// </summary>
    public class UnicodeNormalizationOptions
    {
        public bool RemoveDirectionalMarks { get; set; } = true;
        public bool RemoveDiacritics { get; set; } = true;
        public bool NormalizeCase { get; set; } = true;
        public bool HandleRomanization { get; set; } = true;
        public bool NormalizePunctuation { get; set; } = true;
        public bool UseKnownVariations { get; set; } = true;

        public static UnicodeNormalizationOptions Default => new();
        
        public static UnicodeNormalizationOptions Conservative => new()
        {
            RemoveDirectionalMarks = true,
            RemoveDiacritics = false,
            NormalizeCase = true,
            HandleRomanization = false,
            NormalizePunctuation = true,
            UseKnownVariations = false
        };

        public static UnicodeNormalizationOptions Aggressive => new()
        {
            RemoveDirectionalMarks = true,
            RemoveDiacritics = true,
            NormalizeCase = true,
            HandleRomanization = true,
            NormalizePunctuation = true,
            UseKnownVariations = true
        };

        public override string ToString()
        {
            var features = new List<string>();
            if (RemoveDirectionalMarks) features.Add("DirectionalMarks");
            if (RemoveDiacritics) features.Add("Diacritics");
            if (NormalizeCase) features.Add("Case");
            if (HandleRomanization) features.Add("Romanization");
            if (NormalizePunctuation) features.Add("Punctuation");
            if (UseKnownVariations) features.Add("KnownVariations");
            
            return string.Join("|", features);
        }
    }

    /// <summary>
    /// Result of script detection analysis
    /// </summary>
    public class ScriptDetectionResult
    {
        public string PrimaryScript { get; set; }
        public double Confidence { get; set; }
        public Dictionary<string, int> ScriptDistribution { get; set; } = new();
        public bool IsMixedScript { get; set; }

        public bool IsRightToLeft => PrimaryScript == "Arabic" || PrimaryScript == "Hebrew";
        public bool IsCJK => PrimaryScript == "CJK" || PrimaryScript == "Hangul" || 
                           PrimaryScript == "Hiragana" || PrimaryScript == "Katakana";
    }

    #endregion
}