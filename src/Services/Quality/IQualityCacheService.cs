using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Services.Quality
{
    /// <summary>
    /// Service interface for caching quality detection results.
    /// Extracted from QobuzQualityManager to follow Single Responsibility Principle.
    /// </summary>
    public interface IQualityCacheService
    {
        /// <summary>
        /// Gets cached quality result for the specified cache key.
        /// </summary>
        Task<Models.AlbumQualityResult> GetCachedQualityAsync(string cacheKey);

        /// <summary>
        /// Caches a quality result for future use.
        /// </summary>
        Task CacheQualityResultAsync(string cacheKey, Models.AlbumQualityResult result);

        /// <summary>
        /// Clears expired entries from the cache.
        /// </summary>
        Task ClearExpiredEntriesAsync();

        /// <summary>
        /// Gets cache statistics.
        /// </summary>
        QualityCacheStats GetCacheStats();

        /// <summary>
        /// Clears all cached entries.
        /// </summary>
        Task ClearAllAsync();
    }

    /// <summary>
    /// Statistics about the quality cache.
    /// </summary>
    public class QualityCacheStats
    {
        public int TotalEntries { get; set; }
        public int ExpiredEntries { get; set; }
        public double HitRatio { get; set; }
        public long MemoryUsageBytes { get; set; }
    }
}