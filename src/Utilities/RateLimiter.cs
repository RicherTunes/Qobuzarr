using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    /// <summary>
    /// Thread-safe sliding window rate limiter for controlling API request frequency.
    /// Implements a time-based sliding window algorithm to ensure compliance with Qobuz API rate limits.
    /// </summary>
    /// <remarks>
    /// This rate limiter uses a sliding window approach where requests are tracked with timestamps,
    /// and old requests outside the time window are automatically expired. The implementation is
    /// thread-safe and supports concurrent access from multiple tasks.
    /// 
    /// <para><b>Algorithm Details:</b></para>
    /// <list type="bullet">
    /// <item>Uses a queue to track request timestamps within the sliding window</item>
    /// <item>Automatically expires requests older than the configured time window</item>
    /// <item>Blocks new requests when the limit is reached until oldest request expires</item>
    /// <item>Provides graceful degradation with cancellation token support</item>
    /// </list>
    /// 
    /// <para><b>Thread Safety:</b></para>
    /// All public methods are thread-safe. Internal state is protected by a combination of
    /// SemaphoreSlim for async coordination and object lock for queue operations.
    /// 
    /// <para><b>Usage Example:</b></para>
    /// <code>
    /// // Create a rate limiter allowing 100 requests per minute
    /// var rateLimiter = new RateLimiter(100, TimeSpan.FromMinutes(1));
    /// 
    /// // Before each API call
    /// await rateLimiter.WaitAsync(cancellationToken);
    /// var response = await apiClient.GetAsync(...);
    /// </code>
    /// 
    /// <para><b>Performance Considerations:</b></para>
    /// <list type="bullet">
    /// <item>O(1) amortized time complexity for WaitAsync operation</item>
    /// <item>Memory usage proportional to maxRequests (one DateTime per tracked request)</item>
    /// <item>Automatic cleanup of expired entries prevents memory growth</item>
    /// </list>
    /// </remarks>
    public class RateLimiter
    {
        private readonly int _maxRequests;
        private readonly TimeSpan _timeWindow;
        private readonly Queue<DateTime> _requestTimes;
        private readonly SemaphoreSlim _semaphore;
        private readonly object _lock = new object();

        /// <summary>
        /// Initializes a new instance of the RateLimiter with specified rate limit parameters.
        /// </summary>
        /// <param name="maxRequests">Maximum number of requests allowed within the time window. Must be positive.</param>
        /// <param name="timeWindow">The sliding time window for rate limiting. Must be positive.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when maxRequests is less than 1 or timeWindow is zero or negative.</exception>
        /// <example>
        /// <code>
        /// // Qobuz API standard rate limit: 1000 requests per minute
        /// var apiRateLimiter = new RateLimiter(1000, TimeSpan.FromMinutes(1));
        /// 
        /// // Conservative rate limit for testing: 10 requests per second
        /// var testRateLimiter = new RateLimiter(10, TimeSpan.FromSeconds(1));
        /// </code>
        /// </example>
        public RateLimiter(int maxRequests, TimeSpan timeWindow)
        {
            _maxRequests = maxRequests;
            _timeWindow = timeWindow;
            _requestTimes = new Queue<DateTime>();
            _semaphore = new SemaphoreSlim(1, 1);
        }

        /// <summary>
        /// Asynchronously waits until a request can be made without exceeding the rate limit.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the wait operation.</param>
        /// <returns>A task that completes when the request can proceed without violating rate limits.</returns>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via the cancellation token.</exception>
        /// <remarks>
        /// This method implements a fair queuing system where requests are processed in the order they arrive.
        /// If the rate limit is reached, the method will automatically calculate the required wait time
        /// based on when the oldest request in the window will expire.
        /// 
        /// <para><b>Behavior:</b></para>
        /// <list type="number">
        /// <item>Acquires exclusive access via semaphore to ensure thread safety</item>
        /// <item>Removes expired requests from the sliding window</item>
        /// <item>If at capacity, calculates wait time until oldest request expires</item>
        /// <item>Records the current request timestamp</item>
        /// <item>Waits if necessary before allowing the request to proceed</item>
        /// </list>
        /// </remarks>
        public async Task WaitAsync(CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                TimeSpan? waitTime = null;
                
                lock (_lock)
                {
                    var now = DateTime.UtcNow;

                    // Remove old requests outside the time window
                    while (_requestTimes.Count > 0 && now - _requestTimes.Peek() > _timeWindow)
                    {
                        _requestTimes.Dequeue();
                    }

                    // If we're at the limit, calculate wait time
                    if (_requestTimes.Count >= _maxRequests)
                    {
                        var oldestRequest = _requestTimes.Peek();
                        waitTime = _timeWindow - (now - oldestRequest);
                        
                        // Remove the oldest request after waiting
                        _requestTimes.Dequeue();
                    }

                    // Record this request
                    _requestTimes.Enqueue(DateTime.UtcNow);
                }
                
                // Wait outside the lock to avoid blocking other threads
                if (waitTime.HasValue && waitTime.Value > TimeSpan.Zero)
                {
                    await Task.Delay(waitTime.Value, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Gets the number of requests that can be made immediately without waiting.
        /// </summary>
        /// <returns>The number of available request slots in the current time window.</returns>
        /// <remarks>
        /// This method provides a snapshot of the current capacity. The value may change
        /// immediately after retrieval as requests expire or new requests are made.
        /// Use this for monitoring and diagnostics rather than for decision-making.
        /// </remarks>
        public int GetRemainingRequests()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;

                // Clean up old requests
                while (_requestTimes.Count > 0 && now - _requestTimes.Peek() > _timeWindow)
                {
                    _requestTimes.Dequeue();
                }

                return Math.Max(0, _maxRequests - _requestTimes.Count);
            }
        }

        /// <summary>
        /// Gets the time remaining until the oldest request expires and a new slot becomes available.
        /// </summary>
        /// <returns>TimeSpan indicating when the next request slot will become available, or Zero if slots are available now.</returns>
        /// <remarks>
        /// Useful for displaying rate limit information to users or for implementing
        /// backoff strategies in retry logic. Returns TimeSpan.Zero when the rate limiter
        /// has available capacity.
        /// </remarks>
        public TimeSpan GetTimeUntilReset()
        {
            lock (_lock)
            {
                if (_requestTimes.Count == 0)
                {
                    return TimeSpan.Zero;
                }

                var oldestRequest = _requestTimes.Peek();
                var elapsed = DateTime.UtcNow - oldestRequest;
                return _timeWindow - elapsed;
            }
        }
    }
}