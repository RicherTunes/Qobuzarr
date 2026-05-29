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

        /// <inheritdoc/>
        public override bool ShouldCache(string endpoint)
        {
            var ep = CanonicalizeEndpoint(endpoint);

            // Never cache signed, time-limited streaming URLs (or auth). "track/getFileUrl" shares
            // the "track/get" prefix, so it MUST be excluded BEFORE the metadata checks below —
            // the previous "/track/get" Contains pattern risked caching an expiring stream URL
            // (and, because real endpoints arrive without a leading slash, matched nothing at all,
            // silently disabling metadata caching entirely).
            if (ep.Contains("getfileurl") || ep.Contains("/auth") || ep.Contains("user/login"))
            {
                return false;
            }

            // Cache search results and metadata. Matching is leading-slash- and case-insensitive
            // (the endpoint may arrive as "track/get" or "/track/get").
            return ep.Contains("search") ||
                   ep.Contains("album/get") ||
                   ep.Contains("artist/get") ||
                   ep.Contains("track/get") ||
                   ep.Contains("playlist/get") ||
                   ep.Contains("label/get");
        }

        /// <inheritdoc/>
        public override TimeSpan GetCacheDuration(string endpoint)
        {
            var ep = CanonicalizeEndpoint(endpoint);
            return ep switch
            {
                var e when e.Contains("search") => QobuzConstants.Cache.ShortDuration,
                var e when e.Contains("album/get") => QobuzConstants.Cache.MediumDuration,
                var e when e.Contains("artist/get") => QobuzConstants.Cache.LongDuration,
                var e when e.Contains("label/get") => QobuzConstants.Cache.LongDuration,
                var e when e.Contains("playlist/get") => QobuzConstants.Cache.MediumDuration,
                var e when e.Contains("track/get") => QobuzConstants.Cache.MediumDuration,
                _ => QobuzConstants.Cache.SessionDuration
            };
        }

        // Normalize the endpoint for matching: strip a leading '/', drop the query string, and
        // lowercase, so cache decisions are independent of the caller's slash/case convention.
        private static string CanonicalizeEndpoint(string? endpoint)
            => (endpoint ?? string.Empty).TrimStart('/').Split('?')[0].ToLowerInvariant();

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
