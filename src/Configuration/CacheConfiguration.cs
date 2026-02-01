using System;

namespace Lidarr.Plugin.Qobuzarr.Configuration
{
    /// <summary>
    /// Configuration constants for Query Intelligence optimization caching
    /// Provides centralized configuration for cache sizes, TTLs, and thresholds
    /// </summary>
    public static class CacheConfiguration
    {
        #region Cache Sizes
        /// <summary>
        /// Default maximum entries in Lidarr context cache
        /// Memory estimate: ~2-5KB per entry
        /// </summary>
        public const int DefaultContextCacheSize = 5000;

        /// <summary>
        /// Default maximum entries in pattern cache  
        /// Memory estimate: ~1KB per entry
        /// </summary>
        public const int DefaultPatternCacheSize = 10000;

        /// <summary>
        /// Default maximum entries in substring cache
        /// Memory estimate: ~2KB per entry  
        /// </summary>
        public const int DefaultSubstringCacheSize = 20000;

        /// <summary>
        /// Maximum cache entries for performance testing
        /// Used in test scenarios with large datasets
        /// </summary>
        public const int LargeCacheSize = 50000;
        #endregion

        #region TTL Values
        /// <summary>
        /// Default TTL for Lidarr context cache entries
        /// Balances freshness with performance
        /// </summary>
        public static readonly TimeSpan DefaultContextCacheTTL = TimeSpan.FromHours(6);

        /// <summary>
        /// Default TTL for pattern cache entries
        /// Pattern detection is stable, longer TTL acceptable
        /// </summary>
        public static readonly TimeSpan DefaultPatternCacheTTL = TimeSpan.FromHours(24);

        /// <summary>
        /// Default TTL for substring cache entries
        /// Substring matches are highly stable
        /// </summary>
        public static readonly TimeSpan DefaultSubstringCacheTTL = TimeSpan.FromHours(48);

        /// <summary>
        /// Extended TTL for high-confidence cache entries
        /// </summary>
        public static readonly TimeSpan ExtendedCacheTTL = TimeSpan.FromDays(7);
        #endregion

        #region Similarity Thresholds
        /// <summary>
        /// Default similarity threshold for substring matching
        /// 0.85 provides good balance of accuracy vs coverage
        /// </summary>
        public const double DefaultSimilarityThreshold = 0.85;

        /// <summary>
        /// High confidence threshold for ML predictions and cache matches
        /// Used for automatic decision making
        /// </summary>
        public const double HighConfidenceThreshold = 0.90;

        /// <summary>
        /// Minimum similarity threshold - below this, matches are rejected
        /// </summary>
        public const double MinimumSimilarityThreshold = 0.60;

        /// <summary>
        /// Maximum similarity threshold (perfect match)
        /// </summary>
        public const double MaximumSimilarityThreshold = 1.00;
        #endregion

        #region Memory Estimates (bytes per entry)
        /// <summary>
        /// Estimated memory usage per context cache entry
        /// Includes metadata, queries, and overhead
        /// </summary>
        public const int ContextCacheEntrySize = 2048;

        /// <summary>
        /// Estimated memory usage per pattern cache entry
        /// Includes patterns, hit counts, and metadata
        /// </summary>
        public const int PatternCacheEntrySize = 1024;

        /// <summary>
        /// Estimated memory usage per substring cache entry
        /// Includes normalized strings and similarity data
        /// </summary>
        public const int SubstringCacheEntrySize = 2048;
        #endregion

        #region Query Complexity Thresholds
        /// <summary>
        /// Default threshold for simple complexity classification
        /// Artists/albums with complexity score <= this are Simple
        /// </summary>
        public const int DefaultSimpleThreshold = 1;

        /// <summary>
        /// Default threshold for medium complexity classification
        /// Artists/albums with complexity score <= this are Medium (after Simple check)
        /// </summary>
        public const int DefaultMediumThreshold = 4;

        /// <summary>
        /// Threshold above which queries are always classified as Complex
        /// Ensures quality preservation for difficult cases
        /// </summary>
        public const int ComplexThreshold = 8;
        #endregion

        #region Performance Constants
        /// <summary>
        /// Default page size for API requests
        /// Balances API efficiency with memory usage
        /// </summary>
        public const int DefaultPageSize = 100;

        /// <summary>
        /// Percentage of cache to evict when cache is full
        /// 10% provides good balance of performance vs memory
        /// </summary>
        public const double CacheEvictionPercentage = 0.10;

        /// <summary>
        /// Maximum number of cache eviction entries per operation
        /// Prevents excessive processing during eviction
        /// </summary>
        public const int MaxEvictionCount = 5000;

        /// <summary>
        /// Minimum delay between cache statistics logging (milliseconds)
        /// Prevents log spam during high activity periods
        /// </summary>
        public const int LoggingIntervalMs = 300000; // 5 minutes
        #endregion

        #region ML Configuration
        /// <summary>
        /// Default confidence threshold for ML predictions
        /// Predictions below this threshold fall back to rule-based classification
        /// </summary>
        public const float DefaultMLConfidenceThreshold = 0.7f;

        /// <summary>
        /// Default interval between ML model retraining (hours)
        /// </summary>
        public const int DefaultMLRetrainIntervalHours = 24;

        /// <summary>
        /// Default batch size for triggering ML model retraining
        /// Model retrains when this many new patterns are collected
        /// </summary>
        public const int DefaultMLRetrainBatchSize = 1000;

        /// <summary>
        /// Number of features extracted for ML model
        /// Used for feature vector validation
        /// </summary>
        public const int MLFeatureCount = 25;

        /// <summary>
        /// Minimum accuracy threshold for ML model
        /// Model retrains if accuracy falls below this
        /// </summary>
        public const double MinimumMLAccuracy = 0.70;
        #endregion

        #region Validation Helpers
        /// <summary>
        /// Validates cache size parameter
        /// </summary>
        /// <param name="size">Cache size to validate</param>
        /// <param name="parameterName">Parameter name for exception</param>
        /// <exception cref="ArgumentOutOfRangeException">If size is invalid</exception>
        public static void ValidateCacheSize(int size, string parameterName)
        {
            if (size <= 0)
                throw new ArgumentOutOfRangeException(parameterName, "Cache size must be positive");

            if (size > LargeCacheSize)
                throw new ArgumentOutOfRangeException(parameterName, $"Cache size cannot exceed {LargeCacheSize}");
        }

        /// <summary>
        /// Validates similarity threshold parameter
        /// </summary>
        /// <param name="threshold">Similarity threshold to validate</param>
        /// <param name="parameterName">Parameter name for exception</param>
        /// <exception cref="ArgumentOutOfRangeException">If threshold is invalid</exception>
        public static void ValidateSimilarityThreshold(double threshold, string parameterName)
        {
            if (threshold < MinimumSimilarityThreshold || threshold > MaximumSimilarityThreshold)
                throw new ArgumentOutOfRangeException(parameterName,
                    $"Similarity threshold must be between {MinimumSimilarityThreshold} and {MaximumSimilarityThreshold}");
        }

        /// <summary>
        /// Gets estimated memory usage for a cache configuration
        /// </summary>
        /// <param name="entryCount">Number of cache entries</param>
        /// <param name="bytesPerEntry">Bytes per cache entry</param>
        /// <returns>Estimated memory usage in bytes</returns>
        public static long EstimateMemoryUsage(int entryCount, int bytesPerEntry)
        {
            return (long)entryCount * bytesPerEntry;
        }
        #endregion
    }
}
