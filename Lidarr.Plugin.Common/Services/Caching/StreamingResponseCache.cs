using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using Lidarr.Plugin.Common.Interfaces;

namespace Lidarr.Plugin.Common.Services.Caching
{
    /// <summary>
    /// Generic response cache implementation for streaming service plugins.
    /// Uses an in-memory cache with TTL support.
    /// </summary>
    public abstract class StreamingResponseCache : IStreamingResponseCache
    {
        private readonly ConcurrentDictionary<string, CacheItem> _cache;
        private readonly object _cleanupLock = new object();
        private DateTime _lastCleanup = DateTime.UtcNow;
        
        protected StreamingResponseCache()
        {
            _cache = new ConcurrentDictionary<string, CacheItem>();
        }

        /// <inheritdoc/>
        public T? Get<T>(string endpoint, Dictionary<string, string> parameters) where T : class
        {
            if (!ShouldCache(endpoint))
                return null;

            var cacheKey = GenerateCacheKey(endpoint, parameters);
            
            if (_cache.TryGetValue(cacheKey, out var cacheItem))
            {
                if (cacheItem.ExpiresAt > DateTime.UtcNow)
                {
                    OnCacheHit(endpoint, cacheKey);
                    return cacheItem.Value as T;
                }
                else
                {
                    // Item expired, remove it
                    _cache.TryRemove(cacheKey, out _);
                }
            }

            OnCacheMiss(endpoint, cacheKey);
            
            // Periodic cleanup
            CleanupExpiredItems();
            
            return null;
        }

        /// <inheritdoc/>
        public void Set<T>(string endpoint, Dictionary<string, string> parameters, T value) where T : class
        {
            var duration = GetCacheDuration(endpoint);
            Set(endpoint, parameters, value, duration);
        }

        /// <inheritdoc/>
        public void Set<T>(string endpoint, Dictionary<string, string> parameters, T value, TimeSpan duration) where T : class
        {
            if (!ShouldCache(endpoint) || value == null)
                return;

            var cacheKey = GenerateCacheKey(endpoint, parameters);
            var expiresAt = DateTime.UtcNow.Add(duration);
            
            var cacheItem = new CacheItem
            {
                Value = value,
                ExpiresAt = expiresAt,
                CreatedAt = DateTime.UtcNow
            };

            _cache.AddOrUpdate(cacheKey, cacheItem, (key, oldValue) => cacheItem);
            OnCacheSet(endpoint, cacheKey, duration);
        }

        /// <inheritdoc/>
        public abstract bool ShouldCache(string endpoint);

        /// <inheritdoc/>
        public abstract TimeSpan GetCacheDuration(string endpoint);

        /// <inheritdoc/>
        public virtual string GenerateCacheKey(string endpoint, Dictionary<string, string> parameters)
        {
            // Exclude authentication tokens from cache key to allow sharing cached data
            var relevantParams = parameters
                .Where(p => !IsSensitiveParameter(p.Key))
                .OrderBy(p => p.Key)
                .Select(p => $"{p.Key}={p.Value}")
                .ToArray();

            var paramString = string.Join("&", relevantParams);
            var key = $"{GetServiceName()}_{endpoint}_{paramString}";
            return Math.Abs(key.GetHashCode()).ToString();
        }

        /// <inheritdoc/>
        public void Clear()
        {
            _cache.Clear();
            OnCacheCleared();
        }

        /// <inheritdoc/>
        public virtual void ClearEndpoint(string endpoint)
        {
            var keysToRemove = _cache.Keys
                .Where(key => key.Contains($"_{endpoint}_"))
                .ToList();

            foreach (var key in keysToRemove)
            {
                _cache.TryRemove(key, out _);
            }

            OnEndpointCleared(endpoint, keysToRemove.Count);
        }

        /// <summary>
        /// Gets the service name for cache key prefixing.
        /// </summary>
        protected abstract string GetServiceName();

        /// <summary>
        /// Determines if a parameter is sensitive and should be excluded from cache keys.
        /// </summary>
        protected virtual bool IsSensitiveParameter(string parameterName)
        {
            var lowerName = parameterName?.ToLowerInvariant() ?? "";
            return lowerName.Contains("token") ||
                   lowerName.Contains("secret") ||
                   lowerName.Contains("password") ||
                   lowerName.Contains("auth") ||
                   lowerName.Contains("credential") ||
                   lowerName.Contains("key") ||
                   lowerName == "request_sig" ||
                   lowerName == "app_id";
        }

        /// <summary>
        /// Called when a cache hit occurs.
        /// </summary>
        protected virtual void OnCacheHit(string endpoint, string cacheKey) { }

        /// <summary>
        /// Called when a cache miss occurs.
        /// </summary>
        protected virtual void OnCacheMiss(string endpoint, string cacheKey) { }

        /// <summary>
        /// Called when an item is set in the cache.
        /// </summary>
        protected virtual void OnCacheSet(string endpoint, string cacheKey, TimeSpan duration) { }

        /// <summary>
        /// Called when the entire cache is cleared.
        /// </summary>
        protected virtual void OnCacheCleared() { }

        /// <summary>
        /// Called when an endpoint's cache entries are cleared.
        /// </summary>
        protected virtual void OnEndpointCleared(string endpoint, int itemsRemoved) { }

        private void CleanupExpiredItems()
        {
            // Only cleanup every 5 minutes to avoid performance impact
            if (DateTime.UtcNow - _lastCleanup < TimeSpan.FromMinutes(5))
                return;

            lock (_cleanupLock)
            {
                if (DateTime.UtcNow - _lastCleanup < TimeSpan.FromMinutes(5))
                    return;

                var now = DateTime.UtcNow;
                var expiredKeys = _cache
                    .Where(kvp => kvp.Value.ExpiresAt <= now)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredKeys)
                {
                    _cache.TryRemove(key, out _);
                }

                _lastCleanup = now;
                
                if (expiredKeys.Count > 0)
                {
                    OnExpiredItemsCleanup(expiredKeys.Count);
                }
            }
        }

        /// <summary>
        /// Called when expired items are cleaned up.
        /// </summary>
        protected virtual void OnExpiredItemsCleanup(int itemsRemoved) { }

        private class CacheItem
        {
            public object Value { get; set; }
            public DateTime ExpiresAt { get; set; }
            public DateTime CreatedAt { get; set; }
        }
    }
}