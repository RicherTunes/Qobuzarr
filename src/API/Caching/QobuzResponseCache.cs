using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using Lidarr.Plugin.Qobuzarr.Configuration;

namespace Lidarr.Plugin.Qobuzarr.API.Caching
{
    /// <summary>
    /// Implementation of response caching for Qobuz API calls.
    /// Manages cache key generation, TTL determination, and cache operations.
    /// </summary>
    public class QobuzResponseCache : IQobuzResponseCache
    {
        private readonly ICacheManager _cacheManager;
        private readonly ICached<object> _cache;
        private readonly Logger _logger;

        public QobuzResponseCache(ICacheManager cacheManager, Logger logger)
        {
            _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = _cacheManager.GetCache<object>(GetType());
        }

        /// <inheritdoc/>
        public T? Get<T>(string endpoint, Dictionary<string, string> parameters) where T : class
        {
            if (!ShouldCache(endpoint))
                return null;

            var cacheKey = GenerateCacheKey(endpoint, parameters);
            var cached = _cache.Find(cacheKey);

            if (cached != null)
            {
                _logger.Debug("Cache hit for {0}", endpoint);
                return cached as T;
            }

            _logger.Debug("Cache miss for {0}", endpoint);
            return null;
        }

        /// <inheritdoc/>
        public void Set<T>(string endpoint, Dictionary<string, string> parameters, T value) where T : class
        {
            var duration = GetCacheDuration(endpoint);
            Set(endpoint, parameters, value, duration);
        }

        /// <inheritdoc/>
        public void Set<T>(string endpoint, Dictionary<string, string> parameters, T value, TimeSpan duration) where T : class
        {
            if (!ShouldCache(endpoint) || value == null)
                return;

            var cacheKey = GenerateCacheKey(endpoint, parameters);
            _cache.Set(cacheKey, value, duration);
            _logger.Debug("Cached response for {0} (TTL: {1})", endpoint, duration);
        }

        /// <inheritdoc/>
        public bool ShouldCache(string endpoint)
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
        public TimeSpan GetCacheDuration(string endpoint)
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
        public string GenerateCacheKey(string endpoint, Dictionary<string, string> parameters)
        {
            // Exclude authentication tokens from cache key to allow sharing cached data
            var relevantParams = parameters
                .Where(p => p.Key != "user_auth_token" && p.Key != "app_id")
                .OrderBy(p => p.Key)
                .Select(p => $"{p.Key}={p.Value}")
                .Join("&");

            var key = $"qobuz_api_{endpoint}_{relevantParams}";
            return key.GetHashCode().ToString();
        }

        /// <inheritdoc/>
        public void Clear()
        {
            _cache.Clear();
            _logger.Debug("Cleared all cached responses");
        }

        /// <inheritdoc/>
        public void ClearEndpoint(string endpoint)
        {
            // Note: ICached doesn't support partial clearing, so we log a warning
            _logger.Warn("Partial cache clearing not supported - use Clear() to clear all cached responses");
        }
    }
}