using System;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Services.Resilience
{
    /// <summary>
    /// Provides resilience patterns for API calls including circuit breaker, retry, and timeout policies
    /// </summary>
    public interface IResilienceService : IDisposable
    {
        /// <summary>
        /// Execute an action with resilience policies applied
        /// </summary>
        Task<T> ExecuteWithResilienceAsync<T>(Func<Task<T>> action, string operationKey);
        
        /// <summary>
        /// Execute an action with custom retry count
        /// </summary>
        Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, int retryCount = 3);
        
        /// <summary>
        /// Check if circuit is open for a specific operation
        /// </summary>
        bool IsCircuitOpen(string operationKey);
        
        /// <summary>
        /// Reset circuit breaker for a specific operation
        /// </summary>
        void ResetCircuit(string operationKey);
        
        /// <summary>
        /// Get current resilience statistics
        /// </summary>
        ResilienceStatistics GetStatistics();
    }
    
    public class ResilienceStatistics
    {
        public int TotalRequests { get; set; }
        public int SuccessfulRequests { get; set; }
        public int FailedRequests { get; set; }
        public int RetriedRequests { get; set; }
        public int CircuitOpenCount { get; set; }
        public DateTime LastFailure { get; set; }
        public double SuccessRate => TotalRequests > 0 ? (double)SuccessfulRequests / TotalRequests : 0;
    }
}