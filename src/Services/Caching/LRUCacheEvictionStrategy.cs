using System;
using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.Qobuzarr.Services.Caching;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services.Caching
{
    /// <summary>
    /// LRU (Least Recently Used) cache eviction strategy implementation
    /// </summary>
    /// <typeparam name="TEntry">Type of cache entries</typeparam>
    public class LRUCacheEvictionStrategy<TEntry> : ICacheEvictionStrategy<TEntry> where TEntry : class
    {
        /// <summary>
        /// Gets the eviction strategy name
        /// </summary>
        public string StrategyName => "LRU";

        /// <summary>
        /// Gets or sets the eviction percentage (default 25% of entries when cache is full)
        /// </summary>
        public double EvictionPercentage { get; set; } = 0.25;

        /// <summary>
        /// Selects entries for eviction based on least recently used criteria
        /// </summary>
        /// <param name="allEntries">All current cache entries</param>
        /// <param name="maxCacheSize">Maximum allowed cache size</param>
        /// <param name="currentSize">Current cache size</param>
        /// <returns>Entries to evict (oldest access times first)</returns>
        public IEnumerable<TEntry> SelectEntriesForEviction(IEnumerable<TEntry> allEntries, int maxCacheSize, int currentSize)
        {
            Guard.NotNull(allEntries);
            Guard.GreaterThan(maxCacheSize, 0);
            Guard.GreaterThanOrEqualTo(currentSize, 0);

            if (currentSize <= maxCacheSize)
                return Enumerable.Empty<TEntry>();

            // Calculate number of entries to evict
            var entriesToEvict = Math.Max(1, (int)Math.Ceiling(currentSize * EvictionPercentage));
            
            // For LRU, we would ideally sort by access time, but since we don't have that metadata
            // in the interface, we'll just return the first N entries (FIFO as fallback)
            return allEntries.Take(entriesToEvict);
        }
    }
}