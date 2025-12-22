using System;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Security;
using Lidarr.Plugin.Qobuzarr.Indexers;

namespace Qobuzarr.Tests.Unit.Indexers
{
    /// <summary>
    /// Tests to verify QobuzIndexer can be constructed without accessing Settings property.
    /// This prevents the DryIoc NullReferenceException that occurs when Definition is not set.
    /// </summary>
    public class QobuzIndexerConstructorTests
    {
        private readonly Mock<IHttpClient> _httpClientMock;
        private readonly Mock<IIndexerStatusService> _indexerStatusServiceMock;
        private readonly Mock<IConfigService> _configServiceMock;
        private readonly Mock<IParsingService> _parsingServiceMock;
        private readonly Mock<IQobuzAuthenticationService> _authServiceMock;
        private readonly Mock<IQobuzApiClient> _apiClientMock;
        private readonly Mock<ISecureMLModelLoader> _secureModelLoaderMock;
        private readonly Logger _logger;

        public QobuzIndexerConstructorTests()
        {
            _httpClientMock = new Mock<IHttpClient>();
            _indexerStatusServiceMock = new Mock<IIndexerStatusService>();
            _configServiceMock = new Mock<IConfigService>();
            _parsingServiceMock = new Mock<IParsingService>();
            _authServiceMock = new Mock<IQobuzAuthenticationService>();
            _apiClientMock = new Mock<IQobuzApiClient>();
            _secureModelLoaderMock = new Mock<ISecureMLModelLoader>();
            _logger = LogManager.CreateNullLogger();
        }

        /// <summary>
        /// Verifies that QobuzIndexer can be constructed without throwing NullReferenceException.
        /// This simulates DI construction where Definition is not yet set.
        /// 
        /// Prior to the fix, this test would fail with:
        /// System.NullReferenceException at IndexerBase.get_Settings()
        /// </summary>
        [Fact]
        public void Constructor_WithoutDefinition_ShouldNotThrow()
        {
            // Act
            var exception = Record.Exception(() =>
            {
                var indexer = new QobuzIndexer(
                    _httpClientMock.Object,
                    _indexerStatusServiceMock.Object,
                    _configServiceMock.Object,
                    _parsingServiceMock.Object,
                    _authServiceMock.Object,
                    _apiClientMock.Object,
                    _secureModelLoaderMock.Object,
                    _logger);
            });

            // Assert
            exception.Should().BeNull("constructor should not access Settings property before Definition is set");
        }

        /// <summary>
        /// Verifies that QobuzIndexer properties that don't require settings work immediately.
        /// </summary>
        [Fact]
        public void Constructor_WithoutDefinition_BasicPropertiesShouldWork()
        {
            // Arrange
            var indexer = new QobuzIndexer(
                _httpClientMock.Object,
                _indexerStatusServiceMock.Object,
                _configServiceMock.Object,
                _parsingServiceMock.Object,
                _authServiceMock.Object,
                _apiClientMock.Object,
                _secureModelLoaderMock.Object,
                _logger);

            // Act & Assert - these should not throw even without Definition
            indexer.Name.Should().NotBeNullOrEmpty();
            indexer.Protocol.Should().NotBeNullOrEmpty();
            indexer.SupportsRss.Should().BeFalse();
            indexer.SupportsSearch.Should().BeTrue();
        }

        /// <summary>
        /// Verifies that lazy initialization defers Settings access until actually needed.
        /// </summary>
        [Fact]
        public void Constructor_LazyManagers_ShouldNotAccessSettingsImmediately()
        {
            // Arrange & Act
            var indexer = new QobuzIndexer(
                _httpClientMock.Object,
                _indexerStatusServiceMock.Object,
                _configServiceMock.Object,
                _parsingServiceMock.Object,
                _authServiceMock.Object,
                _apiClientMock.Object,
                _secureModelLoaderMock.Object,
                _logger);

            // Assert - The indexer was constructed successfully, proving Settings wasn't accessed
            // If Settings was accessed in the constructor, we would have gotten NullReferenceException
            indexer.Should().NotBeNull();
        }
    }
}
