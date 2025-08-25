using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Services.Interfaces
{
    /// <summary>
    /// Interface for comprehensive health monitoring of plugin services.
    /// </summary>
    /// <remarks>
    /// This interface provides health check capabilities for all plugin components
    /// including API connectivity, authentication, storage, and service availability.
    /// 
    /// Key Features:
    /// - Overall plugin health assessment
    /// - Individual component health checks
    /// - Dependency health validation
    /// - Performance health monitoring
    /// - Health trend tracking
    /// 
    /// Health checks help ensure plugin reliability and provide diagnostics
    /// for troubleshooting operational issues.
    /// </remarks>
    public interface IHealthCheckService
    {
        /// <summary>
        /// Performs a comprehensive health check of all plugin components.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Overall health check result</returns>
        Task<OverallHealthResult> CheckOverallHealthAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks the health of the Qobuz API connection.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>API health check result</returns>
        Task<ComponentHealthResult> CheckApiHealthAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks the health of authentication services.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Authentication health check result</returns>
        Task<ComponentHealthResult> CheckAuthenticationHealthAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks the health of storage systems (database, disk).
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Storage health check result</returns>
        Task<ComponentHealthResult> CheckStorageHealthAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks the health of quality services.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Quality services health check result</returns>
        Task<ComponentHealthResult> CheckQualityServicesHealthAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a quick health status without detailed checks.
        /// </summary>
        /// <returns>Quick health status</returns>
        HealthStatus GetQuickHealthStatus();

        /// <summary>
        /// Gets health check history.
        /// </summary>
        /// <param name="maxResults">Maximum number of historical results</param>
        /// <returns>List of historical health check results</returns>
        List<HealthCheckHistoryEntry> GetHealthHistory(int maxResults = 10);

        /// <summary>
        /// Starts continuous health monitoring.
        /// </summary>
        /// <param name="interval">Check interval</param>
        /// <param name="cancellationToken">Token to stop monitoring</param>
        /// <returns>Task representing the monitoring operation</returns>
        Task StartContinuousMonitoringAsync(System.TimeSpan interval, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Overall plugin health result.
    /// </summary>
    public class OverallHealthResult
    {
        public HealthStatus Status { get; set; }
        public List<ComponentHealthResult> ComponentResults { get; set; } = new();
        public string Summary { get; set; }
        public System.TimeSpan CheckDuration { get; set; }
        public System.DateTime CheckTime { get; set; }
        public int HealthyComponents { get; set; }
        public int UnhealthyComponents { get; set; }
        public List<string> Warnings { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// Individual component health result.
    /// </summary>
    public class ComponentHealthResult
    {
        public string ComponentName { get; set; }
        public HealthStatus Status { get; set; }
        public string StatusMessage { get; set; }
        public System.TimeSpan ResponseTime { get; set; }
        public System.DateTime CheckTime { get; set; }
        public Dictionary<string, object> Metrics { get; set; } = new();
        public string Error { get; set; }
        public bool IsEssential { get; set; }
    }

    /// <summary>
    /// Health check history entry.
    /// </summary>
    public class HealthCheckHistoryEntry
    {
        public System.DateTime CheckTime { get; set; }
        public HealthStatus Status { get; set; }
        public System.TimeSpan CheckDuration { get; set; }
        public string Summary { get; set; }
        public int ComponentCount { get; set; }
        public int HealthyComponents { get; set; }
    }

    /// <summary>
    /// Health status levels.
    /// </summary>
    public enum HealthStatus
    {
        Unknown = 0,
        Healthy = 1,
        Degraded = 2,
        Unhealthy = 3,
        Critical = 4
    }
}