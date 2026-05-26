using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Abstractions.Contracts;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Integration;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Models.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Qobuzarr.Tests.Builders;
using Xunit;

namespace Qobuzarr.Tests.Integration;

/// <summary>
/// Tests for <see cref="QobuzIndexerAdapter"/> covering both happy paths
/// and error handling for all adapter operations.
/// </summary>
public class QobuzIndexerAdapterTests
{
    // ----------------------------------------------------------------
    // Fakes
    // ----------------------------------------------------------------

    /// <summary>
    /// In-memory fake for <see cref="IIndexerStatusReporter"/> that records
    /// all status transitions and error reports for assertion.
    /// </summary>
    private sealed class FakeStatusReporter : IIndexerStatusReporter
    {
        public IndexerStatus CurrentStatus { get; private set; } = IndexerStatus.Idle;
        public List<(IndexerStatus Status, string? Message)> StatusHistory { get; } = new();
        public List<Exception> ReportedErrors { get; } = new();

        public ValueTask ReportStatusAsync(IndexerStatus status, string? message = null, CancellationToken cancellationToken = default)
        {
            CurrentStatus = status;
            StatusHistory.Add((status, message));
            return ValueTask.CompletedTask;
        }

        public ValueTask ReportErrorAsync(Exception error, CancellationToken cancellationToken = default)
        {
            CurrentStatus = IndexerStatus.Error;
            ReportedErrors.Add(error);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Configurable fake for <see cref="IQobuzApiClient"/>. Tests set
    /// <see cref="GetAsyncHandler"/> to control what the adapter sees.
    /// </summary>
    private sealed class FakeApiClient : IQobuzApiClient
    {
        public bool SessionValid { get; set; } = true;

        /// <summary>
        /// Handler invoked for every GetAsync call. The test can supply a
        /// lambda that returns the desired fake response (or throws).
        /// </summary>
        public Func<string, Dictionary<string, string>?, object?>? GetAsyncHandler { get; set; }

        public Task<T> GetAsync<T>(string endpoint, Dictionary<string, string>? parameters = null) where T : class
        {
            if (GetAsyncHandler is null)
                return Task.FromResult(default(T)!);

            var result = GetAsyncHandler(endpoint, parameters);
            return Task.FromResult((T)result!);
        }

        public Task<T> PostAsync<T>(string endpoint, object? data = null) where T : class
            => Task.FromResult(default(T)!);

        public void SetSession(QobuzSession session) { }
        public void ClearSession() { }
        public bool HasValidSession() => SessionValid;

        public Task<string> GetStreamingUrlAsync(string trackId, int formatId, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<QobuzStreamResponse> GetStreamingInfoAsync(string trackId, int formatId, CancellationToken cancellationToken = default)
            => Task.FromResult(new QobuzStreamResponse());

        // Gate is not wired in the bridge-less test fake; callers treat null as "always healthy".
        public Lidarr.Plugin.Common.Services.Bridge.AuthFailureGate? Gate => null;
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static (QobuzIndexerAdapter Adapter, FakeStatusReporter Reporter, FakeApiClient ApiClient)
        CreateAdapter(int searchLimit = 100)
    {
        var reporter = new FakeStatusReporter();
        var apiClient = new FakeApiClient();
        var settings = new QobuzarrStreamingSettings
        {
            Email = "test@test.com",
            Password = "pass",
            DownloadPath = "/music",
            PreferredQuality = 6,
            CountryCode = "US",
            SearchLimit = searchLimit
        };
        var logger = NullLoggerFactory.Instance.CreateLogger<QobuzIndexerAdapter>();
        var adapter = new QobuzIndexerAdapter(apiClient, reporter, logger, settings);
        return (adapter, reporter, apiClient);
    }

    // ================================================================
    // InitializeAsync
    // ================================================================

    [Fact]
    public async Task InitializeAsync_WithValidSession_ReturnsSuccess()
    {
        var (adapter, reporter, _) = CreateAdapter();

        var result = await adapter.InitializeAsync();

        Assert.True(result.IsValid);
        Assert.Contains(reporter.StatusHistory, s => s.Status == IndexerStatus.Authenticating);
        Assert.Equal(IndexerStatus.Idle, reporter.CurrentStatus);
    }

    [Fact]
    public async Task InitializeAsync_WithInvalidSession_ReturnsFailure()
    {
        var (adapter, reporter, apiClient) = CreateAdapter();
        apiClient.SessionValid = false;

        var result = await adapter.InitializeAsync();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("valid session", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(IndexerStatus.Error, reporter.CurrentStatus);
    }

    [Fact]
    public async Task InitializeAsync_WhenExceptionThrown_ReportsErrorAndRethrows()
    {
        var (_, reporter, apiClient) = CreateAdapter();
        // Make HasValidSession throw to trigger the catch block.
        // Since HasValidSession is not async, we use a different approach:
        // set the reporter to throw during the first status report.
        // Instead, let's use the actual adapter with an api client that throws from
        // a different call. The simplest way is to use a real adapter but override
        // the status reporter's ReportStatusAsync to throw on first call.

        // Actually, the InitializeAsync only calls HasValidSession() which is sync.
        // The exception path is hit when any code in the try block throws.
        // Let's make the reporter throw during the Authenticating status report to trigger catch.
        var throwingReporter = new ThrowingStatusReporter(throwOnStatus: IndexerStatus.Authenticating);
        var settings = new QobuzarrStreamingSettings
        {
            Email = "test@test.com",
            Password = "pass",
            DownloadPath = "/music",
            PreferredQuality = 6,
            CountryCode = "US",
            SearchLimit = 100
        };
        var logger = NullLoggerFactory.Instance.CreateLogger<QobuzIndexerAdapter>();
        var adapter = new QobuzIndexerAdapter(apiClient, throwingReporter, logger, settings);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.InitializeAsync().AsTask());
        Assert.Equal("Simulated failure", ex.Message);
        Assert.Single(throwingReporter.ReportedErrors);
        Assert.Same(ex, throwingReporter.ReportedErrors[0]);
    }

    // ================================================================
    // SearchAlbumsAsync
    // ================================================================

    [Fact]
    public async Task SearchAlbumsAsync_HappyPath_ReturnsMappedResults()
    {
        var (adapter, reporter, apiClient) = CreateAdapter(searchLimit: 50);

        var testAlbum = QobuzAlbumBuilder.New()
            .WithId("abc-123")
            .WithTitle("Kind of Blue")
            .WithArtist("Miles Davis", "artist-md")
            .WithGenre("Jazz")
            .Build();
        testAlbum.UPC = "0884977399721";

        apiClient.GetAsyncHandler = (endpoint, parameters) =>
        {
            Assert.Equal("/album/search", endpoint);
            Assert.Equal("Kind of Blue", parameters!["query"]);
            Assert.Equal("50", parameters["limit"]);

            return new QobuzAlbumSearchResponse
            {
                Albums = new QobuzSearchResultContainer<QobuzAlbum>
                {
                    Items = new List<QobuzAlbum> { testAlbum },
                    Total = 1
                }
            };
        };

        var results = await adapter.SearchAlbumsAsync("Kind of Blue");

        Assert.Single(results);
        var album = results[0];
        Assert.Equal("abc-123", album.Id);
        Assert.Contains("Kind of Blue", album.Title);
        Assert.Equal("Miles Davis", album.Artist.Name);
        Assert.Equal("0884977399721", album.Upc);
        Assert.Contains("Jazz", album.Genres);
        Assert.Equal("abc-123", album.ExternalIds["qobuz"]);

        // Status transitions: Searching -> Idle
        Assert.Contains(reporter.StatusHistory, s => s.Status == IndexerStatus.Searching);
        Assert.Equal(IndexerStatus.Idle, reporter.CurrentStatus);
    }

    [Fact]
    public async Task SearchAlbumsAsync_EmptyQuery_ReturnsEmptyList()
    {
        var (adapter, _, apiClient) = CreateAdapter();
        bool apiCalled = false;
        apiClient.GetAsyncHandler = (_, _) =>
        {
            apiCalled = true;
            return null;
        };

        var results = await adapter.SearchAlbumsAsync("");

        Assert.Empty(results);
        Assert.False(apiCalled, "API should not be called for empty query");
    }

    [Fact]
    public async Task SearchAlbumsAsync_WhitespaceQuery_ReturnsEmptyList()
    {
        var (adapter, _, _) = CreateAdapter();

        var results = await adapter.SearchAlbumsAsync("   ");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAlbumsAsync_NullResponse_ReturnsEmptyList()
    {
        var (adapter, _, apiClient) = CreateAdapter();
        apiClient.GetAsyncHandler = (_, _) => null;

        var results = await adapter.SearchAlbumsAsync("test");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAlbumsAsync_ErrorPath_ReportsErrorAndRethrows()
    {
        var (adapter, reporter, apiClient) = CreateAdapter();
        var expectedException = new InvalidOperationException("API failure");
        apiClient.GetAsyncHandler = (_, _) => throw expectedException;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.SearchAlbumsAsync("test").AsTask());

        Assert.Same(expectedException, thrown);
        Assert.Single(reporter.ReportedErrors);
        Assert.Same(expectedException, reporter.ReportedErrors[0]);
    }

    // ================================================================
    // GetAlbumAsync
    // ================================================================

    [Fact]
    public async Task GetAlbumAsync_HappyPath_ReturnsMappedAlbum()
    {
        var (adapter, reporter, apiClient) = CreateAdapter();

        var testAlbum = QobuzAlbumBuilder.New()
            .WithId("xyz-789")
            .WithTitle("A Love Supreme")
            .WithArtist("John Coltrane", "artist-jc")
            .Build();
        testAlbum.UPC = "0602537863136";

        apiClient.GetAsyncHandler = (endpoint, parameters) =>
        {
            Assert.Equal("/album/get", endpoint);
            Assert.Equal("xyz-789", parameters!["album_id"]);
            return testAlbum;
        };

        var result = await adapter.GetAlbumAsync("xyz-789");

        Assert.NotNull(result);
        Assert.Equal("xyz-789", result!.Id);
        Assert.Contains("A Love Supreme", result.Title);
        Assert.Equal("John Coltrane", result.Artist.Name);
        Assert.Equal("0602537863136", result.Upc);
    }

    [Fact]
    public async Task GetAlbumAsync_NullAlbumId_ReturnsNull()
    {
        var (adapter, _, _) = CreateAdapter();

        var result = await adapter.GetAlbumAsync(null!);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAlbumAsync_EmptyAlbumId_ReturnsNull()
    {
        var (adapter, _, _) = CreateAdapter();

        var result = await adapter.GetAlbumAsync("");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAlbumAsync_ApiReturnsNull_ReturnsNull()
    {
        var (adapter, _, apiClient) = CreateAdapter();
        apiClient.GetAsyncHandler = (_, _) => null;

        var result = await adapter.GetAlbumAsync("some-id");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAlbumAsync_ErrorPath_ReportsErrorAndRethrows()
    {
        var (adapter, reporter, apiClient) = CreateAdapter();
        var expectedException = new InvalidOperationException("Network error");
        apiClient.GetAsyncHandler = (_, _) => throw expectedException;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.GetAlbumAsync("some-id").AsTask());

        Assert.Same(expectedException, thrown);
        Assert.Single(reporter.ReportedErrors);
        Assert.Same(expectedException, reporter.ReportedErrors[0]);
    }

    // ================================================================
    // MapToStreamingAlbum edge cases
    // ================================================================

    [Fact]
    public async Task MapToStreamingAlbum_NullArtist_MapsAsUnknownArtist()
    {
        var (adapter, _, apiClient) = CreateAdapter();

        var album = new QobuzAlbum
        {
            Id = "id-1",
            Title = "No Artist Album",
            Artist = null,
            UPC = "000000000000"
        };

        apiClient.GetAsyncHandler = (_, _) => album;

        var result = await adapter.GetAlbumAsync("id-1");

        Assert.NotNull(result);
        Assert.Equal("Unknown Artist", result!.Artist.Name);
    }

    [Fact]
    public async Task MapToStreamingAlbum_NullImage_DoesNotCrash()
    {
        var (adapter, _, apiClient) = CreateAdapter();

        var album = new QobuzAlbum
        {
            Id = "id-2",
            Title = "No Image Album",
            Artist = new QobuzArtist { Name = "Test", Id = "a1" },
            Image = null,
            UPC = "000000000001"
        };

        apiClient.GetAsyncHandler = (_, _) => album;

        var result = await adapter.GetAlbumAsync("id-2");

        Assert.NotNull(result);
        Assert.Empty(result!.CoverArtUrls);
    }

    [Fact]
    public async Task MapToStreamingAlbum_NullGenre_EmptyGenresList_HandledGracefully()
    {
        var (adapter, _, apiClient) = CreateAdapter();

        // Genre = null with GenresList defaulting to empty list (its field initializer).
        // GetGenre() returns null which the adapter filters out via string.IsNullOrEmpty.
        var album = new QobuzAlbum
        {
            Id = "id-3",
            Title = "No Genre Album",
            Artist = new QobuzArtist { Name = "Test", Id = "a1" },
            Genre = null,
            UPC = "000000000002"
        };

        apiClient.GetAsyncHandler = (_, _) => album;

        var result = await adapter.GetAlbumAsync("id-3");

        Assert.NotNull(result);
        // GetGenre() returns null, which is filtered out by string.IsNullOrEmpty
        Assert.Empty(result!.Genres);
    }

    [Fact]
    public async Task MapToStreamingAlbum_NullGenresList_HandledGracefully()
    {
        // Verifies the fix for the null-safety gap: QobuzAlbum.GetGenre()
        // now uses GenresList?.FirstOrDefault() so a null GenresList
        // (from API deserializing a missing/null genres_list field) returns
        // null instead of throwing.
        var (adapter, _, apiClient) = CreateAdapter();

        var album = new QobuzAlbum
        {
            Id = "id-3b",
            Title = "Null GenresList Album",
            Artist = new QobuzArtist { Name = "Test", Id = "a1" },
            Genre = null,
            GenresList = null!,
            UPC = "000000000002"
        };

        apiClient.GetAsyncHandler = (_, _) => album;

        var result = await adapter.GetAlbumAsync("id-3b");

        Assert.NotNull(result);
        // GetGenre() returns null, which is filtered out by string.IsNullOrEmpty
        Assert.Empty(result!.Genres);
    }

    [Fact]
    public async Task MapToStreamingAlbum_WithCoverArt_MapsAllSizes()
    {
        var (adapter, _, apiClient) = CreateAdapter();

        var album = new QobuzAlbum
        {
            Id = "id-4",
            Title = "Art Album",
            Artist = new QobuzArtist { Name = "Artist", Id = "a1" },
            Image = new QobuzImage
            {
                Small = "https://img/small.jpg",
                Medium = "https://img/medium.jpg",
                Large = "https://img/large.jpg",
                ExtraLarge = "https://img/xl.jpg"
            },
            UPC = "000000000003"
        };

        apiClient.GetAsyncHandler = (_, _) => album;

        var result = await adapter.GetAlbumAsync("id-4");

        Assert.NotNull(result);
        Assert.Equal("https://img/small.jpg", result!.CoverArtUrls["small"]);
        Assert.Equal("https://img/medium.jpg", result.CoverArtUrls["medium"]);
        Assert.Equal("https://img/large.jpg", result.CoverArtUrls["large"]);
        Assert.Equal("https://img/xl.jpg", result.CoverArtUrls["extralarge"]);
    }

    [Fact]
    public async Task MapToStreamingAlbum_WithQuality_MapsHiRes()
    {
        var (adapter, _, apiClient) = CreateAdapter();

        var album = QobuzAlbumBuilder.New()
            .WithId("id-5")
            .WithTitle("HiRes Album")
            .WithQuality(24, 192000)
            .Build();

        apiClient.GetAsyncHandler = (_, _) => album;

        var result = await adapter.GetAlbumAsync("id-5");

        Assert.NotNull(result);
        Assert.Single(result!.AvailableQualities);
        var q = result.AvailableQualities[0];
        Assert.Equal("FLAC", q.Format);
        Assert.Equal(24, q.BitDepth);
        Assert.Equal(192000, q.SampleRate);
        Assert.True(q.IsHighResolution);
        Assert.Equal("Hi-Res", q.Name);
    }

    [Fact]
    public async Task MapToStreamingAlbum_WithCdQuality_MapsLossless()
    {
        var (adapter, _, apiClient) = CreateAdapter();

        var album = QobuzAlbumBuilder.New()
            .WithId("id-6")
            .WithTitle("CD Album")
            .WithQuality(16, 44100)
            .Build();

        apiClient.GetAsyncHandler = (_, _) => album;

        var result = await adapter.GetAlbumAsync("id-6");

        Assert.NotNull(result);
        Assert.Single(result!.AvailableQualities);
        var q = result.AvailableQualities[0];
        Assert.Equal("FLAC", q.Format);
        Assert.Equal(16, q.BitDepth);
        Assert.Equal(44100, q.SampleRate);
        Assert.False(q.IsHighResolution);
        Assert.Equal("Lossless", q.Name);
    }

    // ================================================================
    // Constructor null-guard tests
    // ================================================================

    [Fact]
    public void Constructor_NullApiClient_Throws()
    {
        Assert.Throws<ArgumentNullException>("apiClient", () =>
            new QobuzIndexerAdapter(
                null!,
                new FakeStatusReporter(),
                NullLoggerFactory.Instance.CreateLogger<QobuzIndexerAdapter>(),
                new QobuzarrStreamingSettings()));
    }

    [Fact]
    public void Constructor_NullStatusReporter_Throws()
    {
        Assert.Throws<ArgumentNullException>("statusReporter", () =>
            new QobuzIndexerAdapter(
                new FakeApiClient(),
                null!,
                NullLoggerFactory.Instance.CreateLogger<QobuzIndexerAdapter>(),
                new QobuzarrStreamingSettings()));
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>("logger", () =>
            new QobuzIndexerAdapter(
                new FakeApiClient(),
                new FakeStatusReporter(),
                null!,
                new QobuzarrStreamingSettings()));
    }

    [Fact]
    public void Constructor_NullSettings_Throws()
    {
        Assert.Throws<ArgumentNullException>("settings", () =>
            new QobuzIndexerAdapter(
                new FakeApiClient(),
                new FakeStatusReporter(),
                NullLoggerFactory.Instance.CreateLogger<QobuzIndexerAdapter>(),
                null!));
    }

    // ================================================================
    // SearchTracksAsync (stub)
    // ================================================================

    [Fact]
    public async Task SearchTracksAsync_ReturnsEmptyList()
    {
        var (adapter, _, _) = CreateAdapter();

        var results = await adapter.SearchTracksAsync("anything");

        Assert.Empty(results);
    }

    // ================================================================
    // SearchAlbumsStreamAsync / SearchTracksStreamAsync
    // ================================================================

    [Fact]
    public async Task SearchAlbumsStreamAsync_ReturnsResults()
    {
        var (adapter, _, apiClient) = CreateAdapter();

        var testAlbum = QobuzAlbumBuilder.New()
            .WithId("stream-1")
            .WithTitle("Streaming Album")
            .WithArtist("Stream Artist", "artist-sa")
            .Build();

        apiClient.GetAsyncHandler = (endpoint, _) =>
        {
            if (endpoint == "/album/search")
            {
                return new QobuzAlbumSearchResponse
                {
                    Albums = new QobuzSearchResultContainer<QobuzAlbum>
                    {
                        Items = new List<QobuzAlbum> { testAlbum },
                        Total = 1
                    }
                };
            }
            return null;
        };

        var items = new List<Lidarr.Plugin.Abstractions.Models.StreamingAlbum>();
        await foreach (var album in adapter.SearchAlbumsStreamAsync("Streaming Album"))
        {
            items.Add(album);
        }

        Assert.Single(items);
        Assert.Equal("stream-1", items[0].Id);
        Assert.Contains("Streaming Album", items[0].Title);
    }

    [Fact]
    public async Task SearchTracksStreamAsync_ReturnsEmpty()
    {
        var (adapter, _, _) = CreateAdapter();

        var items = new List<Lidarr.Plugin.Abstractions.Models.StreamingTrack>();
        await foreach (var track in adapter.SearchTracksStreamAsync("anything"))
        {
            items.Add(track);
        }

        Assert.Empty(items);
    }

    // ================================================================
    // DisposeAsync
    // ================================================================

    [Fact]
    public async Task DisposeAsync_CompletesWithoutError()
    {
        var (adapter, _, _) = CreateAdapter();

        // Should not throw
        await adapter.DisposeAsync();
    }

    // ----------------------------------------------------------------
    // ThrowingStatusReporter helper for InitializeAsync error path
    // ----------------------------------------------------------------

    /// <summary>
    /// Status reporter that throws on a specific status to trigger catch blocks.
    /// </summary>
    private sealed class ThrowingStatusReporter : IIndexerStatusReporter
    {
        private readonly IndexerStatus _throwOnStatus;
        public IndexerStatus CurrentStatus { get; private set; } = IndexerStatus.Idle;
        public List<Exception> ReportedErrors { get; } = new();

        public ThrowingStatusReporter(IndexerStatus throwOnStatus)
        {
            _throwOnStatus = throwOnStatus;
        }

        public ValueTask ReportStatusAsync(IndexerStatus status, string? message = null, CancellationToken cancellationToken = default)
        {
            if (status == _throwOnStatus)
                throw new InvalidOperationException("Simulated failure");

            CurrentStatus = status;
            return ValueTask.CompletedTask;
        }

        public ValueTask ReportErrorAsync(Exception error, CancellationToken cancellationToken = default)
        {
            CurrentStatus = IndexerStatus.Error;
            ReportedErrors.Add(error);
            return ValueTask.CompletedTask;
        }
    }
}
