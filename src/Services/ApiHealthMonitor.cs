using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Monitors API health and handles rate limiting variations
    /// Prevents optimization failures due to API behavioral changes
    /// </summary>
    public class ApiHealthMonitor
    {
        private readonly Logger _logger;
        private readonly Dictionary<string, ApiEndpointHealth> _endpointHealth;
        private readonly object _lockObject = new();
        
        private DateTime _lastHealthCheck = DateTime.MinValue;
        private readonly TimeSpan _healthCheckInterval = TimeSpan.FromMinutes(5);

        public ApiHealthMonitor(Logger logger = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _endpointHealth = new Dictionary<string, ApiEndpointHealth>();
        }

        /// <summary>
        /// Records successful API call for health tracking
        /// </summary>
        public void RecordSuccess(string endpoint, TimeSpan responseTime)
        {
            lock (_lockObject)
            {
                if (!_endpointHealth.TryGetValue(endpoint, out var health))
                {
                    health = new ApiEndpointHealth { Endpoint = endpoint };
                    _endpointHealth[endpoint] = health;
                }

                health.RecordSuccess(responseTime);
            }
        }

        /// <summary>
        /// Records API failure for health tracking
        /// </summary>
        public void RecordFailure(string endpoint, string errorType, Exception exception = null)
        {
            lock (_lockObject)
            {
                if (!_endpointHealth.TryGetValue(endpoint, out var health))
                {
                    health = new ApiEndpointHealth { Endpoint = endpoint };
                    _endpointHealth[endpoint] = health;
                }

                health.RecordFailure(errorType, exception);

                // Log concerning patterns
                if (health.ConsecutiveFailures >= 3)
                {
                    _logger.Warn("⚠️ API DEGRADATION: {0} has {1} consecutive failures", 
                               endpoint, health.ConsecutiveFailures);
                }
            }
        }

        /// <summary>
        /// Gets recommended delay before next API call based on health
        /// </summary>
        public TimeSpan GetRecommendedDelay(string endpoint)
        {
            lock (_lockObject)
            {
                if (!_endpointHealth.TryGetValue(endpoint, out var health))
                {
                    return TimeSpan.Zero; // No history, proceed normally
                }

                // Calculate delay based on recent failures
                if (health.ConsecutiveFailures > 0)
                {
                    var baseDelay = TimeSpan.FromSeconds(Math.Pow(2, health.ConsecutiveFailures));
                    return baseDelay > TimeSpan.FromMinutes(5) ? TimeSpan.FromMinutes(5) : baseDelay;
                }

                // Check if we're hitting rate limits
                if (health.RecentRateLimitHits > 2)
                {
                    return TimeSpan.FromSeconds(30);
                }

                return TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Determines if an endpoint is currently healthy
        /// </summary>
        public bool IsEndpointHealthy(string endpoint)
        {
            lock (_lockObject)
            {
                if (!_endpointHealth.TryGetValue(endpoint, out var health))
                {
                    return true; // Unknown endpoint, assume healthy
                }

                return health.ConsecutiveFailures < 5 && health.SuccessRate > 0.5;
            }
        }

        /// <summary>
        /// Gets health summary for monitoring
        /// </summary>
        public ApiHealthSummary GetHealthSummary()
        {
            lock (_lockObject)
            {
                var healthyEndpoints = _endpointHealth.Values.Count(h => h.SuccessRate > 0.8);
                var degradedEndpoints = _endpointHealth.Values.Count(h => h.SuccessRate > 0.5 && h.SuccessRate <= 0.8);
                var unhealthyEndpoints = _endpointHealth.Values.Count(h => h.SuccessRate <= 0.5);

                return new ApiHealthSummary
                {
                    HealthyEndpoints = healthyEndpoints,
                    DegradedEndpoints = degradedEndpoints,
                    UnhealthyEndpoints = unhealthyEndpoints,
                    TotalEndpoints = _endpointHealth.Count,
                    LastHealthCheck = _lastHealthCheck,
                    OverallHealth = unhealthyEndpoints > 0 ? "Unhealthy" : 
                                   degradedEndpoints > 0 ? "Degraded" : "Healthy"
                };
            }
        }

        /// <summary>
        /// Performs periodic health assessment
        /// </summary>
        public async Task PerformHealthCheckAsync()
        {
            if (DateTime.UtcNow - _lastHealthCheck < _healthCheckInterval)
            {
                return;
            }

            _logger.Debug("🏥 API HEALTH CHECK: Assessing endpoint health");

            lock (_lockObject)
            {
                _lastHealthCheck = DateTime.UtcNow;

                foreach (var health in _endpointHealth.Values)
                {
                    health.UpdateMetrics();
                    
                    if (health.SuccessRate < 0.5 && health.TotalCalls > 10)
                    {
                        _logger.Warn("🏥 UNHEALTHY ENDPOINT: {0} has {1:P1} success rate", 
                                   health.Endpoint, health.SuccessRate);
                    }
                }

                // Clean up old entries
                var cutoffTime = DateTime.UtcNow - TimeSpan.FromHours(24);
                var oldEntries = _endpointHealth.Values
                    .Where(h => h.LastActivity < cutoffTime)
                    .Select(h => h.Endpoint)
                    .ToList();

                foreach (var endpoint in oldEntries)
                {
                    _endpointHealth.Remove(endpoint);
                }

                if (oldEntries.Any())
                {
                    _logger.Debug("🧹 HEALTH CLEANUP: Removed {0} old endpoint entries", oldEntries.Count);
                }
            }
        }
    }

    #region Health Tracking Classes

    public class ApiEndpointHealth
    {
        public string Endpoint { get; set; }
        public int TotalCalls { get; private set; }
        public int SuccessfulCalls { get; private set; }
        public int ConsecutiveFailures { get; private set; }
        public int RecentRateLimitHits { get; private set; }
        public DateTime LastActivity { get; private set; }
        public List<TimeSpan> RecentResponseTimes { get; private set; } = new();
        public Dictionary<string, int> ErrorCounts { get; private set; } = new();

        public double SuccessRate => TotalCalls > 0 ? (double)SuccessfulCalls / TotalCalls : 1.0;
        
        public TimeSpan AverageResponseTime => RecentResponseTimes.Any() 
            ? TimeSpan.FromMilliseconds(RecentResponseTimes.Average(t => t.TotalMilliseconds))
            : TimeSpan.Zero;

        public void RecordSuccess(TimeSpan responseTime)
        {
            TotalCalls++;
            SuccessfulCalls++;
            ConsecutiveFailures = 0;
            LastActivity = DateTime.UtcNow;
            
            RecentResponseTimes.Add(responseTime);
            if (RecentResponseTimes.Count > 50) // Keep last 50 response times
            {
                RecentResponseTimes.RemoveAt(0);
            }
        }

        public void RecordFailure(string errorType, Exception exception = null)
        {
            TotalCalls++;
            ConsecutiveFailures++;
            LastActivity = DateTime.UtcNow;

            if (errorType.Contains("rate") || errorType.Contains("limit") || errorType.Contains("429"))
            {
                RecentRateLimitHits++;
            }

            if (!ErrorCounts.ContainsKey(errorType))
            {
                ErrorCounts[errorType] = 0;
            }
            ErrorCounts[errorType]++;
        }

        public void UpdateMetrics()
        {
            // Reset rate limit hits periodically (sliding window)
            if (DateTime.UtcNow - LastActivity > TimeSpan.FromHours(1))
            {
                RecentRateLimitHits = Math.Max(0, RecentRateLimitHits - 1);
            }
        }
    }

    public class ApiHealthSummary
    {
        public int HealthyEndpoints { get; set; }
        public int DegradedEndpoints { get; set; }
        public int UnhealthyEndpoints { get; set; }
        public int TotalEndpoints { get; set; }
        public DateTime LastHealthCheck { get; set; }
        public string OverallHealth { get; set; }
        
        public double HealthPercentage => TotalEndpoints > 0 ? 
            (double)HealthyEndpoints / TotalEndpoints * 100 : 100;
    }

    #endregion
}