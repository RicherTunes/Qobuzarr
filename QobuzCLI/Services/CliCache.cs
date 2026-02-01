using System.Collections.Concurrent;
using Lidarr.Plugin.Qobuzarr.Abstractions;

namespace QobuzCLI.Services;

/// <summary>
/// CLI implementation of IQobuzCache using in-memory storage
/// </summary>
public class CliCache : IQobuzCache
{
    private readonly ConcurrentDictionary<string, CacheItem> _cache = new();
    private readonly Timer _cleanupTimer;

    public CliCache()
    {
        // Clean up expired items every 5 minutes
        _cleanupTimer = new Timer(CleanupExpiredItems, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public T? Get<T>(string key) where T : class
    {
        if (_cache.TryGetValue(key, out var item))
        {
            if (item.ExpirationTime == null || item.ExpirationTime > DateTime.UtcNow)
            {
                return item.Value as T;
            }
            else
            {
                // Item expired, remove it
                _cache.TryRemove(key, out _);
            }
        }

        return null;
    }

    public void Set<T>(string key, T value, TimeSpan? expiration = null) where T : class
    {
        var expirationTime = expiration.HasValue ? DateTime.UtcNow.Add(expiration.Value) : (DateTime?)null;
        var item = new CacheItem(value, expirationTime);
        _cache.AddOrUpdate(key, item, (k, v) => item);
    }

    public void Remove(string key)
    {
        _cache.TryRemove(key, out _);
    }

    public bool Contains(string key)
    {
        if (_cache.TryGetValue(key, out var item))
        {
            if (item.ExpirationTime == null || item.ExpirationTime > DateTime.UtcNow)
            {
                return true;
            }
            else
            {
                // Item expired, remove it
                _cache.TryRemove(key, out _);
            }
        }

        return false;
    }

    private void CleanupExpiredItems(object? state)
    {
        var expiredKeys = new List<string>();
        var now = DateTime.UtcNow;

        foreach (var kvp in _cache)
        {
            if (kvp.Value.ExpirationTime.HasValue && kvp.Value.ExpirationTime <= now)
            {
                expiredKeys.Add(kvp.Key);
            }
        }

        foreach (var key in expiredKeys)
        {
            _cache.TryRemove(key, out _);
        }
    }

    private class CacheItem
    {
        public object Value { get; }
        public DateTime? ExpirationTime { get; }

        public CacheItem(object value, DateTime? expirationTime)
        {
            Value = value;
            ExpirationTime = expirationTime;
        }
    }
}
