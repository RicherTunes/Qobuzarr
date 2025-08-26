using System;
using Lidarr.Plugin.Common.Services.Caching;
using Lidarr.Plugin.Qobuzarr.Configuration;

namespace Lidarr.Plugin.Qobuzarr.Services
{
    /// <summary>
    /// Qobuz-specific implementation of the shared StreamingResponseCache.
    /// Demonstrates usage of the shared library cache with Qobuz-specific logic.
    /// </summary>
    public class QobuzResponseCacheShared : StreamingResponseCache
    {
        /// <inheritdoc/>
        public override bool ShouldCache(string endpoint)
        {
            // Cache search results and metadata, but not authentication or streaming URLs
            return endpoint.Contains("/search/") ||
                   endpoint.Contains("/album/get") ||
                   endpoint.Contains("/artist/get") ||
                   endpoint.Contains("/track/get") ||
                   endpoint.Contains("/playlist/get") ||
                   endpoint.Contains("/label/get");
        }

        /// <inheritdoc/>
        public override TimeSpan GetCacheDuration(string endpoint)
        {
            return endpoint switch
            {
                var e when e.Contains("/search/") => QobuzConstants.Cache.ShortDuration,
                var e when e.Contains("/album/get") => QobuzConstants.Cache.MediumDuration,
                var e when e.Contains("/artist/get") => QobuzConstants.Cache.LongDuration,
                var e when e.Contains("/label/get") => QobuzConstants.Cache.LongDuration,
                var e when e.Contains("/playlist/get") => QobuzConstants.Cache.MediumDuration,
                var e when e.Contains("/track/get") => QobuzConstants.Cache.MediumDuration,
                _ => QobuzConstants.Cache.SessionDuration
            };
        }

        /// <inheritdoc/>
        protected override string GetServiceName()
        {
            return "qobuz_api";
        }

        /// <summary>
        /// Example of service-specific logging using shared library events.
        /// </summary>
        protected override void OnCacheHit(string endpoint, string cacheKey)
        {
            // Could use NLog or other logging here
            System.Diagnostics.Debug.WriteLine($"Qobuz cache hit for {endpoint}");
        }

        /// <summary>
        /// Example of service-specific logging using shared library events.
        /// </summary>
        protected override void OnCacheMiss(string endpoint, string cacheKey)
        {
            System.Diagnostics.Debug.WriteLine($"Qobuz cache miss for {endpoint}");
        }

        /// <summary>
        /// Example of service-specific logging using shared library events.
        /// </summary>
        protected override void OnExpiredItemsCleanup(int itemsRemoved)
        {
            if (itemsRemoved > 0)
            {
                System.Diagnostics.Debug.WriteLine($"Cleaned up {itemsRemoved} expired Qobuz cache items");
            }
        }
    }
}