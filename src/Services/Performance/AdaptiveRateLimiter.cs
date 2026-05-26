using Lidarr.Plugin.Common.Services.Performance;

namespace Lidarr.Plugin.Qobuzarr.Services.Performance
{
    // Adapter that lives in the plugin assembly so Lidarr's auto-registration
    // discovers and injects an IUniversalAdaptiveRateLimiter implementation.
    public class AdaptiveRateLimiter : NamedServiceRateLimiter
    {
        public AdaptiveRateLimiter() : base("Qobuz") { }
    }
}
