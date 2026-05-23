using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Abstractions.Contracts;
using Lidarr.Plugin.Abstractions.Models;
using Lidarr.Plugin.Common.Services.Bridge;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Models;
using Microsoft.Extensions.Logging;

namespace Lidarr.Plugin.Qobuzarr.Integration;

/// <summary>
/// Adapter that bridges the existing <see cref="IQobuzApiClient"/> to the Common library's
/// <see cref="IIndexer"/> contract. All errors are reported through
/// <see cref="IIndexerStatusReporter"/> and then rethrown -- no silent swallowing.
/// </summary>
public sealed class QobuzIndexerAdapter : IIndexer
{
    private readonly IQobuzApiClient _apiClient;
    private readonly IIndexerStatusReporter _statusReporter;
    private readonly ILogger<QobuzIndexerAdapter> _logger;
    private readonly QobuzarrStreamingSettings _settings;
    private readonly AuthFailureGate? _authGate;

    public QobuzIndexerAdapter(
        IQobuzApiClient apiClient,
        IIndexerStatusReporter statusReporter,
        ILogger<QobuzIndexerAdapter> logger,
        QobuzarrStreamingSettings settings,
        AuthFailureGate? authGate = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _statusReporter = statusReporter ?? throw new ArgumentNullException(nameof(statusReporter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _authGate = authGate;
    }

    /// <summary>
    /// Returns true when auth is latched bad AND no probe slot is available — the caller
    /// should return an empty result without touching the network. This is the
    /// fix for the real-world Qobuz IP-ban incident: Lidarr's search loop drove the
    /// adapter at full rate while auth was expired; without this gate every search
    /// fired through to the API and Qobuz banned the source IP.
    /// </summary>
    private bool IsAuthShortCircuited()
    {
        if (_authGate is null) return false;
        if (_authGate.IsHealthy) return false;
        return !_authGate.TryAcquireProbeSlot();
    }

    private async Task RecordAuthOutcomeFromExceptionAsync(Exception ex)
    {
        if (_authGate is null) return;
        if (LooksLikeAuthFailure(ex))
        {
            // Was: sync-over-async via `.AsTask().GetAwaiter().GetResult()`.
            // That pattern deadlocks when invoked from a context that
            // captures the synchronization context (e.g. the older Lidarr
            // search loop). Use straight await and ConfigureAwait(false) so
            // the gate's handler can be implemented either sync or async by
            // future plugin authors without the call site reintroducing the
            // deadlock. Caller is now responsible for awaiting this method.
            await _authGate.Handler.HandleFailureAsync(new AuthFailure
            {
                ErrorCode = (ex as Lidarr.Plugin.Qobuzarr.Exceptions.QobuzApiException)?.StatusCode?.ToString(),
                Message = ex.Message,
            }).ConfigureAwait(false);
        }
    }

    private static bool LooksLikeAuthFailure(Exception ex)
    {
        if (ex is Lidarr.Plugin.Qobuzarr.Exceptions.QobuzApiException qae &&
            qae.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            return true;
        }
        if (ex is System.Net.Http.HttpRequestException hre &&
            hre.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            return true;
        }
        if (ex is Lidarr.Plugin.Qobuzarr.Authentication.QobuzAuthenticationException) return true;
        return false;
    }

    /// <inheritdoc />
    public async ValueTask<PluginValidationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _statusReporter.ReportStatusAsync(IndexerStatus.Authenticating, "Checking Qobuz session", cancellationToken).ConfigureAwait(false);

            if (!_apiClient.HasValidSession())
            {
                await _statusReporter.ReportStatusAsync(IndexerStatus.Error, "Qobuz session is not authenticated", cancellationToken).ConfigureAwait(false);
                return PluginValidationResult.Failure(new[] { "Qobuz API client does not have a valid session. Ensure credentials are configured and authentication succeeded." });
            }

            await _statusReporter.ReportStatusAsync(IndexerStatus.Idle, "Qobuz indexer ready", cancellationToken).ConfigureAwait(false);
            return PluginValidationResult.Success();
        }
        catch (Exception ex)
        {
            await _statusReporter.ReportErrorAsync(ex, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<StreamingAlbum>> SearchAlbumsAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<StreamingAlbum>();
        }
        if (IsAuthShortCircuited())
        {
            _logger.LogDebug("Short-circuiting Qobuz album search; auth latched bad and probe slot exhausted");
            return Array.Empty<StreamingAlbum>();
        }

        try
        {
            await _statusReporter.ReportStatusAsync(IndexerStatus.Searching, $"Searching albums: {query}", cancellationToken).ConfigureAwait(false);

            // Walk pages until: (a) the server reports no more results,
            // (b) the safety cap is reached, or (c) a page returns no new
            // album ids — the dedup net protects against an API that
            // silently ignores `offset` and re-returns the same first page.
            // A mid-pagination failure preserves the accumulated results
            // instead of throwing them away.
            const int maxPages = 20;
            var pageLimit = _settings.SearchLimit;
            var result = new List<StreamingAlbum>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var offset = 0;
            for (var page = 0; page < maxPages; page++)
            {
                List<QobuzAlbum> albums;
                bool hasMore;
                int nextOffset;
                try
                {
                    var parameters = new Dictionary<string, string>
                    {
                        ["query"] = query,
                        ["limit"] = pageLimit.ToString(),
                        ["offset"] = offset.ToString(),
                    };

                    var response = await _apiClient.GetAsync<QobuzAlbumSearchResponse>("/album/search", parameters).ConfigureAwait(false);
                    albums = response?.GetAlbums() ?? new List<QobuzAlbum>();
                    hasMore = response?.Albums?.HasMoreResults == true;
                    nextOffset = response?.Albums?.GetNextOffset() ?? (offset + albums.Count);
                }
                catch (Exception ex) when (page > 0)
                {
                    await RecordAuthOutcomeFromExceptionAsync(ex).ConfigureAwait(false);
                    _logger.LogWarning(ex, "Qobuz album search page {Page} failed; returning {Count} accumulated results", page, result.Count);
                    break;
                }

                var newCount = 0;
                foreach (var album in albums)
                {
                    if (album?.Id != null && seenIds.Add(album.Id))
                    {
                        result.Add(MapToStreamingAlbum(album));
                        newCount++;
                    }
                }

                if (!hasMore || newCount == 0)
                {
                    break;
                }

                offset = nextOffset;
            }

            _logger.LogDebug("Qobuz album search for '{Query}' returned {Count} results", query, result.Count);

            await _statusReporter.ReportStatusAsync(IndexerStatus.Idle, null, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            await RecordAuthOutcomeFromExceptionAsync(ex).ConfigureAwait(false);
            _logger.LogError(ex, "Error searching Qobuz albums for query '{Query}'", query);
            await _statusReporter.ReportErrorAsync(ex, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask<StreamingAlbum?> GetAlbumAsync(string albumId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(albumId))
        {
            return null;
        }
        if (IsAuthShortCircuited())
        {
            _logger.LogDebug("Short-circuiting Qobuz album fetch; auth latched bad and probe slot exhausted");
            return null;
        }

        try
        {
            await _statusReporter.ReportStatusAsync(IndexerStatus.Searching, $"Fetching album: {albumId}", cancellationToken).ConfigureAwait(false);

            var parameters = new Dictionary<string, string>
            {
                ["album_id"] = albumId
            };

            var album = await _apiClient.GetAsync<QobuzAlbum>("/album/get", parameters).ConfigureAwait(false);

            await _statusReporter.ReportStatusAsync(IndexerStatus.Idle, null, cancellationToken).ConfigureAwait(false);

            return album is not null ? MapToStreamingAlbum(album) : null;
        }
        catch (Exception ex)
        {
            await RecordAuthOutcomeFromExceptionAsync(ex).ConfigureAwait(false);
            _logger.LogError(ex, "Error fetching Qobuz album '{AlbumId}'", albumId);
            await _statusReporter.ReportErrorAsync(ex, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<StreamingTrack>> SearchTracksAsync(string query, CancellationToken cancellationToken = default)
    {
        // Track search is not wired in this slice; return empty.
        await _statusReporter.ReportStatusAsync(IndexerStatus.Idle, null, cancellationToken).ConfigureAwait(false);
        return Array.Empty<StreamingTrack>();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Current implementation is a thin wrapper over <see cref="SearchAlbumsAsync"/> that
    /// yields each result individually. True server-side pagination (streaming pages on
    /// demand) is deferred until the Qobuz API pagination slice is implemented.
    /// </remarks>
    public async IAsyncEnumerable<StreamingAlbum> SearchAlbumsStreamAsync(
        string query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var results = await SearchAlbumsAsync(query, cancellationToken).ConfigureAwait(false);
        foreach (var album in results)
        {
            yield return album;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Current implementation is a thin wrapper over <see cref="SearchTracksAsync"/> that
    /// yields each result individually. True server-side pagination is deferred.
    /// </remarks>
    public async IAsyncEnumerable<StreamingTrack> SearchTracksStreamAsync(
        string query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var results = await SearchTracksAsync(query, cancellationToken).ConfigureAwait(false);
        foreach (var track in results)
        {
            yield return track;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        // The QobuzApiClient is owned by the DI container, not this adapter.
        return ValueTask.CompletedTask;
    }

    // ---- Mapping helpers ----

    private static StreamingAlbum MapToStreamingAlbum(QobuzAlbum qAlbum)
    {
        var album = new StreamingAlbum
        {
            Id = qAlbum.Id ?? string.Empty,
            Title = qAlbum.GetFullTitle(),
            Artist = MapToStreamingArtist(qAlbum.Artist),
            ReleaseDate = qAlbum.ReleaseDate != DateTime.MinValue ? qAlbum.ReleaseDate : null,
            TrackCount = qAlbum.TracksCount,
            Duration = qAlbum.DurationSeconds > 0 ? TimeSpan.FromSeconds(qAlbum.DurationSeconds) : null,
            Label = qAlbum.GetLabelName(),
            Upc = qAlbum.UPC ?? string.Empty,
        };

        // Album type heuristic
        if (qAlbum.TracksCount > 0)
        {
            album.Type = qAlbum.TracksCount <= 3 ? StreamingAlbumType.Single
                       : qAlbum.TracksCount <= 6 ? StreamingAlbumType.EP
                       : StreamingAlbumType.Album;
        }

        // Detect compilations by artist name
        var artistName = qAlbum.Artist?.Name;
        if (artistName is not null && artistName.Contains("Various", StringComparison.OrdinalIgnoreCase))
        {
            album.Type = StreamingAlbumType.Compilation;
        }

        // Genres
        var genre = qAlbum.GetGenre();
        if (!string.IsNullOrEmpty(genre))
        {
            album.Genres.Add(genre);
        }

        if (qAlbum.GenresList is { Count: > 0 })
        {
            foreach (var g in qAlbum.GenresList.Where(g => !album.Genres.Contains(g)))
            {
                album.Genres.Add(g);
            }
        }

        // External IDs
        if (!string.IsNullOrEmpty(qAlbum.Id))
        {
            album.ExternalIds["qobuz"] = qAlbum.Id;
        }

        // Cover art
        if (qAlbum.Image is not null)
        {
            if (!string.IsNullOrEmpty(qAlbum.Image.Small))
                album.CoverArtUrls["small"] = qAlbum.Image.Small;
            if (!string.IsNullOrEmpty(qAlbum.Image.Medium))
                album.CoverArtUrls["medium"] = qAlbum.Image.Medium;
            if (!string.IsNullOrEmpty(qAlbum.Image.Large))
                album.CoverArtUrls["large"] = qAlbum.Image.Large;
            if (!string.IsNullOrEmpty(qAlbum.Image.ExtraLarge))
                album.CoverArtUrls["extralarge"] = qAlbum.Image.ExtraLarge;
        }

        // Quality
        if (qAlbum.MaximumBitDepth.HasValue || qAlbum.MaximumSampleRate.HasValue)
        {
            var quality = new StreamingQuality
            {
                Id = $"qobuz-max",
                Format = "FLAC",
                BitDepth = qAlbum.MaximumBitDepth,
                SampleRate = qAlbum.MaximumSampleRate.HasValue
                    ? (int)Math.Round(qAlbum.MaximumSampleRate.Value) // Qobuz reports Hz (e.g., 96000, 192000)
                    : null,
            };
            quality.Name = quality.IsHighResolution ? "Hi-Res" : "Lossless";
            album.AvailableQualities.Add(quality);
        }

        // Additional artists
        if (qAlbum.Artists is { Count: > 0 })
        {
            foreach (var a in qAlbum.Artists.Where(a => a.Id != qAlbum.Artist?.Id))
            {
                album.AdditionalArtists.Add(MapToStreamingArtist(a));
            }
        }

        return album;
    }

    private static StreamingArtist MapToStreamingArtist(QobuzArtist? qArtist)
    {
        if (qArtist is null)
        {
            return new StreamingArtist { Name = "Unknown Artist" };
        }

        var artist = new StreamingArtist
        {
            Id = qArtist.Id ?? string.Empty,
            Name = qArtist.Name ?? "Unknown Artist",
            Biography = qArtist.Biography?.Content ?? string.Empty,
        };

        var imageUrl = qArtist.GetBestImageUrl();
        if (!string.IsNullOrEmpty(imageUrl))
        {
            artist.ImageUrls["default"] = imageUrl;
        }

        if (qArtist.Image is not null)
        {
            if (!string.IsNullOrEmpty(qArtist.Image.Small))
                artist.ImageUrls["small"] = qArtist.Image.Small;
            if (!string.IsNullOrEmpty(qArtist.Image.Medium))
                artist.ImageUrls["medium"] = qArtist.Image.Medium;
            if (!string.IsNullOrEmpty(qArtist.Image.Large))
                artist.ImageUrls["large"] = qArtist.Image.Large;
        }

        return artist;
    }
}
