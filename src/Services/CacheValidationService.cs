using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Cache validation service that handles cache invalidation and disk space management
    /// Prevents stale data issues and disk space exhaustion
    /// </summary>
    public class CacheValidationService
    {
        private readonly Logger _logger;
        private readonly string _cacheDirectory;
        private readonly Dictionary<string, CacheEntry> _cacheMetadata;
        private readonly object _lockObject = new();

        // Cache limits
        private readonly long _maxCacheSizeBytes;
        private readonly TimeSpan _defaultCacheExpiry;

        public CacheValidationService(
            string cacheDirectory, 
            long maxCacheSizeMB = 1024, // 1GB default
            TimeSpan? defaultExpiry = null,
            Logger logger = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _cacheDirectory = cacheDirectory;
            _maxCacheSizeBytes = maxCacheSizeMB * 1024 * 1024;
            _defaultCacheExpiry = defaultExpiry ?? TimeSpan.FromDays(7);
            _cacheMetadata = new Dictionary<string, CacheEntry>();

            EnsureCacheDirectoryExists();
            LoadCacheMetadata();
        }

        /// <summary>
        /// Validates cache entry and checks if it's still valid
        /// </summary>
        public CacheValidationResult ValidateCacheEntry(string key, DateTime? lastModified = null)
        {
            // Defensive: Check for null/empty key
            if (string.IsNullOrWhiteSpace(key))
            {
                return new CacheValidationResult 
                { 
                    IsValid = false, 
                    Reason = "Cache key is null or empty" 
                };
            }

            try
            {
                lock (_lockObject)
                {
                    if (!_cacheMetadata.TryGetValue(key, out var entry))
                {
                    return new CacheValidationResult 
                    { 
                        IsValid = false, 
                        Reason = "Cache entry not found" 
                    };
                }

                // Check if expired
                if (entry.ExpiryTime < DateTime.UtcNow)
                {
                    _logger.Debug("💨 CACHE EXPIRED: {0} expired at {1}", key, entry.ExpiryTime);
                    return new CacheValidationResult 
                    { 
                        IsValid = false, 
                        Reason = "Cache entry expired" 
                    };
                }

                // Check if source was modified after cache
                if (lastModified.HasValue && lastModified > entry.CreatedTime)
                {
                    _logger.Debug("🔄 CACHE STALE: {0} source modified after cache", key);
                    return new CacheValidationResult 
                    { 
                        IsValid = false, 
                        Reason = "Source data modified after cache" 
                    };
                }

                // Check if file still exists
                var filePath = Path.Combine(_cacheDirectory, entry.FileName);
                if (!File.Exists(filePath))
                {
                    _logger.Debug("📁 CACHE MISSING: {0} file not found", key);
                    _cacheMetadata.Remove(key);
                    return new CacheValidationResult 
                    { 
                        IsValid = false, 
                        Reason = "Cache file missing from disk" 
                    };
                }

                return new CacheValidationResult 
                { 
                    IsValid = true, 
                    CachePath = filePath,
                    CacheAge = DateTime.UtcNow - entry.CreatedTime
                };
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "🛡️ DEFENSIVE: Cache validation failed for key '{0}', treating as invalid", key);
                return new CacheValidationResult 
                { 
                    IsValid = false, 
                    Reason = $"Cache validation error: {ex.Message}" 
                };
            }
        }

        /// <summary>
        /// Adds new cache entry with validation
        /// </summary>
        public bool AddCacheEntry(string key, string fileName, TimeSpan? expiry = null)
        {
            lock (_lockObject)
            {
                try
                {
                    var filePath = Path.Combine(_cacheDirectory, fileName);
                    if (!File.Exists(filePath))
                    {
                        _logger.Warn("⚠️ CACHE ADD FAILED: File not found - {0}", fileName);
                        return false;
                    }

                    var fileInfo = new FileInfo(filePath);
                    var entry = new CacheEntry
                    {
                        Key = key,
                        FileName = fileName,
                        FileSizeBytes = fileInfo.Length,
                        CreatedTime = DateTime.UtcNow,
                        ExpiryTime = DateTime.UtcNow + (expiry ?? _defaultCacheExpiry),
                        AccessCount = 1,
                        LastAccessTime = DateTime.UtcNow
                    };

                    _cacheMetadata[key] = entry;
                    _logger.Debug("💾 CACHE ADDED: {0} ({1:F1}KB)", key, fileInfo.Length / 1024.0);

                    // Check if cache cleanup is needed
                    CheckCacheSize();
                    
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to add cache entry: {0}", key);
                    return false;
                }
            }
        }

        /// <summary>
        /// Records cache access for LRU tracking
        /// </summary>
        public void RecordCacheAccess(string key)
        {
            lock (_lockObject)
            {
                if (_cacheMetadata.TryGetValue(key, out var entry))
                {
                    entry.AccessCount++;
                    entry.LastAccessTime = DateTime.UtcNow;
                }
            }
        }

        /// <summary>
        /// Cleans up expired and oversized cache entries
        /// </summary>
        public CacheCleanupResult PerformCacheCleanup(bool forceCleanup = false)
        {
            lock (_lockObject)
            {
                var startTime = DateTime.UtcNow;
                var removedEntries = 0;
                var reclaimedBytes = 0L;

                _logger.Debug("🧹 CACHE CLEANUP: Starting cleanup process");

                // Remove expired entries
                var expiredKeys = _cacheMetadata
                    .Where(kvp => kvp.Value.ExpiryTime < DateTime.UtcNow)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredKeys)
                {
                    if (RemoveCacheEntry(key))
                    {
                        removedEntries++;
                    }
                }

                // Check disk space and remove LRU entries if needed
                var currentSize = GetCurrentCacheSize();
                if (currentSize > _maxCacheSizeBytes || forceCleanup)
                {
                    var lruEntries = _cacheMetadata.Values
                        .OrderBy(e => e.LastAccessTime)
                        .ThenBy(e => e.AccessCount)
                        .ToList();

                    var targetSize = (long)(_maxCacheSizeBytes * 0.8); // Clean to 80% of max
                    
                    foreach (var entry in lruEntries)
                    {
                        if (currentSize <= targetSize) break;

                        if (RemoveCacheEntry(entry.Key))
                        {
                            removedEntries++;
                            reclaimedBytes += entry.FileSizeBytes;
                            currentSize -= entry.FileSizeBytes;
                        }
                    }
                }

                var duration = DateTime.UtcNow - startTime;
                _logger.Info("🧹 CACHE CLEANUP COMPLETE: {0} entries removed, {1:F1}MB reclaimed in {2:F1}s", 
                           removedEntries, reclaimedBytes / 1024.0 / 1024.0, duration.TotalSeconds);

                return new CacheCleanupResult
                {
                    RemovedEntries = removedEntries,
                    ReclaimedBytes = reclaimedBytes,
                    Duration = duration,
                    RemainingEntries = _cacheMetadata.Count
                };
            }
        }

        /// <summary>
        /// Gets cache statistics for monitoring
        /// </summary>
        public CacheValidationStatistics GetCacheStatistics()
        {
            lock (_lockObject)
            {
                var totalSize = _cacheMetadata.Values.Sum(e => e.FileSizeBytes);
                var expiredEntries = _cacheMetadata.Values.Count(e => e.ExpiryTime < DateTime.UtcNow);
                
                return new CacheValidationStatistics
                {
                    TotalEntries = _cacheMetadata.Count,
                    TotalSizeBytes = totalSize,
                    TotalSizeMB = totalSize / 1024.0 / 1024.0,
                    MaxSizeMB = _maxCacheSizeBytes / 1024.0 / 1024.0,
                    UtilizationPercent = _maxCacheSizeBytes > 0 ? (double)totalSize / _maxCacheSizeBytes * 100 : 0,
                    ExpiredEntries = expiredEntries,
                    AverageEntryAge = _cacheMetadata.Values.Any() 
                        ? TimeSpan.FromTicks((long)_cacheMetadata.Values.Average(e => (DateTime.UtcNow - e.CreatedTime).Ticks))
                        : TimeSpan.Zero
                };
            }
        }

        #region Private Methods

        private void EnsureCacheDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(_cacheDirectory))
                {
                    Directory.CreateDirectory(_cacheDirectory);
                    _logger.Debug("📁 CACHE DIR CREATED: {0}", _cacheDirectory);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to create cache directory: {0}", _cacheDirectory);
            }
        }

        private void LoadCacheMetadata()
        {
            // Simple implementation - in production would use a database or structured file
            _logger.Debug("Loading cache metadata...");
        }

        private bool RemoveCacheEntry(string key)
        {
            try
            {
                if (_cacheMetadata.TryGetValue(key, out var entry))
                {
                    var filePath = Path.Combine(_cacheDirectory, entry.FileName);
                    
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                    
                    _cacheMetadata.Remove(key);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to remove cache entry: {0}", key);
                return false;
            }
        }

        private long GetCurrentCacheSize()
        {
            return _cacheMetadata.Values.Sum(e => e.FileSizeBytes);
        }

        private void CheckCacheSize()
        {
            var currentSize = GetCurrentCacheSize();
            if (currentSize > _maxCacheSizeBytes)
            {
                _logger.Warn("💾 CACHE SIZE EXCEEDED: {0:F1}MB > {1:F1}MB, cleanup needed", 
                           currentSize / 1024.0 / 1024.0, _maxCacheSizeBytes / 1024.0 / 1024.0);
            }
        }

        #endregion
    }

    #region Cache Data Classes

    public class CacheEntry
    {
        public string Key { get; set; }
        public string FileName { get; set; }
        public long FileSizeBytes { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime ExpiryTime { get; set; }
        public int AccessCount { get; set; }
        public DateTime LastAccessTime { get; set; }
    }

    public class CacheValidationResult
    {
        public bool IsValid { get; set; }
        public string Reason { get; set; }
        public string CachePath { get; set; }
        public TimeSpan CacheAge { get; set; }
    }

    public class CacheCleanupResult
    {
        public int RemovedEntries { get; set; }
        public long ReclaimedBytes { get; set; }
        public TimeSpan Duration { get; set; }
        public int RemainingEntries { get; set; }

        public double ReclaimedMB => ReclaimedBytes / 1024.0 / 1024.0;
    }

    public class CacheValidationStatistics
    {
        public int TotalEntries { get; set; }
        public long TotalSizeBytes { get; set; }
        public double TotalSizeMB { get; set; }
        public double MaxSizeMB { get; set; }
        public double UtilizationPercent { get; set; }
        public int ExpiredEntries { get; set; }
        public TimeSpan AverageEntryAge { get; set; }

        public bool IsNearCapacity => UtilizationPercent > 80;
        public bool HasExpiredEntries => ExpiredEntries > 0;
    }

    #endregion
}