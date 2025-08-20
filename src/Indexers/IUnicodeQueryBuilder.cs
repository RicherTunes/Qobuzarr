using System.Collections.Generic;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Interface for generating Unicode-aware query variants to handle international artists and albums.
    /// Solves the core problem of missed albums due to special characters, diacritics, and non-ASCII text.
    /// </summary>
    public interface IUnicodeQueryBuilder
    {
        /// <summary>
        /// Generate a prioritized list of query variants for an artist and album combination.
        /// Variants are ordered by probability of success based on complexity analysis.
        /// </summary>
        /// <param name="artist">The artist name (may contain Unicode characters)</param>
        /// <param name="album">The album title (may contain Unicode characters)</param>
        /// <param name="maxVariants">Maximum number of variants to generate (default: 6)</param>
        /// <returns>Ordered list of query strings, most likely to succeed first</returns>
        List<string> GenerateQueryVariants(string artist, string album, int maxVariants = 6);
        
        /// <summary>
        /// Generate variants for artist name only, useful for artist-specific searches.
        /// </summary>
        /// <param name="artist">The artist name</param>
        /// <param name="maxVariants">Maximum number of variants to generate</param>
        /// <returns>Ordered list of artist name variants</returns>
        List<string> GenerateArtistVariants(string artist, int maxVariants = 4);
        
        /// <summary>
        /// Generate variants for album title only, useful for album-specific searches.
        /// </summary>
        /// <param name="album">The album title</param>
        /// <param name="maxVariants">Maximum number of variants to generate</param>
        /// <returns>Ordered list of album title variants</returns>
        List<string> GenerateAlbumVariants(string album, int maxVariants = 4);
        
        /// <summary>
        /// Determine if a query contains problematic Unicode characters that require special handling.
        /// </summary>
        /// <param name="query">The query string to analyze</param>
        /// <returns>True if the query contains Unicode that may cause search issues</returns>
        bool RequiresUnicodeHandling(string query);
        
        /// <summary>
        /// Get statistics about query variant performance for optimization.
        /// </summary>
        /// <returns>Performance metrics for different variant types</returns>
        UnicodeQueryStatistics GetPerformanceStatistics();
        
        /// <summary>
        /// Record the success or failure of a specific query variant for learning.
        /// </summary>
        /// <param name="originalQuery">The original query with Unicode</param>
        /// <param name="variantUsed">The specific variant that was tried</param>
        /// <param name="wasSuccessful">Whether this variant found results</param>
        /// <param name="resultCount">Number of results found (0 if unsuccessful)</param>
        void RecordVariantResult(string originalQuery, string variantUsed, bool wasSuccessful, int resultCount);
    }
    
    /// <summary>
    /// Statistics for tracking Unicode query variant performance
    /// </summary>
    public class UnicodeQueryStatistics
    {
        public int TotalQueries { get; set; }
        public int UnicodeQueries { get; set; }
        public Dictionary<string, VariantPerformance> VariantStats { get; set; } = new();
        public double OverallSuccessRate { get; set; }
        public double UnicodeSuccessRate { get; set; }
        public Dictionary<string, int> TopFailurePatterns { get; set; } = new();
    }
    
    /// <summary>
    /// Performance metrics for a specific query variant type
    /// </summary>
    public class VariantPerformance
    {
        public int TimesUsed { get; set; }
        public int TimesSuccessful { get; set; }
        public double SuccessRate => TimesUsed > 0 ? (double)TimesSuccessful / TimesUsed : 0.0;
        public double AverageResultCount { get; set; }
        public double AverageResponseTime { get; set; }
    }
}