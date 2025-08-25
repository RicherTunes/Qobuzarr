using System.Threading;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Services.Interfaces
{
    /// <summary>
    /// Interface for diagnostic API client with enhanced troubleshooting capabilities.
    /// </summary>
    /// <remarks>
    /// This interface extends the standard API client with diagnostic-specific features
    /// for troubleshooting, testing, and monitoring API health.
    /// 
    /// Key Features:
    /// - All standard API client operations
    /// - Connectivity testing without authentication
    /// - Comprehensive health checks across multiple endpoints  
    /// - Detailed diagnostic metrics and reporting
    /// - Performance measurement and latency tracking
    /// 
    /// WARNING: Implementations typically bypass rate limiting and caching,
    /// making them unsuitable for production use but ideal for diagnostics.
    /// </remarks>
    public interface IQobuzDiagnosticApiClient : IQobuzApiClient
    {
        /// <summary>
        /// Tests API connectivity and authentication without making actual content requests.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Connectivity test results with latency measurements</returns>
        Task<ApiConnectivityTestResult> TestConnectivityAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs a comprehensive API health check across multiple endpoints.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Health check results with per-endpoint metrics</returns>
        Task<ApiHealthCheckResult> PerformHealthCheckAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets detailed diagnostic metrics for all endpoints that have been used.
        /// </summary>
        /// <returns>Comprehensive diagnostic report with performance metrics</returns>
        DiagnosticReport GetDiagnosticReport();
    }

    // Forward declaration of diagnostic result types from implementation
    // These are defined in the concrete implementation files
    public class ApiConnectivityTestResult
    {
        public bool Success { get; set; }
        public bool IsConnectable { get; set; }
        public bool IsAuthenticated { get; set; }
        public string Error { get; set; }
    }

    public class ApiHealthCheckResult 
    {
        public bool Success { get; set; }
        public double AverageResponseTime { get; set; }
    }

    public class DiagnosticReport
    {
        public long TotalRequests { get; set; }
        public long TotalErrors { get; set; }
        public double ErrorRate { get; set; }
    }
}