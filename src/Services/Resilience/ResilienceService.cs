using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Lidarr.Plugin.Qobuzarr.Services.Resilience
{
    /// <summary>
    /// Implementation of resilience patterns using Polly library
    /// </summary>
    public class ResilienceService : IResilienceService
    {
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<string, IAsyncPolicy> _policies;
        private readonly ConcurrentDictionary<string, CircuitState> _circuitStates;
        private readonly ResilienceStatistics _statistics;
        private readonly object _statsLock = new object();

        public ResilienceService(Logger logger)
        {
            _logger = logger;
            _policies = new ConcurrentDictionary<string, IAsyncPolicy>();
            _circuitStates = new ConcurrentDictionary<string, CircuitState>();
            _statistics = new ResilienceStatistics();
        }

        public async Task<T> ExecuteWithResilienceAsync<T>(Func<Task<T>> action, string operationKey)
        {
            var policy = GetOrCreatePolicy(operationKey);
            
            try
            {
                IncrementTotalRequests();
                
                var result = await policy.ExecuteAsync(async () =>
                {
                    return await action().ConfigureAwait(false);
                }).ConfigureAwait(false);
                
                IncrementSuccessfulRequests();
                return result;
            }
            catch (Exception ex)
            {
                IncrementFailedRequests();
                UpdateLastFailure();
                
                _logger.Error(ex, "Operation {0} failed after resilience attempts", operationKey);
                throw;
            }
        }

        public async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, int retryCount = 3)
        {
            var retryPolicy = Policy
                .Handle<Exception>(ex => !(ex is TaskCanceledException))
                .WaitAndRetryAsync(
                    retryCount,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Exponential backoff
                    onRetry: (exception, timeSpan, retry, context) =>
                    {
                        IncrementRetriedRequests();
                        _logger.Warn("Retry {0} after {1}s due to: {2}", 
                            retry, timeSpan.TotalSeconds, exception.Message);
                    });

            try
            {
                IncrementTotalRequests();
                var result = await retryPolicy.ExecuteAsync(action).ConfigureAwait(false);
                IncrementSuccessfulRequests();
                return result;
            }
            catch (Exception ex)
            {
                IncrementFailedRequests();
                UpdateLastFailure();
                throw;
            }
        }

        public bool IsCircuitOpen(string operationKey)
        {
            return _circuitStates.TryGetValue(operationKey, out var state) && 
                   state.IsOpen;
        }

        public void ResetCircuit(string operationKey)
        {
            if (_circuitStates.TryRemove(operationKey, out _))
            {
                _policies.TryRemove(operationKey, out _);
                _logger.Info("Circuit breaker reset for operation: {0}", operationKey);
            }
        }

        public ResilienceStatistics GetStatistics()
        {
            lock (_statsLock)
            {
                return new ResilienceStatistics
                {
                    TotalRequests = _statistics.TotalRequests,
                    SuccessfulRequests = _statistics.SuccessfulRequests,
                    FailedRequests = _statistics.FailedRequests,
                    RetriedRequests = _statistics.RetriedRequests,
                    CircuitOpenCount = _statistics.CircuitOpenCount,
                    LastFailure = _statistics.LastFailure
                };
            }
        }

        private IAsyncPolicy GetOrCreatePolicy(string operationKey)
        {
            return _policies.GetOrAdd(operationKey, key =>
            {
                // Create a comprehensive resilience policy
                var retryPolicy = Policy
                    .Handle<Exception>(ex => !(ex is TaskCanceledException))
                    .WaitAndRetryAsync(
                        3,
                        retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                        onRetry: (exception, timeSpan, retry, context) =>
                        {
                            IncrementRetriedRequests();
                            _logger.Debug("Operation {0}: Retry {1} after {2}s", 
                                key, retry, timeSpan.TotalSeconds);
                        });

                var circuitBreakerPolicy = Policy
                    .Handle<Exception>()
                    .AdvancedCircuitBreakerAsync(
                        failureThreshold: 0.5,
                        samplingDuration: TimeSpan.FromSeconds(60),
                        minimumThroughput: 5,
                        durationOfBreak: TimeSpan.FromSeconds(30),
                        onBreak: (exception, duration) =>
                        {
                            UpdateCircuitState(key, true);
                            IncrementCircuitOpenCount();
                            _logger.Warn("Circuit breaker opened for {0} for {1}s due to: {2}", 
                                key, duration.TotalSeconds, exception?.Message);
                        },
                        onReset: () =>
                        {
                            UpdateCircuitState(key, false);
                            _logger.Info("Circuit breaker reset for {0}", key);
                        },
                        onHalfOpen: () =>
                        {
                            _logger.Debug("Circuit breaker half-open for {0}", key);
                        });

                var timeoutPolicy = Policy
                    .TimeoutAsync(TimeSpan.FromSeconds(30), 
                        TimeoutStrategy.Pessimistic,
                        onTimeoutAsync: async (context, timespan, task) =>
                        {
                            _logger.Warn("Operation {0} timed out after {1}s", key, timespan.TotalSeconds);
                            await Task.CompletedTask;
                        });

                // Combine policies: Timeout wraps CircuitBreaker wraps Retry
                return Policy.WrapAsync(timeoutPolicy, circuitBreakerPolicy, retryPolicy);
            });
        }

        private void UpdateCircuitState(string operationKey, bool isOpen)
        {
            _circuitStates.AddOrUpdate(operationKey, 
                new CircuitState { IsOpen = isOpen, LastStateChange = DateTime.UtcNow },
                (k, v) => new CircuitState { IsOpen = isOpen, LastStateChange = DateTime.UtcNow });
        }

        private void IncrementTotalRequests()
        {
            lock (_statsLock)
            {
                _statistics.TotalRequests++;
            }
        }

        private void IncrementSuccessfulRequests()
        {
            lock (_statsLock)
            {
                _statistics.SuccessfulRequests++;
            }
        }

        private void IncrementFailedRequests()
        {
            lock (_statsLock)
            {
                _statistics.FailedRequests++;
            }
        }

        private void IncrementRetriedRequests()
        {
            lock (_statsLock)
            {
                _statistics.RetriedRequests++;
            }
        }

        private void IncrementCircuitOpenCount()
        {
            lock (_statsLock)
            {
                _statistics.CircuitOpenCount++;
            }
        }

        private void UpdateLastFailure()
        {
            lock (_statsLock)
            {
                _statistics.LastFailure = DateTime.UtcNow;
            }
        }

        private class CircuitState
        {
            public bool IsOpen { get; set; }
            public DateTime LastStateChange { get; set; }
        }

        public void Dispose()
        {
            // Clean up any held resources
            _policies.Clear();
            _circuitStates.Clear();
            GC.SuppressFinalize(this);
        }
    }
}