using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Common.Services.Performance;
using NzbDrone.Common.Http;

namespace QobuzCLI.Services.Adapters
{
    /// <summary>
    /// Adapter that provides the plugin's IAdaptiveRateLimiter implementation to CLI components.
    /// This maintains the plugin-first architecture by using the plugin's rate limiter directly.
    /// </summary>
    public class RateLimiterAdapter : IUniversalAdaptiveRateLimiter
    {
        private readonly IUniversalAdaptiveRateLimiter _pluginRateLimiter;

        public RateLimiterAdapter(IUniversalAdaptiveRateLimiter pluginRateLimiter)
        {
            _pluginRateLimiter = pluginRateLimiter ?? throw new ArgumentNullException(nameof(pluginRateLimiter));
        }

        public async Task<bool> WaitIfNeededAsync(string service, string endpoint, CancellationToken cancellationToken = default)
        {
            return await _pluginRateLimiter.WaitIfNeededAsync(service, endpoint, cancellationToken);
        }

        public void RecordResponse(string service, string endpoint, HttpResponseMessage response)
        {
            _pluginRateLimiter.RecordResponse(service, endpoint, response);
        }

        public void RecordAuthFailure(string service, string endpoint)
        {
            _pluginRateLimiter.RecordAuthFailure(service, endpoint);
        }

        public int GetCurrentLimit(string service, string endpoint)
        {
            return _pluginRateLimiter.GetCurrentLimit(service, endpoint);
        }

        public ServiceRateLimitStats GetServiceStats(string service)
        {
            return _pluginRateLimiter.GetServiceStats(service);
        }

        public GlobalRateLimitStats GetGlobalStats()
        {
            return _pluginRateLimiter.GetGlobalStats();
        }

        public void Dispose()
        {
            _pluginRateLimiter?.Dispose();
        }
    }
}
