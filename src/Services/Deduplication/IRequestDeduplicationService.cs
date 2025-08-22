using System;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Services.Deduplication
{
    /// <summary>
    /// Service to prevent duplicate API requests for the same data
    /// </summary>
    public interface IRequestDeduplicationService : IDisposable
    {
        /// <summary>
        /// Execute a request with deduplication. If the same request is already in flight,
        /// wait for it to complete and return the same result.
        /// </summary>
        Task<T> DeduplicateRequestAsync<T>(string requestKey, Func<Task<T>> requestFactory, TimeSpan? cacheDuration = null);
        
        /// <summary>
        /// Check if a request is currently in flight
        /// </summary>
        bool IsRequestInFlight(string requestKey);
        
        /// <summary>
        /// Clear all cached results and in-flight requests
        /// </summary>
        void Clear();
        
        /// <summary>
        /// Get deduplication statistics
        /// </summary>
        DeduplicationStatistics GetStatistics();
    }
    
    public class DeduplicationStatistics
    {
        public int TotalRequests { get; set; }
        public int DuplicatesSaved { get; set; }
        public int InFlightRequests { get; set; }
        public int CachedResults { get; set; }
        public double DeduplicationRate => TotalRequests > 0 ? (double)DuplicatesSaved / TotalRequests : 0;
    }
}