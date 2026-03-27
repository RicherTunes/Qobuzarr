using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Abstractions.Contracts;
using Lidarr.Plugin.Abstractions.Models;
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

    public QobuzIndexerAdapter(
        IQobuzApiClient apiClient,
        IIndexerStatusReporter statusReporter,
        ILogger<QobuzIndexerAdapter> logger,
        QobuzarrStreamingSettings settings)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _statusReporter = statusReporter ?? throw new ArgumentNullException(nameof(statusReporter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
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

        try
        {
            await _statusReporter.ReportStatusAsync(IndexerStatus.Searching, $"Searching albums: {query}", cancellationToken).ConfigureAwait(false);

            var parameters = new Dictionary<string, string>
            {
                ["query"] = query,
                ["limit"] = _settings.SearchLimit.ToString()
            };

            var response = await _apiClient.GetAsync<QobuzAlbumSearchResponse>("/album/search", parameters).ConfigureAwait(false);

            var albums = response?.GetAlbums() ?? new List<QobuzAlbum>();
            var result = albums.Select(MapToStreamingAlbum).ToList();

            _logger.LogDebug("Qobuz album search for '{Query}' returned {Count} results", query, result.Count);

            await _statusReporter.ReportStatusAsync(IndexerStatus.Idle, null, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
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

        // Genres
        var genre = qAlbum.GetGenre();
        if (!string.IsNullOrEmpty(genre) && genre != "Unknown")
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
                    ? (int)(qAlbum.MaximumSampleRate.Value * 1000) // Qobuz reports kHz, model uses Hz
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
