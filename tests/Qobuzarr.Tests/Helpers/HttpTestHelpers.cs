using System.Net;
using System.Net.Http;
using NzbDrone.Common.Http;

// Forwarding shim — delegates all calls to the shared TestKit factory.
// Consumers keep their existing `using Qobuzarr.Tests.Helpers;` import.
// See Lidarr.Plugin.Common.TestKit.Http.HttpResponseFactory for the canonical source.

namespace Qobuzarr.Tests.Helpers;

/// <summary>
/// Passthrough shim for <see cref="Lidarr.Plugin.Common.TestKit.Http.HttpResponseFactory"/>.
/// New code should import <c>Lidarr.Plugin.Common.TestKit.Http</c> directly.
/// </summary>
public static class HttpTestHelpers
{
    public static HttpRequest CreateRequest(string url = "http://test.com", HttpMethod? method = null)
    {
        var request = new HttpRequest(url);
        if (method != null)
        {
            request.Method = method;
        }

        return request;
    }

    public static HttpResponse CreateResponse(string content = "{}", HttpStatusCode statusCode = HttpStatusCode.OK, HttpRequest? request = null)
    {
        request ??= CreateRequest();
        return statusCode == HttpStatusCode.OK
            ? Lidarr.Plugin.Common.TestKit.Http.HttpResponseFactory.Ok(request, content)
            : Lidarr.Plugin.Common.TestKit.Http.HttpResponseFactory.Error(request, statusCode, content);
    }

    public static HttpResponse CreateBinaryResponse(byte[] data, HttpStatusCode statusCode = HttpStatusCode.OK, HttpRequest? request = null)
    {
        request ??= CreateRequest();
        return Lidarr.Plugin.Common.TestKit.Http.HttpResponseFactory.CreateBinaryResponse(request, data, statusCode);
    }

    public static HttpResponse CreateErrorResponse(HttpStatusCode statusCode, string errorContent = "Error", HttpRequest? request = null)
    {
        request ??= CreateRequest();
        return Lidarr.Plugin.Common.TestKit.Http.HttpResponseFactory.Error(request, statusCode, errorContent);
    }
}
