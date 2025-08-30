using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Constants;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Common.Services.Caching;
using Microsoft.Extensions.Logging;

namespace Lidarr.Plugin.Qobuzarr.API.Caching
{
    /// <summary>
    /// Implementation of response caching for Qobuz API calls.
    /// Manages cache key generation, TTL determination, and cache operations.
    /// </summary>
    public class QobuzResponseCache : StreamingResponseCache, IQobuzResponseCache
    {
        private readonly Logger _logger;

        public QobuzResponseCache(Logger logger, IPerformanceMonitoringService? performanceMonitor = null)
            : base(CreateMsLogger(logger))
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // Using base Get/Set implementations from StreamingResponseCache

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

        protected override string GetServiceName() => QobuzarrConstants.ServiceName;

        // Override GenerateCacheKey to match Qobuz-specific masking
        public override string GenerateCacheKey(string endpoint, Dictionary<string, string> parameters)
        {
            var filtered = parameters
                .Where(p => !IsSensitiveParameter(p.Key))
                .OrderBy(p => p.Key)
                .Select(p => $"{p.Key}={p.Value}");
            var key = $"qobuz_api_{endpoint}_{string.Join("&", filtered)}";
            return Math.Abs(key.GetHashCode()).ToString();
        }

        private static Microsoft.Extensions.Logging.ILogger CreateMsLogger(Logger nlog)
        {
            // Best-effort: create a basic console logger if no factory is provided; avoid hard coupling
            using var factory = LoggerFactory.Create(builder => builder.AddConsole());
            return factory.CreateLogger("QobuzResponseCache");
        }
    }
}
