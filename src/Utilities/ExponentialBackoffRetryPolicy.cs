using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Configuration;

namespace Lidarr.Plugin.Qobuzarr.Utilities
{
    /// <summary>
    /// Exponential backoff retry policy for resilient operations.
    /// Replaces hardcoded retry logic throughout the codebase.
    /// </summary>
    public class ExponentialBackoffRetryPolicy : IRetryPolicy
    {
        private readonly Logger _logger;
        private readonly Random _random = new Random();

        public ExponentialBackoffRetryPolicy(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, RetryContext retryContext = null!)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));

            var context = retryContext ?? new RetryContext();
            var attempt = 0;
            Exception? lastException = null;

            while (attempt < context.MaxAttempts)
            {
                try
                {
                    if (attempt > 0)
                    {
                        _logger.Debug("Retry attempt {0}/{1} for {2}", attempt + 1, context.MaxAttempts, context.OperationType);
                    }
                    
                    return await operation();
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    attempt++;

                    if (!ShouldRetry(ex, attempt) || attempt >= context.MaxAttempts)
                    {
                        _logger.Error(ex, "Operation {0} failed after {1} attempts", context.OperationType, attempt);
                        throw;
                    }

                    var delay = GetRetryDelay(attempt, ex);
                    if (context.UseJitter)
                    {
                        // Add 25% jitter to prevent thundering herd
                        var jitter = delay.TotalMilliseconds * 0.25 * _random.NextDouble();
                        delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds + jitter);
                    }

                    // Cap delay at maximum
                    if (delay > context.MaxDelay)
                        delay = context.MaxDelay;

                    _logger.Warn(ex, "Operation {0} failed (attempt {1}/{2}), retrying in {3}ms: {4}",
                        context.OperationType, attempt, context.MaxAttempts, delay.TotalMilliseconds, ex.Message);

                    await Task.Delay(delay);
                }
            }

            // This should never be reached due to the logic above
            if (lastException != null)
                throw lastException;

            throw new InvalidOperationException("Unexpected retry logic state");
        }

        public T Execute<T>(Func<T> operation, RetryContext retryContext = null!)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));

            var context = retryContext ?? new RetryContext();
            var attempt = 0;
            Exception? lastException = null;

            while (attempt < context.MaxAttempts)
            {
                try
                {
                    if (attempt > 0)
                    {
                        _logger.Debug("Sync retry attempt {0}/{1} for {2}", attempt + 1, context.MaxAttempts, context.OperationType);
                    }
                    
                    return operation();
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    attempt++;

                    if (!ShouldRetry(ex, attempt) || attempt >= context.MaxAttempts)
                    {
                        _logger.Error(ex, "Sync operation {0} failed after {1} attempts", context.OperationType, attempt);
                        throw;
                    }

                    var delay = GetRetryDelay(attempt, ex);
                    if (context.UseJitter)
                    {
                        var jitter = delay.TotalMilliseconds * 0.25 * _random.NextDouble();
                        delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds + jitter);
                    }

                    if (delay > context.MaxDelay)
                        delay = context.MaxDelay;

                    _logger.Warn(ex, "Sync operation {0} failed (attempt {1}/{2}), waiting {3}ms: {4}",
                        context.OperationType, attempt, context.MaxAttempts, delay.TotalMilliseconds, ex.Message);

                    Thread.Sleep(delay);
                }
            }

            if (lastException != null)
                throw lastException;

            throw new InvalidOperationException("Unexpected retry logic state");
        }

        public bool ShouldRetry(Exception exception, int attemptNumber)
        {
            // Don't retry on certain exception types
            if (exception is ArgumentNullException or ArgumentException or UnauthorizedAccessException)
            {
                return false;
            }

            // Always retry on network/IO related exceptions
            if (exception is TimeoutException or TaskCanceledException)
            {
                return true;
            }

            // Retry on HTTP errors that indicate temporary issues
            if (exception is System.Net.Http.HttpRequestException httpEx)
            {
                // Always retry on rate limiting (429) or service unavailable (503)
                if (httpEx.Message.Contains("429") || httpEx.Message.Contains("503") ||
                    httpEx.Message.Contains("TooManyRequests") || httpEx.Message.Contains("ServiceUnavailable"))
                {
                    _logger.Debug("Retrying due to HTTP {0} error", 
                        httpEx.Message.Contains("429") ? "429 (Too Many Requests)" : "503 (Service Unavailable)");
                    return true;
                }
                
                // Also retry on other transient HTTP errors
                if (httpEx.Message.Contains("502") || httpEx.Message.Contains("504") ||
                    httpEx.Message.Contains("BadGateway") || httpEx.Message.Contains("GatewayTimeout"))
                {
                    return true;
                }
            }

            // Default: retry most exceptions up to max attempts
            return true;
        }

        public TimeSpan GetRetryDelay(int attemptNumber, Exception? exception = null)
        {
            if (attemptNumber <= 0)
                return TimeSpan.Zero;

            // Exponential backoff: base * 2^(attempt-1)
            var baseDelayMs = 1000; // 1 second base delay
            var exponential = Math.Pow(2, attemptNumber - 1);
            var delayMs = baseDelayMs * exponential;

            // Cap at 30 seconds
            return TimeSpan.FromMilliseconds(Math.Min(delayMs, QobuzConstants.Timeouts.MaxRetryDelayMs));
        }
    }
}