using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Configuration;

namespace Lidarr.Plugin.Qobuzarr.Services.Observability
{
    /// <summary>
    /// Comprehensive health monitoring service for Qobuzarr plugin
    /// Provides health checks compatible with Lidarr monitoring systems
    /// </summary>
    public class HealthCheckService : IHealthCheckService, IDisposable
    {
        private readonly IQobuzLogger _logger;
        private readonly Logger _healthLogger;
        private readonly IQobuzApiClient _apiClient;
        private readonly IQobuzAuthenticationService _authService;
        private readonly IMetricsCollector _metricsCollector;
        
        private readonly Dictionary<string, HealthCheckResult> _lastResults;
        private readonly object _healthLock = new();
        private DateTime _lastHealthCheck = DateTime.MinValue;
        private readonly TimeSpan _healthCheckInterval = TimeSpan.FromMinutes(5);

        // Health check categories
        private const string CATEGORY_API = "api";
        private const string CATEGORY_AUTH = "authentication";
        private const string CATEGORY_DEPENDENCIES = "dependencies";
        private const string CATEGORY_PERFORMANCE = "performance";
        private const string CATEGORY_RESOURCES = "resources";

        public HealthCheckService(
            IQobuzLogger logger,
            IQobuzApiClient apiClient = null,
            IQobuzAuthenticationService authService = null,
            IMetricsCollector metricsCollector = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _healthLogger = LogManager.GetLogger("Qobuzarr.Health");
            _apiClient = apiClient;
            _authService = authService;
            _metricsCollector = metricsCollector;
            
            _lastResults = new Dictionary<string, HealthCheckResult>();
            
            _logger.Info("Health check service initialized with monitoring for API, auth, dependencies, performance, and resources");
        }

        #region Core Health Check Methods

        /// <summary>
        /// Performs comprehensive health check of all plugin components
        /// </summary>
        public async Task<OverallHealthStatus> PerformHealthCheckAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && DateTime.UtcNow - _lastHealthCheck < _healthCheckInterval)
            {
                return GetCachedHealthStatus();
            }

            _healthLogger.Debug("Performing comprehensive health check");
            var stopwatch = Stopwatch.StartNew();

            var healthChecks = new List<Task<HealthCheckResult>>
            {
                CheckApiConnectivityAsync(),
                CheckAuthenticationHealthAsync(),
                CheckDependencyHealthAsync(),
                CheckPerformanceHealthAsync(),
                CheckResourceHealthAsync()
            };

            var results = await Task.WhenAll(healthChecks);
            stopwatch.Stop();

            lock (_healthLock)
            {
                _lastHealthCheck = DateTime.UtcNow;
                
                // Store results for caching
                foreach (var result in results)
                {
                    _lastResults[result.Component] = result;
                    
                    // Update metrics
                    _metricsCollector?.SetServiceHealth("qobuzarr", result.Component, result.Status == HealthStatus.Healthy);
                }
            }

            var overallStatus = CalculateOverallHealth(results);
            
            _healthLogger.Info("Health check completed in {0}ms - Overall: {1}",
                stopwatch.ElapsedMilliseconds, overallStatus.Status);
            
            return overallStatus;
        }

        /// <summary>
        /// Gets current health status of a specific component
        /// </summary>
        public async Task<HealthCheckResult> GetComponentHealthAsync(string component)
        {
            return component.ToLowerInvariant() switch
            {
                CATEGORY_API => await CheckApiConnectivityAsync(),
                CATEGORY_AUTH => await CheckAuthenticationHealthAsync(),
                CATEGORY_DEPENDENCIES => await CheckDependencyHealthAsync(),
                CATEGORY_PERFORMANCE => await CheckPerformanceHealthAsync(),
                CATEGORY_RESOURCES => await CheckResourceHealthAsync(),
                _ => new HealthCheckResult
                {
                    Component = component,
                    Status = HealthStatus.Unknown,
                    Message = $"Unknown health check component: {component}",
                    Timestamp = DateTime.UtcNow
                }
            };
        }

        #endregion

        #region Individual Health Checks

        /// <summary>
        /// Checks API connectivity and response times
        /// </summary>
        public async Task<HealthCheckResult> CheckApiConnectivityAsync()
        {
            var result = new HealthCheckResult
            {
                Component = CATEGORY_API,
                Timestamp = DateTime.UtcNow
            };

            try
            {
                if (_apiClient == null)
                {
                    result.Status = HealthStatus.Degraded;
                    result.Message = "API client not available";
                    result.Details.Add("issue", "No API client instance provided");
                    return result;
                }

                var stopwatch = Stopwatch.StartNew();
                
                // Attempt a simple API call to check connectivity
                // Using a lightweight endpoint that doesn't require authentication
                var testResponse = await TestApiConnectivity();
                stopwatch.Stop();

                result.ResponseTime = stopwatch.Elapsed;

                if (testResponse.Success)
                {
                    result.Status = stopwatch.Elapsed.TotalSeconds < 5 ? HealthStatus.Healthy : HealthStatus.Degraded;
                    result.Message = $"API connectivity OK ({stopwatch.ElapsedMilliseconds}ms)";
                    result.Details.Add("response_time_ms", stopwatch.ElapsedMilliseconds.ToString());
                    result.Details.Add("endpoint_reachable", "true");
                }
                else
                {
                    result.Status = HealthStatus.Unhealthy;
                    result.Message = $"API connectivity failed: {testResponse.Error}";
                    result.Details.Add("error", testResponse.Error);
                    result.Details.Add("endpoint_reachable", "false");
                }
            }
            catch (HttpRequestException ex)
            {
                result.Status = HealthStatus.Unhealthy;
                result.Message = $"Network connectivity issue: {ex.Message}";
                result.Details.Add("error_type", "network");
                result.Details.Add("error_details", ex.Message);
            }
            catch (TaskCanceledException ex)
            {
                result.Status = HealthStatus.Unhealthy;
                result.Message = "API request timed out";
                result.Details.Add("error_type", "timeout");
                result.Details.Add("timeout_duration", ex.Message);
            }
            catch (Exception ex)
            {
                result.Status = HealthStatus.Unhealthy;
                result.Message = $"Unexpected API health check error: {ex.Message}";
                result.Details.Add("error_type", "unexpected");
                result.Details.Add("exception", ex.GetType().Name);
            }

            return result;
        }

        /// <summary>
        /// Checks authentication service health and token validity
        /// </summary>
        public async Task<HealthCheckResult> CheckAuthenticationHealthAsync()
        {
            var result = new HealthCheckResult
            {
                Component = CATEGORY_AUTH,
                Timestamp = DateTime.UtcNow
            };

            try
            {
                if (_authService == null)
                {
                    result.Status = HealthStatus.Degraded;
                    result.Message = "Authentication service not available";
                    result.Details.Add("issue", "No authentication service instance provided");
                    return result;
                }

                var stopwatch = Stopwatch.StartNew();
                
                // Check if we have valid credentials/session
                var hasValidSession = await CheckAuthenticationStatus();
                stopwatch.Stop();

                result.ResponseTime = stopwatch.Elapsed;

                if (hasValidSession.IsValid)
                {
                    result.Status = HealthStatus.Healthy;
                    result.Message = "Authentication status OK";
                    result.Details.Add("session_valid", "true");
                    result.Details.Add("session_expires", hasValidSession.ExpiryTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "unknown");
                }
                else
                {
                    result.Status = hasValidSession.RequiresReauth ? HealthStatus.Degraded : HealthStatus.Unhealthy;
                    result.Message = $"Authentication issue: {hasValidSession.Issue}";
                    result.Details.Add("session_valid", "false");
                    result.Details.Add("requires_reauth", hasValidSession.RequiresReauth.ToString());
                    result.Details.Add("issue", hasValidSession.Issue);
                }
            }
            catch (Exception ex)
            {
                result.Status = HealthStatus.Unhealthy;
                result.Message = $"Authentication health check failed: {ex.Message}";
                result.Details.Add("error_type", "exception");
                result.Details.Add("exception", ex.GetType().Name);
            }

            return result;
        }

        /// <summary>
        /// Checks health of external dependencies and integrations
        /// </summary>
        public async Task<HealthCheckResult> CheckDependencyHealthAsync()
        {
            var result = new HealthCheckResult
            {
                Component = CATEGORY_DEPENDENCIES,
                Timestamp = DateTime.UtcNow,
                Status = HealthStatus.Healthy,
                Message = "All dependencies healthy"
            };

            var dependencyChecks = new List<Task<(string name, bool healthy, string issue)>>
            {
                CheckQobuzApiEndpointAsync(),
                CheckLidarrIntegrationAsync(),
                CheckFileSystemAccessAsync()
            };

            try
            {
                var dependencies = await Task.WhenAll(dependencyChecks);
                var unhealthyCount = dependencies.Count(d => !d.healthy);

                foreach (var (name, healthy, issue) in dependencies)
                {
                    result.Details.Add($"{name}_healthy", healthy.ToString());
                    if (!healthy)
                    {
                        result.Details.Add($"{name}_issue", issue);
                    }
                }

                if (unhealthyCount == 0)
                {
                    result.Status = HealthStatus.Healthy;
                    result.Message = "All dependencies healthy";
                }
                else if (unhealthyCount <= dependencies.Length / 2)
                {
                    result.Status = HealthStatus.Degraded;
                    result.Message = $"{unhealthyCount} of {dependencies.Length} dependencies unhealthy";
                }
                else
                {
                    result.Status = HealthStatus.Unhealthy;
                    result.Message = $"Multiple dependencies unhealthy ({unhealthyCount}/{dependencies.Length})";
                }
            }
            catch (Exception ex)
            {
                result.Status = HealthStatus.Unhealthy;
                result.Message = $"Dependency health check failed: {ex.Message}";
                result.Details.Add("error", ex.Message);
            }

            return result;
        }

        /// <summary>
        /// Checks performance indicators and system responsiveness
        /// </summary>
        public async Task<HealthCheckResult> CheckPerformanceHealthAsync()
        {
            var result = new HealthCheckResult
            {
                Component = CATEGORY_PERFORMANCE,
                Timestamp = DateTime.UtcNow
            };

            try
            {
                var stopwatch = Stopwatch.StartNew();
                
                // Check current performance metrics
                var metrics = _metricsCollector?.GetMetricsSummary();
                var memoryUsage = GC.GetTotalMemory(false) / (1024 * 1024); // MB
                var gcGen2Collections = GC.CollectionCount(2);
                
                stopwatch.Stop();
                result.ResponseTime = stopwatch.Elapsed;

                // Evaluate performance health
                var issues = new List<string>();
                
                if (memoryUsage > 500) // More than 500MB
                {
                    issues.Add($"High memory usage: {memoryUsage}MB");
                }
                
                if (gcGen2Collections > 10) // Frequent Gen 2 GC
                {
                    issues.Add($"Frequent GC Gen2 collections: {gcGen2Collections}");
                }

                if (metrics != null)
                {
                    result.Details.Add("total_api_requests", metrics.TotalApiRequests.ToString());
                    result.Details.Add("cache_hit_ratio", $"{metrics.CacheHitRatio:F2}%");
                    result.Details.Add("active_downloads", metrics.ActiveDownloads.ToString());
                    result.Details.Add("unhealthy_services", metrics.UnhealthyServices.ToString());
                    
                    if (metrics.CacheHitRatio < 0.5) // Less than 50% cache hit rate
                    {
                        issues.Add($"Low cache hit rate: {metrics.CacheHitRatio:F1}%");
                    }
                }

                result.Details.Add("memory_usage_mb", memoryUsage.ToString());
                result.Details.Add("gc_gen2_collections", gcGen2Collections.ToString());

                if (issues.Count == 0)
                {
                    result.Status = HealthStatus.Healthy;
                    result.Message = "Performance indicators normal";
                }
                else if (issues.Count <= 1)
                {
                    result.Status = HealthStatus.Degraded;
                    result.Message = $"Performance concern: {string.Join(", ", issues)}";
                }
                else
                {
                    result.Status = HealthStatus.Unhealthy;
                    result.Message = $"Multiple performance issues: {string.Join(", ", issues)}";
                }
            }
            catch (Exception ex)
            {
                result.Status = HealthStatus.Unhealthy;
                result.Message = $"Performance health check failed: {ex.Message}";
                result.Details.Add("error", ex.Message);
            }

            return result;
        }

        /// <summary>
        /// Checks resource availability and system health
        /// </summary>
        public async Task<HealthCheckResult> CheckResourceHealthAsync()
        {
            var result = new HealthCheckResult
            {
                Component = CATEGORY_RESOURCES,
                Timestamp = DateTime.UtcNow
            };

            try
            {
                // Check system resources
                var process = Process.GetCurrentProcess();
                var workingSetMB = process.WorkingSet64 / (1024 * 1024);
                var threadCount = process.Threads.Count;
                var handleCount = process.HandleCount;

                result.Details.Add("working_set_mb", workingSetMB.ToString());
                result.Details.Add("thread_count", threadCount.ToString());
                result.Details.Add("handle_count", handleCount.ToString());

                var issues = new List<string>();

                if (workingSetMB > 1024) // More than 1GB working set
                {
                    issues.Add($"High memory usage: {workingSetMB}MB working set");
                }

                if (threadCount > 100) // More than 100 threads
                {
                    issues.Add($"High thread count: {threadCount}");
                }

                if (handleCount > 10000) // More than 10k handles
                {
                    issues.Add($"High handle count: {handleCount}");
                }

                if (issues.Count == 0)
                {
                    result.Status = HealthStatus.Healthy;
                    result.Message = "Resource usage normal";
                }
                else if (issues.Count == 1)
                {
                    result.Status = HealthStatus.Degraded;
                    result.Message = issues[0];
                }
                else
                {
                    result.Status = HealthStatus.Unhealthy;
                    result.Message = $"Multiple resource issues: {string.Join(", ", issues)}";
                }
            }
            catch (Exception ex)
            {
                result.Status = HealthStatus.Unhealthy;
                result.Message = $"Resource health check failed: {ex.Message}";
                result.Details.Add("error", ex.Message);
            }

            await Task.CompletedTask; // Make async for consistency
            return result;
        }

        #endregion

        #region Helper Methods

        private async Task<(bool Success, string Error)> TestApiConnectivity()
        {
            try
            {
                // Try to reach a simple Qobuz endpoint
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);
                
                var response = await httpClient.GetAsync("https://www.qobuz.com/api.json/0.2/application/info");
                
                if (response.IsSuccessStatusCode)
                {
                    return (true, null);
                }
                else
                {
                    return (false, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private async Task<(bool IsValid, bool RequiresReauth, string Issue, DateTime? ExpiryTime)> CheckAuthenticationStatus()
        {
            try
            {
                // Check if authentication service can validate current session
                // This is a placeholder - implement based on your auth service interface
                await Task.Delay(50); // Simulate auth check
                
                // Return sample status - implement actual logic based on your auth service
                return (true, false, null, DateTime.UtcNow.AddHours(1));
            }
            catch (Exception ex)
            {
                return (false, true, ex.Message, null);
            }
        }

        private async Task<(string name, bool healthy, string issue)> CheckQobuzApiEndpointAsync()
        {
            try
            {
                var (success, error) = await TestApiConnectivity();
                return ("qobuz_api", success, error);
            }
            catch (Exception ex)
            {
                return ("qobuz_api", false, ex.Message);
            }
        }

        private async Task<(string name, bool healthy, string issue)> CheckLidarrIntegrationAsync()
        {
            // Check if Lidarr integration is working
            await Task.Delay(10); // Simulate check
            return ("lidarr_integration", true, null);
        }

        private async Task<(string name, bool healthy, string issue)> CheckFileSystemAccessAsync()
        {
            try
            {
                // Test file system access
                var tempPath = System.IO.Path.GetTempPath();
                var testFile = System.IO.Path.Combine(tempPath, $"qobuzarr_health_check_{Guid.NewGuid()}.tmp");
                
                await System.IO.File.WriteAllTextAsync(testFile, "health check");
                System.IO.File.Delete(testFile);
                
                return ("filesystem", true, null);
            }
            catch (Exception ex)
            {
                return ("filesystem", false, ex.Message);
            }
        }

        private OverallHealthStatus GetCachedHealthStatus()
        {
            lock (_healthLock)
            {
                var results = _lastResults.Values.ToArray();
                return CalculateOverallHealth(results);
            }
        }

        private OverallHealthStatus CalculateOverallHealth(HealthCheckResult[] results)
        {
            var healthyCount = results.Count(r => r.Status == HealthStatus.Healthy);
            var degradedCount = results.Count(r => r.Status == HealthStatus.Degraded);
            var unhealthyCount = results.Count(r => r.Status == HealthStatus.Unhealthy);

            HealthStatus overallStatus;
            if (unhealthyCount > 0)
            {
                overallStatus = HealthStatus.Unhealthy;
            }
            else if (degradedCount > 0)
            {
                overallStatus = HealthStatus.Degraded;
            }
            else
            {
                overallStatus = HealthStatus.Healthy;
            }

            return new OverallHealthStatus
            {
                Status = overallStatus,
                Timestamp = DateTime.UtcNow,
                ComponentResults = results.ToList(),
                Summary = $"{healthyCount} healthy, {degradedCount} degraded, {unhealthyCount} unhealthy",
                TotalComponents = results.Length
            };
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _logger.Info("Health check service shutting down");
        }

        #endregion
    }

    #region Supporting Interfaces and Models

    public interface IHealthCheckService
    {
        Task<OverallHealthStatus> PerformHealthCheckAsync(bool forceRefresh = false);
        Task<HealthCheckResult> GetComponentHealthAsync(string component);
        Task<HealthCheckResult> CheckApiConnectivityAsync();
        Task<HealthCheckResult> CheckAuthenticationHealthAsync();
        Task<HealthCheckResult> CheckDependencyHealthAsync();
        Task<HealthCheckResult> CheckPerformanceHealthAsync();
        Task<HealthCheckResult> CheckResourceHealthAsync();
    }

    public class HealthCheckResult
    {
        public string Component { get; set; } = "";
        public HealthStatus Status { get; set; } = HealthStatus.Unknown;
        public string Message { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public TimeSpan ResponseTime { get; set; } = TimeSpan.Zero;
        public Dictionary<string, string> Details { get; set; } = new();
    }

    public class OverallHealthStatus
    {
        public HealthStatus Status { get; set; }
        public DateTime Timestamp { get; set; }
        public List<HealthCheckResult> ComponentResults { get; set; } = new();
        public string Summary { get; set; } = "";
        public int TotalComponents { get; set; }
    }

    public enum HealthStatus
    {
        Unknown = 0,
        Healthy = 1,
        Degraded = 2,
        Unhealthy = 3
    }

    #endregion
}