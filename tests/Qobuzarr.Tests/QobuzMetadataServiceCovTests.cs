using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NSubstitute;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Services.Metadata;
using Lidarr.Plugin.Qobuzarr.Constants;
using Xunit;
using Qobuzarr.Tests.Helpers;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Coverage tests for QobuzMetadataStrategy.
    /// Target: src/Services/Metadata/QobuzMetadataStrategy.cs
    /// </summary>
    public class QobuzMetadataServiceCovTests : IDisposable
    {
        private readonly Mock<Logger> _mockLogger;
        private readonly Mock<IHttpClient> _mockHttpClient;
        private readonly ICacheManager _mockCacheManager;
        private readonly QobuzApiClient _apiClient;

        public QobuzMetadataServiceCovTests()
        {
            _mockLogger = new Mock<Logger>();
            _mockHttpClient = new Mock<IHttpClient>();

            // Setup cache manager using NSubstitute pattern
            _mockCacheManager = Substitute.For<ICacheManager>();
            var mockObjectCache = Substitute.For<ICached<object>>();
            mockObjectCache.Find(Arg.Any<string>()).Returns((object)null);
            mockObjectCache.When(x => x.Set(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<TimeSpan?>()))
                           .Do(callInfo => { /* no-op */ });
            _mockCacheManager.GetCache<object>(Arg.Any<Type>()).Returns(mockObjectCache);

            // Create QobuzApiClient using backward-compatible constructor
            _apiClient = new QobuzApiClient(_mockHttpClient.Object, _mockCacheManager, _mockLogger.Object);
        }

        public void Dispose()
        {
            _mockHttpClient?.VerifyAll();
        }

        #region Helper Methods

        private HttpResponse CreateStreamingUrlResponse(string trackId)
        {
            var json = $$"""
                {
                    "url": "https://stream.example.com/track-{{trackId}}.flac",
                    "format_id": 6,
                    "mime_type": "audio/flac",
                    "track_id": "{{trackId}}"
                }
                """;
            return HttpTestHelpers.CreateResponse(json, HttpStatusCode.OK);
        }

        private HttpResponse CreateTrackMetadataResponse(string trackId, string title = null, string performerName = null)
        {
            var json = $$"""
                {
                    "id": "{{trackId}}",
                    "title": "{{title ?? $"Track Title {trackId}"}}",
                    "track_number": 1,
                    "media_number": 1,
                    "duration": 240,
                    "performer": {
                        "id": "123",
                        "name": "{{performerName ?? "Test Artist"}}"
                    },
                    "album": {
                        "id": "456",
                        "title": "Test Album",
                        "artist": {
                            "id": "123",
                            "name": "Album Artist"
                        },
                        "genres_list": ["Jazz"],
                        "label": {
                            "id": "789",
                            "name": "Test Label"
                        },
                        "release_date_original": "2024-01-01"
                    },
                    "composer": {
                        "id": "999",
                        "name": "Test Composer"
                    },
                    "isrc": "US1234567890",
                    "maximum_bit_depth": 16,
                    "maximum_sampling_rate": 44100.0
                }
                """;
            return HttpTestHelpers.CreateResponse(json, HttpStatusCode.OK);
        }

        private HttpResponse CreateMinimalTrackMetadataResponse(string trackId)
        {
            // Minimal response without optional fields to test fallbacks
            var json = $$"""
                {
                    "id": "{{trackId}}",
                    "title": "Minimal Track",
                    "track_number": 1,
                    "media_number": 1,
                    "duration": 180
                }
                """;
            return HttpTestHelpers.CreateResponse(json, HttpStatusCode.OK);
        }

        private QobuzAlbum CreateTestAlbum(int trackCount = 1)
        {
            var tracks = new List<QobuzTrack>();
            for (int i = 0; i < trackCount; i++)
            {
                tracks.Add(new QobuzTrack
                {
                    Id = $"100{i}",
                    Title = $"Track {i + 1}",
                    TrackNumber = i + 1,
                    DiscNumber = 1,
                    DurationSeconds = 240,
                    Performer = new QobuzArtist { Id = "123", Name = "Test Artist" },
                    Album = new QobuzAlbum
                    {
                        Id = "456",
                        Title = "Test Album",
                        Artist = new QobuzArtist { Id = "123", Name = "Album Artist" }
                    },
                    MaximumBitDepth = 16,
                    MaximumSampleRate = 44100.0
                });
            }

            return new QobuzAlbum
            {
                Id = "album-456",
                Title = "Test Album",
                Artist = new QobuzArtist { Id = "123", Name = "Test Artist" },
                TracksCount = trackCount,
                DurationSeconds = trackCount * 240,
                TracksContainer = new QobuzTracksContainer { Items = tracks }
            };
        }

        #endregion

        #region Constructor Tests (Source lines 23-29)

        [Fact]
        public void Constructor_NullApiClient_ThrowsArgumentNullException()
        {
            // Source line 28: _qobuzApiClient = qobuzApiClient ?? throw new ArgumentNullException(nameof(qobuzApiClient));
            var act = () => new QobuzMetadataStrategy(_mockLogger.Object, null);

            act.Should().ThrowExactly<ArgumentNullException>()
                .WithParameterName("qobuzApiClient");
        }

        [Fact]
        public void Constructor_NullLogger_SucceedsAndDefaultsToLogManager()
        {
            // Source line 27: _logger = logger ?? LogManager.GetCurrentClassLogger();
            var act = () => new QobuzMetadataStrategy(null, _apiClient);

            act.Should().NotThrow("null logger defaults to LogManager.GetCurrentClassLogger()");
        }

        [Fact]
        public void Constructor_ValidParams_CreatesInstance()
        {
            var strategy = new QobuzMetadataStrategy(_mockLogger.Object, _apiClient);

            strategy.Should().NotBeNull();
            strategy.StrategyName.Should().Be(QobuzarrConstants.ServiceName);
        }

        #endregion

        #region StrategyName Property Tests (Source line 21)

        [Fact]
        public void StrategyName_ReturnsQobuzConstant()
        {
            // Source line 21: public string StrategyName => Constants.QobuzarrConstants.ServiceName;
            var strategy = new QobuzMetadataStrategy(_mockLogger.Object, _apiClient);

            var name = strategy.StrategyName;

            name.Should().Be("Qobuz", "StrategyName returns QobuzarrConstants.ServiceName");
        }

        #endregion

        #region CanHandle Tests (Source lines 31-35)

        [Fact]
        public void CanHandle_NullQobuzAlbum_ReturnsFalse()
        {
            // Source line 34: return qobuzAlbum != null;
            var strategy = new QobuzMetadataStrategy(_mockLogger.Object, _apiClient);

            var result = strategy.CanHandle(null, null);

            result.Should().BeFalse("null QobuzAlbum cannot be handled");
        }

        [Fact]
        public void CanHandle_ValidQobuzAlbum_NullLidarrAlbum_ReturnsTrue()
        {
            // Source line 34: return qobuzAlbum != null;
            var strategy = new QobuzMetadataStrategy(_mockLogger.Object, _apiClient);
            var album = CreateTestAlbum();

            var result = strategy.CanHandle(album, null);

            result.Should().BeTrue("valid QobuzAlbum can be handled even with null LidarrAlbum");
        }

        [Fact]
        public void CanHandle_ValidAlbums_ReturnsTrue()
        {
            // Source line 34: return qobuzAlbum != null;
            var strategy = new QobuzMetadataStrategy(_mockLogger.Object, _apiClient);
            var qobuzAlbum = CreateTestAlbum();
            var lidarrAlbum = new Lidarr.Plugin.Qobuzarr.Models.Lidarr.LidarrAlbum();

            var result = strategy.CanHandle(qobuzAlbum, lidarrAlbum);

            result.Should().BeTrue("LidarrAlbum is ignored, only QobuzAlbum matters");
        }

        #endregion

        #region DownloadAlbumAsync Tests (Source lines 37-64)

        [Fact]
        public async Task DownloadAlbumAsync_NullQobuzAlbum_ThrowsArgumentNullException()
        {
            // Source line 39-40: if (qobuzAlbum == null) throw new ArgumentNullException(nameof(qobuzAlbum));
            var strategy = new QobuzMetadataStrategy(_mockLogger.Object, _apiClient);

            var act = async () => await strategy.DownloadAlbumAsync(null);

            await act.Should().ThrowExactlyAsync<ArgumentNullException>()
                .WithParameterName("qobuzAlbum");
        }

        [Fact]
        public async Task DownloadAlbumAsync_ValidAlbum_ReturnsCorrectResult()
        {
            var strategy = new QobuzMetadataStrategy(_mockLogger.Object, _apiClient);
            var album = CreateTestAlbum(trackCount: 1);
            var trackId = album.GetTracks()[0].Id;

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync((HttpRequest req) =>
                {
                    var url = req.Url?.ToString() ?? string.Empty;
                    if (url.Contains("track/getFileUrl"))
                        return CreateStreamingUrlResponse(trackId);
                    if (url.Contains("track/get") && !url.Contains("getFileUrl"))
                        return CreateTrackMetadataResponse(trackId);
                    throw new InvalidOperationException($"Unexpected request: {url}");
                });

            var result = await strategy.DownloadAlbumAsync(album);

            result.Should().NotBeNull();
            result.TrackDownloads.Should().HaveCount(1, "album has one track");
            result.MetadataStrategy.Should().Be(QobuzarrConstants.ServiceName);
            result.ApiCallsSaved.Should().Be(0, "Qobuz strategy doesn't save API calls");
            result.AdditionalApiCalls.Should().Be(album.TracksCount + 2, "tracks + album + tracklist");

            var trackDownload = result.TrackDownloads[0];
            trackDownload.StreamingUrl.Should().Be($"https://stream.example.com/track-{trackId}.flac");
            trackDownload.Title.Should().Be($"Track Title {trackId}");
            trackDownload.Artist.Should().Be("Test Artist");
            trackDownload.Album.Should().Be("Test Album");
            trackDownload.Genre.Should().Be("Jazz", "from metadata album genres_list");
            trackDownload.Label.Should().Be("Test Label", "from metadata album label");
            trackDownload.Composer.Should().Be("Test Composer", "from metadata composer");
            trackDownload.ISRC.Should().Be("US1234567890");
            trackDownload.MetadataSource.Should().Be(QobuzarrConstants.ServiceName);
        }

        [Fact]
        public async Task DownloadAlbumAsync_StreamingUrlApiError_ThrowsHttpException()
        {
            // Source lines 108-119: GetStreamingUrlAsync catches and rethrows
            var strategy = new QobuzMetadataStrategy(_mockLogger.Object, _apiClient);
            var album = CreateTestAlbum(trackCount: 1);
            var trackId = album.GetTracks()[0].Id;

            var errorResponse = HttpTestHelpers.CreateErrorResponse(HttpStatusCode.InternalServerError, "Server error");
            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync((HttpRequest req) =>
                {
                    var url = req.Url?.ToString() ?? string.Empty;
                    if (url.Contains("track/getFileUrl"))
                        throw new HttpException(errorResponse);
                    return CreateTrackMetadataResponse(trackId);
                });

            var act = async () => await strategy.DownloadAlbumAsync(album);

            await act.Should().ThrowAsync<HttpException>();
        }

        [Fact]
        public async Task DownloadAlbumAsync_TrackMetadataApiError_ThrowsHttpException()
        {
            // Source lines 121-132: GetQobuzTrackAsync catches and rethrows
            var strategy = new QobuzMetadataStrategy(_mockLogger.Object, _apiClient);
            var album = CreateTestAlbum(trackCount: 1);
            var trackId = album.GetTracks()[0].Id;

            var errorResponse = HttpTestHelpers.CreateErrorResponse(HttpStatusCode.BadGateway, "Bad gateway");
            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync((HttpRequest req) =>
                {
                    var url = req.Url?.ToString() ?? string.Empty;
                    if (url.Contains("track/getFileUrl"))
                        return CreateStreamingUrlResponse(trackId);
                    throw new HttpException(errorResponse);
                });

            var act = async () => await strategy.DownloadAlbumAsync(album);

            await act.Should().ThrowAsync<HttpException>();
        }

        #endregion

        #region CreateTrackDownloadFromQobuz Fallback Tests (Source lines 66-106)

        [Fact]
        public async Task DownloadAlbumAsync_MetadataNullFields_UsesFallbacksFromQobuzTrack()
        {
            // Source lines 66-106: various ?? fallbacks in CreateTrackDownloadFromQobuz
            var strategy = new QobuzMetadataStrategy(_mockLogger.Object, _apiClient);
            var album = CreateTestAlbum(trackCount: 1);
            var track = album.GetTracks()[0];
            var trackId = track.Id;

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync((HttpRequest req) =>
                {
                    var url = req.Url?.ToString() ?? string.Empty;
                    if (url.Contains("track/getFileUrl"))
                        return CreateStreamingUrlResponse(trackId);
                    if (url.Contains("track/get") && !url.Contains("getFileUrl"))
                        return CreateMinimalTrackMetadataResponse(trackId);
                    throw new InvalidOperationException($"Unexpected request: {url}");
                });

            var result = await strategy.DownloadAlbumAsync(album);

            // Verify fallback values from qobuzTrack when metadata is null
            var trackDownload = result.TrackDownloads[0];
            trackDownload.Title.Should().Be("Minimal Track", "from metadata when available");
            trackDownload.Artist.Should().Be("Test Artist", "fallback: metadata.Performer?.Name ?? qobuzTrack.GetPerformerName()");
            trackDownload.AlbumArtist.Should().Be("Album Artist", "fallback: metadata.Album?.Artist?.Name ?? qobuzTrack.AlbumArtistName");
            trackDownload.Album.Should().Be("Test Album", "fallback: metadata.Album?.Title ?? qobuzTrack.AlbumTitle");
            trackDownload.Genre.Should().BeNull("metadata.Album?.GenresList is null");
            trackDownload.Label.Should().BeNull("metadata.Album?.Label?.Name is null");
            trackDownload.Composer.Should().BeNull("metadata.Composer?.Name is null");
            trackDownload.ISRC.Should().BeNull("both metadata.ISRC and qobuzTrack.ISRC are null");
            trackDownload.ReleaseDate.Should().BeNull("metadata.Album?.ReleaseDate is null");
        }

        [Fact]
        public async Task DownloadAlbumAsync_QualityInfo_PopulatedFromQobuzTrack()
        {
            // Source lines 93-96: Quality, BitRate, SampleRate, BitDepth from qobuzTrack
            var strategy = new QobuzMetadataStrategy(_mockLogger.Object, _apiClient);
            var album = CreateTestAlbum(trackCount: 1);
            var trackId = album.GetTracks()[0].Id;

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync((HttpRequest req) =>
                {
                    var url = req.Url?.ToString() ?? string.Empty;
                    if (url.Contains("track/getFileUrl"))
                        return CreateStreamingUrlResponse(trackId);
                    return CreateTrackMetadataResponse(trackId);
                });

            var result = await strategy.DownloadAlbumAsync(album);

            var trackDownload = result.TrackDownloads[0];
            trackDownload.Quality.Should().Be("Lossless", "from qobuzTrack.Quality with 16-bit");
            trackDownload.BitRate.Should().Be(1411, "from qobuzTrack.BitRate for 16-bit");
            trackDownload.SampleRate.Should().Be(44100, "from qobuzTrack.SampleRate");
            trackDownload.BitDepth.Should().Be(16, "from qobuzTrack.BitDepth");
        }

        #endregion

        #region Multiple Tracks Tests

        [Fact]
        public async Task DownloadAlbumAsync_MultipleTracks_AllProcessed()
        {
            var strategy = new QobuzMetadataStrategy(_mockLogger.Object, _apiClient);
            var album = CreateTestAlbum(trackCount: 3);
            var tracks = album.GetTracks();
            var callIndex = 0;

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync((HttpRequest req) =>
                {
                    var url = req.Url?.ToString() ?? string.Empty;
                    if (url.Contains("track/getFileUrl"))
                    {
                        var trackId = tracks[callIndex / 2].Id;
                        callIndex++;
                        return CreateStreamingUrlResponse(trackId);
                    }
                    if (url.Contains("track/get") && !url.Contains("getFileUrl"))
                    {
                        var trackId = tracks[callIndex / 2].Id;
                        callIndex++;
                        return CreateTrackMetadataResponse(trackId);
                    }
                    throw new InvalidOperationException($"Unexpected request: {url}");
                });

            var result = await strategy.DownloadAlbumAsync(album);

            result.TrackDownloads.Should().HaveCount(3, "album has three tracks");
            result.IsSuccessful.Should().BeTrue("all tracks downloaded successfully");
            result.TotalDuration.Should().Be(TimeSpan.FromSeconds(720), "3 tracks x 240 seconds");

            for (int i = 0; i < 3; i++)
            {
                result.TrackDownloads[i].TrackNumber.Should().Be(i + 1);
                result.TrackDownloads[i].DiscNumber.Should().Be(1);
            }
        }

        #endregion

        #region AdditionalApiCalls Calculation Tests (Source line 62)

        [Fact]
        public async Task DownloadAlbumAsync_AdditionalApiCalls_EqualsTracksCountPlusTwo()
        {
            // Source line 62: AdditionalApiCalls = qobuzAlbum.TracksCount + 2
            var strategy = new QobuzMetadataStrategy(_mockLogger.Object, _apiClient);
            var album = CreateTestAlbum(trackCount: 5);
            var tracks = album.GetTracks();

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync((HttpRequest req) =>
                {
                    var url = req.Url?.ToString() ?? string.Empty;
                    if (url.Contains("track/getFileUrl"))
                        return CreateStreamingUrlResponse(tracks[0].Id);
                    if (url.Contains("track/get") && !url.Contains("getFileUrl"))
                        return CreateTrackMetadataResponse(tracks[0].Id);
                    throw new InvalidOperationException($"Unexpected request: {url}");
                });

            var result = await strategy.DownloadAlbumAsync(album);

            // 5 tracks + 2 (album details + tracklist)
            result.AdditionalApiCalls.Should().Be(7, "5 tracks + 2 for album/tracklist = 7");
        }

        [Fact]
        public async Task DownloadAlbumAsync_EmptyAlbum_ReturnsZeroTracks()
        {
            var strategy = new QobuzMetadataStrategy(_mockLogger.Object, _apiClient);
            var album = CreateTestAlbum(trackCount: 0);

            var result = await strategy.DownloadAlbumAsync(album);

            result.TrackDownloads.Should().HaveCount(0, "empty album has no tracks");
            result.IsSuccessful.Should().BeFalse("no tracks means not successful");
            result.AdditionalApiCalls.Should().Be(2, "0 tracks + 2 for album/tracklist = 2");
        }

        #endregion

        #region IsSuccessful Property Tests (Source line 40 in IMetadataStrategy.cs)

        [Fact]
        public async Task DownloadAlbumAsync_WithTracks_IsSuccessfulTrue()
        {
            // Source line 40 (IMetadataStrategy.cs): public bool IsSuccessful => TrackDownloads.Any();
            var strategy = new QobuzMetadataStrategy(_mockLogger.Object, _apiClient);
            var album = CreateTestAlbum(trackCount: 1);
            var trackId = album.GetTracks()[0].Id;

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync((HttpRequest req) =>
                {
                    var url = req.Url?.ToString() ?? string.Empty;
                    if (url.Contains("track/getFileUrl"))
                        return CreateStreamingUrlResponse(trackId);
                    return CreateTrackMetadataResponse(trackId);
                });

            var result = await strategy.DownloadAlbumAsync(album);

            result.IsSuccessful.Should().BeTrue("result has track downloads");
        }

        #endregion

        #region TotalDuration Property Tests (Source line 41 in IMetadataStrategy.cs)

        [Fact]
        public async Task DownloadAlbumAsync_TotalDuration_SumOfTrackDurations()
        {
            // Source line 41 (IMetadataStrategy.cs): public TimeSpan TotalDuration => TimeSpan.FromSeconds(TrackDownloads.Sum(t => t.Duration?.TotalSeconds ?? 0));
            var strategy = new QobuzMetadataStrategy(_mockLogger.Object, _apiClient);
            var album = CreateTestAlbum(trackCount: 2);
            var tracks = album.GetTracks();
            var callIndex = 0;

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync((HttpRequest req) =>
                {
                    var url = req.Url?.ToString() ?? string.Empty;
                    if (url.Contains("track/getFileUrl"))
                    {
                        var trackId = tracks[callIndex / 2].Id;
                        callIndex++;
                        return CreateStreamingUrlResponse(trackId);
                    }
                    if (url.Contains("track/get") && !url.Contains("getFileUrl"))
                    {
                        var trackId = tracks[callIndex / 2].Id;
                        callIndex++;
                        return CreateTrackMetadataResponse(trackId);
                    }
                    throw new InvalidOperationException($"Unexpected request: {url}");
                });

            var result = await strategy.DownloadAlbumAsync(album);

            // Each track has DurationSeconds = 240 (set in CreateTestAlbum)
            result.TotalDuration.Should().Be(TimeSpan.FromSeconds(480), "2 tracks x 240 seconds = 480");
        }

        #endregion

        #region TrackDownload Validation Tests

        [Fact]
        public async Task DownloadAlbumAsync_TrackDownload_HasValidQobuzTrackId()
        {
            // Source line 74: QobuzTrackId = int.Parse(qobuzTrack.Id)
            var strategy = new QobuzMetadataStrategy(_mockLogger.Object, _apiClient);
            var album = CreateTestAlbum(trackCount: 1);
            var trackId = album.GetTracks()[0].Id;

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync((HttpRequest req) =>
                {
                    var url = req.Url?.ToString() ?? string.Empty;
                    if (url.Contains("track/getFileUrl"))
                        return CreateStreamingUrlResponse(trackId);
                    return CreateTrackMetadataResponse(trackId);
                });

            var result = await strategy.DownloadAlbumAsync(album);

            result.TrackDownloads[0].QobuzTrackId.Should().Be(1000, "parsed from track Id '1000'");
        }

        [Fact]
        public async Task DownloadAlbumAsync_TrackDownload_PreservesTrackAndDiscNumbers()
        {
            // Source lines 81-82: TrackNumber and DiscNumber from qobuzTrack
            var strategy = new QobuzMetadataStrategy(_mockLogger.Object, _apiClient);
            var album = CreateTestAlbum(trackCount: 2);
            var tracks = album.GetTracks();
            var callIndex = 0;

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync((HttpRequest req) =>
                {
                    var url = req.Url?.ToString() ?? string.Empty;
                    if (url.Contains("track/getFileUrl"))
                    {
                        var trackId = tracks[callIndex / 2].Id;
                        callIndex++;
                        return CreateStreamingUrlResponse(trackId);
                    }
                    if (url.Contains("track/get") && !url.Contains("getFileUrl"))
                    {
                        var trackId = tracks[callIndex / 2].Id;
                        callIndex++;
                        return CreateTrackMetadataResponse(trackId);
                    }
                    throw new InvalidOperationException($"Unexpected request: {url}");
                });

            var result = await strategy.DownloadAlbumAsync(album);

            result.TrackDownloads[0].TrackNumber.Should().Be(1);
            result.TrackDownloads[0].DiscNumber.Should().Be(1);
            result.TrackDownloads[1].TrackNumber.Should().Be(2);
            result.TrackDownloads[1].DiscNumber.Should().Be(1);
        }

        #endregion
    }
}
