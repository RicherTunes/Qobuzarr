using System;
using System.Collections.Generic;

namespace Lidarr.Plugin.Qobuzarr.Services.Caching
{
    public interface ICacheStorage<TEntry> where TEntry : class
    {
        int Count { get; }
        void AddOrUpdate(string key, TEntry entry);
        void AddToList(string key, TEntry entry);
        bool TryGetEntry(string key, out TEntry entry);
        bool TryGetEntries(string key, out IList<TEntry> entries);
        IEnumerable<TEntry> GetAllEntries();
        IEnumerable<string> GetAllKeys();
        bool Remove(string key);
        bool RemoveFromList(string key, TEntry entry);
        void Clear();
    }
}

