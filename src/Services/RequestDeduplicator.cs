using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Request deduplication service that prevents cache stampede effects by coalescing concurrent requests
    /// Critical for preventing API rate limiting when multiple users simultaneously search for the same content
    /// </summary>
    /// <remarks>
    /// Cache Stampede Problem:
    /// - 50 users search "Taylor Swift" simultaneously
    /// - Without deduplication: 50 parallel API calls for same data
    /// - With deduplication: 1 API call, 49 users wait for result
    /// 
    /// Benefits:
    /// - Prevents API rate limiting and potential service bans
    /// - Reduces server load and improves response times
    /// - Maintains cache consistency across concurrent requests
    /// - Provides automatic cleanup of completed requests
    /// </remarks>
    public class RequestDeduplicator
    {
        private readonly ConcurrentDictionary<string, TaskInfo> _pendingRequests;
        private readonly Timer _cleanupTimer;
        private readonly Logger _logger;
        private readonly TimeSpan _requestTimeout;
        private readonly TimeSpan _cleanupInterval;

        // Default timeouts and limits
        private const int DEFAULT_REQUEST_TIMEOUT_SECONDS = 60; // 1 minute timeout
        private const int DEFAULT_CLEANUP_INTERVAL_SECONDS = 30; // 30 second cleanup
        private const int MAX_CONCURRENT_DEDUPLICATED_REQUESTS = 1000; // Prevent memory abuse

        public RequestDeduplicator(
            Logger logger = null, 
            TimeSpan? requestTimeout = null,
            TimeSpan? cleanupInterval = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _pendingRequests = new ConcurrentDictionary<string, TaskInfo>();
            _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(DEFAULT_REQUEST_TIMEOUT_SECONDS);
            _cleanupInterval = cleanupInterval ?? TimeSpan.FromSeconds(DEFAULT_CLEANUP_INTERVAL_SECONDS);

            // Start cleanup timer to prevent memory leaks
            _cleanupTimer = new Timer(CleanupExpiredRequests, null, _cleanupInterval, _cleanupInterval);

            _logger.Debug("RequestDeduplicator initialized with {0}s timeout and {1}s cleanup interval",
                         _requestTimeout.TotalSeconds, _cleanupInterval.TotalSeconds);
        }

        /// <summary>
        /// Gets or creates a request, ensuring only one execution per unique key
        /// </summary>
        /// <typeparam name="T">Type of the result</typeparam>
        /// <param name="key">Unique key for the request (e.g., "search_taylor_swift")</param>
        /// <param name="factory">Function to execute if no pending request exists</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>Result from either the factory function or a concurrent execution</returns>
        public async Task<T> GetOrCreateAsync<T>(
            string key, 
            Func<Task<T>> factory,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            // Check memory limits to prevent abuse
            if (_pendingRequests.Count >= MAX_CONCURRENT_DEDUPLICATED_REQUESTS)
            {
                _logger.Warn("⚠️ DEDUPLICATION LIMIT: Too many concurrent requests ({0}), executing without deduplication", 
                           _pendingRequests.Count);
                return await factory();
            }

            var startTime = DateTime.UtcNow;
            
            // Try to get existing pending request
            if (_pendingRequests.TryGetValue(key, out var existingTaskInfo))
            {
                if (!existingTaskInfo.IsExpired(_requestTimeout))
                {
                    _logger.Debug("🔄 REQUEST COALESCING: Joining existing request for key: {0}", key);
                    
                    try
                    {
                        // Wait for the existing request to complete
                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        timeoutCts.CancelAfter(_requestTimeout);
                        
                        var result = await existingTaskInfo.GetResultAsync<T>(timeoutCts.Token);
                        
                        var waitTime = DateTime.UtcNow - startTime;
                        _logger.Debug("✅ COALESCED REQUEST COMPLETE: Key '{0}' completed in {1:F1}s (waited for existing)", 
                                     key, waitTime.TotalSeconds);
                        
                        return result;
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.Debug("⏰ COALESCED REQUEST TIMEOUT: Key '{0}' timed out, falling back to new request", key);
                        // Fall through to create new request
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug("❌ COALESCED REQUEST FAILED: Key '{0}' failed, falling back to new request: {1}", 
                                     key, ex.Message);
                        // Fall through to create new request
                    }
                }
                else
                {
                    _logger.Debug("⏰ EXPIRED REQUEST: Removing expired request for key: {0}", key);
                    _pendingRequests.TryRemove(key, out _);
                }
            }

            // Create new request execution
            return await ExecuteNewRequest(key, factory, cancellationToken, startTime);
        }

        /// <summary>
        /// Executes a new request with proper cleanup and error handling
        /// </summary>
        private async Task<T> ExecuteNewRequest<T>(
            string key,
            Func<Task<T>> factory,
            CancellationToken cancellationToken,
            DateTime startTime)
        {
            var taskCompletionSource = new TaskCompletionSource<object>();
            var taskInfo = new TaskInfo(taskCompletionSource, startTime);

            // Try to add this as the pending request
            var addedTaskInfo = _pendingRequests.GetOrAdd(key, taskInfo);

            if (addedTaskInfo != taskInfo)
            {
                // Another request was added concurrently, join that one
                _logger.Debug("🔄 CONCURRENT ADDITION: Another request added for key '{0}', joining it", key);
                
                try
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(_requestTimeout);
                    
                    return await addedTaskInfo.GetResultAsync<T>(timeoutCts.Token);
                }
                catch (Exception ex)
                {
                    _logger.Debug("❌ CONCURRENT REQUEST FAILED: Fallback execution for key '{0}': {1}", key, ex.Message);
                    // Execute directly without deduplication as fallback
                    return await factory();
                }
            }

            // We are the chosen request executor
            _logger.Debug("🚀 NEW REQUEST: Executing factory for key: {0}", key);

            try
            {
                // Execute the factory function
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_requestTimeout);

                var result = await factory();
                
                // Set the result for all waiting requests
                taskCompletionSource.SetResult(result);
                
                var executionTime = DateTime.UtcNow - startTime;
                _logger.Info("✅ DEDUPLICATED REQUEST COMPLETE: Key '{0}' completed in {1:F1}s", 
                            key, executionTime.TotalSeconds);
                
                return result;
            }
            catch (OperationCanceledException)
            {
                _logger.Debug("⏰ REQUEST CANCELLED: Key '{0}' was cancelled", key);
                taskCompletionSource.SetCanceled();
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "❌ REQUEST FAILED: Factory execution failed for key: {0}", key);
                taskCompletionSource.SetException(ex);
                throw;
            }
            finally
            {
                // Clean up the pending request
                _pendingRequests.TryRemove(key, out _);
                _logger.Debug("🧹 CLEANUP: Removed pending request for key: {0}", key);
            }
        }

        /// <summary>
        /// Creates a cache-safe key for search requests
        /// </summary>
        public string CreateSearchKey(string artist, string album = null)
        {
            var normalizedArtist = NormalizeKeyComponent(artist);
            
            if (string.IsNullOrWhiteSpace(album))
                return $"search_artist_{normalizedArtist}";
                
            var normalizedAlbum = NormalizeKeyComponent(album);
            return $"search_{normalizedArtist}_{normalizedAlbum}";
        }

        /// <summary>
        /// Creates a cache-safe key for discography requests
        /// </summary>
        public string CreateDiscographyKey(string artist)
        {
            var normalizedArtist = NormalizeKeyComponent(artist);
            return $"discography_{normalizedArtist}";
        }

        /// <summary>
        /// Creates a cache-safe key for quality detection requests
        /// </summary>
        public string CreateQualityKey(int albumId, int preferredQuality)
        {
            return $"quality_{albumId}_{preferredQuality}";
        }

        /// <summary>
        /// Creates a cache-safe key for streaming URL requests
        /// </summary>
        public string CreateStreamingUrlKey(int trackId, int quality)
        {
            return $"stream_{trackId}_{quality}";
        }

        /// <summary>
        /// Normalizes a key component for consistent caching
        /// </summary>
        private string NormalizeKeyComponent(string component)
        {
            if (string.IsNullOrWhiteSpace(component))
                return "empty";

            // Remove special characters and normalize case
            return System.Text.RegularExpressions.Regex.Replace(
                component.ToLowerInvariant().Trim(), 
                @"[^\w\s]", "")
                .Replace(" ", "_")
                .Substring(0, Math.Min(component.Length, 50)); // Limit length
        }

        /// <summary>
        /// Periodic cleanup of expired requests to prevent memory leaks
        /// </summary>
        private void CleanupExpiredRequests(object? state)
        {
            try
            {
                var expiredKeys = new List<string>();
                var cleanupStartTime = DateTime.UtcNow;

                foreach (var kvp in _pendingRequests)
                {
                    if (kvp.Value.IsExpired(_requestTimeout))
                    {
                        expiredKeys.Add(kvp.Key);
                    }
                }

                var removedCount = 0;
                foreach (var expiredKey in expiredKeys)
                {
                    if (_pendingRequests.TryRemove(expiredKey, out var taskInfo))
                    {
                        removedCount++;
                        
                        // Cancel the expired task if it's still running
                        if (!taskInfo.TaskCompletionSource.Task.IsCompleted)
                        {
                            taskInfo.TaskCompletionSource.SetCanceled();
                        }
                    }
                }

                if (removedCount > 0)
                {
                    var cleanupTime = DateTime.UtcNow - cleanupStartTime;
                    _logger.Debug("🧹 REQUEST CLEANUP: Removed {0} expired requests in {1:F1}ms", 
                                 removedCount, cleanupTime.TotalMilliseconds);
                }

                // Log statistics periodically
                if (_pendingRequests.Count > 0)
                {
                    _logger.Debug("📊 DEDUPLICATION STATS: {0} active deduplicated requests", _pendingRequests.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to cleanup expired requests");
            }
        }

        /// <summary>
        /// Gets current deduplication statistics
        /// </summary>
        public RequestDeduplicationStatistics GetStatistics()
        {
            return new RequestDeduplicationStatistics
            {
                ActiveRequests = _pendingRequests.Count,
                MaxConcurrentRequests = MAX_CONCURRENT_DEDUPLICATED_REQUESTS,
                RequestTimeout = _requestTimeout,
                CleanupInterval = _cleanupInterval
            };
        }

        /// <summary>
        /// Disposes the deduplicator and cleans up resources
        /// </summary>
        public void Dispose()
        {
            try
            {
                _cleanupTimer?.Dispose();

                // Cancel all pending requests
                foreach (var kvp in _pendingRequests)
                {
                    if (!kvp.Value.TaskCompletionSource.Task.IsCompleted)
                    {
                        kvp.Value.TaskCompletionSource.SetCanceled();
                    }
                }

                _pendingRequests.Clear();
                _logger.Debug("RequestDeduplicator disposed");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during RequestDeduplicator disposal");
            }
        }
    }

    /// <summary>
    /// Internal class to track pending request information
    /// </summary>
    internal class TaskInfo
    {
        public TaskCompletionSource<object> TaskCompletionSource { get; }
        public DateTime CreatedAt { get; }

        public TaskInfo(TaskCompletionSource<object> taskCompletionSource, DateTime createdAt)
        {
            TaskCompletionSource = taskCompletionSource;
            CreatedAt = createdAt;
        }

        public bool IsExpired(TimeSpan timeout)
        {
            return DateTime.UtcNow - CreatedAt > timeout;
        }

        public async Task<T> GetResultAsync<T>(CancellationToken cancellationToken)
        {
            var result = await TaskCompletionSource.Task.ConfigureAwait(false);
            return (T)result;
        }
    }

    /// <summary>
    /// Statistics for monitoring request deduplication effectiveness
    /// </summary>
    public class RequestDeduplicationStatistics
    {
        public int ActiveRequests { get; set; }
        public int MaxConcurrentRequests { get; set; }
        public TimeSpan RequestTimeout { get; set; }
        public TimeSpan CleanupInterval { get; set; }
    }
}