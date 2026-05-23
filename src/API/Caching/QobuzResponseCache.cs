using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Constants;
using Lidarr.Plugin.Qobuzarr.Services;
using CommonUtilities = Lidarr.Plugin.Common.Utilities;
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
            if (string.IsNullOrEmpty(endpoint)) return false;

            // Anchor to end-of-string. The earlier substring approach mismatched
            // both ways: the trailing-slash pattern "/search/" never matched any
            // real endpoint (so /album/search was never cached), and the bare
            // "/track/get" pattern matched "/track/getFileUrl" — the streaming
            // URL endpoint, which MUST NOT be cached as it returns short-lived
            // signed URLs.
            return endpoint.EndsWith("/album/search", StringComparison.Ordinal) ||
                   endpoint.EndsWith("/track/search", StringComparison.Ordinal) ||
                   endpoint.EndsWith("/artist/search", StringComparison.Ordinal) ||
                   endpoint.EndsWith("/album/get", StringComparison.Ordinal) ||
                   endpoint.EndsWith("/artist/get", StringComparison.Ordinal) ||
                   endpoint.EndsWith("/track/get", StringComparison.Ordinal) ||
                   endpoint.EndsWith("/playlist/get", StringComparison.Ordinal) ||
                   endpoint.EndsWith("/label/get", StringComparison.Ordinal);
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
            // Use stable hashing for deterministic cache keys across processes
            return CommonUtilities.HashingUtility.ComputeMD5Hash(key);
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
