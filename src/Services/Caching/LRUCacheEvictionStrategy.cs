using System;
using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services.Caching
{
    /// <summary>
    /// Interface for cache entries that support creation time-based eviction
    /// </summary>
    public interface ITimestampedCacheEntry
    {
        /// <summary>
        /// Gets the creation timestamp of the cache entry
        /// </summary>
        DateTime CreatedAt { get; }
    }

    /// <summary>
    /// LRU (Least Recently Used) cache eviction strategy
    /// Evicts oldest entries first based on creation timestamp
    /// </summary>
    /// <typeparam name="TEntry">Type of cache entries that implement ITimestampedCacheEntry</typeparam>
    public class LRUCacheEvictionStrategy<TEntry> : ICacheEvictionStrategy<TEntry> 
        where TEntry : class, ITimestampedCacheEntry
    {
        /// <summary>
        /// Gets the eviction strategy name
        /// </summary>
        public string StrategyName => "LRU (Oldest First)";

        /// <summary>
        /// Gets or sets the eviction percentage (0.0 to 1.0)
        /// Defaults to 10% as per cache configuration
        /// </summary>
        public double EvictionPercentage { get; set; }

        /// <summary>
        /// Initializes a new LRU cache eviction strategy
        /// </summary>
        /// <param name="evictionPercentage">Percentage of entries to evict (0.0 to 1.0)</param>
        public LRUCacheEvictionStrategy(double evictionPercentage = CacheConfiguration.CacheEvictionPercentage)
        {
            EvictionPercentage = Guard.InRange(
                evictionPercentage, 
                0.0, 
                1.0, 
                nameof(evictionPercentage));
        }

        /// <summary>
        /// Selects entries for eviction based on LRU policy (oldest entries first)
        /// </summary>
        /// <param name="allEntries">All current cache entries</param>
        /// <param name="maxCacheSize">Maximum allowed cache size</param>
        /// <param name="currentSize">Current cache size</param>
        /// <returns>Collection of oldest entries to evict</returns>
        public IEnumerable<TEntry> SelectEntriesForEviction(IEnumerable<TEntry> allEntries, int maxCacheSize, int currentSize)
        {
            Guard.NotNull(allEntries);
            Guard.GreaterThan(maxCacheSize, 0);
            Guard.GreaterThanOrEqualTo(currentSize, 0);

            // Only evict if we're over the limit
            if (currentSize <= maxCacheSize)
            {
                return Enumerable.Empty<TEntry>();
            }

            // Calculate how many entries to evict
            var evictionCount = Math.Min(
                (int)(currentSize * EvictionPercentage),
                CacheConfiguration.MaxEvictionCount);

            // Ensure we evict at least enough to get under the limit
            var minimumEviction = currentSize - maxCacheSize;
            evictionCount = Math.Max(evictionCount, minimumEviction);

            // Select oldest entries for eviction
            return allEntries
                .OrderBy(entry => entry.CreatedAt)
                .Take(evictionCount);
        }
    }

    /// <summary>
    /// Hit-count based cache eviction strategy
    /// Evicts least frequently used entries first
    /// </summary>
    /// <typeparam name="TEntry">Type of cache entries</typeparam>
    public class LFUCacheEvictionStrategy<TEntry> : ICacheEvictionStrategy<TEntry> 
        where TEntry : class
    {
        private readonly Func<TEntry, int> _hitCountAccessor;

        /// <summary>
        /// Gets the eviction strategy name
        /// </summary>
        public string StrategyName => "LFU (Least Frequently Used)";

        /// <summary>
        /// Gets or sets the eviction percentage (0.0 to 1.0)
        /// </summary>
        public double EvictionPercentage { get; set; }

        /// <summary>
        /// Initializes a new LFU cache eviction strategy
        /// </summary>
        /// <param name="hitCountAccessor">Function to get hit count from cache entry</param>
        /// <param name="evictionPercentage">Percentage of entries to evict (0.0 to 1.0)</param>
        public LFUCacheEvictionStrategy(
            Func<TEntry, int> hitCountAccessor,
            double evictionPercentage = CacheConfiguration.CacheEvictionPercentage)
        {
            _hitCountAccessor = Guard.NotNull(hitCountAccessor);
            EvictionPercentage = Guard.InRange(
                evictionPercentage, 
                0.0, 
                1.0, 
                nameof(evictionPercentage));
        }

        /// <summary>
        /// Selects entries for eviction based on LFU policy (least frequently used entries first)
        /// </summary>
        /// <param name="allEntries">All current cache entries</param>
        /// <param name="maxCacheSize">Maximum allowed cache size</param>
        /// <param name="currentSize">Current cache size</param>
        /// <returns>Collection of least frequently used entries to evict</returns>
        public IEnumerable<TEntry> SelectEntriesForEviction(IEnumerable<TEntry> allEntries, int maxCacheSize, int currentSize)
        {
            Guard.NotNull(allEntries);
            Guard.GreaterThan(maxCacheSize, 0);
            Guard.GreaterThanOrEqualTo(currentSize, 0);

            // Only evict if we're over the limit
            if (currentSize <= maxCacheSize)
            {
                return Enumerable.Empty<TEntry>();
            }

            // Calculate how many entries to evict
            var evictionCount = Math.Min(
                (int)(currentSize * EvictionPercentage),
                CacheConfiguration.MaxEvictionCount);

            // Ensure we evict at least enough to get under the limit
            var minimumEviction = currentSize - maxCacheSize;
            evictionCount = Math.Max(evictionCount, minimumEviction);

            // Select least frequently used entries for eviction
            return allEntries
                .OrderBy(entry => _hitCountAccessor(entry))
                .Take(evictionCount);
        }
    }
}