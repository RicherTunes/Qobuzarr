using System;
using System.Collections.Generic;

namespace Lidarr.Plugin.Qobuzarr.Services.Monitoring
{
    /// <summary>
    /// Interface for collecting and aggregating search operation metrics
    /// </summary>
    public interface ISearchMetricsCollector : IDisposable
    {
        /// <summary>
        /// Start tracking a new search operation
        /// </summary>
        SearchMetric StartSearch(string artist, string album);
        
        /// <summary>
        /// Complete a search operation and record metrics
        /// </summary>
        void CompleteSearch(SearchMetric metric, bool success, int resultsCount, int apiCallsCount);
        
        /// <summary>
        /// Record an error that occurred during search
        /// </summary>
        void RecordError(SearchMetric metric, Exception exception);
        
        /// <summary>
        /// Record a completed search metric
        /// </summary>
        void RecordSearch(SearchMetric metric);
        
        /// <summary>
        /// Get current aggregated statistics
        /// </summary>
        SearchStatistics GetStatistics();
    }
}