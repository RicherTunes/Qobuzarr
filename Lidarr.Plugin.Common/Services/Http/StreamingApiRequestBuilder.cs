using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Lidarr.Plugin.Common.Utilities;

namespace Lidarr.Plugin.Common.Services.Http
{
    /// <summary>
    /// Builder for creating HTTP requests specific to streaming service APIs.
    /// Provides a fluent interface for common streaming service request patterns.
    /// </summary>
    public class StreamingApiRequestBuilder
    {
        private readonly string _baseUrl;
        private readonly Dictionary<string, string> _headers = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _queryParams = new Dictionary<string, string>();
        private HttpMethod _method = HttpMethod.Get;
        private object _bodyContent;
        private string _endpoint;
        private TimeSpan? _timeout;

        public StreamingApiRequestBuilder(string baseUrl)
        {
            _baseUrl = baseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(baseUrl));
        }

        /// <summary>
        /// Sets the API endpoint path.
        /// </summary>
        public StreamingApiRequestBuilder Endpoint(string endpoint)
        {
            _endpoint = endpoint?.TrimStart('/');
            return this;
        }

        /// <summary>
        /// Sets the HTTP method.
        /// </summary>
        public StreamingApiRequestBuilder Method(HttpMethod method)
        {
            _method = method ?? throw new ArgumentNullException(nameof(method));
            return this;
        }

        /// <summary>
        /// Convenience method for GET requests.
        /// </summary>
        public StreamingApiRequestBuilder Get() => Method(HttpMethod.Get);

        /// <summary>
        /// Convenience method for POST requests.
        /// </summary>
        public StreamingApiRequestBuilder Post() => Method(HttpMethod.Post);

        /// <summary>
        /// Convenience method for PUT requests.
        /// </summary>
        public StreamingApiRequestBuilder Put() => Method(HttpMethod.Put);

        /// <summary>
        /// Convenience method for DELETE requests.
        /// </summary>
        public StreamingApiRequestBuilder Delete() => Method(HttpMethod.Delete);

        /// <summary>
        /// Adds an authorization header with Bearer token.
        /// </summary>
        public StreamingApiRequestBuilder BearerToken(string token)
        {
            if (!string.IsNullOrEmpty(token))
            {
                _headers["Authorization"] = $"Bearer {token}";
            }
            return this;
        }

        /// <summary>
        /// Adds an API key header.
        /// </summary>
        public StreamingApiRequestBuilder ApiKey(string headerName, string apiKey)
        {
            if (!string.IsNullOrEmpty(headerName) && !string.IsNullOrEmpty(apiKey))
            {
                _headers[headerName] = apiKey;
            }
            return this;
        }

        /// <summary>
        /// Adds a custom header.
        /// </summary>
        public StreamingApiRequestBuilder Header(string name, string value)
        {
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
            {
                _headers[name] = value;
            }
            return this;
        }

        /// <summary>
        /// Adds multiple headers from a dictionary.
        /// </summary>
        public StreamingApiRequestBuilder Headers(Dictionary<string, string> headers)
        {
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    _headers[header.Key] = header.Value;
                }
            }
            return this;
        }

        /// <summary>
        /// Adds a query parameter.
        /// </summary>
        public StreamingApiRequestBuilder Query(string name, string value)
        {
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
            {
                _queryParams[name] = value;
            }
            return this;
        }

        /// <summary>
        /// Adds a query parameter with integer value.
        /// </summary>
        public StreamingApiRequestBuilder Query(string name, int value)
        {
            if (!string.IsNullOrEmpty(name))
            {
                _queryParams[name] = value.ToString();
            }
            return this;
        }

        /// <summary>
        /// Adds a query parameter with boolean value.
        /// </summary>
        public StreamingApiRequestBuilder Query(string name, bool value)
        {
            if (!string.IsNullOrEmpty(name))
            {
                _queryParams[name] = value.ToString().ToLowerInvariant();
            }
            return this;
        }

        /// <summary>
        /// Adds multiple query parameters from a dictionary.
        /// </summary>
        public StreamingApiRequestBuilder QueryParams(Dictionary<string, string> parameters)
        {
            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    if (!string.IsNullOrEmpty(param.Key) && !string.IsNullOrEmpty(param.Value))
                    {
                        _queryParams[param.Key] = param.Value;
                    }
                }
            }
            return this;
        }

        /// <summary>
        /// Sets JSON body content for POST/PUT requests.
        /// </summary>
        public StreamingApiRequestBuilder JsonBody(object content)
        {
            _bodyContent = content;
            _headers["Content-Type"] = "application/json";
            return this;
        }

        /// <summary>
        /// Sets form URL encoded body content.
        /// </summary>
        public StreamingApiRequestBuilder FormBody(Dictionary<string, string> formData)
        {
            _bodyContent = formData;
            _headers["Content-Type"] = "application/x-www-form-urlencoded";
            return this;
        }

        /// <summary>
        /// Sets a custom timeout for this request.
        /// </summary>
        public StreamingApiRequestBuilder Timeout(TimeSpan timeout)
        {
            _timeout = timeout;
            return this;
        }

        /// <summary>
        /// Sets common headers for music streaming APIs.
        /// </summary>
        public StreamingApiRequestBuilder WithStreamingDefaults(string userAgent = null)
        {
            _headers["Accept"] = "application/json";
            _headers["Accept-Encoding"] = "gzip, deflate";
            _headers["Accept-Language"] = "en-US,en;q=0.9";
            
            if (!string.IsNullOrEmpty(userAgent))
            {
                _headers["User-Agent"] = userAgent;
            }

            return this;
        }

        /// <summary>
        /// Adds cache control headers to prevent caching.
        /// </summary>
        public StreamingApiRequestBuilder NoCache()
        {
            _headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            _headers["Pragma"] = "no-cache";
            _headers["Expires"] = "0";
            return this;
        }

        /// <summary>
        /// Builds the final HttpRequestMessage.
        /// </summary>
        public HttpRequestMessage Build()
        {
            var url = BuildUrl();
            var request = new HttpRequestMessage(_method, url);

            // Add headers
            foreach (var header in _headers)
            {
                if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    continue; // Will be set with content

                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // Add body content
            if (_bodyContent != null && (_method == HttpMethod.Post || _method == HttpMethod.Put))
            {
                var contentType = _headers.GetValueOrDefault("Content-Type", "application/json");
                
                if (contentType.Contains("application/json"))
                {
                    var json = JsonSerializer.Serialize(_bodyContent);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }
                else if (contentType.Contains("application/x-www-form-urlencoded") && _bodyContent is Dictionary<string, string> formData)
                {
                    request.Content = new FormUrlEncodedContent(formData);
                }
            }

            return request;
        }

        /// <summary>
        /// Builds the request and returns information suitable for logging.
        /// Sensitive data (auth headers, tokens) are masked.
        /// </summary>
        public StreamingApiRequestInfo BuildForLogging()
        {
            var url = BuildUrl();
            var maskedHeaders = HttpClientExtensions.MaskSensitiveParams(_headers);
            var maskedQueryParams = HttpClientExtensions.MaskSensitiveParams(_queryParams);

            return new StreamingApiRequestInfo
            {
                Method = _method.Method,
                Url = url,
                Headers = maskedHeaders,
                QueryParameters = maskedQueryParams,
                HasBody = _bodyContent != null,
                Timeout = _timeout
            };
        }

        private string BuildUrl()
        {
            var url = string.IsNullOrEmpty(_endpoint) ? _baseUrl : $"{_baseUrl}/{_endpoint}";
            
            if (_queryParams.Any())
            {
                url = HttpClientExtensions.BuildUrlWithParams(url, _queryParams);
            }

            return url;
        }
    }

    /// <summary>
    /// Information about a streaming API request suitable for logging.
    /// </summary>
    public class StreamingApiRequestInfo
    {
        public string Method { get; set; }
        public string Url { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> QueryParameters { get; set; } = new Dictionary<string, string>();
        public bool HasBody { get; set; }
        public TimeSpan? Timeout { get; set; }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Method} {Url}");
            
            if (Headers.Any())
            {
                sb.AppendLine("Headers:");
                foreach (var header in Headers)
                {
                    sb.AppendLine($"  {header.Key}: {header.Value}");
                }
            }

            if (QueryParameters.Any())
            {
                sb.AppendLine("Query Parameters:");
                foreach (var param in QueryParameters)
                {
                    sb.AppendLine($"  {param.Key}: {param.Value}");
                }
            }

            if (HasBody)
            {
                sb.AppendLine("Body: [PRESENT]");
            }

            if (Timeout.HasValue)
            {
                sb.AppendLine($"Timeout: {Timeout.Value.TotalSeconds}s");
            }

            return sb.ToString();
        }
    }
}