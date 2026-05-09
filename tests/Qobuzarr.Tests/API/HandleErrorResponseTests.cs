using System.Net;
using Lidarr.Plugin.Qobuzarr.API;
using Xunit;

namespace Qobuzarr.Tests.API;

/// <summary>
/// Wave 62 UX-driven tests. Each HTTP status code path through
/// QobuzApiClient.HandleErrorResponse must produce an exception whose
/// message tells the user WHAT TO DO, not just THAT it failed.
/// </summary>
public sealed class HandleErrorResponseTests
{
    [Fact]
    public void Status401_TellsUserToCheckCredentialsOrReauthenticate()
    {
        var ex = Assert.Throws<QobuzApiException>(() =>
            QobuzApiClient.HandleErrorResponse(HttpStatusCode.Unauthorized, "{}"));

        Assert.Equal(401, ex.StatusCode);
        Assert.Equal("AuthenticationFailed", ex.ErrorType);
        // Must give the user something to act on
        Assert.True(
            ex.Message.Contains("credentials", System.StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("password", System.StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("re-authenticate", System.StringComparison.OrdinalIgnoreCase),
            $"401 message should hint at credentials/re-auth: {ex.Message}");
    }

    [Fact]
    public void Status403_NamesAppCredentialsAsLikelyCause()
    {
        var ex = Assert.Throws<QobuzApiException>(() =>
            QobuzApiClient.HandleErrorResponse(HttpStatusCode.Forbidden, "{}"));

        Assert.Equal(403, ex.StatusCode);
        Assert.Contains("credentials", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Status429_TellsUserToWait()
    {
        var ex = Assert.Throws<QobuzApiException>(() =>
            QobuzApiClient.HandleErrorResponse(HttpStatusCode.TooManyRequests, "{}"));

        Assert.Equal(429, ex.StatusCode);
        Assert.Equal("RateLimited", ex.ErrorType);
        Assert.True(
            ex.Message.Contains("wait", System.StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("retry", System.StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("slow down", System.StringComparison.OrdinalIgnoreCase),
            $"429 message should hint at retry/wait: {ex.Message}");
    }

    [Fact]
    public void Status500_DistinguishesServerSideFromUserAction()
    {
        var ex = Assert.Throws<QobuzApiException>(() =>
            QobuzApiClient.HandleErrorResponse(HttpStatusCode.InternalServerError, "{}"));

        Assert.Equal(500, ex.StatusCode);
        Assert.Equal("ServerError", ex.ErrorType);
        // 500-class is on Qobuz's side; the user shouldn't try changing settings.
        Assert.True(
            ex.Message.Contains("Qobuz", System.StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("retry", System.StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("temporarily", System.StringComparison.OrdinalIgnoreCase),
            $"500 message should signal server-side / temporary: {ex.Message}");
    }

    [Fact]
    public void Status404_RemainsCrispAndUnchanged()
    {
        // 404 is sometimes a real "we asked for the wrong ID" — the existing
        // crisp message is fine; just lock it down so a future refactor doesn't
        // accidentally bury it.
        var ex = Assert.Throws<QobuzApiException>(() =>
            QobuzApiClient.HandleErrorResponse(HttpStatusCode.NotFound, "{}"));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("NotFound", ex.ErrorType);
    }
}
