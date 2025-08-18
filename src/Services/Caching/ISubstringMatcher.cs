using System;
using System.Collections.Generic;

namespace Lidarr.Plugin.Qobuzarr.Services.Caching
{
    /// <summary>
    /// Interface for substring matching and similarity calculation
    /// </summary>
    public interface ISubstringMatcher
    {
        /// <summary>
        /// Normalizes a string for improved matching accuracy
        /// </summary>
        /// <param name="input">Input string to normalize</param>
        /// <returns>Normalized string optimized for similarity comparison</returns>
        string NormalizeString(string input);

        /// <summary>
        /// Calculates similarity score between two strings using Levenshtein distance
        /// </summary>
        /// <param name="s1">First string for comparison</param>
        /// <param name="s2">Second string for comparison</param>
        /// <returns>Similarity score where 1.0 = identical, 0.0 = completely different</returns>
        double CalculateSimilarity(string s1, string s2);

        /// <summary>
        /// Checks if two strings are similar above the specified threshold
        /// </summary>
        /// <param name="s1">First string for comparison</param>
        /// <param name="s2">Second string for comparison</param>
        /// <param name="threshold">Similarity threshold (0.0 to 1.0)</param>
        /// <returns>True if strings are similar above threshold, false otherwise</returns>
        bool IsSimilar(string s1, string s2, double threshold);

        /// <summary>
        /// Finds entries with artist names that contain or are contained by the search artist
        /// </summary>
        /// <typeparam name="TEntry">Type of cache entries</typeparam>
        /// <param name="entries">Collection of entries to search</param>
        /// <param name="searchArtist">Normalized artist name to match against</param>
        /// <param name="searchAlbum">Normalized album name for similarity scoring</param>
        /// <param name="artistAccessor">Function to get normalized artist from entry</param>
        /// <param name="albumAccessor">Function to get normalized album from entry</param>
        /// <param name="similarityThreshold">Minimum similarity threshold</param>
        /// <returns>Matching cache entries</returns>
        IEnumerable<TEntry> FindArtistMatches<TEntry>(
            IEnumerable<TEntry> entries,
            string searchArtist,
            string searchAlbum,
            Func<TEntry, string> artistAccessor,
            Func<TEntry, string> albumAccessor,
            double similarityThreshold) where TEntry : class;

        /// <summary>
        /// Finds entries with album names that contain or are contained by the search album
        /// </summary>
        /// <typeparam name="TEntry">Type of cache entries</typeparam>
        /// <param name="entries">Collection of entries to search</param>
        /// <param name="searchArtist">Normalized artist name for similarity scoring</param>
        /// <param name="searchAlbum">Normalized album name to match against</param>
        /// <param name="artistAccessor">Function to get normalized artist from entry</param>
        /// <param name="albumAccessor">Function to get normalized album from entry</param>
        /// <param name="similarityThreshold">Minimum similarity threshold</param>
        /// <returns>Matching cache entries</returns>
        IEnumerable<TEntry> FindAlbumMatches<TEntry>(
            IEnumerable<TEntry> entries,
            string searchArtist,
            string searchAlbum,
            Func<TEntry, string> artistAccessor,
            Func<TEntry, string> albumAccessor,
            double similarityThreshold) where TEntry : class;

        /// <summary>
        /// Performs comprehensive fuzzy matching using combined artist+album similarity
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
        IEnumerable<TEntry> FindFuzzyMatches<TEntry>(
            IEnumerable<TEntry> entries,
            string searchArtist,
            string searchAlbum,
            Func<TEntry, string> artistAccessor,
            Func<TEntry, string> albumAccessor,
            double similarityThreshold,
            int maxResults = 5) where TEntry : class;
    }
}