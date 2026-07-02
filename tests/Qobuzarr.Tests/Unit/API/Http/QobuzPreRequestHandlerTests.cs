using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Services.Http;
using Lidarr.Plugin.Qobuzarr.API.PreRequest;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Moq;
using NLog;
using Xunit;

namespace Qobuzarr.Tests.Unit.API.Http
{
    public class QobuzPreRequestHandlerTests
    {
        // LOOP-011 (#23): when many requests find the session invalid at once, exactly ONE re-login must
        // run (single-flight) -- not N parallel full re-auths (each scrapes the web player + hits the login
        // rate limit). Before the SemaphoreSlim + recheck-under-gate fix, all concurrent callers re-authed.
        [Fact]
        public async Task EnsureValidSession_ConcurrentInvalid_ReauthenticatesExactlyOnce()
        {
            QobuzSession current = new QobuzSession { AuthToken = "stale", AppId = "a", ExpiresAt = DateTime.UtcNow.AddHours(-1) };
            var authCalls = 0;

            var authService = new Mock<IQobuzAuthenticationService>();
            authService.Setup(x => x.GetCachedSession()).Returns(() => Volatile.Read(ref current));
            authService.Setup(x => x.ValidateSessionAsync(It.IsAny<QobuzSession>()))
                .ReturnsAsync((QobuzSession s) => s != null && s.AuthToken == "valid");
            authService.Setup(x => x.AuthenticateAsync(It.IsAny<QobuzCredentials>()))
                .Returns(async (QobuzCredentials _) =>
                {
                    Interlocked.Increment(ref authCalls);
                    await Task.Delay(60).ConfigureAwait(false); // widen the race window
                    return new QobuzSession { AuthToken = "valid", AppId = "a", ExpiresAt = DateTime.UtcNow.AddHours(1) };
                });
            authService.Setup(x => x.StoreSession(It.IsAny<QobuzSession>()))
                .Callback((QobuzSession s) => Volatile.Write(ref current, s));

            var signer = new Mock<IRequestSigner>();
            Func<Task<QobuzCredentials>> creds = () => Task.FromResult(new QobuzCredentials());
            var sut = new QobuzPreRequestHandler(authService.Object, signer.Object, creds, LogManager.GetCurrentClassLogger());

            await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => sut.EnsureValidSessionAsync())));

            authCalls.Should().Be(1, "single-flight must collapse N concurrent renewals into one re-auth");
        }

        [Fact]
        public void InjectAuthParameters_WithCachedSession_AddsAppIdAndUserAuthToken()
        {
            var session = new QobuzSession
            {
                AppId = "app_123",
                AuthToken = "sample_auth_token_123456",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };

            var authService = new Mock<IQobuzAuthenticationService>();
            authService.Setup(x => x.GetCachedSession()).Returns(session);

            var signer = new Mock<IRequestSigner>();

            Func<Task<QobuzCredentials>> credsProvider = () => Task.FromResult(new QobuzCredentials());
            var logger = LogManager.GetCurrentClassLogger();

            var sut = new QobuzPreRequestHandler(authService.Object, signer.Object, credsProvider, logger);

            var parameters = new Dictionary<string, string>();
            sut.InjectAuthParameters(parameters);

            parameters.Should().ContainKey("app_id").WhoseValue.Should().Be(session.AppId);
            parameters.Should().ContainKey("user_auth_token").WhoseValue.Should().Be(session.AuthToken);
        }

        [Fact]
        public void InjectAuthParameters_WithNoSession_DoesNotAddAuthParameters()
        {
            var authService = new Mock<IQobuzAuthenticationService>();
            authService.Setup(x => x.GetCachedSession()).Returns((QobuzSession)null);

            var signer = new Mock<IRequestSigner>();

            Func<Task<QobuzCredentials>> credsProvider = () => Task.FromResult(new QobuzCredentials());
            var logger = LogManager.GetCurrentClassLogger();

            var sut = new QobuzPreRequestHandler(authService.Object, signer.Object, credsProvider, logger);

            var parameters = new Dictionary<string, string>
            {
                ["query"] = "test"
            };

            sut.InjectAuthParameters(parameters);

            parameters.Should().NotContainKey("app_id");
            parameters.Should().NotContainKey("user_auth_token");
            parameters.Should().ContainKey("query");
        }
    }
}

