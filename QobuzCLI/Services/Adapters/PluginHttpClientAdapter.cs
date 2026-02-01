using System;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.API.Http;
using NzbDrone.Common.Http;
// Use alias to resolve HttpClient ambiguity
using HttpClient = System.Net.Http.HttpClient;
using HttpMethod = System.Net.Http.HttpMethod;

namespace QobuzCLI.Services.Adapters
{
    /// <summary>
    /// Adapter that implements the plugin's IQobuzHttpClient interface from API.Http namespace
    /// for use in CLI context. This bridges the CLI's HttpClient to the plugin's HTTP interface.
    /// </summary>
    public class PluginHttpClientAdapter : IQobuzHttpClient
    {
        private readonly HttpClient _httpClient;
        // Base URL is configured upstream in request builders (not needed here)

        public PluginHttpClientAdapter(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<HttpResponse> ExecuteAsync(HttpRequest request, CancellationToken cancellationToken = default)
        {
            // Create HttpRequestMessage from plugin's HttpRequest
            var requestMessage = new HttpRequestMessage
            {
                Method = ConvertMethod(request.Method),
                RequestUri = new Uri(request.Url.ToString())
            };

            // Add headers
            if (request.Headers != null)
            {
                foreach (var header in request.Headers)
                {
                    requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            // Add content if present
            if (request.ContentData != null && request.ContentData.Length > 0)
            {
                requestMessage.Content = new ByteArrayContent(request.ContentData);
                if (request.Headers?.ContentType != null)
                {
                    requestMessage.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(request.Headers.ContentType);
                }
            }

            // Execute request
            var response = await _httpClient.SendAsync(requestMessage, cancellationToken);

            // Create response headers
            var responseHeaders = new HttpHeader();
            foreach (var header in response.Headers)
            {
                responseHeaders[header.Key] = string.Join(", ", header.Value);
            }

            // Read response content
            var responseContent = await response.Content.ReadAsStringAsync();

            // Create HttpResponse using the proper constructor
            var httpResponse = new HttpResponse(request, responseHeaders, responseContent, response.StatusCode);

            return httpResponse;
        }

        public HttpRequestBuilder BuildRequest(string url, string method = "GET")
        {
            // Create a new request builder for the given URL
            var builder = new HttpRequestBuilder(url);

            // Set method based on string input
            builder.Method = method.ToUpperInvariant() switch
            {
                "POST" => HttpMethod.Post,
                "PUT" => HttpMethod.Put,
                "DELETE" => HttpMethod.Delete,
                "HEAD" => HttpMethod.Head,
                "OPTIONS" => HttpMethod.Options,
                "PATCH" => new HttpMethod("PATCH"),
                _ => HttpMethod.Get
            };

            // Add default headers
            builder.SetHeader("User-Agent", "Qobuzarr/1.0");
            builder.SetHeader("Accept", "application/json");

            return builder;
        }

        private HttpMethod ConvertMethod(HttpMethod method)
        {
            // System.Net.Http.HttpMethod is used by both namespaces
            // Direct conversion is possible since they're the same type
            return method;
        }
    }
}
