using System;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Abstractions
{
    /// <summary>
    /// Interface for rate limiting API requests to respect service limits.
    /// Addresses architectural debt identified in PR #4 assessment.
    /// </summary>
    public interface IRateLimiter
    {
        /// <summary>
        /// Wait for rate limit allowance before proceeding with request.
        /// </summary>
        /// <param name="requestType">Type of request for rate limit categorization</param>
        /// <returns>Task that completes when request is allowed</returns>
        Task WaitForAllowanceAsync(string requestType = "default");
        
        /// <summary>
        /// Check if a request would be allowed without waiting.
        /// </summary>
        /// <param name="requestType">Type of request to check</param>
        /// <returns>True if request can proceed immediately</returns>
        bool IsRequestAllowed(string requestType = "default");
        
        /// <summary>
        /// Get current rate limit status and metrics.
        /// </summary>
        /// <returns>Rate limit status information</returns>
        RateLimitStatus GetStatus();
        
        /// <summary>
        /// Record a completed request for rate limit tracking.
        /// </summary>
        /// <param name="requestType">Type of request completed</param>
        /// <param name="responseTime">Time taken for the request</param>
        void RecordRequest(string requestType, TimeSpan responseTime);
    }
    
    /// <summary>
    /// Rate limit status information
    /// </summary>
    public class RateLimitStatus
    {
        public int RequestsPerMinute { get; set; }
        public int RemainingRequests { get; set; }
        public TimeSpan TimeUntilReset { get; set; }
        public bool IsThrottled { get; set; }
        public double AverageResponseTime { get; set; }
    }
}