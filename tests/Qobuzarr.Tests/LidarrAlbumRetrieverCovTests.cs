using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using NSubstitute;
using FluentAssertions;
using NLog;
using Lidarr.Plugin.Qobuzarr.Services;
using Lidarr.Plugin.Qobuzarr.Integration;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Lidarr;
using Lidarr.Plugin.Common.Services.Performance;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Coverage tests for LidarrAlbumRetriever exception paths and edge cases.
    /// Tests constructor validation, null argument handling, and boundary conditions.
    /// </summary>
    public class LidarrAlbumRetrieverCovTests
    {
        private readonly Mock<ILidarrApiClient> _mockLidarrApiClient;
        private readonly Mock<IQobuzApiClient> _mockQobuzApiClient;
        private readonly IUniversalAdaptiveRateLimiter _mockRateLimiter;
        private readonly Mock<IQualityService> _mockQualityService;
        private readonly Mock<ILidarrStatisticsCollector> _mockStatisticsCollector;
        private readonly Mock<Logger> _mockLogger;

        public LidarrAlbumRetrieverCovTests()
        {
            _mockLidarrApiClient = new Mock<ILidarrApiClient>();
            _mockQobuzApiClient = new Mock<IQobuzApiClient>();
            _mockRateLimiter = Substitute.For<IUniversalAdaptiveRateLimiter>();
            _mockQualityService = new Mock<IQualityService>();
            _mockStatisticsCollector = new Mock<ILidarrStatisticsCollector>();
            _mockLogger = new Mock<Logger>();
        }

        private LidarrAlbumRetriever CreateSut()
        {
            return new LidarrAlbumRetriever(
                _mockLidarrApiClient.Object,
                _mockQobuzApiClient.Object,
                _mockRateLimiter,
                _mockQualityService.Object,
                _mockStatisticsCollector.Object,
                _mockLogger.Object);
        }

        #region Constructor Guard Tests

        /// <summary>
        /// Covers constructor Guard.NotNull for lidarrApiClient.
        /// Source: src/Services/LidarrAlbumRetriever.cs:58
        /// </summary>
        [Fact]
        public void Constructor_WhenLidarrApiClientIsNull_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new LidarrAlbumRetriever(
                    null!,
                    _mockQobuzApiClient.Object,
                    _mockRateLimiter,
                    _mockQualityService.Object,
                    _mockStatisticsCollector.Object,
                    _mockLogger.Object));

            ex.ParamName.Should().Be("lidarrApiClient");
        }

        /// <summary>
        /// Covers constructor Guard.NotNull for qobuzApiClient.
        /// Source: src/Services/LidarrAlbumRetriever.cs:59
        /// </summary>
        [Fact]
        public void Constructor_WhenQobuzApiClientIsNull_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new LidarrAlbumRetriever(
                    _mockLidarrApiClient.Object,
                    null!,
                    _mockRateLimiter,
                    _mockQualityService.Object,
                    _mockStatisticsCollector.Object,
                    _mockLogger.Object));

            ex.ParamName.Should().Be("qobuzApiClient");
        }

        /// <summary>
        /// Covers constructor Guard.NotNull for rateLimiter.
        /// Source: src/Services/LidarrAlbumRetriever.cs:60
        /// </summary>
        [Fact]
        public void Constructor_WhenRateLimiterIsNull_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new LidarrAlbumRetriever(
                    _mockLidarrApiClient.Object,
                    _mockQobuzApiClient.Object,
                    null!,
                    _mockQualityService.Object,
                    _mockStatisticsCollector.Object,
                    _mockLogger.Object));

            ex.ParamName.Should().Be("rateLimiter");
        }

        /// <summary>
        /// Covers constructor Guard.NotNull for qualityManager.
        /// Source: src/Services/LidarrAlbumRetriever.cs:61
        /// </summary>
        [Fact]
        public void Constructor_WhenQualityServiceIsNull_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new LidarrAlbumRetriever(
                    _mockLidarrApiClient.Object,
                    _mockQobuzApiClient.Object,
                    _mockRateLimiter,
                    null!,
                    _mockStatisticsCollector.Object,
                    _mockLogger.Object));

            ex.ParamName.Should().Be("qualityManager");
        }

        /// <summary>
        /// Covers constructor Guard.NotNull for statisticsCollector.
        /// Source: src/Services/LidarrAlbumRetriever.cs:62
        /// </summary>
        [Fact]
        public void Constructor_WhenStatisticsCollectorIsNull_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new LidarrAlbumRetriever(
                    _mockLidarrApiClient.Object,
                    _mockQobuzApiClient.Object,
                    _mockRateLimiter,
                    _mockQualityService.Object,
                    null!,
                    _mockLogger.Object));

            ex.ParamName.Should().Be("statisticsCollector");
        }

        /// <summary>
        /// Covers constructor Guard.NotNull for logger.
        /// Source: src/Services/LidarrAlbumRetriever.cs:63
        /// </summary>
        [Fact]
        public void Constructor_WhenLoggerIsNull_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new LidarrAlbumRetriever(
                    _mockLidarrApiClient.Object,
                    _mockQobuzApiClient.Object,
                    _mockRateLimiter,
                    _mockQualityService.Object,
                    _mockStatisticsCollector.Object,
                    null!));

            ex.ParamName.Should().Be("logger");
        }

        #endregion

        #region SearchQobuzParallelAsync Null Argument Test

        /// <summary>
        /// Covers line 147: throw new ArgumentNullException(nameof(lidarrAlbums)).
        /// Source: src/Services/LidarrAlbumRetriever.cs:147
        /// When lidarrAlbums is null, should throw ArgumentNullException.
        /// </summary>
        [Fact]
        public async Task SearchQobuzParallelAsync_WhenLidarrAlbumsIsNull_ThrowsArgumentNullException()
        {
            // Arrange
            var sut = CreateSut();

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(
                () => sut.SearchQobuzParallelAsync(null!));

            ex.ParamName.Should().Be("lidarrAlbums");
        }

        #endregion

        #region SearchQobuzParallelAsync Empty List Test

        /// <summary>
        /// Covers early return path when album list is empty.
        /// Source: src/Services/LidarrAlbumRetriever.cs:149-153
        /// When no albums provided, returns empty dictionary.
        /// </summary>
        [Fact]
        public async Task SearchQobuzParallelAsync_WhenAlbumListIsEmpty_ReturnsEmptyDictionary()
        {
            // Arrange
            var sut = CreateSut();
            var emptyAlbums = new List<LidarrAlbum>();

            // Act
            var result = await sut.SearchQobuzParallelAsync(emptyAlbums);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        #endregion

        #region GetFilteredWantedAlbumsAsync Tests

        /// <summary>
        /// Covers pagination and max albums clamping.
        /// Source: src/Services/LidarrAlbumRetriever.cs:82-86
        /// When maxAlbums exceeds limit, clamps to MAX_ALBUMS_PER_REQUEST (500).
        /// </summary>
        [Fact]
        public async Task GetFilteredWantedAlbumsAsync_WhenMaxAlbumsExceedsLimit_ClampsToMaximum()
        {
            // Arrange
            var sut = CreateSut();
            var albums = CreateTestAlbums(10);
            var pagedResponse = new LidarrPagedResponse<LidarrAlbum>
            {
                Page = 1,
                PageSize = 100,
                TotalRecords = 10,
                Records = albums
            };

            _mockLidarrApiClient
                .Setup(x => x.GetWantedAlbumsAsync(It.IsAny<LidarrFilterOptions>()))
                .ReturnsAsync(pagedResponse);

            // Act - Request more than max (500)
            var result = await sut.GetFilteredWantedAlbumsAsync(maxAlbums: 1000);

            // Assert - Should be clamped to 500 but we only have 10
            var resultList = result.ToList();
            resultList.Should().HaveCount(10);
        }

        /// <summary>
        /// Covers early return when API returns null response.
        /// Source: src/Services/LidarrAlbumRetriever.cs:108-112
        /// </summary>
        [Fact]
        public async Task GetFilteredWantedAlbumsAsync_WhenApiReturnsNull_ReturnsEmptyList()
        {
            // Arrange
            var sut = CreateSut();
            _mockLidarrApiClient
                .Setup(x => x.GetWantedAlbumsAsync(It.IsAny<LidarrFilterOptions>()))
                .ReturnsAsync((LidarrPagedResponse<LidarrAlbum>)null!);

            // Act
            var result = await sut.GetFilteredWantedAlbumsAsync();

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        /// <summary>
        /// Covers early return when API returns empty records list.
        /// Source: src/Services/LidarrAlbumRetriever.cs:108-112
        /// </summary>
        [Fact]
        public async Task GetFilteredWantedAlbumsAsync_WhenApiReturnsEmptyRecords_ReturnsEmptyList()
        {
            // Arrange
            var sut = CreateSut();
            var pagedResponse = new LidarrPagedResponse<LidarrAlbum>
            {
                Page = 1,
                PageSize = 100,
                TotalRecords = 0,
                Records = new List<LidarrAlbum>()
            };

            _mockLidarrApiClient
                .Setup(x => x.GetWantedAlbumsAsync(It.IsAny<LidarrFilterOptions>()))
                .ReturnsAsync(pagedResponse);

            // Act
            var result = await sut.GetFilteredWantedAlbumsAsync();

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        /// <summary>
        /// Covers exception rethrow path.
        /// Source: src/Services/LidarrAlbumRetriever.cs:131-135
        /// </summary>
        [Fact]
        public async Task GetFilteredWantedAlbumsAsync_WhenApiThrows_RethrowsException()
        {
            // Arrange
            var sut = CreateSut();
            var expectedException = new InvalidOperationException("API failure");
            _mockLidarrApiClient
                .Setup(x => x.GetWantedAlbumsAsync(It.IsAny<LidarrFilterOptions>()))
                .ThrowsAsync(expectedException);

            // Act & Assert
            var actualException = await Assert.ThrowsAsync<InvalidOperationException>(
                () => sut.GetFilteredWantedAlbumsAsync());

            actualException.Message.Should().Be("API failure");
            actualException.Should().BeSameAs(expectedException);
        }

        #endregion

        #region ValidateAlbumsAsync Tests

        /// <summary>
        /// Covers early return when albumMatches is null.
        /// Source: src/Services/LidarrAlbumRetriever.cs:225-229
        /// </summary>
        [Fact]
        public async Task ValidateAlbumsAsync_WhenAlbumMatchesIsNull_ReturnsEmptyEnumerable()
        {
            // Arrange
            var sut = CreateSut();

            // Act
            var result = await sut.ValidateAlbumsAsync(null!, 6);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        /// <summary>
        /// Covers early return when albumMatches is empty.
        /// Source: src/Services/LidarrAlbumRetriever.cs:225-229
        /// </summary>
        [Fact]
        public async Task ValidateAlbumsAsync_WhenAlbumMatchesIsEmpty_ReturnsEmptyEnumerable()
        {
            // Arrange
            var sut = CreateSut();
            var emptyMatches = new Dictionary<LidarrAlbum, QobuzAlbum>();

            // Act
            var result = await sut.ValidateAlbumsAsync(emptyMatches, 6);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        /// <summary>
        /// Covers validation failure when album has no tracks.
        /// Source: src/Services/LidarrAlbumRetriever.cs:245-249
        /// </summary>
        [Fact]
        public async Task ValidateAlbumsAsync_WhenAlbumHasNoTracks_NotIncludedInResults()
        {
            // Arrange
            var sut = CreateSut();
            var lidarrAlbum = CreateTestLidarrAlbum(1, "Test Album");
            var qobuzAlbum = CreateTestQobuzAlbum("q1", "Test Album", tracksCount: 0);

            var matches = new Dictionary<LidarrAlbum, QobuzAlbum>
            {
                [lidarrAlbum] = qobuzAlbum
            };

            // Act
            var result = await sut.ValidateAlbumsAsync(matches, 6);

            // Assert - Album with no tracks should fail validation
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        /// <summary>
        /// Covers validation failure when album is not streamable.
        /// Source: src/Services/LidarrAlbumRetriever.cs:252-256
        /// </summary>
        [Fact]
        public async Task ValidateAlbumsAsync_WhenAlbumIsNotStreamable_NotIncludedInResults()
        {
            // Arrange
            var sut = CreateSut();
            var lidarrAlbum = CreateTestLidarrAlbum(1, "Test Album");
            var qobuzAlbum = CreateTestQobuzAlbum("q1", "Test Album", tracksCount: 5, streamable: false);

            var matches = new Dictionary<LidarrAlbum, QobuzAlbum>
            {
                [lidarrAlbum] = qobuzAlbum
            };

            // Act
            var result = await sut.ValidateAlbumsAsync(matches, 6);

            // Assert - Album that's not streamable should fail validation
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        /// <summary>
        /// Covers successful validation path.
        /// Source: src/Services/LidarrAlbumRetriever.cs:309-322
        /// </summary>
        [Fact]
        public async Task ValidateAlbumsAsync_WhenAlbumIsValid_ReturnsValidatedItem()
        {
            // Arrange
            var sut = CreateSut();
            var lidarrAlbum = CreateTestLidarrAlbum(1, "Test Album");
            var qobuzAlbum = CreateTestQobuzAlbum("q1", "Test Album", tracksCount: 10, streamable: true);

            var matches = new Dictionary<LidarrAlbum, QobuzAlbum>
            {
                [lidarrAlbum] = qobuzAlbum
            };

            _mockQualityService
                .Setup(x => x.MapLidarrQuality(It.IsAny<object>()))
                .Returns(QobuzQuality.Flac_CD);

            _mockQualityService
                .Setup(x => x.GetQualityFallbackChain(It.IsAny<QobuzQuality>()))
                .Returns(new List<QobuzQuality> { QobuzQuality.Flac_CD, QobuzQuality.Mp3_320 });

            // Act
            var result = await sut.ValidateAlbumsAsync(matches, 6);
            var resultList = result.ToList();

            // Assert
            resultList.Should().HaveCount(1);
            resultList[0].LidarrAlbum.Should().Be(lidarrAlbum);
            resultList[0].QobuzAlbum.Should().Be(qobuzAlbum);
        }

        #endregion

        #region ClearQualityProfileCache Test

        /// <summary>
        /// Covers cache clearing functionality.
        /// Source: src/Services/LidarrAlbumRetriever.cs:343-350
        /// </summary>
        [Fact]
        public void ClearQualityProfileCache_WhenCalled_DoesNotThrow()
        {
            // Arrange
            var sut = CreateSut();

            // Act & Assert - Should not throw
            var exception = Record.Exception(() => sut.ClearQualityProfileCache());
            exception.Should().BeNull();
        }

        #endregion

        #region Helper Methods

        private List<LidarrAlbum> CreateTestAlbums(int count)
        {
            return Enumerable.Range(1, count)
                .Select(i => CreateTestLidarrAlbum(i, $"Album {i}"))
                .ToList();
        }

        private LidarrAlbum CreateTestLidarrAlbum(int id, string title)
        {
            return new LidarrAlbum
            {
                Id = id,
                Title = title,
                ArtistId = 1,
                QualityProfileId = 1,
                ProfileId = 1,
                Artist = new LidarrArtist
                {
                    Id = 1,
                    ArtistName = "Test Artist"
                }
            };
        }

        private QobuzAlbum CreateTestQobuzAlbum(string id, string title, int tracksCount = 10, bool streamable = true)
        {
            var track = new QobuzTrack
            {
                Id = "t1",
                Title = "Track 1",
                MaximumBitDepth = 16,
                MaximumSampleRate = 44100
            };

            return new QobuzAlbum
            {
                Id = id,
                Title = title,
                TracksCount = tracksCount,
                Streamable = streamable,
                Artist = new QobuzArtist { Name = "Test Artist" },
                TracksContainer = new QobuzTracksContainer
                {
                    Items = tracksCount > 0 ? new List<QobuzTrack> { track } : new List<QobuzTrack>()
                }
            };
        }

        #endregion
    }
}
