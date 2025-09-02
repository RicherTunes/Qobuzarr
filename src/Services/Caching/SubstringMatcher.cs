using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Lidarr.Plugin.Qobuzarr.Services.Caching;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services.Caching
{
    /// <summary>
    /// Substring matcher implementation for fuzzy string matching
    /// </summary>
    public class SubstringMatcher : ISubstringMatcher
    {
        /// <summary>
        /// Normalizes a string for improved matching accuracy
        /// </summary>
        /// <param name="input">Input string to normalize</param>
        /// <returns>Normalized string optimized for similarity comparison</returns>
        public string NormalizeString(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            // Normalize Unicode and remove accents
            var normalized = input.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            
            foreach (char c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            
            normalized = sb.ToString().Normalize(NormalizationForm.FormC);
            
            // Remove extra whitespace and punctuation
            normalized = Regex.Replace(normalized, @"\s+", " ");
            normalized = Regex.Replace(normalized, @"[^\w\s]", "");
            
            return normalized.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Calculates similarity score between two strings using Levenshtein distance
        /// </summary>
        /// <param name="s1">First string for comparison</param>
        /// <param name="s2">Second string for comparison</param>
        /// <returns>Similarity score where 1.0 = identical, 0.0 = completely different</returns>
        public double CalculateSimilarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2))
                return 1.0;
                
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                return 0.0;

            var distance = LevenshteinDistance(s1, s2);
            var maxLength = Math.Max(s1.Length, s2.Length);
            
            return 1.0 - ((double)distance / maxLength);
        }

        /// <summary>
        /// Checks if two strings are similar above the specified threshold
        /// </summary>
        /// <param name="s1">First string for comparison</param>
        /// <param name="s2">Second string for comparison</param>
        /// <param name="threshold">Similarity threshold (0.0 to 1.0)</param>
        /// <returns>True if strings are similar above threshold, false otherwise</returns>
        public bool IsSimilar(string s1, string s2, double threshold)
        {
            Guard.InRange(threshold, 0.0, 1.0);
            return CalculateSimilarity(s1, s2) >= threshold;
        }

        /// <summary>
        /// Finds entries with artist names that contain or are contained by the search artist
        /// </summary>
        public IEnumerable<TEntry> FindArtistMatches<TEntry>(
            IEnumerable<TEntry> entries,
            string searchArtist,
            string searchAlbum,
            Func<TEntry, string> artistAccessor,
            Func<TEntry, string> albumAccessor,
            double similarityThreshold) where TEntry : class
        {
            Guard.NotNull(entries);
            Guard.NotNull(artistAccessor);
            Guard.NotNull(albumAccessor);

            var normalizedSearchArtist = NormalizeString(searchArtist);
            
            return entries.Where(entry =>
            {
                var entryArtist = NormalizeString(artistAccessor(entry));
                return IsSubstringMatch(normalizedSearchArtist, entryArtist) ||
                       CalculateSimilarity(normalizedSearchArtist, entryArtist) >= similarityThreshold;
            });
        }

        /// <summary>
        /// Finds entries with album names that contain or are contained by the search album
        /// </summary>
        public IEnumerable<TEntry> FindAlbumMatches<TEntry>(
            IEnumerable<TEntry> entries,
            string searchArtist,
            string searchAlbum,
            Func<TEntry, string> artistAccessor,
            Func<TEntry, string> albumAccessor,
            double similarityThreshold) where TEntry : class
        {
            Guard.NotNull(entries);
            Guard.NotNull(artistAccessor);
            Guard.NotNull(albumAccessor);

            var normalizedSearchAlbum = NormalizeString(searchAlbum);
            
            return entries.Where(entry =>
            {
                var entryAlbum = NormalizeString(albumAccessor(entry));
                return IsSubstringMatch(normalizedSearchAlbum, entryAlbum) ||
                       CalculateSimilarity(normalizedSearchAlbum, entryAlbum) >= similarityThreshold;
            });
        }

        /// <summary>
        /// Performs comprehensive fuzzy matching using combined artist+album similarity
        /// </summary>
        public IEnumerable<TEntry> FindFuzzyMatches<TEntry>(
            IEnumerable<TEntry> entries,
            string searchArtist,
            string searchAlbum,
            Func<TEntry, string> artistAccessor,
            Func<TEntry, string> albumAccessor,
            double similarityThreshold,
            int maxResults = 5) where TEntry : class
        {
            Guard.NotNull(entries);
            Guard.NotNull(artistAccessor);
            Guard.NotNull(albumAccessor);
            Guard.InRange(maxResults, 1, int.MaxValue);

            var normalizedSearchArtist = NormalizeString(searchArtist);
            var normalizedSearchAlbum = NormalizeString(searchAlbum);
            
            return entries
                .Select(entry => new {
                    Entry = entry,
                    ArtistSimilarity = CalculateSimilarity(normalizedSearchArtist, NormalizeString(artistAccessor(entry))),
                    AlbumSimilarity = CalculateSimilarity(normalizedSearchAlbum, NormalizeString(albumAccessor(entry)))
                })
                .Where(x => (x.ArtistSimilarity + x.AlbumSimilarity) / 2.0 >= similarityThreshold)
                .OrderByDescending(x => (x.ArtistSimilarity + x.AlbumSimilarity) / 2.0)
                .Take(maxResults)
                .Select(x => x.Entry);
        }

        /// <summary>
        /// Checks if two strings have a substring relationship
        /// </summary>
        private bool IsSubstringMatch(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                return false;
                
            return s1.Contains(s2, StringComparison.OrdinalIgnoreCase) ||
                   s2.Contains(s1, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Calculates Levenshtein distance between two strings
        /// </summary>
        private int LevenshteinDistance(string source, string target)
        {
            if (string.IsNullOrEmpty(source))
                return target?.Length ?? 0;
                
            if (string.IsNullOrEmpty(target))
                return source.Length;

            var matrix = new int[source.Length + 1, target.Length + 1];

            for (int i = 0; i <= source.Length; i++)
                matrix[i, 0] = i;
                
            for (int j = 0; j <= target.Length; j++)
                matrix[0, j] = j;

            for (int i = 1; i <= source.Length; i++)
            {
                for (int j = 1; j <= target.Length; j++)
                {
                    var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                    
                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost);
                }
            }

            return matrix[source.Length, target.Length];
        }
    }
}
