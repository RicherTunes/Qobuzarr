using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Abstractions.Contracts;
using Lidarr.Plugin.Abstractions.Models;
using Lidarr.Plugin.Common.Services.Download;
using Lidarr.Plugin.Common.Interfaces;
using Lidarr.Plugin.Common.Services.Performance;

namespace Lidarr.Plugin.Qobuzarr.Plugin
{
    /// <summary>
    /// Minimal adapter that satisfies the pr-plugins IDownloadClient contract.
    /// NOTE: This initial version provides a functional skeleton with in-memory tracking only.
    ///       The actual Qobuz download orchestration will be mapped in a subsequent change.
    /// </summary>
    internal sealed class QobuzDownloadClientAdapter : IDownloadClient
    {
        private readonly QobuzSettings _settings;
        private readonly ConcurrentDictionary<string, StreamingDownloadItem> _items = new();
        private HttpClient? _httpClient;
        private SimpleDownloadOrchestrator? _orchestrator;
        private readonly IUniversalAdaptiveRateLimiter _limiter = new UniversalAdaptiveRateLimiter();

        public QobuzDownloadClientAdapter(QobuzSettings settings) => _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        public async ValueTask<PluginValidationResult> InitializeAsync(CancellationToken cancellationToken = default)
        {
            // Validate required settings
            var path = _settings.DownloadPath;
            var userId = _settings.UserId;
            var token = _settings.AuthToken;
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
            {
                var errors = new List<string>();
                if (string.IsNullOrWhiteSpace(path)) errors.Add("downloadPath is not configured");
                if (string.IsNullOrWhiteSpace(userId)) errors.Add("userId is required");
                if (string.IsNullOrWhiteSpace(token)) errors.Add("authToken is required");
                return PluginValidationResult.Failure(errors);
            }

            // Prepare HttpClient tuned for streaming downloads
            var handler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer = 8,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            };
            _httpClient = new HttpClient(handler, disposeHandler: true);
            _httpClient.Timeout = TimeSpan.FromSeconds(120);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Qobuzarr/1.0 (+https://github.com/richertunes/qobuzarr)");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            // Wire orchestrator with API shim
            var shim = new QobuzApiShim(_httpClient, _settings.AppId, userId, token, _settings.CountryCode, _settings.Locale);

            _orchestrator = new SimpleDownloadOrchestrator(
                serviceName: "Qobuz",
                httpClient: _httpClient,
                getAlbumAsync: shim.GetAlbumAsync,
                getTrackAsync: shim.GetTrackAsync,
                getAlbumTrackIdsAsync: shim.GetAlbumTrackIdsAsync,
                getStreamAsync: async (trackId, quality) =>
                {
                    var formatId = _settings.PreferredQuality;
                    var (url, ext) = await shim.GetStreamAsync(trackId, formatId).ConfigureAwait(false);
                    return (url, ext);
                }
            );

            // Live probe similar to indexer Initialize (host Test button)
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(3));
                _ = await shim.SearchAlbumsAsync("a", 1).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return PluginValidationResult.Failure(new []{"Credential probe timed out (network?)"});
            }
            catch (HttpRequestException ex)
            {
                return PluginValidationResult.Failure(new []{$"Credential probe failed: {ex.Message}"});
            }
            catch (Exception ex)
            {
                return PluginValidationResult.Failure(new []{$"Credential probe error: {ex.Message}"});
            }

            return PluginValidationResult.Success();
        }

        public ValueTask<string> EnqueueAlbumDownloadAsync(string albumId, string outputPath, CancellationToken cancellationToken = default)
        {
            if (_orchestrator == null || _httpClient == null)
            {
                throw new InvalidOperationException("Adapter not initialized");
            }

            var id = Guid.NewGuid().ToString("N");
            var root = _settings.DownloadPath;
            var createFolders = _settings.CreateAlbumFolders;
            var effectiveOut = outputPath;

            var item = new StreamingDownloadItem
            {
                Id = id,
                AlbumId = albumId,
                OutputPath = outputPath,
                StartedAt = DateTime.UtcNow,
                Status = StreamingDownloadStatus.Queued,
                Progress = 0,
                LastUpdated = DateTime.UtcNow
            };
            _items[id] = item;

            _ = Task.Run(async () =>
            {
                try
                {
                    item.Status = StreamingDownloadStatus.Downloading;

                    // Fetch album to compute folder & friendly name
                    await _limiter.WaitIfNeededAsync("Qobuz", "album/get", cancellationToken);
                    var album = await new QobuzApiShim(_httpClient!, _settings.AppId, _settings.UserId, _settings.AuthToken, _settings.CountryCode, _settings.Locale).GetAlbumAsync(albumId).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(effectiveOut))
                    {
                        var artist = album?.Artist?.Name ?? "Unknown Artist";
                        var title = album?.Title ?? "Unknown Album";
                        var safeArtist = SanitizeFolder(artist);
                        var safeAlbum = SanitizeFolder(title);
                        effectiveOut = System.IO.Path.Combine(root, safeArtist, safeAlbum);
                    }

                    var progress = new Progress<DownloadProgress>(p =>
                    {
                        item.Progress = p.PercentComplete;
                        item.CurrentTrack = p.CurrentTrack;
                        item.LastUpdated = DateTime.UtcNow;
                    });

                    var desiredQuality = _settings.PreferredQuality;
                    var quality = new StreamingQuality { Id = desiredQuality.ToString(), Name = desiredQuality == 5 ? "MP3 320" : (desiredQuality == 6 ? "FLAC CD" : "FLAC 24/96"), Format = desiredQuality == 5 ? "MP3" : "FLAC", BitDepth = desiredQuality == 5 ? null : (desiredQuality == 6 ? 16 : 24), SampleRate = desiredQuality == 5 ? null : (desiredQuality == 6 ? 44100 : 96000) };

                    System.IO.Directory.CreateDirectory(effectiveOut);
                    await _limiter.WaitIfNeededAsync("Qobuz", "album/download", cancellationToken);
                    var result = await _orchestrator!.DownloadAlbumAsync(albumId, effectiveOut, quality, progress, cancellationToken).ConfigureAwait(false);

                    item.CompletedAt = DateTime.UtcNow;
                    item.Success = result.Success;
                    item.Status = result.Success ? StreamingDownloadStatus.Completed : StreamingDownloadStatus.Failed;
                    if (!result.Success)
                    {
                        item.ErrorMessage = string.Join("; ", result.TrackResults.Where(r => !r.Success && !string.IsNullOrWhiteSpace(r.ErrorMessage)).Select(r => r.ErrorMessage).Take(3));
                    }
                }
                catch (OperationCanceledException)
                {
                    item.Status = StreamingDownloadStatus.Cancelled;
                }
                catch (Exception ex)
                {
                    item.Status = StreamingDownloadStatus.Failed;
                    item.ErrorMessage = ex.Message;
                }
                finally
                {
                    item.LastUpdated = DateTime.UtcNow;
                }
            }, cancellationToken);

            return ValueTask.FromResult(id);
        }

        public ValueTask<bool> RemoveDownloadAsync(string downloadId, bool deleteData = false, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_items.TryRemove(downloadId, out _));
        }

        public ValueTask<IReadOnlyList<StreamingDownloadItem>> GetActiveDownloadsAsync(CancellationToken cancellationToken = default)
        {
            var list = new List<StreamingDownloadItem>(_items.Values);
            return ValueTask.FromResult((IReadOnlyList<StreamingDownloadItem>)list);
        }

        public ValueTask<StreamingDownloadItem?> GetDownloadAsync(string downloadId, CancellationToken cancellationToken = default)
        {
            _items.TryGetValue(downloadId, out var item);
            return ValueTask.FromResult(item);
        }

        public ValueTask DisposeAsync()
        {
            _items.Clear();
            return ValueTask.CompletedTask;
        }

        private static string SanitizeFolder(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unknown";
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            foreach (var ch in invalid) name = name.Replace(ch, '_');
            return name.Trim();
        }
    }
}
