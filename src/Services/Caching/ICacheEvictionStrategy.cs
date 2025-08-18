using System;
using System.Collections.Generic;

namespace Lidarr.Plugin.Qobuzarr.Services.Caching
{
    /// <summary>
    /// Interface for cache eviction strategies
    /// </summary>
    /// <typeparam name="TEntry">Type of cache entries</typeparam>
    public interface ICacheEvictionStrategy<TEntry> where TEntry : class
    {
        /// <summary>
        /// Determines which entries should be evicted based on the strategy's policy
        /// </summary>
        /// <param name="allEntries">All current cache entries</param>
        /// <param name="maxCacheSize">Maximum allowed cache size</param>
        /// <param name="currentSize">Current cache size</param>
        /// <returns>Collection of entries to evict</returns>
        IEnumerable<TEntry> SelectEntriesForEviction(IEnumerable<TEntry> allEntries, int maxCacheSize, int currentSize);

        /// <summary>
        /// Gets the eviction strategy name for logging and diagnostics
        /// </summary>
        string StrategyName { get; }

        /// <summary>
        /// Gets or sets the eviction percentage (0.0 to 1.0)
        /// Percentage of entries to evict when cache exceeds maximum size
        /// </summary>
        double EvictionPercentage { get; set; }
    }
}