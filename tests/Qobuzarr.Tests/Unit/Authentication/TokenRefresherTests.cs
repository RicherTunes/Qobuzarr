using System;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Moq;
using NLog;
using Xunit;

namespace Qobuzarr.Tests.Unit.Authentication;

[Trait("Category", "Unit")]
public class TokenRefresherTests
{
    private readonly Mock<IQobuzAuthenticationService> _mockAuth = new();
    private readonly TokenRefresher _refresher;

    public TokenRefresherTests()
    {
        _refresher = new TokenRefresher(_mockAuth.Object, LogManager.GetCurrentClassLogger());
    }

    private static QobuzSession CreateSession(TimeSpan expiresIn) => new()
    {
        UserId = "12345",
        AuthToken = "token-abc",
        ExpiresAt = DateTime.UtcNow.Add(expiresIn),
        CreatedAt = DateTime.UtcNow
    };

    #region ShouldRefresh

    [Fact]
    public void ShouldRefresh_NullSession_ReturnsFalse()
    {
        Assert.False(_refresher.ShouldRefresh(null!));
    }

    [Fact]
    public void ShouldRefresh_ExpiredSession_ReturnsFalse()
    {
        var session = CreateSession(TimeSpan.FromMinutes(-10));
        Assert.False(_refresher.ShouldRefresh(session));
    }

    [Fact]
    public void ShouldRefresh_FreshSession_ReturnsFalse()
    {
        var session = CreateSession(TimeSpan.FromHours(24));
        Assert.False(_refresher.ShouldRefresh(session));
    }

    [Fact]
    public void ShouldRefresh_NearExpiry_ReturnsTrue()
    {
        var session = CreateSession(TimeSpan.FromMinutes(2));
        Assert.True(_refresher.ShouldRefresh(session));
    }

    [Fact]
    public void ShouldRefresh_InvalidSession_ReturnsFalse()
    {
        var session = new QobuzSession { UserId = null, AuthToken = null, ExpiresAt = DateTime.UtcNow.AddHours(1) };
        Assert.False(_refresher.ShouldRefresh(session));
    }

    #endregion

    #region CanRefreshSession

    [Fact]
    public void CanRefreshSession_NullSession_ReturnsFalse()
    {
        Assert.False(_refresher.CanRefreshSession(null!));
    }

    [Fact]
    public void CanRefreshSession_ValidSession_ReturnsTrue()
    {
        var session = CreateSession(TimeSpan.FromHours(1));
        Assert.True(_refresher.CanRefreshSession(session));
    }

    [Fact]
    public void CanRefreshSession_ExpiredSession_ReturnsFalse()
    {
        var session = CreateSession(TimeSpan.FromMinutes(-10));
        Assert.False(_refresher.CanRefreshSession(session));
    }

    #endregion

    #region ShouldRefreshToken

    [Fact]
    public void ShouldRefreshToken_EmptyToken_ReturnsFalse()
    {
        Assert.False(_refresher.ShouldRefreshToken(""));
    }

    [Fact]
    public void ShouldRefreshToken_NullToken_ReturnsFalse()
    {
        Assert.False(_refresher.ShouldRefreshToken(null!));
    }

    [Fact]
    public void ShouldRefreshToken_NoCachedSession_ReturnsFalse()
    {
        _mockAuth.Setup(x => x.GetCachedSession()).Returns((QobuzSession?)null);
        Assert.False(_refresher.ShouldRefreshToken("valid-token"));
    }

    [Fact]
    public void ShouldRefreshToken_FreshCachedSession_ReturnsFalse()
    {
        _mockAuth.Setup(x => x.GetCachedSession()).Returns(CreateSession(TimeSpan.FromHours(24)));
        Assert.False(_refresher.ShouldRefreshToken("valid-token"));
    }

    #endregion

    #region RefreshSessionAsync (null guard)

    [Fact]
    public async Task RefreshSessionAsync_NullSession_ThrowsArgumentNull()
    {
        var creds = new QobuzCredentials { Email = "a@b.com", MD5Password = "abc123" };
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _refresher.RefreshSessionAsync(null!, creds));
    }

    [Fact]
    public async Task RefreshSessionAsync_NullCredentials_ThrowsArgumentNull()
    {
        var session = CreateSession(TimeSpan.FromHours(1));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _refresher.RefreshSessionAsync(session, null!));
    }

    #endregion

    #region RefreshSessionAsync (single-arg, returns same session)

    [Fact]
    public async Task RefreshSessionAsync_WithoutCredentials_ReturnsSameSession()
    {
        var session = CreateSession(TimeSpan.FromHours(1));
        var result = await _refresher.RefreshSessionAsync(session);
        Assert.Same(session, result);
    }

    [Fact]
    public async Task RefreshSessionAsync_NullSessionSingleArg_ReturnsNull()
    {
        var result = await _refresher.RefreshSessionAsync((QobuzSession?)null);
        Assert.Null(result);
    }

    #endregion

    #region Dispose

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var refresher = new TokenRefresher(_mockAuth.Object, LogManager.GetCurrentClassLogger());
        refresher.Dispose();
    }

    [Fact]
    public void Dispose_Idempotent()
    {
        var refresher = new TokenRefresher(_mockAuth.Object, LogManager.GetCurrentClassLogger());
        refresher.Dispose();
        refresher.Dispose();
    }

    #endregion
}
