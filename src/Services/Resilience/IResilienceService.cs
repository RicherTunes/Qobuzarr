using System;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Qobuzarr.Services.Resilience
{
    /// <summary>
    /// Service for providing resilience patterns like retry, circuit breaker, etc.
    /// </summary>
    public interface IResilienceService
    {
        /// <summary>
        /// Executes an action with resilience patterns applied
        /// </summary>
        Task<T> ExecuteWithResilienceAsync<T>(Func<Task<T>> action, string operationName);

        /// <summary>
        /// Executes an action with resilience patterns applied
        /// </summary>
        Task ExecuteWithResilienceAsync(Func<Task> action, string operationName);
    }
}