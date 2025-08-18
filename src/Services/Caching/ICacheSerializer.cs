using System;
using System.Collections.Generic;

namespace Lidarr.Plugin.Qobuzarr.Services.Caching
{
    /// <summary>
    /// Interface for cache serialization and deserialization operations
    /// </summary>
    /// <typeparam name="TEntry">Type of cache entries to serialize</typeparam>
    public interface ICacheSerializer<TEntry> where TEntry : class
    {
        /// <summary>
        /// Serializes cache entries to a string representation
        /// </summary>
        /// <param name="entries">Cache entries to serialize</param>
        /// <returns>Serialized string representation</returns>
        string SerializeEntries(IEnumerable<TEntry> entries);

        /// <summary>
        /// Deserializes cache entries from a string representation
        /// </summary>
        /// <param name="serializedData">Serialized string data</param>
        /// <returns>Deserialized cache entries</returns>
        IEnumerable<TEntry> DeserializeEntries(string serializedData);

        /// <summary>
        /// Serializes a single cache entry to a string representation
        /// </summary>
        /// <param name="entry">Cache entry to serialize</param>
        /// <returns>Serialized string representation</returns>
        string SerializeEntry(TEntry entry);

        /// <summary>
        /// Deserializes a single cache entry from a string representation
        /// </summary>
        /// <param name="serializedData">Serialized string data</param>
        /// <returns>Deserialized cache entry</returns>
        TEntry DeserializeEntry(string serializedData);

        /// <summary>
        /// Gets the serialization format name for logging and diagnostics
        /// </summary>
        string SerializationFormat { get; }

        /// <summary>
        /// Validates whether the serialized data is in the expected format
        /// </summary>
        /// <param name="serializedData">Serialized data to validate</param>
        /// <returns>True if data is valid for this serializer, false otherwise</returns>
        bool IsValidFormat(string serializedData);

        /// <summary>
        /// Estimates the serialized size of cache entries in bytes
        /// </summary>
        /// <param name="entries">Cache entries to estimate size for</param>
        /// <returns>Estimated size in bytes</returns>
        long EstimateSerializedSize(IEnumerable<TEntry> entries);
    }
}