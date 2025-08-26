using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Common.Utilities
{
    /// <summary>
    /// Centralized retry and error handling utilities for streaming service plugins.
    /// This class provides the single source of truth for all retry logic.
    /// </summary>
    public static class RetryUtilities
    {
        /// <summary>
        /// Determines if an exception is retryable based on common patterns.
        /// </summary>
        public static bool IsRetryableException(Exception ex)
        {
            if (ex == null)
                return false;

            // Network-related exceptions
            if (ex is HttpRequestException ||
                ex is TaskCanceledException ||
                ex is OperationCanceledException ||
                ex is TimeoutException ||
                ex is System.Net.Sockets.SocketException ||
                ex is System.IO.IOException)
            {
                return true;
            }

            // Check for specific HTTP status codes in WebException
            if (ex is WebException webEx)
            {
                if (webEx.Response is HttpWebResponse response)
                {
                    return IsRetryableStatusCode(response.StatusCode);
                }
                return true; // Most WebExceptions are retryable
            }

            // Check inner exception
            if (ex.InnerException != null)
            {
                return IsRetryableException(ex.InnerException);
            }

            // Check for specific error messages
            var message = ex.Message?.ToLowerInvariant() ?? "";
            return message.Contains("timeout") ||
                   message.Contains("connection") ||
                   message.Contains("network") ||
                   message.Contains("temporarily") ||
                   message.Contains("rate limit") ||
                   message.Contains("too many requests");
        }

        /// <summary>
        /// Determines if an HTTP status code is retryable.
        /// </summary>
        public static bool IsRetryableStatusCode(HttpStatusCode statusCode)
        {
            switch (statusCode)
            {
                case HttpStatusCode.RequestTimeout:        // 408
                case HttpStatusCode.TooManyRequests:        // 429
                case HttpStatusCode.InternalServerError:    // 500
                case HttpStatusCode.BadGateway:             // 502
                case HttpStatusCode.ServiceUnavailable:     // 503
                case HttpStatusCode.GatewayTimeout:         // 504
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Executes an action with exponential backoff retry logic.
        /// </summary>
        public static async Task<T> ExecuteWithRetryAsync<T>(
            Func<Task<T>> action,
            int maxRetries = 3,
            int initialDelayMs = 1000,
            string operationName = null,
            Action<Exception, int, string> onRetry = null)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var attempt = 0;
            var delay = initialDelayMs;

            while (true)
            {
                try
                {
                    attempt++;
                    return await action();
                }
                catch (Exception ex) when (attempt < maxRetries && IsRetryableException(ex))
                {
                    var opName = operationName ?? "operation";
                    onRetry?.Invoke(ex, attempt, $"Attempt {attempt} of {maxRetries} failed for {opName}. Retrying in {delay}ms...");
                    
                    // Add jitter to prevent thundering herd
                    var jitter = new Random().Next(0, 500);
                    await Task.Delay(delay + jitter);
                    delay = Math.Min(delay * 2, 30000); // Exponential backoff with max 30s cap
                }
                catch (Exception ex) when (attempt >= maxRetries)
                {
                    var opName = operationName ?? "operation";
                    onRetry?.Invoke(ex, maxRetries, $"All {maxRetries} attempts failed for {opName}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Executes an action with simple retry logic (no exponential backoff).
        /// </summary>
        public static async Task<T> SimpleRetryAsync<T>(
            Func<Task<T>> action,
            int maxRetries = 3,
            int delayMs = 1000)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    return await action();
                }
                catch (Exception ex) when (i < maxRetries - 1 && IsRetryableException(ex))
                {
                    await Task.Delay(delayMs);
                }
            }

            // Final attempt without catching
            return await action();
        }

        /// <summary>
        /// Executes an action with timeout and retry logic.
        /// </summary>
        public static async Task<T> ExecuteWithTimeoutAndRetryAsync<T>(
            Func<CancellationToken, Task<T>> action,
            TimeSpan timeout,
            int maxRetries = 3,
            string operationName = null,
            Action<Exception, int, string> onRetry = null)
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                using (var cts = new CancellationTokenSource(timeout))
                {
                    try
                    {
                        return await action(cts.Token);
                    }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested)
                    {
                        throw new TimeoutException($"Operation timed out after {timeout.TotalSeconds} seconds");
                    }
                }
            }, maxRetries, 1000, operationName, onRetry);
        }

        /// <summary>
        /// Implements circuit breaker pattern for repeated failures.
        /// </summary>
        public class CircuitBreaker
        {
            private readonly int _failureThreshold;
            private readonly TimeSpan _resetTimeout;
            private int _failureCount;
            private DateTime _lastFailureTime;
            private CircuitBreakerState _state;

            public CircuitBreaker(int failureThreshold = 5, TimeSpan? resetTimeout = null)
            {
                _failureThreshold = failureThreshold;
                _resetTimeout = resetTimeout ?? TimeSpan.FromMinutes(1);
                _state = CircuitBreakerState.Closed;
            }

            public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
            {
                if (_state == CircuitBreakerState.Open)
                {
                    if (DateTime.UtcNow - _lastFailureTime > _resetTimeout)
                    {
                        _state = CircuitBreakerState.HalfOpen;
                    }
                    else
                    {
                        throw new CircuitBreakerOpenException("Circuit breaker is open");
                    }
                }

                try
                {
                    var result = await action();
                    
                    if (_state == CircuitBreakerState.HalfOpen)
                    {
                        _state = CircuitBreakerState.Closed;
                        _failureCount = 0;
                    }
                    
                    return result;
                }
                catch (Exception)
                {
                    _failureCount++;
                    _lastFailureTime = DateTime.UtcNow;
                    
                    if (_failureCount >= _failureThreshold)
                    {
                        _state = CircuitBreakerState.Open;
                    }
                    
                    throw;
                }
            }

            private enum CircuitBreakerState
            {
                Closed,
                Open,
                HalfOpen
            }
        }

        public class CircuitBreakerOpenException : Exception
        {
            public CircuitBreakerOpenException(string message) : base(message) { }
        }

        /// <summary>
        /// Rate limiter for API calls.
        /// </summary>
        public class RateLimiter
        {
            private readonly int _maxRequests;
            private readonly TimeSpan _timeWindow;
            private readonly Queue<DateTime> _requestTimes;
            private readonly object _lock = new object();

            public RateLimiter(int maxRequests, TimeSpan timeWindow)
            {
                _maxRequests = maxRequests;
                _timeWindow = timeWindow;
                _requestTimes = new Queue<DateTime>();
            }

            public async Task WaitIfNeededAsync()
            {
                TimeSpan waitTime = TimeSpan.Zero;
                
                lock (_lock)
                {
                    var now = DateTime.UtcNow;
                    var windowStart = now - _timeWindow;

                    // Remove old requests outside the time window
                    while (_requestTimes.Count > 0 && _requestTimes.Peek() < windowStart)
                    {
                        _requestTimes.Dequeue();
                    }

                    // If at limit, calculate wait time
                    if (_requestTimes.Count >= _maxRequests)
                    {
                        var oldestRequest = _requestTimes.Peek();
                        waitTime = oldestRequest + _timeWindow - now;
                    }
                }
                
                // Wait outside the lock to avoid blocking
                if (waitTime > TimeSpan.Zero)
                {
                    await Task.Delay(waitTime).ConfigureAwait(false);
                }
                
                lock (_lock)
                {
                    _requestTimes.Enqueue(DateTime.UtcNow);
                }
            }
        }
    }
}