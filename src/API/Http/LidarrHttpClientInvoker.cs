using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;

namespace Lidarr.Plugin.Qobuzarr.API.Http
{
    /// <summary>
    /// Adapter that exposes Lidarr's <see cref="IQobuzHttpClient"/> (which itself wraps
    /// <see cref="NzbDrone.Common.Http.IHttpClient"/>) as a <see cref="HttpMessageInvoker"/> so the common
    /// <see cref="Lidarr.Plugin.Common.Services.Http.CachingHttpExecutor"/> can drive it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This adapter exists because the <c>CachingHttpExecutor</c> is built around
    /// <see cref="HttpMessageInvoker"/>, but the qobuzarr metadata API path goes through Lidarr's own
    /// <see cref="NzbDrone.Common.Http.IHttpClient"/> abstraction (which enforces user-agent and other
    /// host-side concerns). Translating <see cref="HttpRequestMessage"/> -&gt; <see cref="HttpRequest"/>
    /// preserves Lidarr's transport while letting the executor own cache, soft-revalidate, stale-if-error
    /// and terminal-eviction semantics.
    /// </para>
    /// <para>
    /// Resilience (rate limiting, retries, per-host gate) remains in
    /// <see cref="QobuzHttpClient.ExecuteAsync"/>, so the executor should be configured with a low-retry
    /// <see cref="Lidarr.Plugin.Common.Utilities.ResiliencePolicy"/> to avoid layered backoff.
    /// </para>
    /// </remarks>
    internal sealed class LidarrHttpClientInvoker : HttpMessageInvoker
    {
        private readonly IQobuzHttpClient _httpClient;
        private readonly Logger _logger;

        public LidarrHttpClientInvoker(IQobuzHttpClient httpClient, Logger logger)
            : base(new NoOpHandler(), disposeHandler: true)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            // Translate HttpRequestMessage -> Lidarr HttpRequest via the existing builder.
            // The builder ensures Lidarr-specific defaults (UA, Accept) are applied.
            var method = request.Method?.Method ?? "GET";
            var requestUri = request.RequestUri;

            // Split the URL into a base path + query pairs so Lidarr's HttpRequestBuilder can encode
            // each query param itself (consistent with the legacy QobuzApiClient behavior).
            // Passing a pre-encoded query string in the URL alone risks Lidarr re-decoding it on Build().
            string baseUrl;
            IEnumerable<KeyValuePair<string, string>> queryPairs;
            if (requestUri == null)
            {
                baseUrl = string.Empty;
                queryPairs = Array.Empty<KeyValuePair<string, string>>();
            }
            else
            {
                baseUrl = requestUri.GetLeftPart(UriPartial.Path);
                queryPairs = ParseQueryPairs(requestUri.Query);
            }

            var builder = _httpClient.BuildRequest(baseUrl, method);
            foreach (var pair in queryPairs)
            {
                builder.AddQueryParam(pair.Key, pair.Value);
            }

            // Apply non-default headers from the message (preserves any per-request mutations).
            if (request.Headers != null)
            {
                foreach (var header in request.Headers)
                {
                    if (string.Equals(header.Key, "Accept", StringComparison.OrdinalIgnoreCase))
                    {
                        // BuildRequest already sets Accept; skip to avoid duplication.
                        continue;
                    }
                    var value = header.Value != null ? string.Join(",", header.Value) : null;
                    if (!string.IsNullOrEmpty(value))
                    {
                        try { builder.SetHeader(header.Key, value); }
                        catch { /* ignore unsupported headers */ }
                    }
                }
            }

            var lidarrRequest = builder.Build();
            if (request.Content != null)
            {
                // Lidarr's HttpRequest.SetContent takes a string; serialize the body content as UTF-8.
                var body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(body))
                {
                    lidarrRequest.SetContent(body);
                    var contentType = request.Content.Headers?.ContentType?.ToString();
                    if (!string.IsNullOrEmpty(contentType))
                    {
                        lidarrRequest.Headers.ContentType = contentType;
                    }
                }
            }

            // Let HttpException propagate as-is. Legacy callers throw HttpException up to consumers
            // (e.g., E2EHermeticGateTests asserts on it), and QobuzHttpClient.ExecuteAsync already
            // exhausts retries/budget before re-throwing. CachingHttpExecutor inspects the returned
            // HttpResponseMessage's status code (it does not need a transport-level exception to
            // drive stale-if-error or terminal eviction).
            var lidarrResponse = await _httpClient.ExecuteAsync(lidarrRequest, cancellationToken).ConfigureAwait(false);
            return Translate(lidarrResponse, request.RequestUri);
        }

        private static HttpResponseMessage Translate(HttpResponse lidarrResponse, Uri requestUri)
        {
            var statusCode = lidarrResponse?.StatusCode ?? HttpStatusCode.InternalServerError;
            var message = new HttpResponseMessage(statusCode);

            // Body. Prefer ResponseData (raw bytes) when available so binary responses round-trip cleanly.
            byte[] body;
            if (lidarrResponse?.ResponseData != null && lidarrResponse.ResponseData.Length > 0)
            {
                body = lidarrResponse.ResponseData;
            }
            else if (!string.IsNullOrEmpty(lidarrResponse?.Content))
            {
                body = System.Text.Encoding.UTF8.GetBytes(lidarrResponse.Content);
            }
            else
            {
                body = Array.Empty<byte>();
            }

            var byteContent = new ByteArrayContent(body);

            // Headers — split into request/content headers per HttpClient conventions.
            string? contentType = null;
            if (lidarrResponse?.Headers != null)
            {
                try { contentType = lidarrResponse.Headers.ContentType; }
                catch { /* lidarr headers can throw on malformed values */ }

                foreach (var header in lidarrResponse.Headers)
                {
                    var name = header.Key;
                    var value = header.Value?.ToString();
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(value)) continue;

                    if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "Last-Modified", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "Content-Encoding", StringComparison.OrdinalIgnoreCase))
                    {
                        try { byteContent.Headers.TryAddWithoutValidation(name, value); }
                        catch { /* ignore */ }
                    }
                    else
                    {
                        try { message.Headers.TryAddWithoutValidation(name, value); }
                        catch { /* ignore */ }
                    }
                }
            }

            if (!string.IsNullOrEmpty(contentType))
            {
                try { byteContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType); }
                catch { /* ignore */ }
            }

            message.Content = byteContent;
            if (requestUri != null)
            {
                try { message.RequestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri); }
                catch { /* best-effort */ }
            }

            return message;
        }

        private static IEnumerable<KeyValuePair<string, string>> ParseQueryPairs(string query)
        {
            if (string.IsNullOrEmpty(query)) yield break;
            var trimmed = query.TrimStart('?');
            if (string.IsNullOrEmpty(trimmed)) yield break;

            foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0)
                {
                    var key = Uri.UnescapeDataString(pair);
                    if (!string.IsNullOrEmpty(key))
                    {
                        yield return new KeyValuePair<string, string>(key, string.Empty);
                    }
                    continue;
                }

                var rawKey = pair.Substring(0, eq);
                var rawValue = pair.Substring(eq + 1);
                var keyDecoded = Uri.UnescapeDataString(rawKey);
                var valueDecoded = Uri.UnescapeDataString(rawValue);
                if (!string.IsNullOrEmpty(keyDecoded))
                {
                    yield return new KeyValuePair<string, string>(keyDecoded, valueDecoded);
                }
            }
        }

        private sealed class NoOpHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => throw new InvalidOperationException("LidarrHttpClientInvoker overrides SendAsync; the inner handler should never be called.");
        }
    }
}
