using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Constants;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Utilities;
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

        /// <summary>
        /// Normalizes an API endpoint to a canonical form (no leading slash, lowercased) so the
        /// caching predicates match regardless of whether the caller wrote "track/get" or
        /// "/track/get". The plugin passes both shapes (internal helpers use "track/get";
        /// QobuzIndexerAdapter uses "/album/search", "/catalog/search").
        /// Parameters are not part of the endpoint string passed here.
        /// </summary>
        private static string Normalize(string endpoint)
            => (endpoint ?? string.Empty).Trim().TrimStart('/').ToLowerInvariant();

        /// <inheritdoc/>
        public override bool ShouldCache(string endpoint)
        {
            // Cache search results and metadata reads, but NOT authentication or signed,
            // short-lived streaming URLs. Note "track/getFileUrl" must be excluded explicitly:
            // it has a "track/get" prefix, so naive substring matching would cache it.
            var ep = Normalize(endpoint);
            if (ep.Contains("getfileurl"))
            {
                return false;
            }

            return ep.EndsWith("/search") ||   // album/search, catalog/search, playlist/search, label/search
                   ep.EndsWith("/get");        // album/get, track/get, artist/get, playlist/get, label/get
        }

        /// <inheritdoc/>
        public override TimeSpan GetCacheDuration(string endpoint)
        {
            var ep = Normalize(endpoint);
            return ep switch
            {
                _ when ep.EndsWith("/search") => QobuzConstants.Cache.ShortDuration,
                "artist/get" or "label/get" => QobuzConstants.Cache.LongDuration,
                _ when ep.EndsWith("/get") => QobuzConstants.Cache.MediumDuration,
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
            // Use stable hashing for deterministic cache keys across processes
            return HashingUtility.ComputeMD5Hash(key);
        }

        private static readonly Lazy<Microsoft.Extensions.Logging.ILogger> _sharedMsLogger
            = new Lazy<Microsoft.Extensions.Logging.ILogger>(() =>
        {
            var factory = LoggerFactory.Create(builder => builder.AddConsole());
            return factory.CreateLogger("QobuzResponseCache");
        });

        private static Microsoft.Extensions.Logging.ILogger CreateMsLogger(Logger _)
        {
            // Reuse a single logger instance to avoid repeated factory creation
            return _sharedMsLogger.Value;
        }
    }
}
