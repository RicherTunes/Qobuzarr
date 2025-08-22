using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Services.Deduplication
{
    /// <summary>
    /// Implements request deduplication to prevent duplicate API calls
    /// </summary>
    public class RequestDeduplicationService : IRequestDeduplicationService
    {
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<string, InFlightRequest> _inFlightRequests;
        private readonly ConcurrentDictionary<string, CachedResult> _cachedResults;
        private readonly DeduplicationStatistics _statistics;
        private readonly object _statsLock = new object();
        private readonly Timer _cleanupTimer;

        public RequestDeduplicationService(Logger logger)
        {
            _logger = logger;
            _inFlightRequests = new ConcurrentDictionary<string, InFlightRequest>();
            _cachedResults = new ConcurrentDictionary<string, CachedResult>();
            _statistics = new DeduplicationStatistics();
            
            // Cleanup expired cache entries every minute
            _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public async Task<T> DeduplicateRequestAsync<T>(string requestKey, Func<Task<T>> requestFactory, TimeSpan? cacheDuration = null)
        {
            IncrementTotalRequests();
            
            // Check if result is cached and still valid
            if (_cachedResults.TryGetValue(requestKey, out var cached))
            {
                if (cached.ExpiresAt > DateTime.UtcNow)
                {
                    IncrementDuplicatesSaved();
                    _logger.Debug("Returning cached result for request: {0}", requestKey);
                    return (T)cached.Result;
                }
                else
                {
                    // Remove expired entry
                    _cachedResults.TryRemove(requestKey, out _);
                }
            }
            
            // Check if request is already in flight
            var inFlight = _inFlightRequests.GetOrAdd(requestKey, key => new InFlightRequest());
            
            if (inFlight.IsStarted)
            {
                // Request already in flight, wait for it
                IncrementDuplicatesSaved();
                _logger.Debug("Request already in flight, waiting for: {0}", requestKey);
                
                try
                {
                    await inFlight.CompletionSource.Task.ConfigureAwait(false);
                    
                    // Get the result from cache (it should be there now)
                    if (_cachedResults.TryGetValue(requestKey, out cached))
                    {
                        return (T)cached.Result;
                    }
                    
                    // Fallback: execute the request if cache miss
                    _logger.Warn("Cache miss after in-flight completion for: {0}", requestKey);
                    return await ExecuteRequestAsync<T>(requestKey, requestFactory, cacheDuration).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error waiting for in-flight request: {0}", requestKey);
                    throw;
                }
            }
            else
            {
                // This is the first request, execute it
                bool shouldExecute = false;
                lock (inFlight.StartLock)
                {
                    if (!inFlight.IsStarted)
                    {
                        inFlight.IsStarted = true;
                        shouldExecute = true;
                    }
                }
                
                if (shouldExecute)
                {
                    return await ExecuteRequestAsync<T>(requestKey, requestFactory, cacheDuration, inFlight).ConfigureAwait(false);
                }
                else
                {
                    // Another thread started it while we were waiting for the lock
                    return await WaitForInFlightRequestAsync<T>(requestKey, inFlight).ConfigureAwait(false);
                }
            }
        }

        public bool IsRequestInFlight(string requestKey)
        {
            return _inFlightRequests.TryGetValue(requestKey, out var inFlight) && inFlight.IsStarted;
        }

        public void Clear()
        {
            _inFlightRequests.Clear();
            _cachedResults.Clear();
            _logger.Info("Cleared all deduplicated requests and cached results");
        }

        public DeduplicationStatistics GetStatistics()
        {
            lock (_statsLock)
            {
                return new DeduplicationStatistics
                {
                    TotalRequests = _statistics.TotalRequests,
                    DuplicatesSaved = _statistics.DuplicatesSaved,
                    InFlightRequests = _inFlightRequests.Count,
                    CachedResults = _cachedResults.Count
                };
            }
        }

        private async Task<T> ExecuteRequestAsync<T>(string requestKey, Func<Task<T>> requestFactory, 
            TimeSpan? cacheDuration, InFlightRequest inFlight = null)
        {
            try
            {
                _logger.Debug("Executing request: {0}", requestKey);
                var result = await requestFactory().ConfigureAwait(false);
                
                // Cache the result
                var duration = cacheDuration ?? TimeSpan.FromMinutes(5);
                var cached = new CachedResult
                {
                    Result = result,
                    ExpiresAt = DateTime.UtcNow.Add(duration)
                };
                
                _cachedResults[requestKey] = cached;
                
                // Signal completion to waiting threads
                inFlight?.CompletionSource.SetResult(true);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Request failed: {0}", requestKey);
                
                // Signal failure to waiting threads
                inFlight?.CompletionSource.SetException(ex);
                
                throw;
            }
            finally
            {
                // Remove from in-flight requests
                _inFlightRequests.TryRemove(requestKey, out _);
            }
        }

        private async Task<T> WaitForInFlightRequestAsync<T>(string requestKey, InFlightRequest inFlight)
        {
            IncrementDuplicatesSaved();
            _logger.Debug("Waiting for in-flight request: {0}", requestKey);
            
            await inFlight.CompletionSource.Task.ConfigureAwait(false);
            
            if (_cachedResults.TryGetValue(requestKey, out var cached))
            {
                return (T)cached.Result;
            }
            
            throw new InvalidOperationException($"Result not found in cache after in-flight completion: {requestKey}");
        }

        private void CleanupExpiredEntries(object state)
        {
            try
            {
                var now = DateTime.UtcNow;
                var expiredKeys = _cachedResults
                    .Where(kvp => kvp.Value.ExpiresAt <= now)
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var key in expiredKeys)
                {
                    _cachedResults.TryRemove(key, out _);
                }
                
                if (expiredKeys.Count > 0)
                {
                    _logger.Debug("Cleaned up {0} expired cache entries", expiredKeys.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during cache cleanup");
            }
        }

        private void IncrementTotalRequests()
        {
            lock (_statsLock)
            {
                _statistics.TotalRequests++;
            }
        }

        private void IncrementDuplicatesSaved()
        {
            lock (_statsLock)
            {
                _statistics.DuplicatesSaved++;
            }
        }

        private class InFlightRequest
        {
            public bool IsStarted { get; set; }
            public TaskCompletionSource<bool> CompletionSource { get; } = new TaskCompletionSource<bool>();
            public object StartLock { get; } = new object();
        }

        private class CachedResult
        {
            public object Result { get; set; }
            public DateTime ExpiresAt { get; set; }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            Clear();
        }
    }
}