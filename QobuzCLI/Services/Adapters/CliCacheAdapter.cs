using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Lidarr.Plugin.Qobuzarr.Abstractions;

namespace QobuzCLI.Services.Adapters
{
    /// <summary>
    /// Simple in-memory cache adapter for CLI usage.
    /// Implements plugin's IQobuzCache interface following CLAUDE.md architecture.
    /// </summary>
    public class CliCacheAdapter : IQobuzCache
    {
        private readonly ConcurrentDictionary<string, CacheItem> _cache = new();

        private class CacheItem
        {
            public object Value { get; set; } = null!;
            public DateTime? Expiration { get; set; }
            
            public bool IsExpired => Expiration.HasValue && DateTime.UtcNow > Expiration.Value;
        }

        public T? Get<T>(string key) where T : class
        {
            if (string.IsNullOrEmpty(key))
                return null;

            if (_cache.TryGetValue(key, out var item))
            {
                if (item.IsExpired)
                {
                    _cache.TryRemove(key, out _);
                    return null;
                }

                return item.Value as T;
            }

            return null;
        }

        public void Set<T>(string key, T value, TimeSpan? expiration = null) where T : class
        {
            if (string.IsNullOrEmpty(key) || value == null)
                return;

            var item = new CacheItem
            {
                Value = value,
                Expiration = expiration.HasValue ? DateTime.UtcNow.Add(expiration.Value) : null
            };

            _cache.AddOrUpdate(key, item, (_, _) => item);
        }

        public void Remove(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                _cache.TryRemove(key, out _);
            }
        }

        public bool Contains(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            if (_cache.TryGetValue(key, out var item))
            {
                if (item.IsExpired)
                {
                    _cache.TryRemove(key, out _);
                    return false;
                }
                return true;
            }

            return false;
        }
    }
}