using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using Xunit;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.API.Caching;
using Lidarr.Plugin.Qobuzarr.API.Http;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Lidarr.Plugin.Qobuzarr.Services.Interfaces;
using IRequestSigner = Lidarr.Plugin.Common.Services.Http.IRequestSigner;
using Qobuzarr.Tests.Helpers;

namespace Qobuzarr.Tests
{
    /// <summary>
    /// Q-2/Q-3: CancellationToken threading through QobuzApiClient.
    /// The public pagination methods (GetArtistAlbumsAsync, GetLabelAlbumsAsync,
    /// GetPlaylistTracksAsync) and single-shot methods (GetPlaylistAsync,
    /// GetTrackMetadataAsync) accept a CancellationToken but historically dropped it:
    /// GetAsync/ExecuteCachedGetAsync had no token parameter, so a caller's token
    /// could never stop an in-flight pagination loop. These tests pin the contract
    /// that a cancelled token aborts the loop with OperationCanceledException and
    /// stops issuing further HTTP requests.
    /// </summary>
    public class QobuzApiClientCancellationTests
    {
        private readonly Mock<IQobuzHttpClient> _mockHttpClient;
        private readonly Mock<ISessionManager> _mockSessionManager;
        private readonly Mock<IRequestSigner> _mockRequestSigner;
        private readonly Mock<IQobuzResponseCache> _mockResponseCache;
        private readonly Mock<Logger> _mockLogger;
        private readonly QobuzSession _validSession;
        private int _httpCallCount;

        public QobuzApiClientCancellationTests()
        {
            _mockHttpClient = new Mock<IQobuzHttpClient>();
            _mockSessionManager = new Mock<ISessionManager>();
            _mockRequestSigner = new Mock<IRequestSigner>();
            _mockResponseCache = new Mock<IQobuzResponseCache>();
            _mockLogger = new Mock<Logger>();

            _validSession = new QobuzSession
            {
                UserId = "123456",
                AuthToken = "test_token_123",
                AppId = "test_app_id",
                AppSecret = "test_secret",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };
        }

        [Fact]
        public async Task GetArtistAlbumsAsync_PreCancelledToken_ThrowsOCE_AndMakesNoHttpCall()
        {
            var client = CreateClient();
            SetupPagedAlbumResponses(itemsPerPage: 2, total: 6);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => client.GetArtistAlbumsAsync("artist-1", cts.Token));

            _httpCallCount.Should().Be(0, "a pre-cancelled token must abort before any HTTP request is issued");
        }

        [Fact]
        public async Task GetArtistAlbumsAsync_CancelledToken_StopsPagination_AndThrowsOCE()
        {
            var client = CreateClient();
            using var cts = new CancellationTokenSource();
            // 2 items per page, total 6 => without cancellation the loop would issue 3 HTTP calls.
            SetupPagedAlbumResponses(itemsPerPage: 2, total: 6, onRequest: () => cts.Cancel());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => client.GetArtistAlbumsAsync("artist-1", cts.Token));

            _httpCallCount.Should().Be(1, "cancelling after the first page must stop the pagination loop");
        }

        [Fact]
        public async Task GetLabelAlbumsAsync_CancelledToken_StopsPagination_AndThrowsOCE()
        {
            var client = CreateClient();
            using var cts = new CancellationTokenSource();
            SetupPagedAlbumResponses(itemsPerPage: 2, total: 6, onRequest: () => cts.Cancel());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => client.GetLabelAlbumsAsync("label-1", cts.Token));

            _httpCallCount.Should().Be(1, "cancelling after the first page must stop the pagination loop");
        }

        [Fact]
        public async Task GetPlaylistAsync_PreCancelledToken_ThrowsOCE_AndMakesNoHttpCall()
        {
            var client = CreateClient();
            SetupJsonResponse("{\"id\": \"pl-1\"}");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => client.GetPlaylistAsync("pl-1", cancellationToken: cts.Token));

            _httpCallCount.Should().Be(0, "GetPlaylistAsync must honor its own cancellation token");
        }

        [Fact]
        public async Task GetPlaylistTracksAsync_PreCancelledToken_ThrowsOCE_AndMakesNoHttpCall()
        {
            var client = CreateClient();
            SetupJsonResponse("{\"id\": \"pl-1\"}");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => client.GetPlaylistTracksAsync("pl-1", cts.Token));

            _httpCallCount.Should().Be(0);
        }

        [Fact]
        public async Task GetAsync_PreCancelledToken_ThrowsOCE_AndMakesNoHttpCall()
        {
            var client = CreateClient();
            SetupJsonResponse("{\"id\": \"123\"}");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => client.GetAsync<object>("album/get", null, cts.Token));

            _httpCallCount.Should().Be(0);
        }

        [Fact]
        public async Task GetTrackMetadataAsync_PreCancelledToken_ThrowsOCE_AndMakesNoHttpCall()
        {
            var client = CreateClient();
            SetupJsonResponse("{\"id\": \"track-1\"}");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => client.GetTrackMetadataAsync("track-1", cts.Token));

            _httpCallCount.Should().Be(0);
        }

        // ---- Helpers ----

        private QobuzApiClient CreateClient()
        {
            return new QobuzApiClient(
                _mockHttpClient.Object,
                _mockSessionManager.Object,
                _mockRequestSigner.Object,
                _mockResponseCache.Object,
                _mockLogger.Object);
        }

        private void SetupCommon()
        {
            _mockSessionManager.Setup(x => x.GetCurrentSessionAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_validSession);

            _mockResponseCache.Setup(x => x.ShouldCache(It.IsAny<string>()))
                .Returns(false);

            _mockHttpClient.Setup(x => x.BuildRequest(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string url, string method) => new HttpRequestBuilder(url));
        }

        /// <summary>
        /// Sets up ExecuteAsync to return a fixed album page every time. With
        /// itemsPerPage=2 and total=6, an uncancelled pagination loop issues 3 requests.
        /// </summary>
        private void SetupPagedAlbumResponses(int itemsPerPage, int total, Action? onRequest = null)
        {
            SetupCommon();

            var items = new List<string>();
            for (var i = 0; i < itemsPerPage; i++)
            {
                items.Add($"{{\"id\": \"album-{i}\", \"title\": \"Album {i}\"}}");
            }

            var pageJson = $"{{\"albums\": {{\"items\": [{string.Join(",", items)}], \"total\": {total}, \"offset\": 0, \"limit\": 500}}}}";

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
                .Callback(() =>
                {
                    _httpCallCount++;
                    onRequest?.Invoke();
                })
                .ReturnsAsync(() => HttpTestHelpers.CreateResponse(pageJson, HttpStatusCode.OK));
        }

        private void SetupJsonResponse(string json)
        {
            SetupCommon();

            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
                .Callback(() => _httpCallCount++)
                .ReturnsAsync(() => HttpTestHelpers.CreateResponse(json, HttpStatusCode.OK));
        }
    }
}
