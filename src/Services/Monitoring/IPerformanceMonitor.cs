using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Services.Monitoring
{
    /// <summary>
    /// Performance monitoring interface for tracking production path performance.
    /// </summary>
    public interface IPerformanceMonitor
    {
        /// <summary>
        /// Tracks the execution time of an operation and logs performance metrics.
        /// </summary>
        Task<T> TrackOperationAsync<T>(string operationName, Func<Task<T>> operation, Dictionary<string, object> metadata = null);
        
        /// <summary>
        /// Tracks the execution time of a synchronous operation.
        /// </summary>
        T TrackOperation<T>(string operationName, Func<T> operation, Dictionary<string, object> metadata = null);
        
        /// <summary>
        /// Records a performance metric for analysis.
        /// </summary>
        void RecordMetric(string metricName, double value, Dictionary<string, object> metadata = null);
        
        /// <summary>
        /// Gets performance statistics for analysis.
        /// </summary>
        PerformanceStatistics GetStatistics(string operationName = null);
    }

    /// <summary>
    /// Performance statistics for monitoring and analysis.
    /// </summary>
    public class PerformanceStatistics
    {
        public string OperationName { get; set; }
        public long TotalOperations { get; set; }
        public double AverageExecutionTimeMs { get; set; }
        public double MinExecutionTimeMs { get; set; }
        public double MaxExecutionTimeMs { get; set; }
        public double P95ExecutionTimeMs { get; set; }
        public long ErrorCount { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
        public DateTime LastUpdated { get; set; }
    }
}