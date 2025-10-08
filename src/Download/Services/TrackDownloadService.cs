using Lidarr.Plugin.Qobuzarr.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Lidarr.Plugin.Qobuzarr.Exceptions;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Services.Http;
using Lidarr.Plugin.Qobuzarr.Utilities;
using NzbDrone.Common.Disk;
using Lidarr.Plugin.Qobuzarr.Abstractions;
using Lidarr.Plugin.Qobuzarr.Observability;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Default implementation of ITrackDownloadService.
    /// Performs concurrent track downloads, streaming to disk, and metadata tagging.
    /// </summary>
    public class TrackDownloadService : ITrackDownloadService
    {
        private readonly IQobuzApiClient _apiClient;
        private readonly IStreamUrlProvider _streamUrlProvider;
        private readonly IQualityFallbackProvider _qualityFallbackProvider;
        private readonly IAlternateReleaseResolver _alternateResolver;
        private readonly IConcurrencyManager _concurrencyManager;
        private readonly IDownloadSummary _downloadSummary;
        private readonly IDownloadQueueService _queueService;
        private readonly Logger _logger;
        private readonly IMetricsCollector? _metrics;
        private readonly IDiskProvider _diskProvider;

        public TrackDownloadService(
            IQobuzApiClient apiClient,
            IStreamUrlProvider streamUrlProvider,
            IQualityFallbackProvider qualityFallbackProvider,
            IAlternateReleaseResolver alternateResolver,
            IConcurrencyManager concurrencyManager,
            IDownloadSummary downloadSummary,
            IDownloadQueueService queueService,
            Logger logger,
            IDiskProvider diskProvider,
            IMetricsCollector? metrics = null)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _streamUrlProvider = streamUrlProvider ?? throw new ArgumentNullException(nameof(streamUrlProvider));
            _qualityFallbackProvider = qualityFallbackProvider ?? throw new ArgumentNullException(nameof(qualityFallbackProvider));
            _alternateResolver = alternateResolver ?? throw new ArgumentNullException(nameof(alternateResolver));
            _concurrencyManager = concurrencyManager ?? throw new ArgumentNullException(nameof(concurrencyManager));
            _downloadSummary = downloadSummary ?? throw new ArgumentNullException(nameof(downloadSummary));
            _queueService = queueService ?? throw new ArgumentNullException(nameof(queueService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _metrics = metrics;
            _diskProvider = diskProvider ?? throw new ArgumentNullException(nameof(diskProvider));
        }

        public async Task DownloadAlbumAsync(QobuzDownloadItem downloadItem, QobuzAlbum album, QobuzDownloadSettings settings, CancellationToken cancellationToken)
        {
            var albumStopwatch = System.Diagnostics.Stopwatch.StartNew();
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

            // album started metric
            try { _metrics?.IncrementCounter("qobuz_album_started_total", 1); } catch { }

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
                    if (ex.Reason == TrackUnavailableReason.PreviewOnly || ex.Reason == TrackUnavailableReason.RegionalRestriction)
                    {
                        Interlocked.Increment(ref skippedTracks);
                        var propsSkip = new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["reason"] = ex.Reason.ToString(),
                            ["label"] = album?.Label?.Name ?? "unknown",
                            ["isrc"] = track?.ISRC ?? ""
                        };
                        _logger.WarnEvent(LoggingEvents.QobuzRightsSkip, propsSkip, "Skipping track {0} ({1}): {2}", track.GetFullTitle(), track.Id, ex.GetUserFriendlyMessage());
                        try
                        {
                            var labels = new System.Collections.Generic.Dictionary<string, string>
                            {
                                ["reason"] = ex.Reason.ToString(),
                                ["label"] = album?.Label?.Name ?? "unknown"
                            };
                            _metrics?.IncrementCounter("qobuz_rights_skip_total", 1, labels);
                        }
                        catch { }
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

            if (_queueService.ActiveDownloadCount == 0)
            {
                var summaryReport = _downloadSummary.GenerateReport();
                _logger.Info(summaryReport);
            }

            var policy = settings.GetDownloadPolicy();
            var isSuccessful = policy.IsAlbumDownloadSuccessful(totalTracks, successfulTracks, skippedTracks);

            // album-level metrics
            albumStopwatch.Stop();
            try
            {
                _metrics?.RecordHistogram("qobuz_album_duration_seconds", albumStopwatch.Elapsed.TotalSeconds);
                _metrics?.RecordHistogram("qobuz_album_size_bytes", bytesDownloaded);
                if (isSuccessful)
                {
                    if (skippedTracks == 0 && failedTracks == 0)
                    {
                        _metrics?.IncrementCounter("qobuz_album_completed_total", 1);
                    }
                    else
                    {
                        _metrics?.IncrementCounter("qobuz_album_partial_total", 1);
                    }
                }
                else
                {
                    _metrics?.IncrementCounter("qobuz_album_failed_total", 1);
                }
            }
            catch { }

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
            var outputPath = Path.Combine(downloadItem.OutputPath, $"{track.TrackNumber:00} - {track.Title}.flac");
            try
            {
                _logger.Info("Downloading track: {0} to {1}", track.Title, outputPath);

                // 1. Get streaming URL with fallback and rights awareness
                var chain = BuildPreferredChain(settings, _qualityFallbackProvider);
                var probe = await _streamUrlProvider.TryGetStreamUrlAsync(track.Id, chain, cancellationToken).ConfigureAwait(false);

                string? streamUrl = probe.Url;
                int? chosenFormatId = probe.FormatId;
                if (!probe.Success || string.IsNullOrWhiteSpace(streamUrl))
                {
                    // Attempt alternate resolution only for rights/territory cases
                    if (settings.ResolveAlternates && (probe.Reason == TrackUnavailableReason.PreviewOnly || probe.Reason == TrackUnavailableReason.RegionalRestriction))
                    {
                        var altId = await _alternateResolver.ResolvePlayableTrackIdAsync(track, probe.Reason, Math.Max(1, settings.AlternateProbeLimit), TimeSpan.FromHours(Math.Max(1, settings.NegativeCacheTtlHours)), cancellationToken).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(altId))
                        {
                            var altProbe = await _streamUrlProvider.TryGetStreamUrlAsync(altId!, chain, cancellationToken).ConfigureAwait(false);
                            if (altProbe.Success && !string.IsNullOrWhiteSpace(altProbe.Url))
                            {
                                streamUrl = altProbe.Url;
                                chosenFormatId = altProbe.FormatId ?? chosenFormatId;
                                try { _metrics?.IncrementCounter("qobuz_altresolve_success_total", 1); } catch { }
                                var propsAlt = new System.Collections.Generic.Dictionary<string, object>
                                {
                                    ["track_id"] = track.Id,
                                    ["alt_id"] = altId,
                                    ["format"] = (chosenFormatId?.ToString() ?? "")
                                };
                                _logger.InfoEvent(LoggingEvents.QobuzAltResolveHit, propsAlt, "Resolved alternate playable edition for track {0} \u001a {1}", track.Id, altId);
                            }
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(streamUrl))
                {
                    var reason = probe.Reason;
                    if (reason == TrackUnavailableReason.Unknown)
                    {
                        reason = TrackUnavailableReason.NoQualityAvailable;
                    }
                    try
                    {
                        var tried = string.Join(",", chain);
                        var propsTried = new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["reason"] = reason.ToString(),
                            ["formats_tried"] = tried,
                            ["label"] = album?.Label?.Name ?? "unknown",
                            ["isrc"] = track?.ISRC ?? ""
                        };
                        _logger.WarnEvent(LoggingEvents.QobuzRightsSkip, propsTried, "Skip: {0} ({1}) — {2}. Tried formats [{3}]", track.GetFullTitle(), track.Id, reason, tried);
                    }
                    catch { }
                    throw new TrackUnavailableException(track.Id, probe.Detail ?? "No playable formats", reason);
                }

                _logger.Debug("Got streaming URL for track {0}", track.Id);

                // 2. Ensure output directory
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir)) _diskProvider.EnsureFolder(dir);

                // 3. Download to disk
                _logger.Debug("Starting HTTP download for track {0}", track.Title);
                var bytesWritten = await DownloadToFileAsync(streamUrl, outputPath, cancellationToken).ConfigureAwait(false);
                _logger.Debug("Audio file written: {0} bytes", bytesWritten);
                try
                {
                    if (chosenFormatId.HasValue)
                    {
                        _metrics?.IncrementCounter("qobuz_download_success_total", 1,
                            new System.Collections.Generic.Dictionary<string, string> { ["format"] = chosenFormatId.Value.ToString() });
                    }
                    _metrics?.RecordHistogram("qobuz_download_size_bytes", bytesWritten,
                        chosenFormatId.HasValue ? new System.Collections.Generic.Dictionary<string, string> { ["format"] = chosenFormatId.Value.ToString() } : null);
                }
                catch { }

                // 4. Apply tags
                await ApplyMetadataTagsAsync(outputPath, track, album).ConfigureAwait(false);

                var propsSuccess = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["format"] = (chosenFormatId?.ToString() ?? ""),
                    ["bytes"] = bytesWritten,
                    ["label"] = album?.Label?.Name ?? "unknown",
                    ["isrc"] = track?.ISRC ?? ""
                };
                _logger.InfoEvent(LoggingEvents.QobuzDownloadSuccess, propsSuccess, "Downloaded: {0} ({1:F1} MB)", track.Title, bytesWritten / 1024.0 / 1024.0);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Download failed for track: {0}", track.Title);
                if (FileExistsHelper(outputPath))
                {
                    try { DeleteFileHelper(outputPath); } catch { }
                }
                throw;
            }

            // Basic file presence validation
            if (!FileExistsHelper(outputPath) || GetFileLengthHelper(outputPath) < 1024)
            {
                throw new InvalidOperationException($"Downloaded file validation failed: {track.Title}");
            }
        }

        private async Task<long> DownloadToFileAsync(string url, string filePath, CancellationToken cancellationToken)
        {
            var httpClient = SharedSystemHttpClient.Instance;
            var partialPath = filePath + ".partial";
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) _diskProvider.EnsureFolder(directory);

            long existing = 0;
            if (_diskProvider.FileExists(partialPath))
            {
                try { existing = _diskProvider.GetFileInfo(partialPath).Length; } catch { existing = 0; }
            }

            // Resilient download with Retry-After and jittered backoff
            var deadline = DateTime.UtcNow.AddSeconds(90);
            var attempt = 0;
            var totalWritten = existing;
            var buffer = new byte[131072];

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempt++;

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (totalWritten > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(totalWritten, null);
                }

                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    // Handle transient codes with Retry-After/backoff
                    if (IsTransientStatus((int)response.StatusCode))
                    {
                        var delay = GetRetryAfter(response) ?? ComputeBackoff(attempt);
                        if (DateTime.UtcNow + delay > deadline) response.EnsureSuccessStatusCode();
                        _logger.Debug("Download retry {0}: HTTP {1}; waiting {2}ms", attempt, (int)response.StatusCode, (int)delay.TotalMilliseconds);
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    response.EnsureSuccessStatusCode();
                }

                var isPartial = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
                if (!isPartial && _diskProvider.FileExists(partialPath) && totalWritten > 0)
                {
                    try { _diskProvider.DeleteFile(partialPath); } catch { }
                    totalWritten = 0;
                }

                await using (var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var fileStream = new FileStream(partialPath, FileMode.Append, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true))
                {
                    int read;
                    while ((read = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        totalWritten += read;
                    }
                    await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                // Completed this attempt successfully, finalize
                try { _diskProvider.MoveFile(partialPath, filePath, overwrite: true); }
                catch { System.IO.File.Move(partialPath, filePath, overwrite: true); }

            if (!Lidarr.Plugin.Common.Utilities.ValidationUtilities.ValidateDownloadedFile(filePath))
            {
                throw new InvalidOperationException($"Downloaded file failed validation: {Path.GetFileName(filePath)}");
            }
            return totalWritten;
        }
        }

        private static bool IsTransientStatus(int code) => code == 408 || code == 429 || code == 500 || code == 502 || code == 503 || code == 504;

        private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
        {
            try
            {
                if (response.Headers.TryGetValues("Retry-After", out var values))
                {
                    var v = values.FirstOrDefault();
                    if (int.TryParse(v, out var seconds)) return TimeSpan.FromSeconds(Math.Max(0, seconds));
                    if (DateTimeOffset.TryParse(v, out var when))
                    {
                        var delta = when - DateTimeOffset.UtcNow;
                        if (delta > TimeSpan.Zero) return delta;
                    }
                }
            }
            catch { }
            return null;
        }

        private static TimeSpan ComputeBackoff(int attempt)
        {
            var baseSeconds = Math.Min(30, Math.Pow(2, attempt));
            var jitterMs = new Random().Next(100, 600);
            return TimeSpan.FromSeconds(baseSeconds) + TimeSpan.FromMilliseconds(jitterMs);
        }

        private static List<int> BuildPreferredChain(QobuzDownloadSettings settings, IQualityFallbackProvider fallback)
        {
            var list = new List<int>();
            if (!string.IsNullOrWhiteSpace(settings.PreferredFormatsCsv))
            {
                var parts = settings.PreferredFormatsCsv.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                {
                    if (int.TryParse(p, out var id)) list.Add(id);
                }
            }
            if (list.Count == 0)
            {
                list.Add(settings.PreferredQuality);
                list.AddRange(fallback.GetFallbackQualities(settings.PreferredQuality));
            }
            // De-dupe, keep order
            var seen = new HashSet<int>();
            var ordered = new List<int>();
            foreach (var id in list)
            {
                if (seen.Add(id)) ordered.Add(id);
            }
            return ordered;
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
                _logger.InfoEvent(LoggingEvents.QobuzAlbumSummary, new System.Collections.Generic.Dictionary<string, object>{{"successful", successful},{"skipped", skipped},{"failed", failed},{"total", total},{"size_bytes", bytesDownloaded}}, "Album summary: {0}", string.Join(" \u0007 ", parts));
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













