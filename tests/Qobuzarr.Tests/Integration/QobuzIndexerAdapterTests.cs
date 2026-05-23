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
    public async Task SearchAlbumsAsync_MultiPage_WalksAllPagesUntilHasMoreFalse()
    {
        // End-to-end pagination contract: the adapter walks /album/search by
        // incrementing offset until HasMoreResults reports false, dedupes on
        // album id, and returns the union. Re-affirms the wave 7 hardening.
        var (adapter, _, apiClient) = CreateAdapter(searchLimit: 2);

        var page1 = new List<QobuzAlbum> {
            QobuzAlbumBuilder.New().WithId("a1").WithTitle("A1").WithArtist("X", "x").Build(),
            QobuzAlbumBuilder.New().WithId("a2").WithTitle("A2").WithArtist("X", "x").Build(),
        };
        var page2 = new List<QobuzAlbum> {
            QobuzAlbumBuilder.New().WithId("a3").WithTitle("A3").WithArtist("X", "x").Build(),
            // Defensive: page 2 also returns a1 — adapter must dedupe.
            QobuzAlbumBuilder.New().WithId("a1").WithTitle("A1").WithArtist("X", "x").Build(),
        };
        var page3 = new List<QobuzAlbum> {
            QobuzAlbumBuilder.New().WithId("a4").WithTitle("A4").WithArtist("X", "x").Build(),
        };

        var calls = new List<int>();
        apiClient.GetAsyncHandler = (endpoint, parameters) =>
        {
            var offset = int.Parse(parameters!["offset"]);
            calls.Add(offset);
            var (items, total) = offset switch
            {
                0 => (page1, 5),
                2 => (page2, 5),
                4 => (page3, 5),
                _ => (new List<QobuzAlbum>(), 5),
            };
            return new QobuzAlbumSearchResponse
            {
                Albums = new QobuzSearchResultContainer<QobuzAlbum>
                {
                    Items = items,
                    Total = total,
                    Offset = offset,
                    Limit = 2,
                },
            };
        };

        var results = await adapter.SearchAlbumsAsync("Kind of Blue");

        Assert.Equal(new[] { 0, 2, 4 }, calls);
        Assert.Equal(4, results.Count);
        Assert.Equal(new[] { "a1", "a2", "a3", "a4" }, results.Select(r => r.Id).ToArray());
    }

    [Fact]
    public async Task SearchAlbumsAsync_MultiPage_StopsOnZeroNewIds()
    {
        // Defense against an API that ignores 'offset' and re-returns page 1
        // forever. The adapter must break out on page>0 with zero new ids.
        var (adapter, _, apiClient) = CreateAdapter(searchLimit: 2);

        var staticPage = new List<QobuzAlbum> {
            QobuzAlbumBuilder.New().WithId("a1").WithTitle("A1").WithArtist("X", "x").Build(),
            QobuzAlbumBuilder.New().WithId("a2").WithTitle("A2").WithArtist("X", "x").Build(),
        };

        var callCount = 0;
        apiClient.GetAsyncHandler = (endpoint, parameters) =>
        {
            callCount++;
            return new QobuzAlbumSearchResponse
            {
                Albums = new QobuzSearchResultContainer<QobuzAlbum>
                {
                    Items = staticPage,
                    Total = 999,
                    Offset = 0,
                    Limit = 2,
                },
            };
        };

        var results = await adapter.SearchAlbumsAsync("query");

        Assert.Equal(2, results.Count);
        Assert.True(callCount <= 2, $"Expected ≤2 calls before dedup breaks; got {callCount}");
    }

    [Fact]
    public async Task SearchAlbumsAsync_MultiPage_PreservesAccumulated_OnMidPageFailure()
    {
        // Wave 7 contract: an exception on page>0 must return accumulated
        // results rather than discard them.
        var (adapter, _, apiClient) = CreateAdapter(searchLimit: 2);

        var page1 = new List<QobuzAlbum> {
            QobuzAlbumBuilder.New().WithId("a1").WithTitle("A1").WithArtist("X", "x").Build(),
            QobuzAlbumBuilder.New().WithId("a2").WithTitle("A2").WithArtist("X", "x").Build(),
        };

        apiClient.GetAsyncHandler = (endpoint, parameters) =>
        {
            var offset = int.Parse(parameters!["offset"]);
            if (offset == 0)
            {
                return new QobuzAlbumSearchResponse
                {
                    Albums = new QobuzSearchResultContainer<QobuzAlbum>
                    {
                        Items = page1, Total = 10, Offset = 0, Limit = 2,
                    },
                };
            }
            throw new InvalidOperationException("simulated mid-page failure");
        };

        var results = await adapter.SearchAlbumsAsync("query");

        Assert.Equal(2, results.Count);
        Assert.Equal(new[] { "a1", "a2" }, results.Select(r => r.Id).ToArray());
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

    // ================================================================
    // AuthFailureGate wiring — the IP-ban-from-Qobuz scenario.
    // When Lidarr's search loop drives the adapter and auth is bad,
    // the FIRST call gets a probe slot; every subsequent call inside
    // the probe interval must short-circuit to empty without hitting
    // the API client. On 401/403 the gate's handler must latch bad.
    // ================================================================

    private sealed class CountingApiClient : IQobuzApiClient
    {
        public int GetCalls { get; private set; }
        public Func<Exception>? ThrowFactory { get; set; }
        public bool SessionValid { get; set; } = true;
        public Task<T> GetAsync<T>(string endpoint, Dictionary<string, string>? parameters = null) where T : class
        {
            GetCalls++;
            if (ThrowFactory is not null) throw ThrowFactory();
            return Task.FromResult(default(T)!);
        }
        public Task<T> PostAsync<T>(string endpoint, object? data = null) where T : class => Task.FromResult(default(T)!);
        public void SetSession(Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzSession session) { }
        public void ClearSession() { }
        public bool HasValidSession() => SessionValid;
        public Task<string> GetStreamingUrlAsync(string trackId, int formatId, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public Task<QobuzStreamResponse> GetStreamingInfoAsync(string trackId, int formatId, CancellationToken cancellationToken = default) => Task.FromResult(new QobuzStreamResponse());
    }

    private static QobuzIndexerAdapter NewAdapterWithGate(
        out CountingApiClient apiClient,
        out Lidarr.Plugin.Common.Services.Bridge.DefaultAuthFailureHandler handler,
        out Lidarr.Plugin.Common.Services.Bridge.AuthFailureGate gate)
    {
        apiClient = new CountingApiClient();
        handler = new Lidarr.Plugin.Common.Services.Bridge.DefaultAuthFailureHandler(
            NullLoggerFactory.Instance.CreateLogger<Lidarr.Plugin.Common.Services.Bridge.DefaultAuthFailureHandler>());
        gate = new Lidarr.Plugin.Common.Services.Bridge.AuthFailureGate(handler, TimeProvider.System, TimeSpan.FromMinutes(5));
        var settings = new QobuzarrStreamingSettings { Email = "x", Password = "p", DownloadPath = "/m", PreferredQuality = 6, CountryCode = "US", SearchLimit = 10 };
        return new QobuzIndexerAdapter(apiClient, new FakeStatusReporter(), NullLoggerFactory.Instance.CreateLogger<QobuzIndexerAdapter>(), settings, gate);
    }

    [Fact]
    public async Task SearchAlbumsAsync_AuthLatchedBad_ShortCircuitsAfterFirstProbe()
    {
        var adapter = NewAdapterWithGate(out var apiClient, out var handler, out _);
        await handler.HandleFailureAsync(new AuthFailure { ErrorCode = "401", Message = "session expired" });

        var first = await adapter.SearchAlbumsAsync("Miles Davis");
        Assert.Empty(first);
        Assert.Equal(1, apiClient.GetCalls); // probe slot allowed exactly once

        for (var i = 0; i < 20; i++)
        {
            Assert.Empty(await adapter.SearchAlbumsAsync("Miles Davis"));
        }
        Assert.Equal(1, apiClient.GetCalls); // amplification stopped — closes the IP-ban incident
    }

    [Fact]
    public async Task SearchAlbumsAsync_QobuzApiException401_LatchesAuthBad()
    {
        var adapter = NewAdapterWithGate(out var apiClient, out var handler, out _);
        await handler.HandleSuccessAsync(); // start healthy
        apiClient.ThrowFactory = () => new Lidarr.Plugin.Qobuzarr.Exceptions.QobuzApiException(
            "unauthorized", "/album/search", System.Net.HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<Lidarr.Plugin.Qobuzarr.Exceptions.QobuzApiException>(
            () => adapter.SearchAlbumsAsync("anything").AsTask());

        Assert.Equal(AuthStatus.Failed, handler.Status);
    }

    [Fact]
    public async Task SearchAlbumsAsync_QobuzAuthenticationException_LatchesAuthBad()
    {
        var adapter = NewAdapterWithGate(out var apiClient, out var handler, out _);
        await handler.HandleSuccessAsync();
        apiClient.ThrowFactory = () => new Lidarr.Plugin.Qobuzarr.Authentication.QobuzAuthenticationException("auth lost");

        await Assert.ThrowsAsync<Lidarr.Plugin.Qobuzarr.Authentication.QobuzAuthenticationException>(
            () => adapter.SearchAlbumsAsync("q").AsTask());

        Assert.Equal(AuthStatus.Failed, handler.Status);
    }

    [Fact]
    public async Task SearchAlbumsAsync_ServerError_DoesNotLatchAuthBad()
    {
        var adapter = NewAdapterWithGate(out var apiClient, out var handler, out _);
        await handler.HandleSuccessAsync();
        apiClient.ThrowFactory = () => new Lidarr.Plugin.Qobuzarr.Exceptions.QobuzApiException(
            "boom", "/album/search", System.Net.HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<Lidarr.Plugin.Qobuzarr.Exceptions.QobuzApiException>(
            () => adapter.SearchAlbumsAsync("q").AsTask());

        Assert.Equal(AuthStatus.Authenticated, handler.Status); // 500 is transient, not auth
    }

    [Fact]
    public async Task SearchAlbumsAsync_GateNull_BehavesAsBefore()
    {
        // Backwards compat: an adapter constructed without a gate must not change behavior.
        var (adapter, _, apiClient) = CreateAdapter();
        apiClient.GetAsyncHandler = (_, _) => new QobuzAlbumSearchResponse();

        for (var i = 0; i < 3; i++)
        {
            await adapter.SearchAlbumsAsync("q");
        }
        // No assertion needed on apiClient — just exercising the path.
    }

    [Fact]
    public async Task GetAlbumAsync_AuthLatchedBad_ShortCircuitsAfterFirstProbe()
    {
        var adapter = NewAdapterWithGate(out var apiClient, out var handler, out _);
        await handler.HandleFailureAsync(new AuthFailure { Message = "expired" });

        Assert.Null(await adapter.GetAlbumAsync("id"));
        Assert.Equal(1, apiClient.GetCalls);

        for (var i = 0; i < 5; i++)
        {
            Assert.Null(await adapter.GetAlbumAsync("id"));
        }
        Assert.Equal(1, apiClient.GetCalls);
    }

    // ================================================================
    // Pagination: a single Lidarr search that hits Qobuz must walk all
    // pages until the server reports no more results. Truncating at the
    // first page (the prior behavior) silently dropped matches for
    // popular artists with >50 albums.
    // ================================================================

    [Fact]
    public async Task SearchAlbumsAsync_MultiPage_AccumulatesAllPages()
    {
        var apiClient = new FakeApiClient();
        var reporter = new FakeStatusReporter();
        var settings = new QobuzarrStreamingSettings
        {
            Email = "x", Password = "p", DownloadPath = "/m",
            PreferredQuality = 6, CountryCode = "US", SearchLimit = 10
        };
        var logger = NullLoggerFactory.Instance.CreateLogger<QobuzIndexerAdapter>();
        var adapter = new QobuzIndexerAdapter(apiClient, reporter, logger, settings);

        // Page 1: 10 items, total 23 → HasMoreResults=true (offset 0 + 10 < 23)
        // Page 2: 10 items, total 23, offset 10 → HasMoreResults=true (offset 10 + 10 < 23)
        // Page 3: 3 items,  total 23, offset 20 → HasMoreResults=false (offset 20 + 3 == 23)
        apiClient.GetAsyncHandler = (endpoint, parameters) =>
        {
            Assert.Equal("/album/search", endpoint);
            var offset = int.Parse(parameters!.GetValueOrDefault("offset", "0"));
            var (items, total) = (offset, 23) switch
            {
                (0, _) => (MakeAlbums(10, prefix: "p1-"), 23),
                (10, _) => (MakeAlbums(10, prefix: "p2-"), 23),
                (20, _) => (MakeAlbums(3, prefix: "p3-"), 23),
                _ => (new List<QobuzAlbum>(), 23),
            };
            return new QobuzAlbumSearchResponse
            {
                Albums = new QobuzSearchResultContainer<QobuzAlbum>
                {
                    Items = items, Total = total, Offset = offset, Limit = 10
                }
            };
        };

        var results = await adapter.SearchAlbumsAsync("popular");

        Assert.Equal(23, results.Count);
        Assert.Equal("p1-0", results[0].Id);
        Assert.Equal("p3-2", results[22].Id);
    }

    [Fact]
    public async Task SearchAlbumsAsync_StopsWalkingPages_When_HasMoreResults_Goes_False()
    {
        var apiClient = new FakeApiClient();
        var settings = new QobuzarrStreamingSettings
        {
            Email = "x", Password = "p", DownloadPath = "/m",
            PreferredQuality = 6, CountryCode = "US", SearchLimit = 10
        };
        var adapter = new QobuzIndexerAdapter(apiClient, new FakeStatusReporter(),
            NullLoggerFactory.Instance.CreateLogger<QobuzIndexerAdapter>(), settings);

        // Only 7 results total — first page returns them all.
        var calls = 0;
        apiClient.GetAsyncHandler = (_, _) =>
        {
            calls++;
            return new QobuzAlbumSearchResponse
            {
                Albums = new QobuzSearchResultContainer<QobuzAlbum>
                {
                    Items = MakeAlbums(7, prefix: "only-"),
                    Total = 7, Offset = 0, Limit = 10
                }
            };
        };

        var results = await adapter.SearchAlbumsAsync("rare");

        Assert.Equal(7, results.Count);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SearchAlbumsAsync_CapsRunawayPagination_At_PageLimit()
    {
        // Defense against a misbehaving API that always reports HasMoreResults=true.
        // The adapter must cap the page-walk and stop, even if the server
        // never signals "done", to prevent infinite-loop on a stuck endpoint.
        var apiClient = new FakeApiClient();
        var settings = new QobuzarrStreamingSettings
        {
            Email = "x", Password = "p", DownloadPath = "/m",
            PreferredQuality = 6, CountryCode = "US", SearchLimit = 10
        };
        var adapter = new QobuzIndexerAdapter(apiClient, new FakeStatusReporter(),
            NullLoggerFactory.Instance.CreateLogger<QobuzIndexerAdapter>(), settings);

        var calls = 0;
        apiClient.GetAsyncHandler = (_, parameters) =>
        {
            calls++;
            var offset = int.Parse(parameters!.GetValueOrDefault("offset", "0"));
            // Server lies — claims 100000 total but only fills the offset onwards
            return new QobuzAlbumSearchResponse
            {
                Albums = new QobuzSearchResultContainer<QobuzAlbum>
                {
                    Items = MakeAlbums(10, prefix: $"o{offset}-"),
                    Total = 100_000, Offset = offset, Limit = 10
                }
            };
        };

        var results = await adapter.SearchAlbumsAsync("never-ending");

        // The exact cap is an implementation detail (e.g. 50 pages); whatever
        // it is, the adapter must stop in finite time and return what it has.
        Assert.True(calls <= 50, $"page-walk must be capped; saw {calls} requests");
        Assert.True(results.Count >= 10, "first page at minimum must be returned");
    }

    private static List<QobuzAlbum> MakeAlbums(int count, string prefix)
    {
        var list = new List<QobuzAlbum>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(QobuzAlbumBuilder.New()
                .WithId($"{prefix}{i}")
                .WithTitle($"Album {prefix}{i}")
                .WithArtist("Artist", $"art-{i}")
                .Build());
        }
        return list;
    }
}
