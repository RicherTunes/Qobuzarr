using System;
using System.Collections.Generic;

namespace Lidarr.Plugin.Qobuzarr.Services.Caching
{
    /// <summary>
    /// Interface for cache statistics tracking and reporting
    /// </summary>
    public interface ICacheStatistics
    {
        /// <summary>
        /// Records a cache hit for the specified key
        /// </summary>
        /// <param name="key">Cache key that was hit</param>
        void RecordHit(string key);

        /// <summary>
        /// Records a cache miss for the specified key
        /// </summary>
        /// <param name="key">Cache key that was missed</param>
        void RecordMiss(string key);

        /// <summary>
        /// Gets the hit count for a specific key
        /// </summary>
        /// <param name="key">Cache key</param>
        /// <returns>Number of hits for the key, 0 if key not found</returns>
        int GetHitCount(string key);

        /// <summary>
        /// Gets the miss count for a specific key
        /// </summary>
        /// <param name="key">Cache key</param>
        /// <returns>Number of misses for the key, 0 if key not found</returns>
        int GetMissCount(string key);

        /// <summary>
        /// Gets all keys that have hit counts
        /// </summary>
        /// <returns>Collection of keys with hit counts</returns>
        IEnumerable<string> GetHitKeys();

        /// <summary>
        /// Gets all keys that have miss counts
        /// </summary>
        /// <returns>Collection of keys with miss counts</returns>
        IEnumerable<string> GetMissKeys();

        /// <summary>
        /// Gets comprehensive cache statistics
        /// </summary>
        /// <param name="totalEntries">Total number of entries in cache</param>
        /// <param name="uniqueArtists">Number of unique artists in cache</param>
        /// <param name="uniqueAlbums">Number of unique albums in cache</param>
        /// <returns>Statistics object with calculated metrics</returns>
        CacheStatisticsSnapshot GetStatistics(int totalEntries, int uniqueArtists, int uniqueAlbums);

        /// <summary>
        /// Removes statistics for a specific key
        /// </summary>
        /// <param name="key">Key to remove statistics for</param>
        void RemoveKey(string key);

        /// <summary>
        /// Clears all statistics
        /// </summary>
        void Clear();

        /// <summary>
        /// Gets the total number of hits across all keys
        /// </summary>
        int TotalHits { get; }

        /// <summary>
        /// Gets the total number of misses across all keys
        /// </summary>
        int TotalMisses { get; }

        /// <summary>
        /// Gets the overall hit rate (0.0 to 1.0)
        /// </summary>
        double HitRate { get; }
    }

    /// <summary>
    /// Snapshot of cache statistics at a point in time
    /// </summary>
    public class CacheStatisticsSnapshot
    {
        /// <summary>
        /// Total number of entries in cache
        /// </summary>
        public int TotalEntries { get; set; }

        /// <summary>
        /// Total number of cache hits
        /// </summary>
        public int TotalHits { get; set; }

        /// <summary>
        /// Total number of cache misses
        /// </summary>
        public int TotalMisses { get; set; }

        /// <summary>
        /// Number of unique artists in cache
        /// </summary>
        public int UniqueArtists { get; set; }

        /// <summary>
        /// Number of unique albums in cache
        /// </summary>
        public int UniqueAlbums { get; set; }

        /// <summary>
        /// Average number of hits per entry
        /// </summary>
        public double AverageHitsPerEntry { get; set; }

        /// <summary>
        /// Estimated memory usage in bytes
        /// </summary>
        public long CacheSizeBytes { get; set; }

        /// <summary>
        /// Overall hit rate (0.0 to 1.0)
        /// </summary>
        public double HitRate { get; set; }

        /// <summary>
        /// Timestamp when statistics were captured
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}