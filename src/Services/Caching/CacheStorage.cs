using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.Qobuzarr.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Services.Caching
{
    /// <summary>
    /// Thread-safe cache storage implementation using concurrent dictionaries
    /// Supports both single entries and lists of entries per key
    /// </summary>
    /// <typeparam name="TEntry">Type of cache entries</typeparam>
    public class CacheStorage<TEntry> : ICacheStorage<TEntry> where TEntry : class
    {
        private readonly ConcurrentDictionary<string, List<TEntry>> _storage;

        /// <summary>
        /// Initializes a new instance of the cache storage
        /// </summary>
        public CacheStorage()
        {
            _storage = new ConcurrentDictionary<string, List<TEntry>>();
        }

        /// <summary>
        /// Gets the total number of entries across all keys
        /// </summary>
        public int Count => _storage.Values.Sum(list => list.Count);

        /// <summary>
        /// Adds or updates an entry in the cache storage
        /// Creates a new single-entry list if key doesn't exist, replaces if it does
        /// </summary>
        /// <param name="key">Cache key</param>
        /// <param name="entry">Entry to store</param>
        public void AddOrUpdate(string key, TEntry entry)
        {
            Guard.NotNullOrWhiteSpace(key);
            Guard.NotNull(entry);

            _storage.AddOrUpdate(
                key,
                new List<TEntry> { entry },
                (k, existing) => new List<TEntry> { entry });
        }

        /// <summary>
        /// Adds an entry to a list of entries for a given key
        /// Creates new list if key doesn't exist, appends to existing list otherwise
        /// </summary>
        /// <param name="key">Cache key</param>
        /// <param name="entry">Entry to add to the list</param>
        public void AddToList(string key, TEntry entry)
        {
            Guard.NotNullOrWhiteSpace(key);
            Guard.NotNull(entry);

            _storage.AddOrUpdate(
                key,
                new List<TEntry> { entry },
                (k, existingList) =>
                {
                    var newList = new List<TEntry>(existingList) { entry };
                    return newList;
                });
        }

        /// <summary>
        /// Tries to get the first entry by key (useful for single-entry keys)
        /// </summary>
        /// <param name="key">Cache key</param>
        /// <param name="entry">Retrieved entry if found</param>
        /// <returns>True if entry was found, false otherwise</returns>
        public bool TryGetEntry(string key, out TEntry entry)
        {
            entry = null;
            Guard.NotNullOrWhiteSpace(key);

            if (_storage.TryGetValue(key, out var entries) && entries.Any())
            {
                entry = entries.First();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tries to get a list of entries by key
        /// </summary>
        /// <param name="key">Cache key</param>
        /// <param name="entries">Retrieved list of entries if found</param>
        /// <returns>True if entries were found, false otherwise</returns>
        public bool TryGetEntries(string key, out IList<TEntry> entries)
        {
            entries = null;
            Guard.NotNullOrWhiteSpace(key);

            if (_storage.TryGetValue(key, out var entryList) && entryList.Any())
            {
                entries = new List<TEntry>(entryList); // Return defensive copy
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets all stored entries across all keys
        /// </summary>
        /// <returns>Collection of all entries</returns>
        public IEnumerable<TEntry> GetAllEntries()
        {
            return _storage.Values.SelectMany(list => list);
        }

        /// <summary>
        /// Gets all keys in the storage
        /// </summary>
        /// <returns>Collection of all keys</returns>
        public IEnumerable<string> GetAllKeys()
        {
            return _storage.Keys;
        }

        /// <summary>
        /// Removes entries associated with the specified key
        /// </summary>
        /// <param name="key">Key to remove</param>
        /// <returns>True if key was found and removed, false otherwise</returns>
        public bool Remove(string key)
        {
            Guard.NotNullOrWhiteSpace(key);
            return _storage.TryRemove(key, out _);
        }

        /// <summary>
        /// Removes a specific entry from a list associated with a key
        /// </summary>
        /// <param name="key">Cache key</param>
        /// <param name="entry">Entry to remove from the list</param>
        /// <returns>True if entry was found and removed, false otherwise</returns>
        public bool RemoveFromList(string key, TEntry entry)
        {
            Guard.NotNullOrWhiteSpace(key);
            Guard.NotNull(entry);

            if (_storage.TryGetValue(key, out var entries))
            {
                var removed = entries.Remove(entry);
                
                // Remove key if list becomes empty
                if (!entries.Any())
                {
                    _storage.TryRemove(key, out _);
                }
                
                return removed;
            }

            return false;
        }

        /// <summary>
        /// Clears all entries from storage
        /// </summary>

        /// <summary>
        /// Gets all entries that match a predicate
        /// </summary>
        /// <param name="predicate">Predicate to filter entries</param>
        /// <returns>Matching entries</returns>
        public IEnumerable<TEntry> FindEntries(Func<TEntry, bool> predicate)
        {
            Guard.NotNull(predicate);
            return GetAllEntries().Where(predicate);
        }

        /// <summary>
        /// Gets all key-value pairs where values match a predicate
        /// </summary>
        /// <param name="predicate">Predicate to filter entries</param>
        /// <returns>Matching key-entry pairs</returns>
        public IEnumerable<KeyValuePair<string, IList<TEntry>>> FindKeyValuePairs(Func<TEntry, bool> predicate)
        {
            Guard.NotNull(predicate);

            foreach (var kvp in _storage)
            {
                var matchingEntries = kvp.Value.Where(predicate).ToList();
                if (matchingEntries.Any())
                {
                    yield return new KeyValuePair<string, IList<TEntry>>(kvp.Key, matchingEntries);
                }
            }
        }

        public void Clear()
        {
            _storage.Clear();
        }
    }
}
