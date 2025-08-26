using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    /// <summary>
    /// Thread-safe rate limiter implementing a sliding window algorithm to control API request throughput.
    /// Ensures compliance with Qobuz API rate limits while maximizing request efficiency.
    /// </summary>
    /// <remarks>
    /// This implementation uses a sliding window approach that tracks exact request timestamps,
    /// providing more accurate rate limiting than fixed window or token bucket algorithms.
    /// Thread-safety is guaranteed through semaphore-based coordination and lock-protected state.
    /// </remarks>
    public class RateLimiter
    {
        private readonly int _maxRequests;
        private readonly TimeSpan _timeWindow;
        private readonly Queue<DateTime> _requestTimes;
        private readonly SemaphoreSlim _semaphore;
        private readonly object _lock = new object();

        /// <summary>
        /// Initializes a new instance of the RateLimiter class with specified rate limiting parameters.
        /// </summary>
        /// <param name="maxRequests">Maximum number of requests allowed within the time window.</param>
        /// <param name="timeWindow">Duration of the sliding window for rate limiting.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when maxRequests is less than 1 or timeWindow is negative or zero.</exception>
        /// <example>
        /// <code>
        /// // Allow 600 requests per minute (Qobuz standard limit)
        /// var rateLimiter = new RateLimiter(600, TimeSpan.FromMinutes(1));
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
        /// Waits asynchronously until a request can be made without violating rate limits.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the wait operation.</param>
        /// <returns>A task that completes when the request can proceed.</returns>
        /// <exception cref="OperationCanceledException">Thrown when the cancellation token is triggered.</exception>
        /// <remarks>
        /// This method automatically delays execution if the rate limit has been reached,
        /// ensuring smooth request flow without manual retry logic.
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
        /// Gets the number of requests that can be made immediately without hitting rate limits.
        /// </summary>
        /// <returns>Number of available request slots in the current time window.</returns>
        /// <remarks>
        /// Useful for monitoring rate limit status and implementing adaptive request strategies.
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
        /// Gets the time remaining until the oldest request in the window expires, allowing new requests.
        /// </summary>
        /// <returns>TimeSpan indicating when the rate limit window will reset. Returns Zero if no active limits.</returns>
        /// <remarks>
        /// Use this method to implement intelligent retry logic or to display rate limit status to users.
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