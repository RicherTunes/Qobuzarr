using System;
using System.Threading.Tasks;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Services.Resilience
{
    /// <summary>
    /// Basic implementation of resilience patterns
    /// </summary>
    public class ResilienceService : IResilienceService
    {
        private readonly Logger _logger;
        private const int MaxRetries = 3;
        private const int BaseDelayMs = 1000;

        public ResilienceService(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<T> ExecuteWithResilienceAsync<T>(Func<Task<T>> action, string operationName)
        {
            var retryCount = 0;
            
            while (retryCount < MaxRetries)
            {
                try
                {
                    return await action().ConfigureAwait(false);
                }
                catch (Exception ex) when (retryCount < MaxRetries - 1)
                {
                    retryCount++;
                    var delay = BaseDelayMs * (int)Math.Pow(2, retryCount - 1);
                    
                    _logger.Warn(ex, "Operation {0} failed on attempt {1}/{2}. Retrying in {3}ms",
                        operationName, retryCount, MaxRetries, delay);
                    
                    await Task.Delay(delay).ConfigureAwait(false);
                }
            }
            
            // Final attempt without catching
            return await action().ConfigureAwait(false);
        }

        public async Task ExecuteWithResilienceAsync(Func<Task> action, string operationName)
        {
            await ExecuteWithResilienceAsync(async () =>
            {
                await action().ConfigureAwait(false);
                return 0; // Dummy return value
            }, operationName).ConfigureAwait(false);
        }
    }
}