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
using Lidarr.Plugin.Abstractions.Models;
using Lidarr.Plugin.Common.Services.Download;
using Lidarr.Plugin.Common.Services.Lyrics;
using NLog;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Exceptions;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Services.Http;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Lidarr.Plugin.Common.Utilities;
using CommonResults = Lidarr.Plugin.Common.Interfaces;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Default implementation of ITrackDownloadService.
    ///
    /// <para>Wave B: the per-album/per-track download work is delegated to Common's
    /// <see cref="SimpleDownloadOrchestrator"/> (via <see cref="QobuzDownloadOrchestrator"/>) so Qobuz shares
    /// the same robust per-track engine (SSRF guard, retry-with-resume, atomic move, lyrics post-processing,
    /// metadata tagging) as tidal/amazon/apple. This service now owns only the Qobuz-specific glue:
    /// building the orchestrator delegates (stream-URL resolution with quality-fallback accounting), the
    /// custom metadata applier + lyrics post-processor, mapping the orchestrator's result to the album
    /// summary, and enforcing the album-completion policy (incomplete ⇒ <see cref="AlbumDownloadException"/>
    /// ⇒ host reports Failed).</para>
    ///
    /// <para>The lower-level resume/retry HTTP helpers (<see cref="DownloadToFileAsync"/> and friends) are
    /// retained for their regression tests; they are no longer on the production download path (the
    /// orchestrator's URL engine owns resume now) and can be removed once the orchestrator path is
    /// live-validated.</para>
    /// </summary>
    public class TrackDownloadService : ITrackDownloadService
    {
        private readonly IQobuzApiClient _apiClient;
        private readonly IConcurrencyManager _concurrencyManager;
        private readonly IDownloadSummary _downloadSummary;
        private readonly ILyricsEnricher? _lyricsEnricher;
        private readonly Logger _logger;

        public TrackDownloadService(
            IQobuzApiClient apiClient,
            IConcurrencyManager concurrencyManager,
            IDownloadSummary downloadSummary,
            Logger logger,
            ILyricsEnricher? lyricsEnricher = null)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _concurrencyManager = concurrencyManager ?? throw new ArgumentNullException(nameof(concurrencyManager));
            _downloadSummary = downloadSummary ?? throw new ArgumentNullException(nameof(downloadSummary));
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

            var totalTracks = tracks.Count;

            var albumInfo = album != null ? $"{album.GetArtistName()} - {album.GetFullTitle()}" : $"{downloadItem.Artist} - {downloadItem.Title}";
            var albumYear = album?.ReleaseDate.Year > 1900 ? $" ({album.ReleaseDate.Year})" : "";
            _logger.Info("Starting album download: {0}{1} • {2} tracks • {3} concurrent",
                albumInfo, albumYear, totalTracks, _concurrencyManager.CurrentLimit);

            // Per-album skip/fail classifier. The orchestrator's TrackDownloadResult carries only
            // success/failure; the stream-resolution delegate records preview-only / no-quality skips here
            // so the summary + AlbumDownloadException reporting stay faithful to the prior bespoke loop.
            var classifier = new QobuzTrackClassifier();

            // Bridge orchestrator progress to the download item (mirrors the prior per-track UpdateProgress).
            var progress = new InlineProgress<CommonResults.DownloadProgress>(p => downloadItem.UpdateProgress(p.PercentComplete));

            var result = await RunOrchestratorAsync(album, settings, downloadItem, classifier, progress, cancellationToken).ConfigureAwait(false);

            // Map the orchestrator result back to the album completion accounting. A "skipped" track
            // (preview/no-quality) still leaves the album incomplete, so the split below only affects
            // reporting; the completion policy keys off successful vs total.
            var successfulTracks = result.TrackResults.Count(r => r.Success);
            var skippedTracks = Math.Min(classifier.SkippedCount, Math.Max(0, totalTracks - successfulTracks));
            var failedTracks = Math.Max(0, totalTracks - successfulTracks - skippedTracks);

            // Summary / policy
            var bytesDownloaded = downloadItem.TotalSize;
            _downloadSummary.RecordAlbumResult(downloadItem.Artist, downloadItem.Title, successfulTracks, skippedTracks, failedTracks, totalTracks, bytesDownloaded);
            LogAlbumDownloadSummary(downloadItem.Artist, downloadItem.Title, album, successfulTracks, skippedTracks, failedTracks, totalTracks, bytesDownloaded);

            // Per-album quality-fallback summary (replaces per-track Info spam from GetStreamingInfoAsync).
            // Warn when the WHOLE album fell back (the requested tier is entirely unavailable — an
            // operational signal worth surfacing in Lidarr's activity UI); Info for a partial fallback.
            if (downloadItem.QualityFallbackCount > 0)
            {
                const string msg = "Quality fallback used for {0}/{1} tracks ({2})";
                var example = downloadItem.QualityFallbackExample ?? "fallback quality";
                if (totalTracks > 0 && downloadItem.QualityFallbackCount >= totalTracks)
                {
                    _logger.Warn(msg, downloadItem.QualityFallbackCount, totalTracks, example);
                }
                else
                {
                    _logger.Info(msg, downloadItem.QualityFallbackCount, totalTracks, example);
                }
            }

            // Wave C removed the bespoke queue service that previously knew when all active
            // downloads were finished. Avoid regenerating and logging the full cumulative report
            // after every album; the track service only emits a compact progress line.
            _logger.Info(_downloadSummary.GetBriefSummary());

            var policy = settings.GetDownloadPolicy();
            var isSuccessful = policy.IsAlbumDownloadSuccessful(totalTracks, successfulTracks, skippedTracks);

            if (!isSuccessful)
            {
                // Album-completion contract (CLAUDE.md "Album-completion contract"): ALWAYS report Failed
                // on any deficit, even when the deficit is a permanently-restricted track that no retry
                // will ever fix. Reporting Failed here is what lets Lidarr blocklist the grabbed release
                // and fall back to another edition/source when one exists (the Aphex-Twin contract). The
                // re-grab loop for a permanently-restricted track is broken further upstream instead: once
                // this exception's TrackResults show a terminal restriction (see
                // TrackUnavailableReasonExtensions.IsPermanentlyUnavailable), QobuzDownloadClient records
                // the album id in RestrictedReleaseSuppressionStore so the indexer stops offering releases
                // for it in future searches — without ever changing what gets reported to Lidarr for this
                // grab. See QobuzDownloadClient.PerformDownloadAsync's AlbumDownloadException catch.
                var exception = new AlbumDownloadException(
                    album.Id,
                    album.GetFullTitle(),
                    totalTracks,
                    successfulTracks,
                    skippedTracks,
                    failedTracks,
                    MapTrackResults(result, classifier));
                throw exception;
            }
        }

        /// <summary>
        /// Runs the album download through <see cref="QobuzDownloadOrchestrator"/>. Extracted as a protected
        /// virtual seam so the album-completion mapping (incomplete ⇒ <see cref="AlbumDownloadException"/>)
        /// can be exercised in isolation without driving real HTTP.
        /// </summary>
        protected virtual Task<CommonResults.DownloadResult> RunOrchestratorAsync(
            QobuzAlbum album,
            QobuzDownloadSettings settings,
            QobuzDownloadItem downloadItem,
            QobuzTrackClassifier classifier,
            IProgress<CommonResults.DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            var orchestrator = BuildOrchestrator(album, settings, downloadItem, classifier, cancellationToken);
            return orchestrator.DownloadAlbumAsync(
                downloadItem.AlbumId ?? album.Id,
                downloadItem.OutputPath,
                quality: null,
                progress,
                cancellationToken);
        }

        /// <summary>
        /// Builds the Qobuz orchestrator with delegates closured over the album/settings/download item.
        /// </summary>
        protected virtual QobuzDownloadOrchestrator BuildOrchestrator(
            QobuzAlbum album,
            QobuzDownloadSettings settings,
            QobuzDownloadItem downloadItem,
            QobuzTrackClassifier classifier,
            CancellationToken cancellationToken)
        {
            Func<string, Task<StreamingAlbum>> getAlbum = _ => Task.FromResult(new StreamingAlbum
            {
                Id = album.Id ?? string.Empty,
                Title = album.Title ?? string.Empty,
                Artist = new StreamingArtist { Name = album.GetArtistName() },
                TrackCount = album.GetTracks().Count,
            });

            Func<string, Task<IReadOnlyList<string>>> getTrackIds = _ =>
                Task.FromResult<IReadOnlyList<string>>(album.GetTracks().Select(t => t.Id).ToList());

            Func<string, Task<StreamingTrack>> getTrack = id =>
                Task.FromResult<StreamingTrack>(QobuzStreamingTrack.From(ResolveQobuzTrack(album, id), album));

            Func<string, StreamingQuality?, Task<(string Url, string Extension)>> getStream =
                (id, _) => ResolveStreamAsync(id, settings, downloadItem, classifier, cancellationToken);

            var applier = new QobuzAudioMetadataApplier(_logger);
            var postProcessor = new QobuzLyricsPostProcessor(settings, _lyricsEnricher, _logger);
            var maxConcurrent = Math.Max(1, _concurrencyManager.CurrentLimit);

            return new QobuzDownloadOrchestrator(
                SharedSystemHttpClient.Instance,
                getAlbum,
                getTrack,
                getTrackIds,
                getStream,
                maxConcurrent,
                album,
                settings.PreferredQuality,
                applier,
                postProcessor,
                // Allow http in addition to https so any non-TLS CDN URL Qobuz returns still downloads
                // (the prior bespoke path did no SSRF check at all); private-network / metadata-host SSRF
                // is still blocked, a net improvement.
                mediaUriPolicy: new RemoteMediaUriPolicy { AllowHttp = true },
                logger: null);
        }

        private static QobuzTrack ResolveQobuzTrack(QobuzAlbum album, string id)
            => album.GetTracks().FirstOrDefault(t => t.Id == id) ?? new QobuzTrack { Id = id, Title = "Unknown Track" };

        /// <summary>
        /// Stream-URL + format resolution delegate for the orchestrator. Calls the SAME
        /// <see cref="IQobuzApiClient.GetStreamingInfoAsync"/> path the prior loop used, so the download-path
        /// re-authentication (the api client's pre-request session renewal) still applies. Records
        /// quality-fallback accounting and classifies preview-only / no-quality tracks as skips. Returns an
        /// empty URL (never throws, except on cancellation) so a single unresolved track fails just that
        /// track rather than crashing the whole album inside the orchestrator.
        /// </summary>
        internal async Task<(string Url, string Extension)> ResolveStreamAsync(
            string trackId,
            QobuzDownloadSettings settings,
            QobuzDownloadItem downloadItem,
            QobuzTrackClassifier classifier,
            CancellationToken cancellationToken)
        {
            try
            {
                var streamingInfo = await _apiClient.GetStreamingInfoAsync(trackId, settings.PreferredQuality, cancellationToken).ConfigureAwait(false);
                var streamUrl = streamingInfo?.Url;
                if (string.IsNullOrEmpty(streamUrl))
                {
                    // Hard failure (mirrors the prior "Could not get streaming URL"); not a skip.
                    return (string.Empty, string.Empty);
                }

                var actualFormatId = streamingInfo!.FormatId;

                if (streamingInfo.IsQualityFallbackOnly() && streamingInfo.FormatId != settings.PreferredQuality)
                {
                    downloadItem.RecordQualityFallback(settings.PreferredQuality, streamingInfo.FormatId);
                }

                var extension = TrackFileNameBuilder.GetExtensionForFormat(actualFormatId);
                return (streamUrl, extension);
            }
            catch (TrackUnavailableException ex)
            {
                // Record the reason for EVERY classified unavailability, not just preview/no-quality.
                // DownloadAlbumAsync's album-completion decision needs to know WHY every deficit track
                // failed to distinguish a permanently-hopeless album (a purchase-only/subscription-tier
                // restriction — no retry or re-grab will ever fix it) from a recoverable
                // one (a genuine edition mismatch or transient failure, which must stay Failed so Lidarr
                // blocklists + falls back). Previously only Preview/NoQuality were recorded here, so a
                // Restricted track (e.g. the raw "TrackRestrictedByPurchaseCredentials" restriction) fell
                // into the same "no reason recorded" bucket as a genuinely unknown hard failure.
                classifier.RecordSkipped(trackId, ex.Reason);

                if (ex.Reason == TrackUnavailableReason.PreviewOnly || ex.Reason == TrackUnavailableReason.NoQualityAvailable)
                {
                    _logger.Warn("Skipping track {0}: {1}", trackId, ex.GetUserFriendlyMessage());
                }
                else if (ex.Reason.IsPermanentlyUnavailable())
                {
                    _logger.Warn("Track {0} permanently unavailable ({1}): {2}", trackId, ex.Reason, ex.GetUserFriendlyMessage());
                }
                else
                {
                    _logger.Error(ErrorMessageFormatter.FormatTrackError(trackId, ex.Reason, ex.Message));
                }

                return (string.Empty, string.Empty);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // e.g. InvalidOperationException from GetStreamingInfoAsync (sample / restriction / empty);
                // treated as a hard failure, matching the prior general catch in the bespoke loop.
                _logger.Error(ex, "Failed to resolve stream for track {0}", trackId);
                return (string.Empty, string.Empty);
            }
        }

        private static IEnumerable<TrackDownloadResult> MapTrackResults(CommonResults.DownloadResult result, QobuzTrackClassifier classifier)
        {
            return result.TrackResults.Select(r => new TrackDownloadResult
            {
                Success = r.Success,
                TrackId = r.TrackId,
                FilePath = r.FilePath,
                Message = r.ErrorMessage,
                Reason = r.Success ? null : classifier.GetReason(r.TrackId),
            });
        }

        // ───────────────────────────────────────────────────────────────────────────────────────────
        // Retained resume/retry HTTP engine (regression coverage: TrackDownloadRetryTests). No longer on
        // the production download path — the orchestrator's URL engine owns retry-with-resume now (see the
        // class summary). Kept until the orchestrator path is live-validated, then removable.
        // ───────────────────────────────────────────────────────────────────────────────────────────

        // Number of attempts for a single track download. Qobuz's CDN occasionally truncates a
        // response mid-body (HttpIOException "response ended prematurely / ResponseEnded"); without a
        // retry one truncated track fails the whole album, and Lidarr re-grabs into an infinite loop.
        // Each retry resumes from the preserved ".partial" (Range request via ResumeHttpDownloader),
        // so consecutive attempts make forward progress until the file is complete.
        internal virtual int MaxDownloadAttempts => 4;

        // Exponential backoff (1s, 2s, 4s, capped at 8s) between transient-failure retries.
        internal virtual TimeSpan GetRetryDelay(int attempt) =>
            TimeSpan.FromSeconds(Math.Min(8, Math.Pow(2, Math.Max(0, attempt - 1))));

        private static bool IsTransientDownloadException(Exception ex, CancellationToken cancellationToken)
        {
            // An honored cancellation (host/user requested) is never retried.
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            return ex switch
            {
                HttpIOException => true,                       // e.g. ResponseEnded (truncated body)
                HttpRequestException => true,                  // connection reset / DNS blip / 5xx surfaced by EnsureSuccess
                System.Net.Sockets.SocketException => true,    // transport-level reset
                TaskCanceledException => true,                 // per-request HttpClient timeout (token not cancelled — checked above)
                IOException => true,                           // stream copy interrupted
                _ => false,
            };
        }

        internal async Task<long> DownloadToFileAsync(string url, string filePath, CancellationToken cancellationToken)
        {
            var partialPath = filePath + ".partial";
            var maxAttempts = Math.Max(1, MaxDownloadAttempts);

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await DownloadAttemptAsync(url, filePath, partialPath, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (attempt < maxAttempts && IsTransientDownloadException(ex, cancellationToken))
                {
                    _logger.Warn(ex,
                        "Transient download failure for '{0}' (attempt {1}/{2}); resuming from partial after backoff",
                        Path.GetFileName(filePath), attempt, maxAttempts);

                    var delay = GetRetryDelay(attempt);
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        internal virtual async Task<long> DownloadAttemptAsync(string url, string filePath, string partialPath, CancellationToken cancellationToken)
        {
            var httpClient = SharedSystemHttpClient.Instance;
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
            var partialFileMode = PartialFileReset.ResolveWriteMode(serverHonoredRange: isPartial);
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

            Lidarr.Plugin.Common.Utilities.DownloadPayloadValidator.ValidateFileOrThrow(filePath);

            if (!Lidarr.Plugin.Common.Utilities.ValidationUtilities.ValidateDownloadedFile(filePath))
            {
                throw new InvalidOperationException($"Downloaded file failed validation: {Path.GetFileName(filePath)}");
            }
            return totalWritten;
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

        private sealed class InlineProgress<T> : IProgress<T>
        {
            private readonly Action<T> _handler;

            public InlineProgress(Action<T> handler)
            {
                _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            }

            public void Report(T value) => _handler(value);
        }
    }
}
