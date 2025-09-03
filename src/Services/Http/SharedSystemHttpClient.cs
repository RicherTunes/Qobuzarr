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

        public static HttpClient Instance => _client.Value;

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

