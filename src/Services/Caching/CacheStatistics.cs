using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services.Caching
{
    /// <summary>
    /// Thread-safe implementation of cache statistics tracking
    /// </summary>
    public class CacheStatistics : ICacheStatistics
    {
        private readonly ConcurrentDictionary<string, int> _hitCounts;
        private readonly ConcurrentDictionary<string, int> _missCounts;

        /// <summary>
        /// Initializes a new instance of cache statistics
        /// </summary>
        public CacheStatistics()
        {
            _hitCounts = new ConcurrentDictionary<string, int>();
            _missCounts = new ConcurrentDictionary<string, int>();
        }

        /// <summary>
        /// Gets the total number of hits across all keys
        /// </summary>
        public int TotalHits => _hitCounts.Values.Sum();

        /// <summary>
        /// Gets the total number of misses across all keys
        /// </summary>
        public int TotalMisses => _missCounts.Values.Sum();

        /// <summary>
        /// Gets the overall hit rate (0.0 to 1.0)
        /// </summary>
        public double HitRate
        {
            get
            {
                var totalHits = TotalHits;
                var totalRequests = totalHits + TotalMisses;
                return totalRequests > 0 ? (double)totalHits / totalRequests : 0.0;
            }
        }

        /// <summary>
        /// Records a cache hit for the specified key
        /// </summary>
        /// <param name="key">Cache key that was hit</param>
        public void RecordHit(string key)
        {
            Guard.NotNullOrWhiteSpace(key);
            _hitCounts.AddOrUpdate(key, 1, (k, v) => v + 1);
        }

        /// <summary>
        /// Records a cache miss for the specified key
        /// </summary>
        /// <param name="key">Cache key that was missed</param>
        public void RecordMiss(string key)
        {
            Guard.NotNullOrWhiteSpace(key);
            _missCounts.AddOrUpdate(key, 1, (k, v) => v + 1);
        }

        /// <summary>
        /// Gets the hit count for a specific key
        /// </summary>
        /// <param name="key">Cache key</param>
        /// <returns>Number of hits for the key, 0 if key not found</returns>
        public int GetHitCount(string key)
        {
            Guard.NotNullOrWhiteSpace(key);
            return _hitCounts.TryGetValue(key, out var count) ? count : 0;
        }

        /// <summary>
        /// Gets the miss count for a specific key
        /// </summary>
        /// <param name="key">Cache key</param>
        /// <returns>Number of misses for the key, 0 if key not found</returns>
        public int GetMissCount(string key)
        {
            Guard.NotNullOrWhiteSpace(key);
            return _missCounts.TryGetValue(key, out var count) ? count : 0;
        }

        /// <summary>
        /// Gets all keys that have hit counts
        /// </summary>
        /// <returns>Collection of keys with hit counts</returns>
        public IEnumerable<string> GetHitKeys()
        {
            return _hitCounts.Keys;
        }

        /// <summary>
        /// Gets all keys that have miss counts
        /// </summary>
        /// <returns>Collection of keys with miss counts</returns>
        public IEnumerable<string> GetMissKeys()
        {
            return _missCounts.Keys;
        }

        /// <summary>
        /// Gets comprehensive cache statistics
        /// </summary>
        /// <param name="totalEntries">Total number of entries in cache</param>
        /// <param name="uniqueArtists">Number of unique artists in cache</param>
        /// <param name="uniqueAlbums">Number of unique albums in cache</param>
        /// <returns>Statistics object with calculated metrics</returns>
        public CacheStatisticsSnapshot GetStatistics(int totalEntries, int uniqueArtists, int uniqueAlbums)
        {
            Guard.GreaterThanOrEqualTo(totalEntries, 0);
            Guard.GreaterThanOrEqualTo(uniqueArtists, 0);
            Guard.GreaterThanOrEqualTo(uniqueAlbums, 0);

            var totalHits = TotalHits;
            var totalMisses = TotalMisses;

            return new CacheStatisticsSnapshot
            {
                TotalEntries = totalEntries,
                TotalHits = totalHits,
                TotalMisses = totalMisses,
                UniqueArtists = uniqueArtists,
                UniqueAlbums = uniqueAlbums,
                AverageHitsPerEntry = totalEntries > 0 ? (double)totalHits / totalEntries : 0.0,
                CacheSizeBytes = EstimateMemoryUsage(totalEntries),
                HitRate = HitRate,
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Removes statistics for a specific key
        /// </summary>
        /// <param name="key">Key to remove statistics for</param>
        public void RemoveKey(string key)
        {
            Guard.NotNullOrWhiteSpace(key);
            _hitCounts.TryRemove(key, out _);
            _missCounts.TryRemove(key, out _);
        }

        /// <summary>
        /// Clears all statistics
        /// </summary>
        public void Clear()
        {
            _hitCounts.Clear();
            _missCounts.Clear();
        }

        /// <summary>
        /// Estimates memory usage based on entry count and average entry size
        /// Uses configuration-based estimate for substring cache entries
        /// </summary>
        /// <param name="totalEntries">Number of cache entries</param>
        /// <returns>Estimated memory consumption in bytes</returns>
        private long EstimateMemoryUsage(int totalEntries)
        {
            return CacheConfiguration.EstimateMemoryUsage(totalEntries, CacheConfiguration.SubstringCacheEntrySize);
        }
    }
}