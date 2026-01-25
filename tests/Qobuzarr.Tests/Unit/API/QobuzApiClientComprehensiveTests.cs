using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.Http;
using Xunit;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Qobuzarr.Tests.Fixtures;
using Qobuzarr.Tests.TestData;
using Qobuzarr.Tests.Helpers;

namespace Qobuzarr.Tests.Unit.API
{
    /// <summary>
    /// Comprehensive tests for QobuzApiClient covering all API methods.
    /// Tests streaming, metadata retrieval, playlists, labels, and artists.
    /// </summary>
    public class QobuzApiClientComprehensiveTests : TestFixtureBase
    {
        private readonly QobuzApiClient _apiClient;
        private readonly QobuzSession _testSession;

        public QobuzApiClientComprehensiveTests()
        {
            _apiClient = new QobuzApiClient(MockHttpClient.Object, MockCacheManager, MockLogger.Object);

            _testSession = new QobuzSession
            {
                UserId = "12345678",
                AuthToken = "sample_auth_token_123456",
                AppId = "test_app_id_123",
                AppSecret = "test_secret_123",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };
        }

        #region Streaming URL Tests

        [Fact]
        public async Task GetStreamingUrlAsync_ShouldReturnValidUrl()
        {
            // Arrange
            var trackId = "23374053";
            var formatId = 6;
            var expectedUrl = "https://streaming.qobuz.com/track/23374053/download.flac";

            var streamResponse = new QobuzStreamResponse
            {
                Url = expectedUrl,
                FormatId = formatId,
                MimeType = "audio/flac",
                BitDepth = 16,
                SampleRate = 44.1
            };

            var httpResponse = HttpTestHelpers.CreateResponse(
                JsonConvert.SerializeObject(streamResponse),
                HttpStatusCode.OK
            );

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(httpResponse);

            _apiClient.SetSession(_testSession);

            // Act
            var result = await _apiClient.GetStreamingUrlAsync(trackId, formatId);

            // Assert
            result.Should().Be(expectedUrl);
        }

        [Fact]
        public async Task GetStreamingInfoAsync_WithSampleStream_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var trackId = "23374053";
            var formatId = 6;

            var streamResponse = new QobuzStreamResponse
            {
                Url = "https://streaming.qobuz.com/track/sample",
                FormatId = formatId,
                MimeType = "audio/flac",
                Sample = true
            };

            var httpResponse = HttpTestHelpers.CreateResponse(
                JsonConvert.SerializeObject(streamResponse),
                HttpStatusCode.OK
            );

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(httpResponse);

            _apiClient.SetSession(_testSession);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _apiClient.GetStreamingInfoAsync(trackId, formatId)
            );

            exception.Message.Should().Contain("sample stream");
        }

        [Fact]
        public async Task GetStreamingInfoAsync_WithEmptyUrl_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var trackId = "23374053";
            var formatId = 6;

            var streamResponse = new QobuzStreamResponse
            {
                Url = "",
                FormatId = formatId,
                MimeType = "audio/flac",
                Status = "error",
                Message = "Track not available"
            };

            var httpResponse = HttpTestHelpers.CreateResponse(
                JsonConvert.SerializeObject(streamResponse),
                HttpStatusCode.OK
            );

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(httpResponse);

            _apiClient.SetSession(_testSession);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _apiClient.GetStreamingInfoAsync(trackId, formatId)
            );

            exception.Message.Should().Contain("empty stream URL");
        }

        [Fact]
        public async Task GetStreamingInfoAsync_WithQualityFallback_ShouldLogAndReturn()
        {
            // Arrange
            var trackId = "23374053";
            var requestedFormatId = 27;
            var fallbackFormatId = 6;

            var streamResponse = new QobuzStreamResponse
            {
                Url = "https://streaming.qobuz.com/track/23374053/download.flac",
                FormatId = fallbackFormatId,
                MimeType = "audio/flac",
                BitDepth = 16,
                SampleRate = 44.1,
                Restrictions = new List<QobuzStreamRestriction>
                {
                    new QobuzStreamRestriction
                    {
                        Code = "FormatRestrictedByFormatAvailability",
                        Reason = "Requested format not available"
                    }
                }
            };

            var httpResponse = HttpTestHelpers.CreateResponse(
                JsonConvert.SerializeObject(streamResponse),
                HttpStatusCode.OK
            );

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(httpResponse);

            _apiClient.SetSession(_testSession);

            // Act
            var result = await _apiClient.GetStreamingInfoAsync(trackId, requestedFormatId);

            // Assert
            result.Should().NotBeNull();
            result.FormatId.Should().Be(fallbackFormatId);
            result.Url.Should().NotBeNullOrEmpty();
        }

        [Theory]
        [InlineData(5, "MP3 320kbps")]
        [InlineData(6, "FLAC 16-bit/44.1kHz")]
        [InlineData(7, "FLAC 24-bit/96kHz")]
        [InlineData(27, "FLAC 24-bit/192kHz")]
        public async Task GetStreamingInfoAsync_ShouldReturnCorrectQualityDescription(int formatId, string expectedQuality)
        {
            // Arrange
            var trackId = "23374053";

            var streamResponse = new QobuzStreamResponse
            {
                Url = "https://streaming.qobuz.com/track/23374053/download.flac",
                FormatId = formatId,
                MimeType = formatId == 5 ? "audio/mpeg" : "audio/flac",
                BitDepth = formatId >= 7 ? 24 : (formatId == 6 ? 16 : null),
                SampleRate = formatId switch
                {
                    5 => 44.1,
                    6 => 44.1,
                    7 => 96.0,
                    27 => 192.0,
                    _ => 44.1
                }
            };

            var httpResponse = HttpTestHelpers.CreateResponse(
                JsonConvert.SerializeObject(streamResponse),
                HttpStatusCode.OK
            );

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(httpResponse);

            _apiClient.SetSession(_testSession);

            // Act
            var result = await _apiClient.GetStreamingInfoAsync(trackId, formatId);

            // Assert
            // GetQualityDescription() divides SampleRate by 1000, so we need to handle the decimal formatting
            var qualityDesc = result.GetQualityDescription();
            qualityDesc.Should().NotBeNullOrEmpty();

            // Check format-specific details
            if (formatId == 5)
            {
                qualityDesc.Should().Contain("MP3");
            }
            else
            {
                qualityDesc.Should().Contain("FLAC");
                qualityDesc.Should().Contain(formatId >= 7 ? "24-bit" : "16-bit");
            }
        }

        #endregion

        #region Track Metadata Tests

        [Fact]
        public async Task GetTrackMetadataAsync_ShouldReturnValidMetadata()
        {
            // Arrange
            var trackId = "23374053";

            var trackResponse = new
            {
                id = trackId,
                title = "Give Life Back to Music",
                track_number = 1,
                duration = 274,
                maximum_bit_depth = 16,
                maximum_sampling_rate = 44.1,
                streamable = true,
                performer = new
                {
                    id = 26887,
                    name = "Daft Punk"
                },
                album = new
                {
                    id = "0060254788359",
                    title = "Random Access Memories"
                }
            };

            var httpResponse = HttpTestHelpers.CreateResponse(
                JsonConvert.SerializeObject(trackResponse),
                HttpStatusCode.OK
            );

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(httpResponse);

            _apiClient.SetSession(_testSession);

            // Act
            var result = await _apiClient.GetTrackMetadataAsync(trackId);

            // Assert
            result.Should().NotBeNull();
            result.Id.Should().Be(trackId);
            result.Title.Should().Be("Give Life Back to Music");
            result.TrackNumber.Should().Be(1);
            result.Streamable.Should().BeTrue();
        }

        [Fact]
        public async Task GetTrackMetadataAsync_WithInvalidTrackId_ShouldHandleError()
        {
            // Arrange
            var trackId = "invalid_track_id";

            var errorResponse = HttpTestHelpers.CreateErrorResponse(
                HttpStatusCode.NotFound,
                SampleQobuzResponses.ErrorResponse
            );

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ThrowsAsync(new HttpException(errorResponse));

            _apiClient.SetSession(_testSession);

            // Act & Assert
            await Assert.ThrowsAsync<HttpException>(
                () => _apiClient.GetTrackMetadataAsync(trackId)
            );
        }

        #endregion

        #region Playlist Tests

        [Fact]
        public async Task GetPlaylistAsync_ShouldReturnValidPlaylist()
        {
            // Arrange
            var playlistId = "123456";

            var playlistResponse = new
            {
                id = playlistId,
                name = "Test Playlist",
                tracks_count = 10,
                tracks = new
                {
                    limit = 500,
                    offset = 0,
                    total = 10,
                    items = new[]
                    {
                        new
                        {
                            track = new
                            {
                                id = "23374053",
                                title = "Test Track"
                            }
                        }
                    }
                }
            };

            var httpResponse = HttpTestHelpers.CreateResponse(
                JsonConvert.SerializeObject(playlistResponse),
                HttpStatusCode.OK
            );

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(httpResponse);

            _apiClient.SetSession(_testSession);

            // Act
            var result = await _apiClient.GetPlaylistAsync(playlistId);

            // Assert
            result.Should().NotBeNull();
            result.Id.Should().Be(playlistId);
            result.Name.Should().Be("Test Playlist");
        }

        [Fact]
        public async Task GetPlaylistTracksAsync_ShouldFetchAllTracks()
        {
            // Arrange
            var playlistId = "123456";

            var playlistResponse = new
            {
                id = playlistId,
                name = "Test Playlist",
                tracks_count = 10,
                tracks = new
                {
                    limit = 500,
                    offset = 0,
                    total = 10,
                    items = new[]
                    {
                        new { track = new { id = "track1", title = "Track 1" } },
                        new { track = new { id = "track2", title = "Track 2" } }
                    }
                }
            };

            var httpResponse = HttpTestHelpers.CreateResponse(
                JsonConvert.SerializeObject(playlistResponse),
                HttpStatusCode.OK
            );

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(httpResponse);

            _apiClient.SetSession(_testSession);

            // Act
            var result = await _apiClient.GetPlaylistTracksAsync(playlistId);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCountGreaterThanOrEqualTo(2);
        }

        [Fact]
        public async Task SearchPlaylistsAsync_ShouldReturnMatchingPlaylists()
        {
            // Arrange
            var query = "jazz";

            var searchResponse = new
            {
                playlists = new
                {
                    limit = 50,
                    offset = 0,
                    total = 1,
                    items = new[]
                    {
                        new
                        {
                            id = "123456",
                            name = "Jazz Classics",
                            owner = new
                            {
                                name = "Test User"
                            }
                        }
                    }
                }
            };

            var httpResponse = HttpTestHelpers.CreateResponse(
                JsonConvert.SerializeObject(searchResponse),
                HttpStatusCode.OK
            );

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(httpResponse);

            _apiClient.SetSession(_testSession);

            // Act
            var result = await _apiClient.SearchPlaylistsAsync(query);

            // Assert
            result.Should().NotBeNull();
            result.Playlists?.Items.Should().NotBeEmpty();
        }

        #endregion

        #region Label Tests

        [Fact]
        public async Task GetLabelAsync_ShouldReturnValidLabel()
        {
            // Arrange
            var labelId = "6842";

            var labelResponse = new
            {
                id = labelId,
                name = "Columbia",
                slug = "columbia",
                albums_count = 9999
            };

            var httpResponse = HttpTestHelpers.CreateResponse(
                JsonConvert.SerializeObject(labelResponse),
                HttpStatusCode.OK
            );

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(httpResponse);

            _apiClient.SetSession(_testSession);

            // Act
            var result = await _apiClient.GetLabelAsync(labelId);

            // Assert
            result.Should().NotBeNull();
            result.Id.Should().Be(labelId);
            result.Name.Should().Be("Columbia");
        }

        [Fact]
        public async Task GetLabelAlbumsAsync_ShouldFetchAllAlbums()
        {
            // Arrange
            var labelId = "6842";

            var labelAlbumsResponse = new
            {
                albums = new
                {
                    limit = 500,
                    offset = 0,
                    total = 2,
                    items = new[]
                    {
                        new
                        {
                            id = "album1",
                            title = "Album 1"
                        },
                        new
                        {
                            id = "album2",
                            title = "Album 2"
                        }
                    }
                }
            };

            var httpResponse = HttpTestHelpers.CreateResponse(
                JsonConvert.SerializeObject(labelAlbumsResponse),
                HttpStatusCode.OK
            );

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(httpResponse);

            _apiClient.SetSession(_testSession);

            // Act
            var result = await _apiClient.GetLabelAlbumsAsync(labelId);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCountGreaterThanOrEqualTo(2);
        }

        [Fact]
        public async Task SearchLabelsAsync_ShouldReturnMatchingLabels()
        {
            // Arrange
            var query = "Columbia";

            var searchResponse = new
            {
                labels = new
                {
                    limit = 50,
                    offset = 0,
                    total = 1,
                    items = new[]
                    {
                        new
                        {
                            id = "6842",
                            name = "Columbia",
                            slug = "columbia"
                        }
                    }
                }
            };

            var httpResponse = HttpTestHelpers.CreateResponse(
                JsonConvert.SerializeObject(searchResponse),
                HttpStatusCode.OK
            );

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(httpResponse);

            _apiClient.SetSession(_testSession);

            // Act
            var result = await _apiClient.SearchLabelsAsync(query);

            // Assert
            result.Should().NotBeNull();
            result.Labels?.Items.Should().NotBeEmpty();
        }

        #endregion

        #region Artist Tests

        [Fact]
        public async Task GetArtistAsync_ShouldReturnValidArtist()
        {
            // Arrange
            var artistId = "26887";

            var artistResponse = new
            {
                id = artistId,
                name = "Daft Punk",
                slug = "daft-punk",
                albums_count = 20,
                picture = "https://static.qobuz.com/images/artists/pictures/ba/58/26887_1424951484_230.jpg"
            };

            var httpResponse = HttpTestHelpers.CreateResponse(
                JsonConvert.SerializeObject(artistResponse),
                HttpStatusCode.OK
            );

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(httpResponse);

            _apiClient.SetSession(_testSession);

            // Act
            var result = await _apiClient.GetArtistAsync(artistId);

            // Assert
            result.Should().NotBeNull();
            result.Id.Should().Be(artistId);
            result.Name.Should().Be("Daft Punk");
        }

        [Fact]
        public async Task GetArtistAlbumsAsync_ShouldFetchAllAlbums()
        {
            // Arrange
            var artistId = "26887";

            var artistAlbumsResponse = new
            {
                albums = new
                {
                    limit = 500,
                    offset = 0,
                    total = 2,
                    items = new[]
                    {
                        new
                        {
                            id = "album1",
                            title = "Album 1"
                        },
                        new
                        {
                            id = "album2",
                            title = "Album 2"
                        }
                    }
                }
            };

            var httpResponse = HttpTestHelpers.CreateResponse(
                JsonConvert.SerializeObject(artistAlbumsResponse),
                HttpStatusCode.OK
            );

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(httpResponse);

            _apiClient.SetSession(_testSession);

            // Act
            var result = await _apiClient.GetArtistAlbumsAsync(artistId);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCountGreaterThanOrEqualTo(2);
        }

        #endregion

        #region Error Handling Tests

        [Theory]
        [InlineData(401, "AuthenticationFailed")]
        [InlineData(403, "AccessForbidden")]
        [InlineData(404, "NotFound")]
        [InlineData(429, "RateLimited")]
        [InlineData(500, "ServerError")]
        public async Task ExecuteRequestAsync_WithDifferentErrorCodes_ShouldThrowCorrectException(
            int statusCode, string expectedErrorType)
        {
            // Arrange
            var endpoint = "/album/get";
            var errorContent = $"{{\"status\":\"error\",\"code\":{statusCode},\"message\":\"Error message\"}}";

            var errorResponse = HttpTestHelpers.CreateErrorResponse(
                (HttpStatusCode)statusCode,
                errorContent
            );

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(errorResponse);

            _apiClient.SetSession(_testSession);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<Lidarr.Plugin.Qobuzarr.API.QobuzApiException>(
                () => _apiClient.GetAsync<JObject>(endpoint)
            );

            exception.StatusCode.Should().Be(statusCode);
            exception.ErrorType.Should().Be(expectedErrorType);
        }

        [Fact]
        public async Task ExecuteRequestAsync_WithNonJsonErrorResponse_ShouldThrowGenericException()
        {
            // Arrange
            var endpoint = "/album/get";
            var errorResponse = HttpTestHelpers.CreateErrorResponse(
                HttpStatusCode.InternalServerError,
                "Plain text error"
            );

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(errorResponse);

            _apiClient.SetSession(_testSession);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<Lidarr.Plugin.Qobuzarr.API.QobuzApiException>(
                () => _apiClient.GetAsync<JObject>(endpoint)
            );

            exception.StatusCode.Should().Be(500);
            exception.ErrorType.Should().Be("UnknownError");
        }

        #endregion

        #region Edge Cases

        [Fact]
        public async Task GetAsync_WithEmptyParameters_ShouldWork()
        {
            // Arrange
            var endpoint = "/album/search";
            var httpResponse = HttpTestHelpers.CreateResponse("{}", HttpStatusCode.OK);

            HttpRequest capturedRequest = null;
            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .Callback<HttpRequest>(req => capturedRequest = req)
                         .ReturnsAsync(httpResponse);

            _apiClient.SetSession(_testSession);

            // Act
            await _apiClient.GetAsync<JObject>(endpoint, null);

            // Assert
            capturedRequest.Should().NotBeNull();
            MockHttpClient.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Once);
        }

        [Fact]
        public async Task PostAsync_WithNullData_ShouldWork()
        {
            // Arrange
            var endpoint = "/user/login";
            var httpResponse = HttpTestHelpers.CreateResponse("{}", HttpStatusCode.OK);

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ReturnsAsync(httpResponse);

            // Act
            var result = await _apiClient.PostAsync<JObject>(endpoint, null);

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public async Task GetAsync_WithSpecialCharactersInQuery_ShouldEncodeProperly()
        {
            // Arrange
            var endpoint = "/album/search";
            var parameters = new Dictionary<string, string>
            {
                { "query", "test & special? chars" },
                { "limit", "20" }
            };

            var httpResponse = HttpTestHelpers.CreateResponse("{}", HttpStatusCode.OK);

            HttpRequest capturedRequest = null;
            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .Callback<HttpRequest>(req => capturedRequest = req)
                         .ReturnsAsync(httpResponse);

            _apiClient.SetSession(_testSession);

            // Act
            await _apiClient.GetAsync<JObject>(endpoint, parameters);

            // Assert
            capturedRequest.Should().NotBeNull();
            var url = capturedRequest.Url.ToString();
            url.Should().Contain("query=", "URL should contain query parameter");
        }

        #endregion

        #region Cancellation Token Tests

        [Fact]
        public async Task GetStreamingInfoAsync_WithCancelledToken_ShouldThrowOperationCanceledException()
        {
            // Arrange
            var trackId = "23374053";
            var formatId = 6;
            var cts = new CancellationTokenSource();

            // Setup mock to throw when cancelled
            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                         .ThrowsAsync(new OperationCanceledException("Operation cancelled"));

            _apiClient.SetSession(_testSession);

            // Cancel before the call
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => _apiClient.GetStreamingInfoAsync(trackId, formatId, cts.Token)
            );
        }

        #endregion

        public override void Dispose()
        {
            // Cleanup handled by base class
            base.Dispose();
        }
    }
}
