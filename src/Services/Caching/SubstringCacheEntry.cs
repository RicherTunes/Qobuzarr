using System;
using System.Collections.Generic;

namespace Lidarr.Plugin.Qobuzarr.Services.Caching
{
    /// <summary>
    /// Substring cache entry with all metadata for substring matching
    /// </summary>
    public class SubstringCacheEntry
    {
        /// <summary>
        /// Unique cache key for this entry
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Original artist name as provided by user
        /// </summary>
        public string OriginalArtist { get; set; }

        /// <summary>
        /// Original album name as provided by user
        /// </summary>
        public string OriginalAlbum { get; set; }

        /// <summary>
        /// Original query string that generated this entry (optional)
        /// </summary>
        public string OriginalQuery { get; set; }

        /// <summary>
        /// Normalized artist name for matching
        /// </summary>
        public string NormalizedArtist { get; set; }

        /// <summary>
        /// Normalized album name for matching
        /// </summary>
        public string NormalizedAlbum { get; set; }

        /// <summary>
        /// Cached data object (typically API response)
        /// </summary>
        public object Data { get; set; }

        /// <summary>
        /// When this entry was created
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Checks if this entry has expired based on the given expiration timespan
        /// </summary>
        /// <param name="expiration">Expiration timespan</param>
        /// <returns>True if expired, false otherwise</returns>
        public bool IsExpired(TimeSpan expiration)
        {
            return DateTime.UtcNow - CreatedAt > expiration;
        }

        /// <summary>
        /// Gets a descriptive string representation of this cache entry
        /// </summary>
        /// <returns>String representation</returns>
        public override string ToString()
        {
            return $"SubstringCacheEntry[Key={Key}, Artist={OriginalArtist}, Album={OriginalAlbum}, Created={CreatedAt:yyyy-MM-dd HH:mm:ss}]";
        }
    }

    /// <summary>
    /// Result from substring cache lookup with match metadata
    /// </summary>
    public class SubstringCacheResult
    {
        /// <summary>
        /// Type of match found (ExactMatch, ArtistSubstring, AlbumSubstring, FuzzyMatch, ArtistDiscography)
        /// </summary>
        public string MatchType { get; set; }

        /// <summary>
        /// Confidence score for this match (0.0 to 1.0)
        /// </summary>
        public double Confidence { get; set; }

        /// <summary>
        /// The cached data object
        /// </summary>
        public object CachedData { get; set; }

        /// <summary>
        /// Original query that created this cached data
        /// </summary>
        public string OriginalQuery { get; set; }

        /// <summary>
        /// Number of times this cache entry has been hit
        /// </summary>
        public int HitCount { get; set; }

        /// <summary>
        /// Alternative matches that were found (lower confidence)
        /// </summary>
        public List<object> AlternativeMatches { get; set; } = new List<object>();

        /// <summary>
        /// Gets a descriptive string representation of this cache result
        /// </summary>
        /// <returns>String representation</returns>
        public override string ToString()
        {
            return $"SubstringCacheResult[Type={MatchType}, Confidence={Confidence:F2}, Hits={HitCount}, Alternatives={AlternativeMatches?.Count ?? 0}]";
        }
    }
}