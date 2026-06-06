using System;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Lidarr.Plugin.Common.Services.Globalization;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    /// <summary>
    /// Centralized string similarity and normalization utilities to eliminate code duplication.
    /// This class provides the single source of truth for all string matching algorithms.
    /// </summary>
    public static partial class StringSimilarity
    {
        private static readonly UnicodeNormalizer Unicode = new UnicodeNormalizer(NullLogger<UnicodeNormalizer>.Instance);

        [GeneratedRegex(@"[^a-zA-Z0-9\s]")]
        private static partial Regex NonAlphanumericRegex();

        [GeneratedRegex(@"\s+")]
        private static partial Regex MultipleSpacesRegex();

        /// <summary>
        /// Calculates normalized string similarity using Levenshtein distance.
        /// Returns a value between 0.0 (completely different) and 1.0 (identical).
        /// </summary>
        public static double Calculate(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2))
                return 1.0;

            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                return 0.0;

            if (s1.Equals(s2, StringComparison.OrdinalIgnoreCase))
                return 1.0;

            // Light joiner normalization to align with historical behavior
            static string PreNormalize(string x) => x.Replace("&", " and ");

            var p1 = PreNormalize(s1);
            var p2 = PreNormalize(s2);

            // Delegate similarity calculation to shared Unicode-aware implementation
            return Unicode.CalculateInternationalSimilarity(p1, p2);
        }

        /// <summary>
        /// Calculates the Levenshtein distance between two strings.
        /// This is the minimum number of single-character edits required to change one string into the other.
        /// </summary>
        public static int LevenshteinDistance(string s1, string s2)
            // LOOP-010: delegate to Common's canonical implementation (single source of truth).
            => Lidarr.Plugin.Common.Utilities.StringSimilarity.LevenshteinDistance(s1, s2);

        /// <summary>
        /// Normalizes a title for comparison by removing special characters and standardizing format.
        /// This is the standard normalization used across the application.
        /// </summary>
        public static string NormalizeTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;

            var normalized = title.ToLowerInvariant();

            // Common replacements
            normalized = normalized
                .Replace("&", "and")
                .Replace(" - ", " ")
                .Replace("-", " ")
                .Replace(".", " ")
                .Replace(",", " ")
                .Replace("'", "")
                .Replace("\"", "")
                .Replace("(", " ")
                .Replace(")", " ")
                .Replace("[", " ")
                .Replace("]", " ")
                .Replace("{", " ")
                .Replace("}", " ")
                .Replace(":", " ")
                .Replace(";", " ")
                .Replace("!", " ")
                .Replace("?", " ")
                .Replace("/", " ")
                .Replace("\\", " ");

            // Remove any remaining non-alphanumeric characters
            normalized = NonAlphanumericRegex().Replace(normalized, " ");

            // Collapse multiple spaces
            normalized = MultipleSpacesRegex().Replace(normalized, " ");

            return normalized.Trim();
        }

        /// <summary>
        /// Calculates Jaro similarity between two strings.
        /// Returns a value between 0.0 and 1.0 indicating similarity.
        /// </summary>
        public static double JaroSimilarity(string s1, string s2)
            // LOOP-010: delegate to Common's canonical implementation (single source of truth).
            => Lidarr.Plugin.Common.Utilities.StringSimilarity.Jaro(s1, s2);

        /// <summary>
        /// Calculates Jaro-Winkler similarity between two strings.
        /// This gives more weight to strings with common prefixes.
        /// </summary>
        public static double JaroWinklerSimilarity(string s1, string s2, double prefixScale = 0.1)
            // LOOP-010: delegate to Common's canonical implementation (single source of truth).
            => Lidarr.Plugin.Common.Utilities.StringSimilarity.JaroWinkler(s1, s2, prefixScale);

        /// <summary>
        /// Determines if two strings are similar enough based on a threshold.
        /// Uses the most appropriate algorithm based on string characteristics.
        /// </summary>
        public static bool AreSimilar(string s1, string s2, double threshold = 0.85)
        {
            // For very short strings, use exact matching
            if ((s1?.Length ?? 0) < 3 || (s2?.Length ?? 0) < 3)
                return string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase);

            // For strings with similar lengths, use Levenshtein
            var lengthRatio = (double)Math.Min(s1.Length, s2.Length) / Math.Max(s1.Length, s2.Length);
            if (lengthRatio > 0.7)
                return Calculate(s1, s2) >= threshold;

            // For strings with different lengths, use Jaro-Winkler
            return JaroWinklerSimilarity(s1, s2) >= threshold;
        }

        /// <summary>
        /// Calculates track similarity score specifically for music tracks.
        /// Takes into account track number, duration, and title.
        /// </summary>
        public static double CalculateTrackSimilarity(
            string title1, string title2,
            int? trackNumber1, int? trackNumber2,
            TimeSpan? duration1, TimeSpan? duration2)
        {
            double score = 0.0;

            // Title similarity (70% weight)
            var titleSimilarity = Calculate(NormalizeTitle(title1), NormalizeTitle(title2));
            score += titleSimilarity * 0.7;

            // Track number match (20% weight)
            if (trackNumber1.HasValue && trackNumber2.HasValue && trackNumber1 == trackNumber2)
            {
                score += 0.2;
            }

            // Duration similarity (10% weight)
            if (duration1.HasValue && duration2.HasValue)
            {
                var durationDiff = Math.Abs((duration1.Value - duration2.Value).TotalSeconds);
                if (durationDiff <= 5) // Within 5 seconds
                {
                    score += 0.1;
                }
                else if (durationDiff <= 10) // Within 10 seconds
                {
                    score += 0.05;
                }
            }

            return Math.Min(1.0, score);
        }
    }
}
