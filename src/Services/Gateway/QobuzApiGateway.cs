using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Services.Deduplication;
using Lidarr.Plugin.Qobuzarr.Services.Monitoring;
using Lidarr.Plugin.Qobuzarr.Services.Resilience;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Services.Gateway
{
    /// <summary>
    /// Facade implementation that coordinates resilience, deduplication, and monitoring for API calls
    /// </summary>
    public class QobuzApiGateway : IQobuzApiGateway
    {
        private readonly IResilienceService _resilienceService;
        private readonly IRequestDeduplicationService _deduplicationService;
        private readonly ISearchMetricsCollector _metricsCollector;
        private readonly Logger _logger;
        private bool _disposed;

        public QobuzApiGateway(
            IResilienceService resilienceService,
            IRequestDeduplicationService deduplicationService,
            ISearchMetricsCollector metricsCollector,
            Logger logger)
        {
            _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
            _deduplicationService = deduplicationService ?? throw new ArgumentNullException(nameof(deduplicationService));
            _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<T> ExecuteAsync<T>(string operationKey, Func<Task<T>> operation, TimeSpan? cacheDuration = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(QobuzApiGateway));
                
            if (string.IsNullOrWhiteSpace(operationKey))
                throw new ArgumentNullException(nameof(operationKey));
                
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            var stopwatch = Stopwatch.StartNew();
            var metric = _metricsCollector.StartSearch(operationKey, "Gateway");
            
            try
            {
                _logger.Debug("Executing operation: {0}", operationKey);
                
                // Layer 1: Deduplication - prevent duplicate in-flight requests
                var result = await _deduplicationService.DeduplicateRequestAsync(
                    operationKey,
                    async () =>
                    {
                        // Layer 2: Resilience - apply retry, circuit breaker, timeout policies
                        return await _resilienceService.ExecuteWithResilienceAsync(
                            operation,
                            operationKey
                        ).ConfigureAwait(false);
                    },
                    cacheDuration
                ).ConfigureAwait(false);
                
                stopwatch.Stop();
                _metricsCollector.CompleteSearch(metric, true, 1, 1);
                
                _logger.Debug("Operation {0} completed successfully in {1}ms", 
                    operationKey, stopwatch.ElapsedMilliseconds);
                
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _metricsCollector.RecordError(metric, ex);
                
                _logger.Error(ex, "Operation {0} failed after {1}ms", 
                    operationKey, stopwatch.ElapsedMilliseconds);
                    
                throw;
            }
        }

        public SystemHealth GetHealthStatus()
        {
            var resilienceStats = _resilienceService.GetStatistics();
            var deduplicationStats = _deduplicationService.GetStatistics();
            
            // Determine health based on failure rates and circuit states
            var failureRate = resilienceStats.TotalRequests > 0 
                ? (double)resilienceStats.FailedRequests / resilienceStats.TotalRequests 
                : 0;
                
            if (resilienceStats.CircuitOpenCount > 5 || failureRate > 0.5)
            {
                return SystemHealth.Unhealthy;
            }
            
            if (resilienceStats.CircuitOpenCount > 0 || failureRate > 0.1)
            {
                return SystemHealth.Degraded;
            }
            
            return SystemHealth.Healthy;
        }

        public GatewayStatistics GetStatistics()
        {
            var resilienceStats = _resilienceService.GetStatistics();
            var deduplicationStats = _deduplicationService.GetStatistics();
            var searchStats = _metricsCollector.GetStatistics();
            
            return new GatewayStatistics
            {
                TotalRequests = resilienceStats.TotalRequests,
                SuccessfulRequests = resilienceStats.SuccessfulRequests,
                FailedRequests = resilienceStats.FailedRequests,
                DuplicatesSaved = deduplicationStats.DuplicatesSaved,
                CircuitBreakerTrips = resilienceStats.CircuitOpenCount,
                SuccessRate = resilienceStats.SuccessRate,
                DeduplicationRate = deduplicationStats.DeduplicationRate,
                AverageResponseTime = searchStats.AverageSearchDuration,
                LastFailure = resilienceStats.LastFailure,
                ActiveCircuits = deduplicationStats.InFlightRequests,
                OpenCircuits = resilienceStats.CircuitOpenCount
            };
        }

        public void ResetCircuit(string operationKey)
        {
            if (string.IsNullOrWhiteSpace(operationKey))
                throw new ArgumentNullException(nameof(operationKey));
                
            _resilienceService.ResetCircuit(operationKey);
            _logger.Info("Circuit reset for operation: {0}", operationKey);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources in reverse order of dependency
                    _metricsCollector?.Dispose();
                    _deduplicationService?.Dispose();
                    _resilienceService?.Dispose();
                    
                    _logger.Info("QobuzApiGateway disposed");
                }
                
                _disposed = true;
            }
        }
    }
}