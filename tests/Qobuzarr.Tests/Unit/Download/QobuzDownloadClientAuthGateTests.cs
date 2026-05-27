using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Services.Bridge;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Exceptions;
using Xunit;

namespace Qobuzarr.Tests.Unit.Download;

[Trait("Category", "Unit")]
public class QobuzDownloadClientAuthGateTests
{
    #region LooksLikeAuthFailure

    [Fact]
    public void LooksLikeAuthFailure_Http401_ReturnsTrue()
    {
        var ex = new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized);
        Assert.True(QobuzDownloadClient.LooksLikeAuthFailure(ex));
    }

    [Fact]
    public void LooksLikeAuthFailure_Http403_ReturnsTrue()
    {
        var ex = new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden);
        Assert.True(QobuzDownloadClient.LooksLikeAuthFailure(ex));
    }

    [Fact]
    public void LooksLikeAuthFailure_Http500_ReturnsFalse()
    {
        var ex = new HttpRequestException("Server error", null, HttpStatusCode.InternalServerError);
        Assert.False(QobuzDownloadClient.LooksLikeAuthFailure(ex));
    }

    [Fact]
    public void LooksLikeAuthFailure_QobuzApiException401_ReturnsTrue()
    {
        var ex = new QobuzApiException("Unauthorized", "/test", HttpStatusCode.Unauthorized);
        Assert.True(QobuzDownloadClient.LooksLikeAuthFailure(ex));
    }

    [Fact]
    public void LooksLikeAuthFailure_QobuzApiException403_ReturnsTrue()
    {
        var ex = new QobuzApiException("Forbidden", "/test", HttpStatusCode.Forbidden);
        Assert.True(QobuzDownloadClient.LooksLikeAuthFailure(ex));
    }

    [Fact]
    public void LooksLikeAuthFailure_QobuzApiException429_ReturnsFalse()
    {
        var ex = new QobuzApiException("Rate limited", "/test", HttpStatusCode.TooManyRequests);
        Assert.False(QobuzDownloadClient.LooksLikeAuthFailure(ex));
    }

    [Fact]
    public void LooksLikeAuthFailure_GenericException_ReturnsFalse()
    {
        Assert.False(QobuzDownloadClient.LooksLikeAuthFailure(new InvalidOperationException("Something broke")));
    }

    [Fact]
    public void LooksLikeAuthFailure_TimeoutException_ReturnsFalse()
    {
        Assert.False(QobuzDownloadClient.LooksLikeAuthFailure(new TaskCanceledException("Timeout")));
    }

    #endregion

    #region IsAuthShortCircuited

    [Fact]
    public void IsAuthShortCircuited_NullGate_ReturnsFalse()
    {
        Assert.False(QobuzDownloadClient.IsAuthShortCircuited(null));
    }

    [Fact]
    public void IsAuthShortCircuited_HealthyGate_ReturnsFalse()
    {
        var handler = new DefaultAuthFailureHandler(Microsoft.Extensions.Logging.Abstractions.NullLogger<DefaultAuthFailureHandler>.Instance);
        var gate = new AuthFailureGate(handler);
        Assert.False(QobuzDownloadClient.IsAuthShortCircuited(gate));
    }

    #endregion

    #region RecordAuthOutcomeFromException

    [Fact]
    public void RecordAuthOutcomeFromException_NullGate_DoesNotThrow()
    {
        QobuzDownloadClient.RecordAuthOutcomeFromException(null,
            new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized));
    }

    [Fact]
    public void RecordAuthOutcomeFromException_NonAuthException_DoesNotRecordFailure()
    {
        var handler = new DefaultAuthFailureHandler(Microsoft.Extensions.Logging.Abstractions.NullLogger<DefaultAuthFailureHandler>.Instance);
        var gate = new AuthFailureGate(handler);

        QobuzDownloadClient.RecordAuthOutcomeFromException(gate, new InvalidOperationException("Not auth"));

        Assert.True(gate.IsHealthy);
    }

    [Fact]
    public void RecordAuthOutcomeFromException_AuthException_RecordsFailure()
    {
        var handler = new DefaultAuthFailureHandler(Microsoft.Extensions.Logging.Abstractions.NullLogger<DefaultAuthFailureHandler>.Instance);
        var gate = new AuthFailureGate(handler);

        QobuzDownloadClient.RecordAuthOutcomeFromException(gate,
            new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized));

        Assert.False(gate.IsHealthy);
    }

    #endregion
}
