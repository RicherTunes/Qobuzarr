using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NSubstitute;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Security;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Lidarr.Plugin.Qobuzarr.Constants;
using Lidarr.Plugin.Qobuzarr.Download;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Coverage tests for QobuzIndexer.
    /// Tests constructor validation, RequestAction routing, and disposal.
    /// Source: src/Indexers/QobuzIndexer.cs
    /// </summary>
    public class QobuzIndexerCovTests
    {
        private readonly Mock<IHttpClient> _httpClientMock;
        private readonly Mock<IIndexerStatusService> _indexerStatusServiceMock;
        private readonly Mock<IConfigService> _configServiceMock;
        private readonly Mock<IParsingService> _parsingServiceMock;
        private readonly Mock<IQobuzAuthenticationService> _authServiceMock;
        private readonly Mock<IQobuzApiClient> _apiClientMock;
        private readonly Mock<ISecureMLModelLoader> _secureModelLoaderMock;
        private readonly Logger _logger;

        public QobuzIndexerCovTests()
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
        /// Test for ArgumentNullException at line 66:
        /// _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        /// </summary>
        [Fact]
        public void Constructor_NullApiClient_ThrowsArgumentNullException()
        {
            // Act
            var act = () => new QobuzIndexer(
                _httpClientMock.Object,
                _indexerStatusServiceMock.Object,
                _configServiceMock.Object,
                _parsingServiceMock.Object,
                _authServiceMock.Object,
                null!, // apiClient is null - triggers ArgumentNullException at line 66
                _secureModelLoaderMock.Object,
                _logger);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("apiClient");
        }

        /// <summary>
        /// Verifies Name property returns "Qobuzarr" (line 36).
        /// public override string Name => QobuzarrConstants.PluginName;
        /// </summary>
        [Fact]
        public void Name_ReturnsQobuzarrConstantsPluginName()
        {
            // Arrange
            var indexer = CreateIndexer();

            // Act & Assert
            indexer.Name.Should().Be(QobuzarrConstants.PluginName);
        }

        /// <summary>
        /// Verifies PageSize property returns 100 (line 41).
        /// public override int PageSize => 100;
        /// </summary>
        [Fact]
        public void PageSize_Returns100()
        {
            // Arrange
            var indexer = CreateIndexer();

            // Act & Assert
            indexer.PageSize.Should().Be(100);
        }

        /// <summary>
        /// Verifies SupportsRss property returns false (line 39).
        /// public override bool SupportsRss => false;
        /// </summary>
        [Fact]
        public void SupportsRss_ReturnsFalse()
        {
            // Arrange
            var indexer = CreateIndexer();

            // Act & Assert
            indexer.SupportsRss.Should().BeFalse();
        }

        /// <summary>
        /// Verifies SupportsSearch property returns true (line 40).
        /// public override bool SupportsSearch => true;
        /// </summary>
        [Fact]
        public void SupportsSearch_ReturnsTrue()
        {
            // Arrange
            var indexer = CreateIndexer();

            // Act & Assert
            indexer.SupportsSearch.Should().BeTrue();
        }

        /// <summary>
        /// Verifies GetRequestGenerator returns QobuzRequestGenerator (line 118).
        /// public override IIndexerRequestGenerator GetRequestGenerator()
        /// </summary>
        [Fact]
        public void GetRequestGenerator_ReturnsQobuzRequestGenerator()
        {
            // Arrange
            var indexer = CreateIndexer();

            // Act
            var generator = indexer.GetRequestGenerator();

            // Assert
            generator.Should().NotBeNull();
            generator.Should().BeOfType<QobuzRequestGenerator>();
        }

        /// <summary>
        /// Verifies GetRequestGenerator returns same instance on multiple calls (cached).
        /// </summary>
        [Fact]
        public void GetRequestGenerator_ReturnsCachedInstance()
        {
            // Arrange
            var indexer = CreateIndexer();

            // Act
            var first = indexer.GetRequestGenerator();
            var second = indexer.GetRequestGenerator();

            // Assert
            first.Should().BeSameAs(second);
        }

        /// <summary>
        /// Verifies GetParser returns QobuzParser (line 156).
        /// public override IParseIndexerResponse GetParser()
        /// </summary>
        [Fact]
        public void GetParser_ReturnsQobuzParser()
        {
            // Arrange
            var indexer = CreateIndexer();

            // Act
            var parser = indexer.GetParser();

            // Assert
            parser.Should().NotBeNull();
            parser.Should().BeOfType<QobuzParser>();
        }

        /// <summary>
        /// Verifies GetParser returns same instance on multiple calls (cached).
        /// </summary>
        [Fact]
        public void GetParser_ReturnsCachedInstance()
        {
            // Arrange
            var indexer = CreateIndexer();

            // Act
            var first = indexer.GetParser();
            var second = indexer.GetParser();

            // Assert
            first.Should().BeSameAs(second);
        }

        /// <summary>
        /// Tests RequestAction "getavailablegenres" returns genre list (lines 289-294).
        /// case "getavailablegenres": return GetAvailableGenres();
        /// GetAvailableGenres returns 12 genres (line 313-322).
        /// </summary>
        [Fact]
        public void RequestAction_GetAvailableGenres_ReturnsGenreList()
        {
            // Arrange
            var indexer = CreateIndexer();
            var query = new Dictionary<string, string>();

            // Act
            var result = indexer.RequestAction("getavailablegenres", query);

            // Assert - verify anonymous type with genres array of 12 items
            var genresProperty = result.GetType().GetProperty("genres");
            genresProperty.Should().NotBeNull();
            var genres = genresProperty!.GetValue(result) as string[];
            genres.Should().NotBeNull();
            genres.Should().HaveCount(12);
            genres.Should().Contain("Jazz");
            genres.Should().Contain("Classical");
            genres.Should().Contain("Rock");
            genres.Should().Contain("Pop");
        }

        /// <summary>
        /// Tests RequestAction "getmlperformance" returns metrics (line 302).
        /// case "getmlperformance": return _mlManager.Value.GetMLPerformanceMetrics();
        /// </summary>
        [Fact]
        public void RequestAction_GetMLPerformance_ReturnsMetrics()
        {
            // Arrange
            var indexer = CreateIndexer();
            var query = new Dictionary<string, string>();

            // Act
            var result = indexer.RequestAction("getmlperformance", query);

            // Assert - returns a serializable object with ML metrics data
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(result);
            json.Should().NotBeNullOrEmpty();
            json.Length.Should().BeGreaterThan(2, "ML performance metrics should contain data");
        }

        /// <summary>
        /// Tests RequestAction "getmlhealth" returns health status (line 305).
        /// case "getmlhealth": return _mlManager.Value.GetMLHealthStatus();
        /// </summary>
        [Fact]
        public void RequestAction_GetMLHealth_ReturnsHealthStatus()
        {
            // Arrange
            var indexer = CreateIndexer();
            var query = new Dictionary<string, string>();

            // Act
            var result = indexer.RequestAction("getmlhealth", query);

            // Assert - returns a serializable object with ML health data
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(result);
            json.Should().NotBeNullOrEmpty();
            json.Length.Should().BeGreaterThan(2, "ML health status should contain data");
        }

        /// <summary>
        /// Tests RequestAction "getmlreport" returns diagnostic report (line 308).
        /// case "getmlreport": return _mlManager.Value.GetMLDiagnosticReport();
        /// </summary>
        [Fact]
        public void RequestAction_GetMLReport_ReturnsDiagnosticReport()
        {
            // Arrange
            var indexer = CreateIndexer();
            var query = new Dictionary<string, string>();

            // Act
            var result = indexer.RequestAction("getmlreport", query);

            // Assert - returns a serializable object with diagnostic data
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(result);
            json.Should().NotBeNullOrEmpty();
            json.Length.Should().BeGreaterThan(2, "ML diagnostic report should contain data");
        }

        /// <summary>
        /// Tests RequestAction unknown action returns error (lines 311-312).
        /// default: return new { error = $"Unknown action: {action}" };
        /// </summary>
        [Fact]
        public void RequestAction_UnknownAction_ReturnsError()
        {
            // Arrange
            var indexer = CreateIndexer();
            var query = new Dictionary<string, string>();

            // Act
            var result = indexer.RequestAction("unknownaction", query);

            // Assert - verify anonymous type with error property
            var errorProperty = result.GetType().GetProperty("error");
            errorProperty.Should().NotBeNull();
            var errorValue = errorProperty!.GetValue(result) as string;
            errorValue.Should().Be("Unknown action: unknownaction");
        }

        /// <summary>
        /// Tests RequestAction with null action returns error (line 311).
        /// default: return new { error = $"Unknown action: {action}" };
        /// </summary>
        [Fact]
        public void RequestAction_NullAction_ReturnsError()
        {
            // Arrange
            var indexer = CreateIndexer();
            var query = new Dictionary<string, string>();

            // Act
            var result = indexer.RequestAction(null!, query);

            // Assert
            var errorProperty = result.GetType().GetProperty("error");
            errorProperty.Should().NotBeNull();
            var errorValue = errorProperty!.GetValue(result) as string;
            errorValue.Should().Be("Unknown action: ");
        }

        /// <summary>
        /// Tests RequestAction case insensitivity (line 289).
        /// switch (action?.ToLowerInvariant())
        /// </summary>
        [Fact]
        public void RequestAction_IsCaseInsensitive()
        {
            // Arrange
            var indexer = CreateIndexer();
            var query = new Dictionary<string, string>();

            // Act - test uppercase input
            var result = indexer.RequestAction("GETAVAILABLEGENRES", query);

            // Assert
            var genresProperty = result.GetType().GetProperty("genres");
            genresProperty.Should().NotBeNull();
            var genres = genresProperty!.GetValue(result) as string[];
            genres.Should().NotBeNull();
            genres.Should().HaveCount(12);
        }

        /// <summary>
        /// Tests Dispose can be called without throwing (line 352).
        /// public void Dispose()
        /// </summary>
        [Fact]
        public void Dispose_DoesNotThrow()
        {
            // Arrange
            var indexer = CreateIndexer();

            // Act
            var act = () => indexer.Dispose();

            // Assert
            act.Should().NotThrow();
        }

        /// <summary>
        /// Tests Dispose can be called multiple times safely.
        /// </summary>
        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            // Arrange
            var indexer = CreateIndexer();

            // Act
            indexer.Dispose();
            var secondDispose = () => indexer.Dispose();

            // Assert
            secondDispose.Should().NotThrow();
        }

        /// <summary>
        /// Tests Protocol property returns "QobuzarrDownloadProtocol" (line 38).
        /// public override string Protocol => nameof(QobuzarrDownloadProtocol);
        /// </summary>
        [Fact]
        public void Protocol_ReturnsQobuzarrDownloadProtocol()
        {
            // Arrange
            var indexer = CreateIndexer();

            // Act & Assert
            indexer.Protocol.Should().Be(nameof(QobuzarrDownloadProtocol));
        }

        /// <summary>
        /// Helper method to create a QobuzIndexer with all required dependencies.
        /// </summary>
        private QobuzIndexer CreateIndexer()
        {
            return new QobuzIndexer(
                _httpClientMock.Object,
                _indexerStatusServiceMock.Object,
                _configServiceMock.Object,
                _parsingServiceMock.Object,
                _authServiceMock.Object,
                _apiClientMock.Object,
                _secureModelLoaderMock.Object,
                _logger);
        }
    }
}
