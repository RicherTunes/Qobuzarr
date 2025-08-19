using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Newtonsoft.Json;

namespace Lidarr.Plugin.Qobuzarr.Services.Consolidated
{
    /// <summary>
    /// Unified caching service consolidating all cache-related functionality
    /// Replaces: CacheStorage, CacheSerializer, CacheStatistics, CacheValidationService,
    /// LRUCacheEvictionStrategy, SubstringMatcher
    /// </summary>
    public class UnifiedCacheService : IDisposable
    {
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<string, CacheEntry> _cache;
        private readonly ReaderWriterLockSlim _cacheLock;
        private readonly Timer _cleanupTimer;
        private readonly CacheConfiguration _configuration;
        private long _hits;
        private long _misses;
        private long _evictions;

        public UnifiedCacheService(Logger logger, CacheConfiguration configuration = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? new CacheConfiguration();
            _cache = new ConcurrentDictionary<string, CacheEntry>();
            _cacheLock = new ReaderWriterLockSlim();
            
            // Start cleanup timer
            _cleanupTimer = new Timer(
                CleanupExpiredEntries, 
                null, 
                TimeSpan.FromMinutes(5), 
                TimeSpan.FromMinutes(5));
        }

        // Core Cache Operations
        public T Get<T>(string key)
        {
            if (string.IsNullOrEmpty(key))
                return default;

            _cacheLock.EnterReadLock();
            try
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    if (!entry.IsExpired)
                    {
                        Interlocked.Increment(ref _hits);
                        entry.LastAccessed = DateTime.UtcNow;
                        entry.AccessCount++;
                        
                        _logger.Trace("Cache hit for key: {0}", key);
                        return DeserializeValue<T>(entry.Value);
                    }
                    else
                    {
                        // Remove expired entry
                        _cache.TryRemove(key, out _);
                        Interlocked.Increment(ref _evictions);
                    }
                }
                
                Interlocked.Increment(ref _misses);
                _logger.Trace("Cache miss for key: {0}", key);
                return default;
            }
            finally
            {
                _cacheLock.ExitReadLock();
            }
        }

        public void Set<T>(string key, T value, TimeSpan? expiration = null)
        {
            if (string.IsNullOrEmpty(key) || value == null)
                return;

            var expirationTime = expiration ?? _configuration.DefaultExpiration;
            var serializedValue = SerializeValue(value);
            
            var entry = new CacheEntry
            {
                Key = key,
                Value = serializedValue,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(expirationTime),
                LastAccessed = DateTime.UtcNow,
                AccessCount = 0,
                Size = EstimateSize(serializedValue)
            };

            _cacheLock.EnterWriteLock();
            try
            {
                // Check if we need to evict entries
                if (_cache.Count >= _configuration.MaxEntries)
                {
                    EvictLeastRecentlyUsed();
                }

                _cache.AddOrUpdate(key, entry, (k, existing) => entry);
                _logger.Trace("Cached value for key: {0}, expires in {1}", key, expirationTime);
            }
            finally
            {
                _cacheLock.ExitWriteLock();
            }
        }

        public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null)
        {
            var cached = Get<T>(key);
            if (cached != null)
                return cached;

            var value = await factory().ConfigureAwait(false);
            Set(key, value, expiration);
            return value;
        }

        public void Remove(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            if (_cache.TryRemove(key, out _))
            {
                _logger.Trace("Removed cache entry: {0}", key);
            }
        }

        public void Clear()
        {
            _cache.Clear();
            Interlocked.Exchange(ref _hits, 0);
            Interlocked.Exchange(ref _misses, 0);
            Interlocked.Exchange(ref _evictions, 0);
            _logger.Info("Cache cleared");
        }

        // Pattern Matching
        public IEnumerable<T> GetByPattern<T>(string pattern)
        {
            var results = new List<T>();
            
            _cacheLock.EnterReadLock();
            try
            {
                var matchingKeys = _cache.Keys.Where(k => MatchesPattern(k, pattern));
                
                foreach (var key in matchingKeys)
                {
                    var value = Get<T>(key);
                    if (value != null)
                        results.Add(value);
                }
            }
            finally
            {
                _cacheLock.ExitReadLock();
            }
            
            return results;
        }

        // Statistics
        public CacheStatistics GetStatistics()
        {
            var totalRequests = _hits + _misses;
            var hitRate = totalRequests > 0 ? (double)_hits / totalRequests : 0;
            
            return new CacheStatistics
            {
                TotalEntries = _cache.Count,
                Hits = _hits,
                Misses = _misses,
                Evictions = _evictions,
                HitRate = hitRate,
                TotalSize = _cache.Values.Sum(e => e.Size),
                OldestEntry = _cache.Values.OrderBy(e => e.CreatedAt).FirstOrDefault()?.CreatedAt,
                NewestEntry = _cache.Values.OrderByDescending(e => e.CreatedAt).FirstOrDefault()?.CreatedAt
            };
        }

        // Private Methods
        private void EvictLeastRecentlyUsed()
        {
            var entriesToEvict = _cache.Values
                .OrderBy(e => e.LastAccessed)
                .Take(_configuration.EvictionBatchSize)
                .Select(e => e.Key)
                .ToList();

            foreach (var key in entriesToEvict)
            {
                _cache.TryRemove(key, out _);
                Interlocked.Increment(ref _evictions);
            }
            
            _logger.Debug("Evicted {0} cache entries", entriesToEvict.Count);
        }

        private void CleanupExpiredEntries(object state)
        {
            var expiredKeys = _cache
                .Where(kvp => kvp.Value.IsExpired)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _cache.TryRemove(key, out _);
                Interlocked.Increment(ref _evictions);
            }
            
            if (expiredKeys.Any())
            {
                _logger.Debug("Cleaned up {0} expired cache entries", expiredKeys.Count);
            }
        }

        private string SerializeValue<T>(T value)
        {
            try
            {
                return JsonConvert.SerializeObject(value);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to serialize cache value");
                return null;
            }
        }

        private T DeserializeValue<T>(string serialized)
        {
            if (string.IsNullOrEmpty(serialized))
                return default;

            try
            {
                return JsonConvert.DeserializeObject<T>(serialized);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to deserialize cache value");
                return default;
            }
        }

        private long EstimateSize(string value)
        {
            return string.IsNullOrEmpty(value) ? 0 : value.Length * 2; // Rough estimate (2 bytes per char)
        }

        private bool MatchesPattern(string key, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return true;
                
            // Simple wildcard matching
            if (pattern.Contains("*"))
            {
                var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
                return System.Text.RegularExpressions.Regex.IsMatch(key, regex);
            }
            
            return key.Contains(pattern);
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _cacheLock?.Dispose();
            _cache?.Clear();
        }

        private class CacheEntry
        {
            public string Key { get; set; }
            public string Value { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime ExpiresAt { get; set; }
            public DateTime LastAccessed { get; set; }
            public int AccessCount { get; set; }
            public long Size { get; set; }
            
            public bool IsExpired => DateTime.UtcNow > ExpiresAt;
        }
    }

    public class CacheConfiguration
    {
        public int MaxEntries { get; set; } = 10000;
        public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromMinutes(30);
        public int EvictionBatchSize { get; set; } = 100;
    }

    public class CacheStatistics
    {
        public int TotalEntries { get; set; }
        public long Hits { get; set; }
        public long Misses { get; set; }
        public long Evictions { get; set; }
        public double HitRate { get; set; }
        public long TotalSize { get; set; }
        public DateTime? OldestEntry { get; set; }
        public DateTime? NewestEntry { get; set; }
    }
}