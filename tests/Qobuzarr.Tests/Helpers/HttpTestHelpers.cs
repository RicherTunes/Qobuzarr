using System.Net;
using NzbDrone.Common.Http;

namespace Qobuzarr.Tests.Helpers
{
    /// <summary>
    /// Helper class for creating HTTP-related test objects
    /// </summary>
    public static class HttpTestHelpers
    {
        /// <summary>
        /// Creates a mock HttpRequest for testing
        /// </summary>
        /// <param name="url">The URL for the request</param>
        /// <param name="method">The HTTP method (default: GET)</param>
        /// <returns>A configured HttpRequest</returns>
        public static HttpRequest CreateRequest(string url = "http://test.com", System.Net.Http.HttpMethod method = null)
        {
            var request = new HttpRequest(url);
            if (method != null)
            {
                request.Method = method;
            }
            return request;
        }

        /// <summary>
        /// Creates a mock HttpResponse for testing
        /// </summary>
        /// <param name="content">The response content</param>
        /// <param name="statusCode">The HTTP status code</param>
        /// <param name="request">The associated request (will create a default if null)</param>
        /// <returns>A configured HttpResponse</returns>
        public static HttpResponse CreateResponse(string content = "{}", HttpStatusCode statusCode = HttpStatusCode.OK, HttpRequest request = null)
        {
            request ??= CreateRequest();
            var headers = new HttpHeader();
            headers.ContentType = "application/json";

            return new HttpResponse(request, headers, content, statusCode);
        }

        /// <summary>
        /// Creates an HttpResponse with binary data
        /// </summary>
        /// <param name="data">The binary response data</param>
        /// <param name="statusCode">The HTTP status code</param>
        /// <param name="request">The associated request (will create a default if null)</param>
        /// <returns>A configured HttpResponse</returns>
        public static HttpResponse CreateBinaryResponse(byte[] data, HttpStatusCode statusCode = HttpStatusCode.OK, HttpRequest request = null)
        {
            request ??= CreateRequest();
            var headers = new HttpHeader();
            headers.ContentType = "application/octet-stream";

            return new HttpResponse(request, headers, data, statusCode);
        }

        /// <summary>
        /// Creates an HttpResponse for error scenarios
        /// </summary>
        /// <param name="statusCode">The error status code</param>
        /// <param name="errorContent">The error content</param>
        /// <param name="request">The associated request (will create a default if null)</param>
        /// <returns>A configured HttpResponse representing an error</returns>
        public static HttpResponse CreateErrorResponse(HttpStatusCode statusCode, string errorContent = "Error", HttpRequest request = null)
        {
            request ??= CreateRequest();
            var headers = new HttpHeader();
            headers.ContentType = "application/json";

            return new HttpResponse(request, headers, errorContent, statusCode);
        }
    }
}
