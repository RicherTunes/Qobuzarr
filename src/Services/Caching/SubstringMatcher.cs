using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services.Caching
{
    /// <summary>
    /// Implementation of substring matching and similarity calculation algorithms
    /// </summary>
    public class SubstringMatcher : ISubstringMatcher
    {
        private static readonly string[] CommonWords = 
        {
            "the", "a", "an", "and", "or", "of", "in", "on", "at", "to", "for"
        };

        /// <summary>
        /// Normalizes a string for improved matching accuracy
        /// Handles case, punctuation, extra whitespace, and common stop words
        /// </summary>
        /// <param name="input">Input string to normalize</param>
        /// <returns>Normalized string optimized for similarity comparison</returns>
        public string NormalizeString(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            // Convert to lowercase
            var normalized = input.ToLowerInvariant();
            
            // Remove punctuation and extra whitespace
            normalized = Regex.Replace(normalized, @"[^\w\s]", " ");
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
            
            // Remove common words
            var words = normalized.Split(' ').Where(w => !CommonWords.Contains(w));
            
            return string.Join(" ", words);
        }

        /// <summary>
        /// Calculates similarity score between two strings using Levenshtein distance
        /// Normalizes by maximum string length to provide consistent scoring
        /// </summary>
        /// <param name="s1">First string for comparison</param>
        /// <param name="s2">Second string for comparison</param>
        /// <returns>Similarity score where 1.0 = identical, 0.0 = completely different</returns>
        public double CalculateSimilarity(string s1, string s2)
        {
            return Utilities.StringSimilarity.Calculate(s1, s2);
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
            Utilities.Guard.InRange(threshold, 0.0, 1.0);
            return CalculateSimilarity(s1, s2) >= threshold;
        }

        /// <summary>
        /// Finds entries with artist names that contain or are contained by the search artist
        /// Applies similarity scoring to album titles for match qualification
        /// </summary>
        /// <typeparam name="TEntry">Type of cache entries</typeparam>
        /// <param name="entries">Collection of entries to search</param>
        /// <param name="searchArtist">Normalized artist name to match against</param>
        /// <param name="searchAlbum">Normalized album name for similarity scoring</param>
        /// <param name="artistAccessor">Function to get normalized artist from entry</param>
        /// <param name="albumAccessor">Function to get normalized album from entry</param>
        /// <param name="similarityThreshold">Minimum similarity threshold</param>
        /// <returns>Matching cache entries</returns>
        public IEnumerable<TEntry> FindArtistMatches<TEntry>(
            IEnumerable<TEntry> entries,
            string searchArtist,
            string searchAlbum,
            Func<TEntry, string> artistAccessor,
            Func<TEntry, string> albumAccessor,
            double similarityThreshold) where TEntry : class
        {
            Utilities.Guard.NotNull(entries);
            Utilities.Guard.NotNullOrWhiteSpace(searchArtist);
            Utilities.Guard.NotNullOrWhiteSpace(searchAlbum);
            Utilities.Guard.NotNull(artistAccessor);
            Utilities.Guard.NotNull(albumAccessor);
            Utilities.Guard.InRange(similarityThreshold, 0.0, 1.0);

            var matches = new List<TEntry>();

            foreach (var entry in entries)
            {
                var entryArtist = artistAccessor(entry);
                var entryAlbum = albumAccessor(entry);

                // Check for exact artist match or partial artist match
                if (entryArtist == searchArtist || 
                    entryArtist.Contains(searchArtist) || 
                    searchArtist.Contains(entryArtist))
                {
                    var albumSimilarity = CalculateSimilarity(searchAlbum, entryAlbum);
                    
                    // Use slightly lower threshold for partial artist matches
                    var threshold = entryArtist == searchArtist ? similarityThreshold : similarityThreshold * 0.9;
                    
                    if (albumSimilarity >= threshold)
                    {
                        matches.Add(entry);
                    }
                }
            }

            return matches.Distinct();
        }

        /// <summary>
        /// Finds entries with album names that contain or are contained by the search album
        /// Applies similarity scoring to artist names for match qualification
        /// </summary>
        /// <typeparam name="TEntry">Type of cache entries</typeparam>
        /// <param name="entries">Collection of entries to search</param>
        /// <param name="searchArtist">Normalized artist name for similarity scoring</param>
        /// <param name="searchAlbum">Normalized album name to match against</param>
        /// <param name="artistAccessor">Function to get normalized artist from entry</param>
        /// <param name="albumAccessor">Function to get normalized album from entry</param>
        /// <param name="similarityThreshold">Minimum similarity threshold</param>
        /// <returns>Matching cache entries</returns>
        public IEnumerable<TEntry> FindAlbumMatches<TEntry>(
            IEnumerable<TEntry> entries,
            string searchArtist,
            string searchAlbum,
            Func<TEntry, string> artistAccessor,
            Func<TEntry, string> albumAccessor,
            double similarityThreshold) where TEntry : class
        {
            Utilities.Guard.NotNull(entries);
            Utilities.Guard.NotNullOrWhiteSpace(searchArtist);
            Utilities.Guard.NotNullOrWhiteSpace(searchAlbum);
            Utilities.Guard.NotNull(artistAccessor);
            Utilities.Guard.NotNull(albumAccessor);
            Utilities.Guard.InRange(similarityThreshold, 0.0, 1.0);

            var matches = new List<TEntry>();

            foreach (var entry in entries)
            {
                var entryArtist = artistAccessor(entry);
                var entryAlbum = albumAccessor(entry);

                // Check for exact album match or partial album match
                if (entryAlbum == searchAlbum || 
                    entryAlbum.Contains(searchAlbum) || 
                    searchAlbum.Contains(entryAlbum))
                {
                    var artistSimilarity = CalculateSimilarity(searchArtist, entryArtist);
                    
                    // Use slightly lower threshold for partial album matches
                    var threshold = entryAlbum == searchAlbum ? similarityThreshold : similarityThreshold * 0.9;
                    
                    if (artistSimilarity >= threshold)
                    {
                        matches.Add(entry);
                    }
                }
            }

            return matches.Distinct();
        }

        /// <summary>
        /// Performs comprehensive fuzzy matching using combined artist+album similarity
        /// Last resort matching strategy when substring approaches fail
        /// </summary>
        /// <typeparam name="TEntry">Type of cache entries</typeparam>
        /// <param name="entries">Collection of entries to search</param>
        /// <param name="searchArtist">Normalized artist name</param>
        /// <param name="searchAlbum">Normalized album name</param>
        /// <param name="artistAccessor">Function to get normalized artist from entry</param>
        /// <param name="albumAccessor">Function to get normalized album from entry</param>
        /// <param name="similarityThreshold">Minimum similarity threshold</param>
        /// <param name="maxResults">Maximum number of results to return</param>
        /// <returns>Best matching cache entries ordered by similarity</returns>
        public IEnumerable<TEntry> FindFuzzyMatches<TEntry>(
            IEnumerable<TEntry> entries,
            string searchArtist,
            string searchAlbum,
            Func<TEntry, string> artistAccessor,
            Func<TEntry, string> albumAccessor,
            double similarityThreshold,
            int maxResults = 5) where TEntry : class
        {
            Utilities.Guard.NotNull(entries);
            Utilities.Guard.NotNullOrWhiteSpace(searchArtist);
            Utilities.Guard.NotNullOrWhiteSpace(searchAlbum);
            Utilities.Guard.NotNull(artistAccessor);
            Utilities.Guard.NotNull(albumAccessor);
            Utilities.Guard.InRange(similarityThreshold, 0.0, 1.0);
            Guard.GreaterThan(maxResults, 0);

            var combinedQuery = $"{searchArtist} {searchAlbum}";
            var matches = new List<(TEntry entry, double similarity)>();

            foreach (var entry in entries)
            {
                var entryArtist = artistAccessor(entry);
                var entryAlbum = albumAccessor(entry);
                var combinedEntry = $"{entryArtist} {entryAlbum}";
                
                var similarity = CalculateSimilarity(combinedQuery, combinedEntry);
                
                if (similarity >= similarityThreshold)
                {
                    matches.Add((entry, similarity));
                }
            }

            return matches
                .OrderByDescending(m => m.similarity)
                .Take(maxResults)
                .Select(m => m.entry);
        }

    }
}