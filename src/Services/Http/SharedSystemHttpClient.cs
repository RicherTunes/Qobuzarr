using System;
using System.Net;
using System.Net.Http;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Constants;

namespace Lidarr.Plugin.Qobuzarr.Services.Http
{
    /// <summary>
    /// Provides singleton System.Net.Http.HttpClient instances configured for Qobuz API and media traffic.
    /// Avoids socket exhaustion and enables per-host connection limits.
    /// </summary>
    internal static class SharedSystemHttpClient
    {
        private static Lazy<HttpClient> _client = CreateApiClientLazy();
        private static Lazy<HttpClient> _mediaClient = CreateMediaClientLazy();

        // Disposed flag for idempotent Dispose(); 0 = live, 1 = disposed.
        private static int _disposed = 0;

        public static HttpClient Instance => _client.Value;

        /// <summary>
        /// HttpClient for provider-controlled media URLs. Auto redirects stay disabled so Common's media
        /// redirect validator receives each 3xx and can refuse unsafe targets before the next request is sent.
        /// </summary>
        public static HttpClient MediaInstance => _mediaClient.Value;

        /// <summary>
        /// Disposes the underlying <see cref="HttpClient"/> instances (and their <see cref="SocketsHttpHandler"/>s)
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
                DisposeClient(_client.Value);
            }

            if (_mediaClient.IsValueCreated)
            {
                DisposeClient(_mediaClient.Value);
            }
        }

        /// <summary>TEST-ONLY: resets the disposed flag and recreates singleton clients.</summary>
        internal static void ResetForTesting()
        {
            DisposeLazyValue(_client);
            DisposeLazyValue(_mediaClient);
            _client = CreateApiClientLazy();
            _mediaClient = CreateMediaClientLazy();
            System.Threading.Interlocked.Exchange(ref _disposed, 0);
        }

        internal static HttpClient CreateMediaClient()
            => CreateClient(allowAutoRedirect: false);

        private static HttpClient CreateClient()
            => CreateClient(allowAutoRedirect: true);

        private static Lazy<HttpClient> CreateApiClientLazy()
            => new Lazy<HttpClient>(CreateClient, isThreadSafe: true);

        private static Lazy<HttpClient> CreateMediaClientLazy()
            => new Lazy<HttpClient>(CreateMediaClient, isThreadSafe: true);

        private static HttpClient CreateClient(bool allowAutoRedirect)
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = QobuzarrConstants.Defaults.DefaultMaxConcurrencyPerHost,
                UseCookies = false,
                AllowAutoRedirect = allowAutoRedirect
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

        private static void DisposeClient(HttpClient client)
        {
            try { client.Dispose(); }
            catch { /* Non-critical - best-effort on unload path */ }
        }

        private static void DisposeLazyValue(Lazy<HttpClient> lazyClient)
        {
            if (lazyClient.IsValueCreated)
            {
                DisposeClient(lazyClient.Value);
            }
        }
    }
}
