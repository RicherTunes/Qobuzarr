using System;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Services.Gateway
{
    /// <summary>
    /// Facade interface that coordinates resilience, deduplication, and monitoring for API calls.
    /// This prevents direct coupling between QobuzIndexer and individual service implementations.
    /// </summary>
    public interface IQobuzApiGateway : IDisposable
    {
        /// <summary>
        /// Execute an API operation with full resilience, deduplication, and monitoring
        /// </summary>
        /// <typeparam name="T">The return type of the operation</typeparam>
        /// <param name="operationKey">Unique key identifying this operation for deduplication and circuit breaking</param>
        /// <param name="operation">The async operation to execute</param>
        /// <param name="cacheDuration">Optional cache duration for successful results</param>
        /// <returns>The result of the operation</returns>
        Task<T> ExecuteAsync<T>(string operationKey, Func<Task<T>> operation, TimeSpan? cacheDuration = null);
        
        /// <summary>
        /// Check the health status of the gateway and its underlying services
        /// </summary>
        /// <returns>Current system health status</returns>
        SystemHealth GetHealthStatus();
        
        /// <summary>
        /// Get aggregated statistics from all services
        /// </summary>
        /// <returns>Combined statistics from resilience, deduplication, and monitoring services</returns>
        GatewayStatistics GetStatistics();
        
        /// <summary>
        /// Reset circuit breaker for a specific operation
        /// </summary>
        /// <param name="operationKey">The operation key to reset</param>
        void ResetCircuit(string operationKey);
    }
    
    /// <summary>
    /// System health status
    /// </summary>
    public enum SystemHealth
    {
        /// <summary>
        /// All systems operational
        /// </summary>
        Healthy,
        
        /// <summary>
        /// Some circuits open or high error rate
        /// </summary>
        Degraded,
        
        /// <summary>
        /// Multiple failures or critical circuits open
        /// </summary>
        Unhealthy
    }
    
    /// <summary>
    /// Aggregated statistics from all gateway services
    /// </summary>
    public class GatewayStatistics
    {
        public int TotalRequests { get; set; }
        public int SuccessfulRequests { get; set; }
        public int FailedRequests { get; set; }
        public int DuplicatesSaved { get; set; }
        public int CircuitBreakerTrips { get; set; }
        public double SuccessRate { get; set; }
        public double DeduplicationRate { get; set; }
        public TimeSpan AverageResponseTime { get; set; }
        public DateTime LastFailure { get; set; }
        public int ActiveCircuits { get; set; }
        public int OpenCircuits { get; set; }
    }
}