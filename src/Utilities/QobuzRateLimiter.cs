using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.Abstractions;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    /// <summary>
    /// Rate limiter implementation for Qobuz API requests.
    /// </summary>
    public class QobuzRateLimiter : IRateLimiter
    {
        private readonly Logger _logger;
        private readonly SemaphoreSlim _semaphore;
        private DateTime _lastRequest = DateTime.MinValue;
        private readonly TimeSpan _minInterval = TimeSpan.FromMilliseconds(50); // 20 requests per second burst
        private int _requestsThisMinute = 0;
        private DateTime _minuteStart = DateTime.UtcNow;
        private readonly int _maxRequestsPerMinute = 1500; // Qobuz actual limit for authenticated users
        
        // Response time tracking for adaptive rate limiting (using running average for efficiency)
        private readonly Queue<double> _recentResponseTimes = new Queue<double>();
        private readonly object _responseTimeLock = new object();
        private double _sumResponseTimes = 0.0;
        private int _responseCount = 0;
        private double _averageResponseTime = 0.0;

        public QobuzRateLimiter(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _semaphore = new SemaphoreSlim(1, 1);
        }

        public async Task WaitForAllowanceAsync(string requestType = "default")
        {
            await _semaphore.WaitAsync();
            try
            {
                // Reset counter if minute has passed
                if (DateTime.UtcNow - _minuteStart > TimeSpan.FromMinutes(1))
                {
                    _requestsThisMinute = 0;
                    _minuteStart = DateTime.UtcNow;
                }

                // Wait if we've hit the per-minute limit
                if (_requestsThisMinute >= _maxRequestsPerMinute)
                {
                    var waitTime = TimeSpan.FromMinutes(1) - (DateTime.UtcNow - _minuteStart);
                    if (waitTime > TimeSpan.Zero)
                    {
                        _logger.Warn("Rate limit exceeded for {0}, waiting {1}ms", requestType, waitTime.TotalMilliseconds);
                        await Task.Delay(waitTime);
                        _requestsThisMinute = 0;
                        _minuteStart = DateTime.UtcNow;
                    }
                }

                // Enforce minimum interval between requests
                var timeSinceLastRequest = DateTime.UtcNow - _lastRequest;
                if (timeSinceLastRequest < _minInterval)
                {
                    var delay = _minInterval - timeSinceLastRequest;
                    _logger.Trace("Rate limiting {0}: waiting {1}ms", requestType, delay.TotalMilliseconds);
                    await Task.Delay(delay);
                }

                _lastRequest = DateTime.UtcNow;
                _requestsThisMinute++;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public bool IsRequestAllowed(string requestType = "default")
        {
            // Reset counter if minute has passed
            if (DateTime.UtcNow - _minuteStart > TimeSpan.FromMinutes(1))
            {
                _requestsThisMinute = 0;
                _minuteStart = DateTime.UtcNow;
            }

            var timeSinceLastRequest = DateTime.UtcNow - _lastRequest;
            return _requestsThisMinute < _maxRequestsPerMinute && timeSinceLastRequest >= _minInterval;
        }

        public RateLimitStatus GetStatus()
        {
            // Reset counter if minute has passed
            if (DateTime.UtcNow - _minuteStart > TimeSpan.FromMinutes(1))
            {
                _requestsThisMinute = 0;
                _minuteStart = DateTime.UtcNow;
            }

            // Thread-safe read of average response time
            double avgResponseTime;
            lock (_responseTimeLock)
            {
                avgResponseTime = _averageResponseTime;
            }

            return new RateLimitStatus
            {
                RequestsPerMinute = _requestsThisMinute,
                RemainingRequests = Math.Max(0, _maxRequestsPerMinute - _requestsThisMinute),
                TimeUntilReset = TimeSpan.FromMinutes(1) - (DateTime.UtcNow - _minuteStart),
                IsThrottled = _requestsThisMinute >= _maxRequestsPerMinute,
                AverageResponseTime = avgResponseTime
            };
        }

        public void RecordRequest(string requestType, TimeSpan responseTime)
        {
            var responseMs = responseTime.TotalMilliseconds;
            _logger.Trace("Recorded {0} request with {1}ms response time", requestType, responseMs);
            
            lock (_responseTimeLock)
            {
                // Efficient running average calculation
                _recentResponseTimes.Enqueue(responseMs);
                _sumResponseTimes += responseMs;
                _responseCount++;
                
                // Keep only the last 100 response times
                if (_recentResponseTimes.Count > 100)
                {
                    var oldest = _recentResponseTimes.Dequeue();
                    _sumResponseTimes -= oldest;
                    _responseCount = Math.Min(_responseCount, 100);
                }
                
                // Update running average
                _averageResponseTime = _responseCount > 0 
                    ? _sumResponseTimes / _responseCount 
                    : 0.0;
                
                // Adaptive rate limiting: slow down if response times are high
                if (_averageResponseTime > 2000) // If responses take >2 seconds
                {
                    _logger.Info("High average response time ({0:F1}ms), consider reducing request rate", _averageResponseTime);
                }
            }
        }

        // Removed Dispose() as IRateLimiter doesn't require IDisposable
        // Semaphore will be garbage collected when rate limiter is no longer referenced
    }
}