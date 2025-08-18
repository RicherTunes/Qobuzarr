using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Services;
using NzbDrone.Common.Http;

namespace QobuzCLI.Services.Adapters
{
    /// <summary>
    /// Adapter that provides the plugin's IAdaptiveRateLimiter implementation to CLI components.
    /// This maintains the plugin-first architecture by using the plugin's rate limiter directly.
    /// </summary>
    public class RateLimiterAdapter : IAdaptiveRateLimiter
    {
        private readonly IAdaptiveRateLimiter _pluginRateLimiter;

        public RateLimiterAdapter(IAdaptiveRateLimiter pluginRateLimiter)
        {
            _pluginRateLimiter = pluginRateLimiter ?? throw new ArgumentNullException(nameof(pluginRateLimiter));
        }

        public async Task<bool> WaitIfNeededAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            return await _pluginRateLimiter.WaitIfNeededAsync(endpoint, cancellationToken);
        }

        public void RecordResponse(string endpoint, HttpResponseMessage response)
        {
            _pluginRateLimiter.RecordResponse(endpoint, response);
        }

        public void RecordResponse(string endpoint, HttpResponse response)
        {
            _pluginRateLimiter.RecordResponse(endpoint, response);
        }

        public int GetCurrentLimit(string endpoint)
        {
            return _pluginRateLimiter.GetCurrentLimit(endpoint);
        }

        public RateLimitStats GetStats()
        {
            // Direct pass-through since interfaces are now the same
            return _pluginRateLimiter.GetStats();
        }
    }
}