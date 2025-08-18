using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    public class RateLimiter
    {
        private readonly int _maxRequests;
        private readonly TimeSpan _timeWindow;
        private readonly Queue<DateTime> _requestTimes;
        private readonly SemaphoreSlim _semaphore;
        private readonly object _lock = new object();

        public RateLimiter(int maxRequests, TimeSpan timeWindow)
        {
            _maxRequests = maxRequests;
            _timeWindow = timeWindow;
            _requestTimes = new Queue<DateTime>();
            _semaphore = new SemaphoreSlim(1, 1);
        }

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