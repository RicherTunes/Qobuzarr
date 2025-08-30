using Lidarr.Plugin.Common.Interfaces;

namespace Lidarr.Plugin.Qobuzarr.API.Caching
{
    /// <summary>
    /// Manages response caching for Qobuz API calls to reduce redundant requests.
    /// This interface handles cache key generation, TTL determination, and cache operations.
    /// </summary>
    public interface IQobuzResponseCache : IStreamingResponseCache { }
}
