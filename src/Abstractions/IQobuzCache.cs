using System;

namespace Lidarr.Plugin.Qobuzarr.Abstractions
{
    /// <summary>
    /// Simple caching interface that both Lidarr and CLI can implement
    /// </summary>
    public interface IQobuzCache
    {
        T? Get<T>(string key) where T : class;
        void Set<T>(string key, T value, TimeSpan? expiration = null) where T : class;
        void Remove(string key);
        bool Contains(string key);
    }
}