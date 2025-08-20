using System;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Abstractions
{
    /// <summary>
    /// Interface for retry policy abstraction to replace hardcoded retry logic.
    /// Addresses architectural debt identified in PR #4 assessment.
    /// </summary>
    public interface IRetryPolicy
    {
        /// <summary>
        /// Execute operation with retry policy applied.
        /// </summary>
        /// <typeparam name="T">Return type of operation</typeparam>
        /// <param name="operation">Operation to execute with retry</param>
        /// <param name="retryContext">Context information for retry decisions</param>
        /// <returns>Result of successful operation</returns>
        Task<T> ExecuteAsync<T>(Func<Task<T>> operation, RetryContext retryContext = null);
        
        /// <summary>
        /// Execute operation with retry policy applied (synchronous).
        /// </summary>
        /// <typeparam name="T">Return type of operation</typeparam>
        /// <param name="operation">Operation to execute with retry</param>
        /// <param name="retryContext">Context information for retry decisions</param>
        /// <returns>Result of successful operation</returns>
        T Execute<T>(Func<T> operation, RetryContext retryContext = null);
        
        /// <summary>
        /// Check if an exception should trigger a retry.
        /// </summary>
        /// <param name="exception">Exception that occurred</param>
        /// <param name="attemptNumber">Current attempt number (1-based)</param>
        /// <returns>True if operation should be retried</returns>
        bool ShouldRetry(Exception exception, int attemptNumber);
        
        /// <summary>
        /// Calculate delay before next retry attempt.
        /// </summary>
        /// <param name="attemptNumber">Current attempt number (1-based)</param>
        /// <param name="exception">Exception that triggered retry</param>
        /// <returns>Delay before next attempt</returns>
        TimeSpan GetRetryDelay(int attemptNumber, Exception exception = null);
    }
    
    /// <summary>
    /// Context information for retry policy decisions
    /// </summary>
    public class RetryContext
    {
        public string OperationType { get; set; } = "default";
        public int MaxAttempts { get; set; } = 3;
        public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
        public bool UseExponentialBackoff { get; set; } = true;
        public bool UseJitter { get; set; } = true;
    }
}