using System;
using System.Collections.Generic;
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

