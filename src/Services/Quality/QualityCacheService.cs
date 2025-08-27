using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Services.Quality
{
    /// <summary>
    /// Service for caching quality detection results.
    /// Extracted from QobuzQualityManager to follow Single Responsibility Principle.
    /// </summary>
    public class QualityCacheService : IQualityCacheService
    {
        private readonly IQobuzLogger _logger;
        private readonly Dictionary<string, AlbumQualityCache> _qualityCache;
        private readonly SemaphoreSlim _cacheLock;
        private readonly TimeSpan _cacheExpiration;
        
        // Cache configuration
        private const int MAX_CACHE_ENTRIES = 1000;
        private const int CLEANUP_BATCH_SIZE = 100;
        
        // Cache statistics
        private long _cacheHits = 0;
        private long _cacheMisses = 0;

        public QualityCacheService(IQobuzLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _qualityCache = new Dictionary<string, AlbumQualityCache>();
            _cacheLock = new SemaphoreSlim(1, 1);
            _cacheExpiration = TimeSpan.FromHours(24);
        }

        public async Task<Models.AlbumQualityResult> GetCachedQualityAsync(string cacheKey)
        {
            if (string.IsNullOrWhiteSpace(cacheKey))
            {
                return null;
            }

            await _cacheLock.WaitAsync();
            try
            {
                if (_qualityCache.TryGetValue(cacheKey, out var cached))
                {
                    if (DateTime.UtcNow - cached.CachedAt < _cacheExpiration)
                    {
                        Interlocked.Increment(ref _cacheHits);
                        cached.LastAccessedAt = DateTime.UtcNow;
                        cached.AccessCount++;
                        
                        _logger.Debug("Cache hit for key: {0} (accessed {1} times)", cacheKey, cached.AccessCount);
                        return cached.Result;
                    }
                    
                    // Remove expired entry
                    _qualityCache.Remove(cacheKey);
                    _logger.Debug("Removed expired cache entry for key: {0}", cacheKey);
                }
                
                Interlocked.Increment(ref _cacheMisses);
                return null;
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        public async Task CacheQualityResultAsync(string cacheKey, Models.AlbumQualityResult result)
        {
            if (string.IsNullOrWhiteSpace(cacheKey) || result == null)
            {
                return;
            }

            await _cacheLock.WaitAsync();
            try
            {
                // Limit cache size - remove oldest entries if needed
                if (_qualityCache.Count >= MAX_CACHE_ENTRIES)
                {
                    await CleanupOldEntriesAsync();
                }
                
                _qualityCache[cacheKey] = new AlbumQualityCache
                {
                    Result = result,
                    CachedAt = DateTime.UtcNow,
                    LastAccessedAt = DateTime.UtcNow,
                    AccessCount = 0
                };
                
                _logger.Debug("Cached quality result for key: {0} (cache size: {1})", cacheKey, _qualityCache.Count);
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        public async Task ClearExpiredEntriesAsync()
        {
            await _cacheLock.WaitAsync();
            try
            {
                var expiredKeys = _qualityCache
                    .Where(kvp => DateTime.UtcNow - kvp.Value.CachedAt >= _cacheExpiration)
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var key in expiredKeys)
                {
                    _qualityCache.Remove(key);
                }
                
                if (expiredKeys.Count > 0)
                {
                    _logger.Info("Cleared {0} expired cache entries", expiredKeys.Count);
                }
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        public QualityCacheStats GetCacheStats()
        {
            var totalRequests = _cacheHits + _cacheMisses;
            var hitRatio = totalRequests > 0 ? (double)_cacheHits / totalRequests : 0.0;

            var expiredCount = 0;
            var memoryUsage = 0L;

            // Calculate stats without locking (approximate values for performance)
            foreach (var cached in _qualityCache.Values)
            {
                if (DateTime.UtcNow - cached.CachedAt >= _cacheExpiration)
                {
                    expiredCount++;
                }
                
                // Rough memory estimation
                memoryUsage += EstimateEntryMemoryUsage(cached);
            }

            return new QualityCacheStats
            {
                TotalEntries = _qualityCache.Count,
                ExpiredEntries = expiredCount,
                HitRatio = hitRatio,
                MemoryUsageBytes = memoryUsage
            };
        }

        public async Task ClearAllAsync()
        {
            await _cacheLock.WaitAsync();
            try
            {
                var count = _qualityCache.Count;
                _qualityCache.Clear();
                Interlocked.Exchange(ref _cacheHits, 0);
                Interlocked.Exchange(ref _cacheMisses, 0);
                
                _logger.Info("Cleared all cache entries ({0} entries removed)", count);
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        private async Task CleanupOldEntriesAsync()
        {
            // Remove oldest entries (by last access time, then by cache time)
            var oldestKeys = _qualityCache
                .OrderBy(kvp => kvp.Value.LastAccessedAt)
                .ThenBy(kvp => kvp.Value.CachedAt)
                .Take(CLEANUP_BATCH_SIZE)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var key in oldestKeys)
            {
                _qualityCache.Remove(key);
            }
            
            _logger.Debug("Cleaned up {0} old cache entries to make space", oldestKeys.Count);
        }

        private long EstimateEntryMemoryUsage(AlbumQualityCache cached)
        {
            // Rough estimation of memory usage per cache entry
            const int baseSize = 200; // Base object overhead
            var stringSize = (cached.Result?.AlbumId?.Length ?? 0) + (cached.Result?.AlbumTitle?.Length ?? 0) + (cached.Result?.Error?.Length ?? 0);
            return baseSize + (stringSize * 2); // Assume Unicode strings (2 bytes per char)
        }

        /// <summary>
        /// Internal cache entry class.
        /// </summary>
        private class AlbumQualityCache
        {
            public Models.AlbumQualityResult Result { get; set; }
            public DateTime CachedAt { get; set; }
            public DateTime LastAccessedAt { get; set; }
            public int AccessCount { get; set; }
        }
    }
}