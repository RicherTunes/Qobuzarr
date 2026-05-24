using System;
using System.Net;
using System.Net.Http;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Constants;

namespace Lidarr.Plugin.Qobuzarr.Services.Http
{
    /// <summary>
    /// Provides a singleton System.Net.Http.HttpClient configured for downloads and streaming.
    /// Avoids socket exhaustion and enables per-host connection limits.
    /// </summary>
    internal static class SharedSystemHttpClient
    {
        private static readonly Lazy<HttpClient> _client = new Lazy<HttpClient>(CreateClient, isThreadSafe: true);

        // Disposed flag for idempotent Dispose(); 0 = live, 1 = disposed.
        private static int _disposed = 0;

        public static HttpClient Instance => _client.Value;

        /// <summary>
        /// Disposes the underlying <see cref="HttpClient"/> (and its <see cref="SocketsHttpHandler"/>)
        /// if the Lazy value has been created.  Releases the socket pool so that each plugin reload
        /// does not accumulate exhausted connections.
        /// Idempotent — safe to call multiple times (subsequent calls are no-ops).
        /// Called by <see cref="QobuzarrModule.Dispose"/> on plugin unload.
        /// </summary>
        public static void Dispose()
        {
            // CAS: only the first caller proceeds; subsequent calls are no-ops.
            if (System.Threading.Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                return;

            if (_client.IsValueCreated)
            {
                try { _client.Value.Dispose(); }
                catch { /* Non-critical — best-effort on unload path */ }
            }
        }

        /// <summary>TEST-ONLY: resets the disposed flag.</summary>
        internal static void ResetForTesting()
        {
            System.Threading.Interlocked.Exchange(ref _disposed, 0);
        }

        private static HttpClient CreateClient()
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = QobuzarrConstants.Defaults.DefaultMaxConcurrencyPerHost,
                UseCookies = false,
                AllowAutoRedirect = true
            };

            var client = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromMinutes(10)
            };

            try
            {
                // Apply a sensible default UA for Qobuz endpoints
                client.DefaultRequestHeaders.UserAgent.ParseAdd(QobuzConstants.Api.UserAgent);
                client.DefaultRequestHeaders.ConnectionClose = false;
                client.DefaultRequestHeaders.ExpectContinue = false;
            }
            catch
            {
                // If header parsing fails for any reason, continue with defaults
            }

            return client;
        }
    }
}

