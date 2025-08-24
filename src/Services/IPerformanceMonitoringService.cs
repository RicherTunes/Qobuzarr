using System;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Interface for production performance monitoring and telemetry
    /// Enables validation of performance claims through real-world data collection
    /// </summary>
    public interface IPerformanceMonitoringService : IDisposable
    {
        /// <summary>
        /// Records an API call for performance tracking
        /// </summary>
        void RecordApiCall(string endpoint, TimeSpan duration, bool wasCached, string cacheKey = null);

        /// <summary>
        /// Records cache hit/miss for performance tracking
        /// </summary>
        void RecordCacheHit(string cacheType, string key, bool hit, TimeSpan? lookupDuration = null);

        /// <summary>
        /// Records ML query optimization usage
        /// </summary>
        void RecordMLOptimization(string originalQuery, string optimizedQuery, bool successful, double confidenceScore);

        /// <summary>
        /// Records API call reduction metrics
        /// </summary>
        void RecordApiReduction(int originalCalls, int actualCalls, string optimization);

        /// <summary>
        /// Gets current API call reduction percentage
        /// </summary>
        double GetApiReductionPercentage();

        /// <summary>
        /// Gets current cache hit rate
        /// </summary>
        double GetCacheHitRate(string cacheType = null);

        /// <summary>
        /// Gets ML optimization effectiveness percentage
        /// </summary>
        double GetMLOptimizationRate();

        /// <summary>
        /// Gets comprehensive performance metrics
        /// </summary>
        ProductionMetrics GetCurrentMetrics();
    }
}