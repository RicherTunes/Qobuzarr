using Lidarr.Plugin.Common.Services.Http;
using Lidarr.Plugin.Common.Services.Performance;
using Microsoft.Extensions.Logging;

namespace Lidarr.Plugin.Qobuzarr.Integration.Bridge
{
    /// <summary>
    /// Thin Qobuz-specific subclass of <see cref="AdaptiveRateLimitingHandler"/>.
    /// All logic lives in Common; this class exists only to satisfy DI registration
    /// (AddHttpMessageHandler&lt;QobuzRateLimitingHandler&gt;) and to bind the
    /// typed logger.
    ///
    /// <para>
    /// Migration note: the full 95-LOC implementation was lifted to
    /// <c>Lidarr.Plugin.Common.Services.Http.AdaptiveRateLimitingHandler</c> (wave-23).
    /// </para>
    /// </summary>
    public sealed class QobuzRateLimitingHandler : AdaptiveRateLimitingHandler
    {
        public QobuzRateLimitingHandler(
            IUniversalAdaptiveRateLimiter rateLimiter,
            ILogger<QobuzRateLimitingHandler>? logger = null)
            : base(rateLimiter, "Qobuz", logger)
        {
        }
    }
}
