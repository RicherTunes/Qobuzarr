using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NzbDrone.Common.Http;
using Xunit;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Qobuzarr.Tests.Builders;
using Qobuzarr.Tests.Fixtures;
using Qobuzarr.Tests.Helpers;
using Qobuzarr.Tests.TestData;

namespace Qobuzarr.Tests.Integration
{
    /// <summary>
    /// Hermetic end-to-end gate tests that exercise the full search -> metadata -> download-plan
    /// pipeline using mocked HTTP responses. No real Qobuz API calls are made.
    ///
    /// These tests validate that the core user-visible flow works correctly when all
    /// components are wired together, while remaining fully deterministic and CI-safe.
    /// </summary>
    [Trait("Category", "Integration")]
    [Trait("Category", "E2E")]
    [Trait("Area", "E2E/Hermetic")]
    // Shares the global NLog MemoryTarget (via TestLogger) with QobuzAppSecretLogScrubTests.
    // Both call ClearLoggedMessages()/GetLoggedMessages(); running them in parallel let one class's
    // Clear wipe the other's captured lines (flaky scrub-test assertions). Same serialization
    // collection forces sequential execution.
    [Collection(Qobuzarr.Tests.Collections.AuthenticationTestCollection.Name)]
    public class E2EHermeticGateTests : TestFixtureBase
    {
        private readonly QobuzApiClient _apiClient;
        private readonly QobuzSession _validSession;

        // Realistic Qobuz search response JSON for "Daft Punk Random Access Memories"
        private const string SearchResponseJson = @"{
            ""albums"": {
                ""limit"": 20,
                ""offset"": 0,
                ""total"": 1,
                ""items"": [
                    {
                        ""id"": ""0060254788359"",
                        ""title"": ""Random Access Memories"",
                        ""version"": null,
                        ""duration"": 4578,
                        ""tracks_count"": 13,
                        ""release_date_original"": ""2013-05-17"",
                        ""streamable"": true,
                        ""downloadable"": false,
                        ""maximum_bit_depth"": 24,
                        ""maximum_sampling_rate"": 96.0,
                        ""image"": {
                            ""small"": ""https://static.qobuz.com/images/covers/59/83/0060254788359_230.jpg"",
                            ""large"": ""https://static.qobuz.com/images/covers/59/83/0060254788359_600.jpg""
                        },
                        ""artist"": {
                            ""id"": 26887,
                            ""name"": ""Daft Punk""
                        },
                        ""label"": {
                            ""name"": ""Columbia""
                        }
                    }
                ]
            }
        }";

        // Realistic album detail response with track listing
        private const string AlbumDetailResponseJson = @"{
            ""id"": ""0060254788359"",
            ""title"": ""Random Access Memories"",
            ""version"": null,
            ""duration"": 4578,
            ""tracks_count"": 13,
            ""release_date_original"": ""2013-05-17"",
            ""streamable"": true,
            ""downloadable"": false,
            ""maximum_bit_depth"": 24,
            ""maximum_sampling_rate"": 96.0,
            ""image"": {
                ""small"": ""https://static.qobuz.com/images/covers/59/83/0060254788359_230.jpg"",
                ""large"": ""https://static.qobuz.com/images/covers/59/83/0060254788359_600.jpg""
            },
            ""artist"": {
                ""id"": 26887,
                ""name"": ""Daft Punk""
            },
            ""label"": {
                ""name"": ""Columbia""
            },
            ""tracks"": {
                ""offset"": 0,
                ""limit"": 50,
                ""total"": 3,
                ""items"": [
                    {
                        ""id"": 23374053,
                        ""title"": ""Give Life Back to Music"",
                        ""track_number"": 1,
                        ""media_number"": 1,
                        ""duration"": 274,
                        ""streamable"": true,
                        ""maximum_bit_depth"": 24,
                        ""maximum_sampling_rate"": 96.0,
                        ""isrc"": ""USSM11300001"",
                        ""performer"": { ""id"": 26887, ""name"": ""Daft Punk"" }
                    },
                    {
                        ""id"": 23374054,
                        ""title"": ""The Game of Love"",
                        ""track_number"": 2,
                        ""media_number"": 1,
                        ""duration"": 321,
                        ""streamable"": true,
                        ""maximum_bit_depth"": 24,
                        ""maximum_sampling_rate"": 96.0,
                        ""isrc"": ""USSM11300002"",
                        ""performer"": { ""id"": 26887, ""name"": ""Daft Punk"" }
                    },
                    {
                        ""id"": 23374055,
                        ""title"": ""Giorgio by Moroder"",
                        ""track_number"": 3,
                        ""media_number"": 1,
                        ""duration"": 544,
                        ""streamable"": true,
                        ""maximum_bit_depth"": 24,
                        ""maximum_sampling_rate"": 96.0,
                        ""isrc"": ""USSM11300003"",
                        ""performer"": { ""id"": 26887, ""name"": ""Daft Punk"" }
                    }
                ]
            }
        }";

        // Stream URL response for a track
        private const string StreamUrlResponseJson = @"{
            ""url"": ""https://streaming.qobuz.com/file?uid=23374053&fmt=6&token=mock_stream_token"",
            ""format_id"": 6,
            ""mime_type"": ""audio/flac"",
            ""sampling_rate"": 44.1,
            ""bit_depth"": 16
        }";

        // Auth failure response
        private const string AuthFailureResponseJson = @"{
            ""status"": ""error"",
            ""code"": 401,
            ""message"": ""Invalid or expired user_auth_token""
        }";

        // Session expired error
        private const string SessionExpiredResponseJson = @"{
            ""status"": ""error"",
            ""code"": 401,
            ""message"": ""Your session has expired, please log in again""
        }";

        public E2EHermeticGateTests()
        {
            _apiClient = new QobuzApiClient(MockHttpClient.Object, MockCacheManager, MockLogger.Object);
            _validSession = new QobuzSession
            {
                UserId = "12345678",
                AuthToken = "valid_auth_token_e2e",
                AppId = "test_app_id",
                AppSecret = "test_app_secret",
                ExpiresAt = DateTime.UtcNow.AddHours(12),
                CreatedAt = DateTime.UtcNow
            };
        }

        #region Golden Path: Search -> Album Metadata -> Download Plan

        [Fact]
        [Trait("Path", "Golden")]
        public async Task GoldenPath_SearchReturnsAlbums_WithValidSession()
        {
            // Arrange: mock HTTP to return search results
            var searchResponse = HttpTestHelpers.CreateResponse(SearchResponseJson, HttpStatusCode.OK);

            MockHttpClient.Setup(x => x.ExecuteAsync(It.Is<HttpRequest>(
                    r => r.Url.ToString().Contains("/album/search"))))
                .ReturnsAsync(searchResponse);

            _apiClient.SetSession(_validSession);

            // Act: search for an album (simulates what QobuzIndexer does)
            var result = await _apiClient.GetAsync<QobuzSearchResponse>("/album/search",
                new Dictionary<string, string>
                {
                    { "query", "Daft Punk Random Access Memories" },
                    { "limit", "20" }
                });

            // Assert: search returns album data
            result.Should().NotBeNull("search should return a response");
            result.Albums.Should().NotBeNull("response should contain albums container");
            result.Albums.Items.Should().HaveCount(1, "should find exactly one album");

            var album = result.Albums.Items[0];
            album.Title.Should().Be("Random Access Memories");
            album.Artist.Should().NotBeNull();
            album.Artist.Name.Should().Be("Daft Punk");
            album.Id.Should().Be("0060254788359");
            album.TracksCount.Should().Be(13);
        }

        [Fact]
        [Trait("Path", "Golden")]
        public async Task GoldenPath_AlbumMetadataReturnsTrackListing()
        {
            // Arrange: mock HTTP to return album details with tracks
            var albumResponse = HttpTestHelpers.CreateResponse(AlbumDetailResponseJson, HttpStatusCode.OK);

            MockHttpClient.Setup(x => x.ExecuteAsync(It.Is<HttpRequest>(
                    r => r.Url.ToString().Contains("/album/get"))))
                .ReturnsAsync(albumResponse);

            _apiClient.SetSession(_validSession);

            // Act: fetch album metadata (simulates what happens after search result click)
            var album = await _apiClient.GetAsync<QobuzAlbum>("/album/get",
                new Dictionary<string, string>
                {
                    { "album_id", "0060254788359" }
                });

            // Assert: album has full track listing
            album.Should().NotBeNull("album metadata should be returned");
            album.Title.Should().Be("Random Access Memories");
            album.TracksContainer.Should().NotBeNull("album should include tracks");
            album.TracksContainer.Items.Should().HaveCount(3, "mock has 3 tracks");
            album.TracksContainer.Items[0].Title.Should().Be("Give Life Back to Music");
            album.TracksContainer.Items[1].Title.Should().Be("The Game of Love");
            album.TracksContainer.Items[2].Title.Should().Be("Giorgio by Moroder");

            // Verify quality metadata
            album.MaximumBitDepth.Should().Be(24, "album reports 24-bit");
            album.MaximumSampleRate.Should().BeApproximately(96.0, 0.1, "album reports 96kHz");
        }

        [Fact]
        [Trait("Path", "Golden")]
        public async Task GoldenPath_StreamUrlResolvedForTrack()
        {
            // Arrange: mock HTTP to return stream URL
            var streamResponse = HttpTestHelpers.CreateResponse(StreamUrlResponseJson, HttpStatusCode.OK);

            MockHttpClient.Setup(x => x.ExecuteAsync(It.Is<HttpRequest>(
                    r => r.Url.ToString().Contains("/track/getFileUrl"))))
                .ReturnsAsync(streamResponse);

            _apiClient.SetSession(_validSession);

            // Act: resolve stream URL for a track (simulates download preparation)
            var streamInfo = await _apiClient.GetAsync<QobuzStreamResponse>("/track/getFileUrl",
                new Dictionary<string, string>
                {
                    { "track_id", "23374053" },
                    { "format_id", "6" }
                });

            // Assert: stream URL is valid and metadata is correct
            streamInfo.Should().NotBeNull("stream URL response should be returned");
            streamInfo.Url.Should().NotBeNullOrEmpty("URL should be present");
            streamInfo.Url.Should().Contain("streaming.qobuz.com", "URL should point to Qobuz CDN");
            streamInfo.FormatId.Should().Be(6, "format should be FLAC CD quality");
            streamInfo.MimeType.Should().Be("audio/flac", "MIME type should be FLAC");
            streamInfo.IsLossless().Should().BeTrue("FLAC is lossless");
        }

        [Fact]
        [Trait("Path", "Golden")]
        public async Task GoldenPath_FullPipeline_SearchThenMetadataThenStream()
        {
            // Arrange: wire up the full pipeline with sequential mock responses
            var searchResponse = HttpTestHelpers.CreateResponse(SearchResponseJson, HttpStatusCode.OK);
            var albumResponse = HttpTestHelpers.CreateResponse(AlbumDetailResponseJson, HttpStatusCode.OK);
            var streamResponse = HttpTestHelpers.CreateResponse(StreamUrlResponseJson, HttpStatusCode.OK);

            MockHttpClient.Setup(x => x.ExecuteAsync(It.Is<HttpRequest>(
                    r => r.Url.ToString().Contains("/album/search"))))
                .ReturnsAsync(searchResponse);

            MockHttpClient.Setup(x => x.ExecuteAsync(It.Is<HttpRequest>(
                    r => r.Url.ToString().Contains("/album/get"))))
                .ReturnsAsync(albumResponse);

            MockHttpClient.Setup(x => x.ExecuteAsync(It.Is<HttpRequest>(
                    r => r.Url.ToString().Contains("/track/getFileUrl"))))
                .ReturnsAsync(streamResponse);

            _apiClient.SetSession(_validSession);

            // Act Step 1: Search
            var searchResult = await _apiClient.GetAsync<QobuzSearchResponse>("/album/search",
                new Dictionary<string, string> { { "query", "Daft Punk" }, { "limit", "20" } });

            searchResult.Albums.Items.Should().NotBeEmpty("search should find albums");
            var albumId = searchResult.Albums.Items[0].Id;

            // Act Step 2: Fetch album metadata
            var album = await _apiClient.GetAsync<QobuzAlbum>("/album/get",
                new Dictionary<string, string> { { "album_id", albumId } });

            album.TracksContainer.Should().NotBeNull("album should have tracks");
            album.TracksContainer.Items.Should().NotBeEmpty("album should have track items");

            // Act Step 3: Resolve stream URL for each track (download plan)
            var downloadPlan = new List<(string TrackTitle, string StreamUrl, int FormatId)>();
            foreach (var track in album.TracksContainer.Items)
            {
                var stream = await _apiClient.GetAsync<QobuzStreamResponse>("/track/getFileUrl",
                    new Dictionary<string, string>
                    {
                        { "track_id", track.Id.ToString() },
                        { "format_id", "6" }
                    });

                downloadPlan.Add((track.Title, stream.Url, stream.FormatId));
            }

            // Assert: complete download plan was assembled
            downloadPlan.Should().HaveCount(3, "all 3 tracks should have stream URLs");
            downloadPlan.Should().OnlyContain(t => !string.IsNullOrEmpty(t.StreamUrl),
                "every track should have a resolved stream URL");
            downloadPlan.Should().OnlyContain(t => t.FormatId == 6,
                "all tracks should be FLAC CD quality (format 6)");

            // Verify the pipeline used auth headers
            MockHttpClient.Verify(
                x => x.ExecuteAsync(It.Is<HttpRequest>(
                    r => r.Url.ToString().Contains("user_auth_token=valid_auth_token_e2e"))),
                Times.AtLeast(3),
                "all API calls should include the auth token");
        }

        [Fact]
        [Trait("Path", "Golden")]
        public void GoldenPath_BuilderCreatesValidDownloadableAlbum()
        {
            // Arrange & Act: use the builder to create a test album
            var tracks = QobuzTrackBuilder.BuildAlbumTracks(5, "test_album");
            var album = QobuzAlbumBuilder.New()
                .WithId("test_album")
                .WithTitle("Test Album")
                .WithArtist("Test Artist")
                .AsHiResFlac()
                .AsFullAlbum()
                .WithActualTracks(tracks)
                .Build();

            // Assert: album is complete and downloadable
            album.Should().NotBeNull();
            album.Id.Should().Be("test_album");
            album.Title.Should().Be("Test Album");
            album.Artist.Name.Should().Be("Test Artist");
            album.Streamable.Should().BeTrue("default album should be streamable");
            album.MaximumBitDepth.Should().Be(24, "hi-res should be 24-bit");
            album.MaximumSampleRate.Should().Be(192000, "hi-res should be 192kHz");
            album.TracksContainer.Should().NotBeNull("should have tracks container");
            album.TracksContainer.Items.Should().HaveCount(5, "should have 5 tracks");
        }

        #endregion

        #region Auth Failure Path: Invalid Session / Expired Token

        [Fact]
        [Trait("Path", "AuthFailure")]
        public async Task AuthFailure_InvalidToken_ReturnsUnauthorizedGracefully()
        {
            // Arrange: mock HTTP to return 401 Unauthorized
            var authErrorResponse = HttpTestHelpers.CreateErrorResponse(
                HttpStatusCode.Unauthorized, AuthFailureResponseJson);

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new HttpException(authErrorResponse));

            _apiClient.SetSession(_validSession);

            // Act & Assert: API call with invalid token should throw HttpException with 401
            var exception = await Assert.ThrowsAsync<HttpException>(
                () => _apiClient.GetAsync<QobuzSearchResponse>("/album/search",
                    new Dictionary<string, string> { { "query", "test" } }));

            exception.Response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                "server should return 401 for invalid auth token");
        }

        [Fact]
        [Trait("Path", "AuthFailure")]
        public async Task AuthFailure_ExpiredSession_ReturnsUnauthorizedGracefully()
        {
            // Arrange: create an expired session and mock 401 response
            var expiredSession = new QobuzSession
            {
                UserId = "12345678",
                AuthToken = "expired_token_abc",
                AppId = "test_app_id",
                AppSecret = "test_app_secret",
                ExpiresAt = DateTime.UtcNow.AddHours(-2), // Expired 2 hours ago
                CreatedAt = DateTime.UtcNow.AddHours(-26)
            };

            var authErrorResponse = HttpTestHelpers.CreateErrorResponse(
                HttpStatusCode.Unauthorized, SessionExpiredResponseJson);

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new HttpException(authErrorResponse));

            _apiClient.SetSession(expiredSession);

            // Session itself knows it is expired
            expiredSession.IsValid().Should().BeFalse("expired session should report as invalid");

            // Act & Assert: API call with expired session should throw HttpException
            var exception = await Assert.ThrowsAsync<HttpException>(
                () => _apiClient.GetAsync<QobuzSearchResponse>("/album/search",
                    new Dictionary<string, string> { { "query", "test" } }));

            exception.Response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                "expired session should result in 401");
        }

        [Fact]
        [Trait("Path", "AuthFailure")]
        public void AuthFailure_NoSession_ReportsNoValidSession()
        {
            // Arrange: do not set any session on the API client

            // Act & Assert: client should report no valid session
            _apiClient.HasValidSession().Should().BeFalse(
                "API client without a session should report no valid session");
        }

        [Fact]
        [Trait("Path", "AuthFailure")]
        public void AuthFailure_ClearedSession_ReportsNoValidSession()
        {
            // Arrange: set and then clear the session
            _apiClient.SetSession(_validSession);
            _apiClient.HasValidSession().Should().BeTrue("session was set");

            // Act
            _apiClient.ClearSession();

            // Assert
            _apiClient.HasValidSession().Should().BeFalse(
                "cleared session should report as not valid");
        }

        [Fact]
        [Trait("Path", "AuthFailure")]
        public async Task AuthFailure_SearchAfterSessionClear_OmitsAuthHeaders()
        {
            // Arrange: set session, clear it, then make request
            _apiClient.SetSession(_validSession);
            _apiClient.ClearSession();

            var response = HttpTestHelpers.CreateResponse(SearchResponseJson, HttpStatusCode.OK);
            HttpRequest capturedRequest = null;
            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => capturedRequest = r)
                .ReturnsAsync(response);

            // Act: search without valid session
            await _apiClient.GetAsync<QobuzSearchResponse>("/album/search",
                new Dictionary<string, string> { { "query", "test" } });

            // Assert: request should not contain auth token
            capturedRequest.Should().NotBeNull("request should have been made");
            capturedRequest.Url.ToString().Should().NotContain("user_auth_token=valid_auth_token_e2e",
                "cleared session should not leak auth tokens into requests");
        }

        [Fact]
        [Trait("Path", "AuthFailure")]
        public void AuthFailure_CredentialValidation_RejectsEmptyEmail()
        {
            // Arrange: credentials with empty email
            var badCredentials = new QobuzCredentials
            {
                Email = "",
                MD5Password = "some_password"
            };

            // Act & Assert: validation should fail
            badCredentials.IsValid().Should().BeFalse(
                "empty email should not be considered valid");
        }

        [Fact]
        [Trait("Path", "AuthFailure")]
        public void AuthFailure_CredentialValidation_RejectsEmptyPassword()
        {
            // Arrange: credentials with empty password
            var badCredentials = new QobuzCredentials
            {
                Email = "test@example.com",
                MD5Password = ""
            };

            // Act & Assert: validation should fail
            badCredentials.IsValid().Should().BeFalse(
                "empty password should not be considered valid");
        }

        #endregion

        #region Edge Cases

        [Fact]
        [Trait("Path", "Edge")]
        public async Task Edge_EmptySearchResults_HandledGracefully()
        {
            // Arrange: mock HTTP to return empty search results
            var emptySearchJson = @"{
                ""albums"": {
                    ""limit"": 20,
                    ""offset"": 0,
                    ""total"": 0,
                    ""items"": []
                }
            }";
            var emptyResponse = HttpTestHelpers.CreateResponse(emptySearchJson, HttpStatusCode.OK);

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(emptyResponse);

            _apiClient.SetSession(_validSession);

            // Act: search that returns no results
            var result = await _apiClient.GetAsync<QobuzSearchResponse>("/album/search",
                new Dictionary<string, string> { { "query", "xyznonexistentquery999" } });

            // Assert: empty results should not throw, just return empty list
            result.Should().NotBeNull("response should still be returned");
            result.Albums.Should().NotBeNull("albums container should exist");
            result.Albums.Items.Should().BeEmpty("no albums should match the query");
            result.Albums.Total.Should().Be(0, "total should be zero");
        }

        [Fact]
        [Trait("Path", "Edge")]
        public async Task Edge_StreamResponse_WithRestriction_IdentifiesQualityFallback()
        {
            // Arrange: stream response with quality fallback restriction
            var restrictedStreamJson = @"{
                ""url"": ""https://streaming.qobuz.com/file?uid=23374053&fmt=6&token=mock"",
                ""format_id"": 6,
                ""mime_type"": ""audio/flac"",
                ""sampling_rate"": 44.1,
                ""bit_depth"": 16,
                ""restrictions"": [
                    {
                        ""code"": ""FormatRestrictedByFormatAvailability"",
                        ""reason"": """",
                        ""reason_code"": """"
                    }
                ]
            }";
            var restrictedResponse = HttpTestHelpers.CreateResponse(restrictedStreamJson, HttpStatusCode.OK);

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(restrictedResponse);

            _apiClient.SetSession(_validSession);

            // Act
            var stream = await _apiClient.GetAsync<QobuzStreamResponse>("/track/getFileUrl",
                new Dictionary<string, string>
                {
                    { "track_id", "23374053" },
                    { "format_id", "27" } // Requested hi-res, got CD quality
                });

            // Assert: quality fallback detected
            stream.Should().NotBeNull();
            stream.Url.Should().NotBeNullOrEmpty("fallback still provides a URL");
            stream.FormatId.Should().Be(6, "format fell back to CD quality");
            stream.IsQualityFallbackOnly().Should().BeTrue(
                "restriction should be identified as quality fallback, not hard block");
        }

        [Fact]
        [Trait("Path", "Edge")]
        public void Edge_NonStreamableAlbum_IdentifiedByBuilder()
        {
            // Arrange & Act: create a non-streamable album
            var album = QobuzAlbumBuilder.New()
                .WithId("restricted_album")
                .WithTitle("Restricted Album")
                .AsNotStreamable()
                .Build();

            // Assert
            album.Streamable.Should().BeFalse(
                "non-streamable album should be flagged correctly");
        }

        #endregion

        #region Credential Redaction: Tokens Must Not Leak Into Error Messages

        [Fact]
        [Trait("Path", "Redaction")]
        public async Task Redaction_InvalidToken_ExceptionDoesNotLeakAuthToken()
        {
            // Arrange: mock HTTP to throw with 401 response containing error details
            var authErrorResponse = HttpTestHelpers.CreateErrorResponse(
                HttpStatusCode.Unauthorized, AuthFailureResponseJson);

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new HttpException(authErrorResponse));

            _apiClient.SetSession(_validSession);

            // Act: trigger auth failure
            var exception = await Assert.ThrowsAsync<HttpException>(
                () => _apiClient.GetAsync<QobuzSearchResponse>("/album/search",
                    new Dictionary<string, string> { { "query", "test" } }));

            // Assert: sensitive credentials must NOT appear in exception messages
            exception.Message.Should().NotContain("valid_auth_token_e2e",
                "auth token must not leak into exception message");
            exception.Message.Should().NotContain("test_app_secret",
                "app secret must not leak into exception message");
            exception.ToString().Should().NotContain("test_app_secret",
                "app secret must not leak into exception stack trace");
        }

        [Fact]
        [Trait("Path", "Redaction")]
        public async Task Redaction_ExpiredSession_RequestUrlDoesNotLeakAppSecret()
        {
            // Arrange: set session and make a request - verify app_secret is not in URL
            HttpRequest capturedRequest = null;
            var response = HttpTestHelpers.CreateResponse(SearchResponseJson, HttpStatusCode.OK);
            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => capturedRequest = r)
                .ReturnsAsync(response);

            _apiClient.SetSession(_validSession);

            // Act
            await _apiClient.GetAsync<QobuzSearchResponse>("/album/search",
                new Dictionary<string, string> { { "query", "test" } });

            // Assert: app_secret should NOT be exposed directly in request URL
            capturedRequest.Should().NotBeNull("request should have been made");
            capturedRequest.Url.ToString().Should().NotContain("test_app_secret",
                "app secret must not be sent as a plain URL parameter");
        }

        #endregion

        #region Rate Limiting: 429 Response -> Graceful Error

        [Fact]
        [Trait("Path", "Edge")]
        public async Task Edge_RateLimit429_ThrowsHttpException()
        {
            // Arrange: mock HTTP to return 429 Too Many Requests
            var rateLimitResponse = HttpTestHelpers.CreateErrorResponse(
                (HttpStatusCode)429,
                @"{""status"":""error"",""code"":429,""message"":""Too many requests""}");

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new HttpException(rateLimitResponse));

            _apiClient.SetSession(_validSession);

            // Act & Assert: 429 should produce HttpException, not crash or hang
            var exception = await Assert.ThrowsAsync<HttpException>(
                () => _apiClient.GetAsync<QobuzSearchResponse>("/album/search",
                    new Dictionary<string, string> { { "query", "test" } }));

            exception.Response.StatusCode.Should().Be((HttpStatusCode)429,
                "server should return 429 for rate-limited requests");
        }

        #endregion

        #region Log-Capture Redaction: Secrets Must Not Leak Into Structured Log Output

        [Fact]
        [Trait("Path", "Redaction")]
        public async Task LogRedaction_AuthFailure_NoSecretsInLogs()
        {
            // Arrange: use a real NLog logger with MemoryTarget to capture log output
            TestLogger.ClearLoggedMessages();
            var logger = TestLogger.Create("QobuzApiClient");
            var apiClient = new QobuzApiClient(MockHttpClient.Object, MockCacheManager, logger);

            var authErrorResponse = HttpTestHelpers.CreateErrorResponse(
                HttpStatusCode.Unauthorized, AuthFailureResponseJson);

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new HttpException(authErrorResponse));

            apiClient.SetSession(_validSession);

            // Act: trigger auth failure
            await Assert.ThrowsAsync<HttpException>(
                () => apiClient.GetAsync<QobuzSearchResponse>("/album/search",
                    new Dictionary<string, string> { { "query", "test" } }));

            // Assert: actual secrets (auth token, app secret) must not appear in logs.
            // Note: app_id is a public client identifier (like OAuth client_id), not a secret.
            var logMessages = TestLogger.GetLoggedMessages();
            foreach (var msg in logMessages)
            {
                Assert.DoesNotContain("valid_auth_token_e2e", msg);
                Assert.DoesNotContain("test_app_secret", msg);
            }
        }

        [Fact]
        [Trait("Path", "Redaction")]
        public async Task LogRedaction_RateLimit429_NoSecretsInLogs()
        {
            // Arrange: use a real NLog logger with MemoryTarget to capture log output
            TestLogger.ClearLoggedMessages();
            var logger = TestLogger.Create("QobuzApiClient");
            var apiClient = new QobuzApiClient(MockHttpClient.Object, MockCacheManager, logger);

            var rateLimitResponse = HttpTestHelpers.CreateErrorResponse(
                (HttpStatusCode)429,
                @"{""status"":""error"",""code"":429,""message"":""Too many requests""}");

            MockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new HttpException(rateLimitResponse));

            apiClient.SetSession(_validSession);

            // Act: trigger rate limit
            await Assert.ThrowsAsync<HttpException>(
                () => apiClient.GetAsync<QobuzSearchResponse>("/album/search",
                    new Dictionary<string, string> { { "query", "test" } }));

            // Assert: actual secrets (auth token, app secret) must not appear in logs.
            // Note: app_id is a public client identifier (like OAuth client_id), not a secret.
            var logMessages = TestLogger.GetLoggedMessages();
            foreach (var msg in logMessages)
            {
                Assert.DoesNotContain("valid_auth_token_e2e", msg);
                Assert.DoesNotContain("test_app_secret", msg);
            }
        }

        #endregion

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}
