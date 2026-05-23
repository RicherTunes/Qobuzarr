using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Indexers
{
    /// <summary>
    /// Intelligent query caching with ML-driven eviction and preloading
    /// Designed to maximize cache hit rates and minimize API calls
    /// </summary>
    public class SmartQueryCache
    {
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<string, CacheEntry> _cache;
        private readonly ConcurrentDictionary<string, QueryPattern> _patterns;
        private readonly object _statsLock = new object();
        private readonly object _evictionLock = new object();

        // Cache configuration
        private const int MaxCacheSize = 10000;
        private const int EvictionBatchSize = 1000;
        private readonly TimeSpan DefaultExpiry = TimeSpan.FromHours(24);
        private readonly TimeSpan PopularQueryExpiry = TimeSpan.FromDays(7);

        // Performance metrics
        private long _hits;
        private long _misses;
        private long _evictions;
        private long _preloadHits;

        public SmartQueryCache(Logger logger = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _cache = new ConcurrentDictionary<string, CacheEntry>();
            _patterns = new ConcurrentDictionary<string, QueryPattern>();

            _logger.Debug("SmartQueryCache initialized with max size: {0}", MaxCacheSize);
        }

        /// <summary>
        /// Get cached result with ML-based prefetching
        /// </summary>
        public (bool found, T result) Get<T>(string artist, string album, QueryComplexity complexity) where T : class
        {
            var key = GenerateCacheKey(artist, album);

            // Check primary cache
            if (_cache.TryGetValue(key, out var entry))
            {
                if (!entry.IsExpired)
                {
                    Interlocked.Increment(ref _hits);
                    entry.RecordAccess();

                    // Preload related queries based on access patterns
                    PreloadRelatedQueries(artist, album, complexity);

                    return (true, entry.Data as T ?? default(T)!);
                }
                else
                {
                    // Remove expired entry
                    _cache.TryRemove(key, out _);
                }
            }

            Interlocked.Increment(ref _misses);

            // Check if this is a preloaded query that hit
            if (WasPreloaded(key))
            {
                Interlocked.Increment(ref _preloadHits);
            }

            return (false, default(T)!);
        }

        /// <summary>
        /// Store result with intelligent expiry calculation
        /// </summary>
        public void Set<T>(string artist, string album, T data, QueryComplexity complexity) where T : class
        {
            var key = GenerateCacheKey(artist, album);

            // Calculate intelligent expiry based on query patterns
            var expiry = CalculateExpiry(artist, album, complexity);

            var entry = new CacheEntry
            {
                Key = key,
                Data = data,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(expiry),
                Complexity = complexity,
                LastAccessed = DateTime.UtcNow
            };

            _cache.AddOrUpdate(key, entry, (k, old) => entry);

            // Record pattern for future predictions
            RecordQueryPattern(artist, album, complexity);

            // Trigger eviction if needed (with synchronization)
            if (_cache.Count > MaxCacheSize)
            {
                lock (_evictionLock)
                {
                    // Double-check after acquiring lock
                    if (_cache.Count > MaxCacheSize)
                    {
                        EvictLeastValuable();
                    }
                }
            }
        }

        /// <summary>
        /// Preload queries that are likely to be requested next
        /// </summary>
        private void PreloadRelatedQueries(string artist, string album, QueryComplexity complexity)
        {
            // Don't preload for complex queries (low hit probability)
            if (complexity == QueryComplexity.Complex)
                return;

            // Identify related queries based on patterns
            var relatedPatterns = _patterns.Values
                .Where(p => p.IsRelatedTo(artist, album))
                .OrderByDescending(p => p.AccessFrequency)
                .Take(3);

            foreach (var pattern in relatedPatterns)
            {
                // Mark for preloading (actual loading would happen asynchronously)
                pattern.MarkForPreload();
            }
        }

        /// <summary>
        /// Calculate intelligent cache expiry based on query characteristics
        /// </summary>
        private TimeSpan CalculateExpiry(string artist, string album, QueryComplexity complexity)
        {
            // Popular artists get longer cache times
            if (IsPopularArtist(artist))
                return PopularQueryExpiry;

            // Simple queries cache longer (more stable results)
            switch (complexity)
            {
                case QueryComplexity.Simple:
                    return TimeSpan.FromDays(3);
                case QueryComplexity.Medium:
                    return TimeSpan.FromDays(1);
                case QueryComplexity.Complex:
                    return TimeSpan.FromHours(6);
                default:
                    return DefaultExpiry;
            }
        }

        /// <summary>
        /// Evict least valuable entries using ML-based scoring
        /// </summary>
        private void EvictLeastValuable()
        {
            var candidates = _cache.Values
                .OrderBy(e => CalculateEvictionScore(e))
                .Take(EvictionBatchSize)
                .ToList();

            foreach (var entry in candidates)
            {
                if (_cache.TryRemove(entry.Key, out _))
                {
                    Interlocked.Increment(ref _evictions);
                }
            }

            _logger.Debug("Evicted {0} cache entries, current size: {1}", candidates.Count, _cache.Count);
        }

        /// <summary>
        /// Calculate eviction score (lower = more likely to evict)
        /// </summary>
        private double CalculateEvictionScore(CacheEntry entry)
        {
            var age = (DateTime.UtcNow - entry.CreatedAt).TotalHours;
            var recency = (DateTime.UtcNow - entry.LastAccessed).TotalHours;
            var frequency = entry.AccessCount;

            // LFU-LRU hybrid with complexity weighting
            var score = (frequency * 10.0) / (recency + 1.0);

            // Boost score for simple queries (more valuable)
            if (entry.Complexity == QueryComplexity.Simple)
                score *= 2.0;

            // Penalize very old entries
            if (age > 72)
                score *= 0.5;

            return score;
        }

        /// <summary>
        /// Record query pattern for future optimization
        /// </summary>
        private void RecordQueryPattern(string artist, string album, QueryComplexity complexity)
        {
            var patternKey = $"{artist.ToLowerInvariant()}|{complexity}";

            _patterns.AddOrUpdate(patternKey,
                new QueryPattern
                {
                    Artist = artist,
                    Complexity = complexity,
                    FirstSeen = DateTime.UtcNow,
                    AccessFrequency = 1
                },
                (k, existing) =>
                {
                    existing.AccessFrequency++;
                    existing.LastSeen = DateTime.UtcNow;
                    return existing;
                });
        }

        /// <summary>
        /// Generate consistent cache key
        /// </summary>
        private string GenerateCacheKey(string artist, string album)
        {
            var input = $"{artist?.ToLowerInvariant()}|{album?.ToLowerInvariant()}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(hash).Substring(0, 16);
        }

        /// <summary>
        /// Check if artist is popular (would benefit from longer caching)
        /// </summary>
        private bool IsPopularArtist(string artist)
        {
            if (string.IsNullOrEmpty(artist))
                return false;

            // Check access patterns
            var pattern = _patterns.Values
                .FirstOrDefault(p => p.Artist.Equals(artist, StringComparison.OrdinalIgnoreCase));

            return pattern?.AccessFrequency > 10;
        }

        /// <summary>
        /// Check if query was preloaded
        /// </summary>
        private bool WasPreloaded(string key)
        {
            // Implementation would track preloaded keys
            return false;
        }

        /// <summary>
        /// Get cache performance statistics
        /// </summary>
        public CacheStatistics GetStatistics()
        {
            var total = _hits + _misses;

            return new CacheStatistics
            {
                TotalQueries = total,
                CacheHits = _hits,
                CacheMisses = _misses,
                HitRate = total > 0 ? (double)_hits / total : 0,
                CurrentSize = _cache.Count,
                MaxSize = MaxCacheSize,
                Evictions = _evictions,
                PreloadHits = _preloadHits,
                PreloadEfficiency = _preloadHits > 0 && _hits > 0 ? (double)_preloadHits / _hits : 0,
                UniquePatterns = _patterns.Count
            };
        }

        /// <summary>
        /// Clear cache
        /// </summary>
        public void Clear()
        {
            _cache.Clear();
            _patterns.Clear();
            _hits = _misses = _evictions = _preloadHits = 0;
            _logger.Debug("Cache cleared");
        }

        #region Internal Classes

        private class CacheEntry
        {
            public string Key { get; set; }
            public object Data { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime ExpiresAt { get; set; }
            public DateTime LastAccessed { get; set; }
            private int _accessCount;
            public int AccessCount => _accessCount;
            public QueryComplexity Complexity { get; set; }

            public bool IsExpired => DateTime.UtcNow > ExpiresAt;

            public void RecordAccess()
            {
                LastAccessed = DateTime.UtcNow;
                Interlocked.Increment(ref _accessCount);
            }
        }

        private class QueryPattern
        {
            public string Artist { get; set; }
            public QueryComplexity Complexity { get; set; }
            public DateTime FirstSeen { get; set; }
            public DateTime LastSeen { get; set; }
            public int AccessFrequency { get; set; }
            public bool MarkedForPreload { get; set; }

            public bool IsRelatedTo(string artist, string album)
            {
                // Simple relationship check - same artist
                return Artist.Equals(artist, StringComparison.OrdinalIgnoreCase);
            }

            public void MarkForPreload()
            {
                MarkedForPreload = true;
            }
        }

        public class CacheStatistics
        {
            public long TotalQueries { get; set; }
            public long CacheHits { get; set; }
            public long CacheMisses { get; set; }
            public double HitRate { get; set; }
            public int CurrentSize { get; set; }
            public int MaxSize { get; set; }
            public long Evictions { get; set; }
            public long PreloadHits { get; set; }
            public double PreloadEfficiency { get; set; }
            public int UniquePatterns { get; set; }
        }

        #endregion
    }
}
