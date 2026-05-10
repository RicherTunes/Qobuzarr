using System;
using System.Net;
using System.Net.Http;
using Lidarr.Plugin.Common.Services.Performance;
using Lidarr.Plugin.Qobuzarr.Configuration;
using Lidarr.Plugin.Qobuzarr.Constants;
using Lidarr.Plugin.Qobuzarr.Integration.Bridge;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lidarr.Plugin.Qobuzarr.Services.Http
{
    /// <summary>
    /// Singleton-scoped <see cref="System.Net.Http.HttpClient"/> used by qobuzarr's audio
    /// download paths (full FLAC/MP3 body streaming with HTTP range support). Lidarr's
    /// own <c>IHttpClient</c> abstraction is optimised for short API calls and does not
    /// expose <see cref="HttpClient.SendAsync(HttpRequestMessage, HttpCompletionOption, System.Threading.CancellationToken)"/>
    /// with <see cref="HttpCompletionOption.ResponseHeadersRead"/> — that is the only
    /// path that lets us stream chunks to disk without buffering the entire file in RAM.
    /// So this class wraps a raw <see cref="HttpClient"/>, configured with sensible
    /// connection-pool limits, and chains <see cref="QobuzRateLimitingHandler"/> in front
    /// of the transport so every audio request is gated by
    /// <see cref="IUniversalAdaptiveRateLimiter"/> alongside the API path.
    ///
    /// Prior to this class being DI-registered, it was a static class with a
    /// <c>Lazy&lt;HttpClient&gt;</c> whose handler chain had no rate-limit awareness —
    /// audio downloads (large + numerous) bypassed the budget entirely. That gap is
    /// closed here.
    /// </summary>
    public sealed class SharedSystemHttpClient : IDisposable
    {
        private readonly HttpClient _client;
        private bool _disposed;

        public SharedSystemHttpClient(IUniversalAdaptiveRateLimiter? rateLimiter = null,
                                      ILogger<QobuzRateLimitingHandler>? rateLimiterLogger = null)
        {
            var sockets = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = QobuzarrConstants.Defaults.DefaultMaxConcurrencyPerHost,
                UseCookies = false,
                AllowAutoRedirect = true
            };

            // When IUniversalAdaptiveRateLimiter is available (bridge DI path or any
            // container that explicitly registers it), chain QobuzRateLimitingHandler in
            // front of the transport so every audio request honours the global budget
            // and Retry-After. When unavailable (DryIoC auto-discovery without an
            // explicit registration), fall back to the raw transport — same shape as
            // the QobuzHttpClient pattern where the limiter is optional. Audio downloads
            // then behave as they did before this refactor (no per-request gating).
            HttpMessageHandler handler = sockets;
            if (rateLimiter is not null)
            {
                handler = new QobuzRateLimitingHandler(rateLimiter, rateLimiterLogger ?? NullLogger<QobuzRateLimitingHandler>.Instance)
                {
                    InnerHandler = sockets
                };
            }

            _client = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromMinutes(10)
            };

            try
            {
                _client.DefaultRequestHeaders.UserAgent.ParseAdd(QobuzConstants.Api.UserAgent);
                _client.DefaultRequestHeaders.ConnectionClose = false;
                _client.DefaultRequestHeaders.ExpectContinue = false;
            }
            catch
            {
                // Header parse failures are non-fatal — the defaults are still useful.
            }
        }

        /// <summary>
        /// The underlying rate-limited <see cref="HttpClient"/>. Callers should use this
        /// directly — methods like
        /// <see cref="HttpClient.SendAsync(HttpRequestMessage, HttpCompletionOption, System.Threading.CancellationToken)"/>
        /// flow through the rate-limit handler transparently.
        /// </summary>
        public HttpClient HttpClient => _client;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _client.Dispose();
        }
    }
}
