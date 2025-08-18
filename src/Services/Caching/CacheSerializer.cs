using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Lidarr.Plugin.Qobuzarr.Utilities;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Services.Caching
{
    /// <summary>
    /// JSON-based cache serialization implementation
    /// </summary>
    /// <typeparam name="TEntry">Type of cache entries to serialize</typeparam>
    public class CacheSerializer<TEntry> : ICacheSerializer<TEntry> where TEntry : class
    {
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly Logger _logger;

        /// <summary>
        /// Gets the serialization format name
        /// </summary>
        public string SerializationFormat => "JSON";

        /// <summary>
        /// Initializes a new cache serializer with JSON format
        /// </summary>
        /// <param name="logger">Optional logger for diagnostic information</param>
        public CacheSerializer(Logger logger = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
        }

        /// <summary>
        /// Serializes cache entries to a JSON string representation
        /// </summary>
        /// <param name="entries">Cache entries to serialize</param>
        /// <returns>Serialized JSON string representation</returns>
        public string SerializeEntries(IEnumerable<TEntry> entries)
        {
            Guard.NotNull(entries);

            try
            {
                var entryList = entries.ToList();
                if (!entryList.Any())
                {
                    return "[]";
                }

                var json = JsonSerializer.Serialize(entryList, _jsonOptions);
                _logger?.Debug("Serialized {0} cache entries to JSON ({1} characters)", 
                    entryList.Count, json.Length);
                
                return json;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to serialize cache entries to JSON");
                throw new InvalidOperationException("Cache serialization failed", ex);
            }
        }

        /// <summary>
        /// Deserializes cache entries from a JSON string representation
        /// </summary>
        /// <param name="serializedData">Serialized JSON string data</param>
        /// <returns>Deserialized cache entries</returns>
        public IEnumerable<TEntry> DeserializeEntries(string serializedData)
        {
            if (string.IsNullOrWhiteSpace(serializedData))
            {
                return Enumerable.Empty<TEntry>();
            }

            try
            {
                var entries = JsonSerializer.Deserialize<List<TEntry>>(serializedData, _jsonOptions);
                var entryCount = entries?.Count ?? 0;
                
                _logger?.Debug("Deserialized {0} cache entries from JSON ({1} characters)", 
                    entryCount, serializedData.Length);
                
                return entries ?? Enumerable.Empty<TEntry>();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to deserialize cache entries from JSON");
                throw new InvalidOperationException("Cache deserialization failed", ex);
            }
        }

        /// <summary>
        /// Serializes a single cache entry to a JSON string representation
        /// </summary>
        /// <param name="entry">Cache entry to serialize</param>
        /// <returns>Serialized JSON string representation</returns>
        public string SerializeEntry(TEntry entry)
        {
            Guard.NotNull(entry);

            try
            {
                var json = JsonSerializer.Serialize(entry, _jsonOptions);
                _logger?.Debug("Serialized cache entry to JSON ({0} characters)", json.Length);
                return json;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to serialize cache entry to JSON");
                throw new InvalidOperationException("Cache entry serialization failed", ex);
            }
        }

        /// <summary>
        /// Deserializes a single cache entry from a JSON string representation
        /// </summary>
        /// <param name="serializedData">Serialized JSON string data</param>
        /// <returns>Deserialized cache entry</returns>
        public TEntry DeserializeEntry(string serializedData)
        {
            if (string.IsNullOrWhiteSpace(serializedData))
            {
                return null;
            }

            try
            {
                var entry = JsonSerializer.Deserialize<TEntry>(serializedData, _jsonOptions);
                _logger?.Debug("Deserialized cache entry from JSON ({0} characters)", serializedData.Length);
                return entry;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to deserialize cache entry from JSON");
                throw new InvalidOperationException("Cache entry deserialization failed", ex);
            }
        }

        /// <summary>
        /// Validates whether the serialized data is in valid JSON format
        /// </summary>
        /// <param name="serializedData">Serialized data to validate</param>
        /// <returns>True if data is valid JSON, false otherwise</returns>
        public bool IsValidFormat(string serializedData)
        {
            if (string.IsNullOrWhiteSpace(serializedData))
            {
                return true; // Empty data is considered valid (will deserialize to empty collection)
            }

            try
            {
                // Attempt to parse as JSON document to validate structure
                using var document = JsonDocument.Parse(serializedData);
                return true;
            }
            catch (JsonException)
            {
                _logger?.Debug("Invalid JSON format detected in serialized cache data");
                return false;
            }
            catch (Exception ex)
            {
                _logger?.Debug(ex, "Unexpected error validating JSON format");
                return false;
            }
        }

        /// <summary>
        /// Estimates the serialized size of cache entries in bytes using UTF-8 encoding
        /// </summary>
        /// <param name="entries">Cache entries to estimate size for</param>
        /// <returns>Estimated size in bytes</returns>
        public long EstimateSerializedSize(IEnumerable<TEntry> entries)
        {
            Guard.NotNull(entries);

            try
            {
                var entryList = entries.ToList();
                if (!entryList.Any())
                {
                    return Encoding.UTF8.GetByteCount("[]");
                }

                // For large collections, estimate based on sample to avoid performance impact
                const int sampleSize = 10;
                if (entryList.Count <= sampleSize)
                {
                    // Small collection - serialize entire set for accurate measurement
                    var json = SerializeEntries(entryList);
                    return Encoding.UTF8.GetByteCount(json);
                }
                else
                {
                    // Large collection - estimate based on sample
                    var sample = entryList.Take(sampleSize);
                    var sampleJson = SerializeEntries(sample);
                    var sampleSize_ = Encoding.UTF8.GetByteCount(sampleJson);
                    
                    // Estimate total size with overhead for array structure
                    var estimatedSize = (long)(sampleSize_ * ((double)entryList.Count / sampleSize));
                    
                    _logger?.Debug("Estimated serialized size: {0} bytes for {1} entries (based on {2} sample entries)", 
                        estimatedSize, entryList.Count, sampleSize);
                    
                    return estimatedSize;
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Failed to estimate serialized cache size");
                // Return conservative estimate
                return entries.Count() * 1024; // 1KB per entry average
            }
        }
    }
}