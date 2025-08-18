using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    public interface IAdaptiveRateLimiter
    {
        Task<bool> WaitIfNeededAsync(string endpoint, CancellationToken cancellationToken = default);
        void RecordResponse(string endpoint, HttpResponseMessage response);
        void RecordResponse(string endpoint, HttpResponse response);
        int GetCurrentLimit(string endpoint);
        RateLimitStats GetStats();
    }

    public class AdaptiveRateLimiter : IAdaptiveRateLimiter
    {
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<string, EndpointRateLimit> _endpointLimits;
        private readonly SemaphoreSlim _globalSemaphore;
        private DateTime _lastGlobalRequest = DateTime.MinValue;
        private readonly object _lock = new();

        // Configuration
        private const int DEFAULT_REQUESTS_PER_MINUTE = 60;
        private const int MIN_REQUESTS_PER_MINUTE = 10;
        private const int MAX_REQUESTS_PER_MINUTE = 500; // Qobuz can handle 600+, but let's be conservative
        private const double RATE_REDUCTION_FACTOR = 0.75;
        private const double RATE_INCREASE_FACTOR = 1.2; // Aggressive but safe increase
        private const int SUCCESS_THRESHOLD_FOR_INCREASE = 20; // Increase after consistent success

        public AdaptiveRateLimiter(Logger logger)
        {
            _logger = logger;
            _endpointLimits = new ConcurrentDictionary<string, EndpointRateLimit>();
            _globalSemaphore = new SemaphoreSlim(1, 1);
        }

        public async Task<bool> WaitIfNeededAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            var limit = _endpointLimits.GetOrAdd(endpoint, _ => new EndpointRateLimit
            {
                RequestsPerMinute = DEFAULT_REQUESTS_PER_MINUTE,
                LastRequest = DateTime.MinValue,
                ConsecutiveSuccesses = 0,
                ConsecutiveErrors = 0
            });

            await _globalSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var now = DateTime.UtcNow;
                var timeSinceLastRequest = now - limit.LastRequest;
                var minInterval = TimeSpan.FromMilliseconds(60000.0 / limit.RequestsPerMinute);

                if (timeSinceLastRequest < minInterval)
                {
                    var delay = minInterval - timeSinceLastRequest;
                    _logger.Debug("Rate limiting {Endpoint}: waiting {Delay}ms", endpoint, delay.TotalMilliseconds);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }

                limit.LastRequest = DateTime.UtcNow;
                limit.TotalRequests++;
                return true;
            }
            finally
            {
                _globalSemaphore.Release();
            }
        }

        public void RecordResponse(string endpoint, HttpResponseMessage response)
        {
            var limit = _endpointLimits.GetOrAdd(endpoint, _ => new EndpointRateLimit
            {
                RequestsPerMinute = DEFAULT_REQUESTS_PER_MINUTE,
                LastRequest = DateTime.MinValue,
                ConsecutiveSuccesses = 0,
                ConsecutiveErrors = 0
            });

            lock (_lock)
            {
                // Always increment total requests regardless of response type
                limit.TotalRequests++;
                
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    HandleRateLimitResponse(endpoint, limit);
                }
                else if (response.StatusCode == HttpStatusCode.Unauthorized && limit.ConsecutiveErrors > 2)
                {
                    // Qobuz sometimes returns 401 for rate limits
                    HandleSoftRateLimit(endpoint, limit);
                }
                else if (!response.IsSuccessStatusCode)
                {
                    HandleErrorResponse(endpoint, limit);
                }
                else
                {
                    HandleSuccessResponse(endpoint, limit);
                }
            }
        }

        private void HandleRateLimitResponse(string endpoint, EndpointRateLimit limit)
        {
            limit.ConsecutiveErrors = 0;
            limit.ConsecutiveSuccesses = 0;
            limit.RateLimitHits++;

            var oldLimit = limit.RequestsPerMinute;
            limit.RequestsPerMinute = Math.Max(MIN_REQUESTS_PER_MINUTE, 
                (int)(limit.RequestsPerMinute * RATE_REDUCTION_FACTOR));

            _logger.Warn(
                "Rate limit hit for {0}. Reducing from {1} to {2} requests/min",
                endpoint, oldLimit, limit.RequestsPerMinute);
        }

        private void HandleSoftRateLimit(string endpoint, EndpointRateLimit limit)
        {
            var oldLimit = limit.RequestsPerMinute;
            limit.RequestsPerMinute = Math.Max(MIN_REQUESTS_PER_MINUTE,
                (int)(limit.RequestsPerMinute * 0.85)); // Less aggressive reduction

            _logger.Warn(
                "Possible rate limit (401) for {0}. Reducing from {1} to {2} requests/min",
                endpoint, oldLimit, limit.RequestsPerMinute);

            limit.ConsecutiveErrors = 0;
        }

        private void HandleErrorResponse(string endpoint, EndpointRateLimit limit)
        {
            limit.ConsecutiveErrors++;
            limit.ConsecutiveSuccesses = 0;
            limit.TotalErrors++;

            if (limit.ConsecutiveErrors >= 5)
            {
                // Too many errors, might be rate related
                var oldLimit = limit.RequestsPerMinute;
                limit.RequestsPerMinute = Math.Max(MIN_REQUESTS_PER_MINUTE,
                    (int)(limit.RequestsPerMinute * 0.9));

                _logger.Warn(
                    "Multiple errors for {0}. Reducing from {1} to {2} requests/min",
                    endpoint, oldLimit, limit.RequestsPerMinute);
            }
        }

        private void HandleSuccessResponse(string endpoint, EndpointRateLimit limit)
        {
            limit.ConsecutiveSuccesses++;
            limit.ConsecutiveErrors = 0;
            limit.SuccessfulRequests++;

            // Gradually increase rate if consistently successful
            if (limit.ConsecutiveSuccesses >= SUCCESS_THRESHOLD_FOR_INCREASE && 
                limit.RequestsPerMinute < MAX_REQUESTS_PER_MINUTE)
            {
                var oldLimit = limit.RequestsPerMinute;
                limit.RequestsPerMinute = Math.Min(MAX_REQUESTS_PER_MINUTE,
                    (int)(limit.RequestsPerMinute * RATE_INCREASE_FACTOR));

                _logger.Info(
                    "Increasing rate limit for {0} from {1} to {2} requests/min",
                    endpoint, oldLimit, limit.RequestsPerMinute);

                limit.ConsecutiveSuccesses = 0; // Reset counter
            }
        }

        public void RecordResponse(string endpoint, HttpResponse response)
        {
            var limit = _endpointLimits.GetOrAdd(endpoint, _ => new EndpointRateLimit
            {
                RequestsPerMinute = DEFAULT_REQUESTS_PER_MINUTE,
                LastRequest = DateTime.MinValue,
                ConsecutiveSuccesses = 0,
                ConsecutiveErrors = 0
            });

            lock (_lock)
            {
                // Always increment total requests regardless of response type
                limit.TotalRequests++;
                
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    HandleRateLimitResponse(endpoint, limit);
                }
                else if (response.StatusCode == HttpStatusCode.Unauthorized && limit.ConsecutiveErrors > 2)
                {
                    // Qobuz sometimes returns 401 for rate limits
                    HandleSoftRateLimit(endpoint, limit);
                }
                else if (response.HasHttpError)
                {
                    HandleErrorResponse(endpoint, limit);
                }
                else
                {
                    HandleSuccessResponse(endpoint, limit);
                }
            }
        }

        public int GetCurrentLimit(string endpoint)
        {
            return _endpointLimits.TryGetValue(endpoint, out var limit) 
                ? limit.RequestsPerMinute 
                : DEFAULT_REQUESTS_PER_MINUTE;
        }

        public RateLimitStats GetStats()
        {
            var stats = new RateLimitStats();
            
            foreach (var kvp in _endpointLimits)
            {
                var limit = kvp.Value;
                stats.EndpointStats[kvp.Key] = new EndpointStats
                {
                    CurrentLimit = limit.RequestsPerMinute,
                    TotalRequests = limit.TotalRequests,
                    SuccessfulRequests = limit.SuccessfulRequests,
                    TotalErrors = limit.TotalErrors,
                    RateLimitHits = limit.RateLimitHits,
                    SuccessRate = limit.TotalRequests > 0 
                        ? (double)limit.SuccessfulRequests / limit.TotalRequests 
                        : 0
                };
            }

            return stats;
        }

        private class EndpointRateLimit
        {
            public int RequestsPerMinute { get; set; }
            public DateTime LastRequest { get; set; }
            public int ConsecutiveSuccesses { get; set; }
            public int ConsecutiveErrors { get; set; }
            public long TotalRequests { get; set; }
            public long SuccessfulRequests { get; set; }
            public long TotalErrors { get; set; }
            public long RateLimitHits { get; set; }
        }
    }

    public class RateLimitStats
    {
        public Dictionary<string, EndpointStats> EndpointStats { get; set; } = new();
    }

    public class EndpointStats
    {
        public int CurrentLimit { get; set; }
        public long TotalRequests { get; set; }
        public long SuccessfulRequests { get; set; }
        public long TotalErrors { get; set; }
        public long RateLimitHits { get; set; }
        public double SuccessRate { get; set; }
    }
}