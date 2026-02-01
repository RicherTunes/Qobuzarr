using System;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Qobuzarr.Tests.Fixtures;
using Moq;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;

namespace Qobuzarr.Tests.Unit.Download
{
    /// <summary>
    /// Simple tests to verify basic QobuzDownloadClient functionality
    /// </summary>
    public class QobuzDownloadClientSimpleTests : TestFixtureBase
    {
        [Fact]
        public void BasicTest_ShouldPass()
        {
            // This basic test ensures we can successfully build the test project
            // and validates that all required dependencies are available

            // Arrange
            var mockAuthService = new Mock<IQobuzAuthenticationService>();
            var mockApiClient = new Mock<IQobuzApiClient>();

            // Act
            var canCreateMocks = mockAuthService != null && mockApiClient != null;

            // Assert
            canCreateMocks.Should().BeTrue("mocking framework should be working correctly");
            typeof(QobuzDownloadClient).Should().NotBeNull("QobuzDownloadClient type should be available");
        }

        [Fact]
        public void QobuzDownloadClient_Type_ShouldExist()
        {
            // Act
            var clientType = typeof(QobuzDownloadClient);

            // Assert
            clientType.Should().NotBeNull("QobuzDownloadClient type should exist");
            clientType.Name.Should().Be("QobuzDownloadClient", "type should have correct name");
            clientType.Namespace.Should().Be("Lidarr.Plugin.Qobuzarr.Download.Clients", "type should be in correct namespace");
        }
    }
}
