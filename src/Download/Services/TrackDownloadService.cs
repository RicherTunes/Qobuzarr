using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Qobuzarr.Download;
using Lidarr.Plugin.Common.Services.Lyrics;
using NLog;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Exceptions;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Services.Http;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Lidarr.Plugin.Common.Utilities;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Default implementation of ITrackDownloadService.
    /// Performs concurrent track downloads, streaming to disk, and metadata tagging.
    /// </summary>
    public class TrackDownloadService : ITrackDownloadService
    {
        private readonly IQobuzApiClient _apiClient;
        private readonly IConcurrencyManager _concurrencyManager;
        private readonly IDownloadSummary _downloadSummary;
        private readonly IDownloadQueueService _queueService;
        private readonly ILyricsEnricher? _lyricsEnricher;
        private readonly Logger _logger;

        public TrackDownloadService(
            IQobuzApiClient apiClient,
            IConcurrencyManager concurrencyManager,
            IDownloadSummary downloadSummary,
            IDownloadQueueService queueService,
            Logger logger,
            ILyricsEnricher? lyricsEnricher = null)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _concurrencyManager = concurrencyManager ?? throw new ArgumentNullException(nameof(concurrencyManager));
            _downloadSummary = downloadSummary ?? throw new ArgumentNullException(nameof(downloadSummary));
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _lyricsEnricher = lyricsEnricher;
        }

        public async Task DownloadAlbumAsync(QobuzDownloadItem downloadItem, QobuzAlbum album, QobuzDownloadSettings settings, CancellationToken cancellationToken)
        {
            var tracks = album.GetTracks();
            if (!tracks.Any())
            {
                throw new InvalidOperationException("Album has no tracks to download");
            }

            var completedTracks = 0;
            var totalTracks = tracks.Count;

            var albumInfo = album != null ? $"{album.GetArtistName()} - {album.GetFullTitle()}" : $"{downloadItem.Artist} - {downloadItem.Title}";
            var albumYear = album?.ReleaseDate.Year > 1900 ? $" ({album.ReleaseDate.Year})" : "";
            _logger.Info("Starting album download: {0}{1} • {2} tracks • {3} concurrent",
                albumInfo, albumYear, totalTracks, _concurrencyManager.CurrentLimit);

            var successfulTracks = 0;
            var skippedTracks = 0;
            var failedTracks = 0;

            var downloadTasks = tracks.Select(async track =>
            {
                using var slot = await _concurrencyManager.AcquireSlotAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await DownloadSingleTrackAsync(downloadItem, album, track, settings, cancellationToken).ConfigureAwait(false);

                    var completed = Interlocked.Increment(ref completedTracks);
                    Interlocked.Increment(ref successfulTracks);
                    var progress = (double)completed / totalTracks * 100;
                    downloadItem.UpdateProgress(progress);
                    _logger.Debug("Downloaded track {0}/{1}: {2}", completed, totalTracks, track.GetFullTitle());
                    return new TrackDownloadResult { Success = true, TrackId = track.Id };
                }
                catch (TrackUnavailableException ex)
                {
                    var completed = Interlocked.Increment(ref completedTracks);
                    if (ex.Reason == TrackUnavailableReason.PreviewOnly || ex.Reason == TrackUnavailableReason.NoQualityAvailable)
                    {
                        Interlocked.Increment(ref skippedTracks);
                        _logger.Warn("Skipping track {0} ({1}): {2}", track.GetFullTitle(), track.Id, ex.GetUserFriendlyMessage());
                    }
                    else
                    {
                        Interlocked.Increment(ref failedTracks);
                        _logger.Error(ErrorMessageFormatter.FormatTrackError(track.GetFullTitle(), ex.Reason, ex.Message));
                    }
                    var progress = (double)completed / totalTracks * 100;
                    downloadItem.UpdateProgress(progress);
                    return new TrackDownloadResult { Success = false, TrackId = track.Id, Reason = ex.Reason, Message = ex.GetUserFriendlyMessage() };
                }
                catch (Exception ex)
                {
                    var completed = Interlocked.Increment(ref completedTracks);
                    Interlocked.Increment(ref failedTracks);
                    _logger.Error(ex, "Failed to download track: {0}", track.GetFullTitle());
                    var progress = (double)completed / totalTracks * 100;
                    downloadItem.UpdateProgress(progress);
                    return new TrackDownloadResult { Success = false, TrackId = track.Id, Message = ex.Message };
                }
            });

            var results = await Task.WhenAll(downloadTasks).ConfigureAwait(false);

            // Summary / policy
            var bytesDownloaded = downloadItem.TotalSize;
            _downloadSummary.RecordAlbumResult(downloadItem.Artist, downloadItem.Title, successfulTracks, skippedTracks, failedTracks, totalTracks, bytesDownloaded);
            LogAlbumDownloadSummary(downloadItem.Artist, downloadItem.Title, album, successfulTracks, skippedTracks, failedTracks, totalTracks, bytesDownloaded);

            // Per-album quality-fallback summary (replaces per-track Info spam from GetStreamingInfoAsync)
            if (downloadItem.QualityFallbackCount > 0)
            {
                _logger.Info("Quality fallback used for {0}/{1} tracks ({2})",
                    downloadItem.QualityFallbackCount, totalTracks,
                    downloadItem.QualityFallbackExample ?? "fallback quality");
            }

            if (_queueService.ActiveDownloadCount == 0)
            {
                var summaryReport = _downloadSummary.GenerateReport();
                _logger.Info(summaryReport);
            }

            var policy = settings.GetDownloadPolicy();
            var isSuccessful = policy.IsAlbumDownloadSuccessful(totalTracks, successfulTracks, skippedTracks);

            if (!isSuccessful)
            {
                var exception = new AlbumDownloadException(
                    album.Id,
                    album.GetFullTitle(),
                    totalTracks,
                    successfulTracks,
                    skippedTracks,
                    failedTracks,
                    results);
                throw exception;
            }
        }

        private async Task DownloadSingleTrackAsync(QobuzDownloadItem downloadItem, QobuzAlbum album, QobuzTrack track, QobuzDownloadSettings settings, CancellationToken cancellationToken)
        {
            string outputPath = null;
            try
            {
                // 1. Get streaming info from Qobuz API (need format before building path)
                var streamingInfo = await _apiClient.GetStreamingInfoAsync(track.Id, settings.PreferredQuality, cancellationToken).ConfigureAwait(false);
                var streamUrl = streamingInfo?.Url;
                if (string.IsNullOrEmpty(streamUrl))
                {
                    throw new InvalidOperationException($"Could not get streaming URL for track: {track.Title}");
                }

                // Build output path with sanitized filename and correct extension based on actual format
                var actualFormatId = streamingInfo?.FormatId ?? settings.PreferredQuality;
                var filename = TrackFileNameBuilder.Build(track.TrackNumber, track.Title, actualFormatId, track.DiscNumber, album.MediaCount);
                outputPath = Path.Combine(downloadItem.OutputPath, filename);

                _logger.Info("Downloading track: {0} to {1}", track.Title, outputPath);

                if (streamingInfo != null &&
                    streamingInfo.IsQualityFallbackOnly() &&
                    streamingInfo.FormatId != settings.PreferredQuality)
                {
                    downloadItem.RecordQualityFallback(settings.PreferredQuality, streamingInfo.FormatId);

                    var example = downloadItem.QualityFallbackExample;
                    if (downloadItem.QualityFallbackCount == 1)
                    {
                        _logger.Info("Quality fallback: {0} - {1} (track {2}) {3}",
                            downloadItem.Artist,
                            downloadItem.Title,
                            track.Title,
                            example ?? "fallback quality used");
                    }
                    else
                    {
                        _logger.Debug("Quality fallback: {0} - {1} (track {2}) {3}",
                            downloadItem.Artist,
                            downloadItem.Title,
                            track.Title,
                            example ?? "fallback quality used");
                    }
                }

                _logger.Debug("Got streaming URL for track {0}", track.Id);

                // 2. Ensure output directory
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                // 3. Download to disk
                _logger.Debug("Starting HTTP download for track {0}", track.Title);
                var bytesWritten = await DownloadToFileAsync(streamUrl, outputPath, cancellationToken).ConfigureAwait(false);
                _logger.Debug("Audio file written: {0} bytes", bytesWritten);

                // 4. Apply tags
                await ApplyMetadataTagsAsync(outputPath, track, album).ConfigureAwait(false);

                // 5. Best-effort synced lyrics (non-fatal). Canonical gating: SaveSyncedLyrics is the
                //    master toggle; UseLRCLIB gates the LRCLIB fallback (a native source, when one
                //    exists, is always tried). Uses Common's shared enricher; when one isn't injected
                //    (production — Common's type is internalized so DryIoc doesn't auto-register it) a
                //    short-lived instance is constructed per track, mirroring the prior design.
                if (settings.SaveSyncedLyrics)
                {
                    var enricher = _lyricsEnricher;
                    var ownsEnricher = enricher is null;
                    enricher ??= new LyricsEnricher();
                    try
                    {
                        await enricher.TryEnrichAsync(
                            outputPath,
                            album.GetArtistName() ?? "Unknown",
                            track.Title ?? "Unknown",
                            album.GetFullTitle() ?? "",
                            track.DurationSeconds,
                            allowLrclibFallback: settings.UseLRCLIB,
                            cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        if (ownsEnricher)
                        {
                            enricher.Dispose();
                        }
                    }
                }

                _logger.Info("Downloaded: {0} ({1:F1} MB)", track.Title, bytesWritten / 1024.0 / 1024.0);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Download failed for track: {0}", track.Title);
                if (File.Exists(outputPath))
                {
                    try { File.Delete(outputPath); } catch (Exception cleanupEx) { _logger.Debug(cleanupEx, "Best-effort file cleanup failed for {0}", outputPath); }
                }
                throw;
            }

            // Basic file presence validation
            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length < 1024)
            {
                throw new InvalidOperationException($"Downloaded file validation failed: {track.Title}");
            }
        }

        private async Task<long> DownloadToFileAsync(string url, string filePath, CancellationToken cancellationToken)
        {
            var httpClient = SharedSystemHttpClient.Instance;
            var partialPath = filePath + ".partial";
            long existing = 0;
            if (File.Exists(partialPath))
            {
                try { existing = new FileInfo(partialPath).Length; } catch (Exception ex) { _logger.Debug(ex, "Could not read partial file size for {0}", partialPath); existing = 0; }
            }

            var (response, effectiveExisting) = await Lidarr.Plugin.Qobuzarr.Download.ResumeHttpDownloader.SendDownloadRequestAsync(
                httpClient, url, partialPath, existing,
                p => _logger.Debug("Resume partial '{0}' was complete/stale (HTTP 416); restarting download fresh", p),
                cancellationToken).ConfigureAwait(false);
            using var _responseScope = response;
            existing = effectiveExisting;

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
            var contentLength = response.Content.Headers.ContentLength;
            var urlHost = DownloadResponseDiagnostics.TryGetHost(url);
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent || contentLength == 0)
            {
                throw new InvalidOperationException($"Download returned no content (HTTP {(int)response.StatusCode} {response.StatusCode}, Host={urlHost}, Content-Type={contentType}).");
            }

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var isPartial = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
            if (!isPartial && File.Exists(partialPath))
            {
                try { File.Delete(partialPath); } catch (Exception ex) { _logger.Debug(ex, "Best-effort partial file cleanup failed for {0}", partialPath); }
                existing = 0;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            var buffer = new byte[131072];
            long totalWritten = existing;
            int read;

            // Explicit scope ensures fileStream is closed before File.Move.
            // Append ONLY when genuinely resuming (HTTP 206). For a fresh download (server returned
            // 200, ignoring the Range), use Create so the file is truncated even if the stale
            // ".partial" delete above failed (it is swallowed) — otherwise the full fresh body was
            // appended onto stale bytes, silently corrupting the audio file.
            var partialFileMode = isPartial ? FileMode.Append : FileMode.Create;
            await using (var fileStream = new FileStream(partialPath, partialFileMode, FileAccess.Write, FileShare.None, 131072, useAsync: true))
            {
                read = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    throw new InvalidOperationException($"Downloaded stream contained no data (Host={urlHost}, Content-Type={contentType}, Content-Length={contentLength?.ToString() ?? "unknown"}).");
                }

                if (DownloadResponseDiagnostics.IsTextLikeContentType(contentType) || DownloadResponseDiagnostics.LooksLikeTextPayload(buffer, read))
                {
                    var snippet = Encoding.UTF8.GetString(buffer, 0, Math.Min(read, 512))
                        .Replace("\r", " ")
                        .Replace("\n", " ")
                        .Trim();

                    if (DownloadResponseDiagnostics.ShouldRedactSnippet(snippet))
                    {
                        snippet = "[redacted]";
                    }

                    throw new InvalidOperationException($"Unexpected content type '{contentType}' when downloading audio (Host={urlHost}). Snippet: {snippet}");
                }

                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                totalWritten += read;

                while ((read = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    totalWritten += read;
                }

                await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(partialPath, filePath, overwrite: true);

            Lidarr.Plugin.Common.Utilities.AudioMagicBytesValidator.ValidateAudioMagicBytes(filePath);

            if (!Lidarr.Plugin.Common.Utilities.ValidationUtilities.ValidateDownloadedFile(filePath))
            {
                throw new InvalidOperationException($"Downloaded file failed validation: {Path.GetFileName(filePath)}");
            }
            return totalWritten;
        }

        private async Task ApplyMetadataTagsAsync(string filePath, QobuzTrack track, QobuzAlbum album)
        {
            await Task.Run(() =>
            {
                try
                {
                    using var file = TagLib.File.Create(filePath);
                    file.Tag.Title = track.Title;
                    file.Tag.Track = (uint)track.TrackNumber;
                    file.Tag.Disc = (uint)track.DiscNumber;

                    if (album != null)
                    {
                        file.Tag.Album = album.Title;
                        file.Tag.AlbumArtists = new[] { album.Artist?.Name ?? "Unknown Artist" };
                        if (album.ReleaseDate != default) file.Tag.Year = (uint)album.ReleaseDate.Year;
                        if (album.Genre != null) file.Tag.Genres = new[] { album.Genre.Name };
                        if (album.Label != null) file.Tag.Comment = $"Label: {album.Label.Name}";
                    }
                    if (track.Performer != null) file.Tag.Performers = new[] { track.Performer.Name };
                    if (track.Composer != null) file.Tag.Composers = new[] { track.Composer.Name };
                    file.Save();
                    _logger.Debug("Metadata applied to: {0}", Path.GetFileName(filePath));
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to apply metadata to: {0}", Path.GetFileName(filePath));
                }
            });
        }

        private void LogAlbumDownloadSummary(string artistName, string albumTitle, QobuzAlbum album,
            int successful, int skipped, int failed, int total, long bytesDownloaded)
        {
            try
            {
                var albumYear = album?.ReleaseDate.Year > 1900 ? album.ReleaseDate.Year.ToString() : "";
                var albumInfo = !string.IsNullOrEmpty(albumYear) ? $"{artistName} - {albumTitle} ({albumYear})" : $"{artistName} - {albumTitle}";
                var completionRate = total > 0 ? (int)Math.Round((double)successful / total * 100) : 0;
                var tracksInfo = $"{successful}/{total} tracks ({completionRate}%)";
                var sizeInfo = FormatBytes(bytesDownloaded);

                var parts = new List<string> { albumInfo, tracksInfo, sizeInfo };
                if (skipped > 0) parts.Add($"{skipped} skipped");
                if (failed > 0) parts.Add($"{failed} failed");

                _logger.Info("Album summary: {0}", string.Join(" • ", parts));
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error logging album download summary");
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1048576) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1073741824) return $"{bytes / 1048576.0:F1} MB";
            return $"{bytes / 1073741824.0:F2} GB";
        }
    }
}
