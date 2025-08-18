using System;
using System.Collections.Generic;

namespace Lidarr.Plugin.Qobuzarr.Services.Caching
{
    /// <summary>
    /// Interface for cache storage operations
    /// </summary>
    /// <typeparam name="TEntry">Type of cache entries</typeparam>
    public interface ICacheStorage<TEntry> where TEntry : class
    {
        /// <summary>
        /// Adds or updates an entry in the cache storage
        /// </summary>
        /// <param name="key">Cache key</param>
        /// <param name="entry">Entry to store</param>
        void AddOrUpdate(string key, TEntry entry);

        /// <summary>
        /// Adds an entry to a list of entries for a given key
        /// </summary>
        /// <param name="key">Cache key</param>
        /// <param name="entry">Entry to add to the list</param>
        void AddToList(string key, TEntry entry);

        /// <summary>
        /// Tries to get an entry by key
        /// </summary>
        /// <param name="key">Cache key</param>
        /// <param name="entry">Retrieved entry if found</param>
        /// <returns>True if entry was found, false otherwise</returns>
        bool TryGetEntry(string key, out TEntry entry);

        /// <summary>
        /// Tries to get a list of entries by key
        /// </summary>
        /// <param name="key">Cache key</param>
        /// <param name="entries">Retrieved list of entries if found</param>
        /// <returns>True if entries were found, false otherwise</returns>
        bool TryGetEntries(string key, out IList<TEntry> entries);

        /// <summary>
        /// Gets all stored entries across all keys
        /// </summary>
        /// <returns>Collection of all entries</returns>
        IEnumerable<TEntry> GetAllEntries();

        /// <summary>
        /// Gets all keys in the storage
        /// </summary>
        /// <returns>Collection of all keys</returns>
        IEnumerable<string> GetAllKeys();

        /// <summary>
        /// Gets the total number of entries in storage
        /// </summary>
        /// <returns>Total entry count</returns>
        int Count { get; }

        /// <summary>
        /// Removes entries associated with the specified key
        /// </summary>
        /// <param name="key">Key to remove</param>
        /// <returns>True if key was found and removed, false otherwise</returns>
        bool Remove(string key);

        /// <summary>
        /// Removes a specific entry from a list associated with a key
        /// </summary>
        /// <param name="key">Cache key</param>
        /// <param name="entry">Entry to remove from the list</param>
        /// <returns>True if entry was found and removed, false otherwise</returns>
        bool RemoveFromList(string key, TEntry entry);

        /// <summary>
        /// Clears all entries from storage
        /// </summary>
        void Clear();

        /// <summary>
        /// Gets all entries that match a predicate
        /// </summary>
        /// <param name="predicate">Predicate to filter entries</param>
        /// <returns>Matching entries</returns>
        IEnumerable<TEntry> FindEntries(Func<TEntry, bool> predicate);

        /// <summary>
        /// Gets all key-value pairs where values match a predicate
        /// </summary>
        /// <param name="predicate">Predicate to filter entries</param>
        /// <returns>Matching key-entry pairs</returns>
        IEnumerable<KeyValuePair<string, IList<TEntry>>> FindKeyValuePairs(Func<TEntry, bool> predicate);
    }
}