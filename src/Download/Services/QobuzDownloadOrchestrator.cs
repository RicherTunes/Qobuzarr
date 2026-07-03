using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Abstractions.Models;
using Lidarr.Plugin.Common.Interfaces;
using Lidarr.Plugin.Common.Services.Download;
using Lidarr.Plugin.Common.Utilities;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Microsoft.Extensions.Logging;
// The enclosing Lidarr.Plugin.Qobuzarr.Download namespace defines its own TrackDownloadResult, which
// shadows Common's. Alias Common's result type so the orchestrator result mapping is unambiguous.
using CommonTrackDownloadResult = Lidarr.Plugin.Common.Interfaces.TrackDownloadResult;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Qobuz adoption of Common's <see cref="SimpleDownloadOrchestrator"/> (Wave B). The base orchestrator
    /// owns the robust per-track URL download engine — SSRF guard, retry-with-resume on transient
    /// truncation, atomic move, post-processing (lyrics) and metadata tagging seams — which qobuz now
    /// shares with tidal/amazon/apple instead of maintaining its own per-track loop.
    ///
    /// <para><see cref="DownloadAlbumAsync(string, string, StreamingQuality, IProgress{DownloadProgress}, CancellationToken)"/>
    /// is overridden (rather than relying on the base album loop) for two behaviours the current Common pin
    /// has no seam for: (1) Qobuz's <see cref="TrackFileNameBuilder"/> filename (multi-disc aware, correct
    /// extension), and (2) the post-download audio-payload validation
    /// (<see cref="DownloadPayloadValidator"/> + <see cref="ValidationUtilities"/> + the 1&#160;KiB floor)
    /// that must fail the track. Both reuse the base per-track engine via
    /// <see cref="SimpleDownloadOrchestrator.DownloadTrackAsync(string, string, StreamingQuality, CancellationToken)"/>.
    /// A future Common change can lift the filename + payload seams down and let this class shrink to a thin
    /// naming/validation override over the shared album loop.</para>
    /// </summary>
    public class QobuzDownloadOrchestrator : SimpleDownloadOrchestrator
    {
        private readonly QobuzAlbum _album;
        private readonly int _maxConcurrentTracks;
        private readonly int _namingFormatId;

        public QobuzDownloadOrchestrator(
            HttpClient httpClient,
            Func<string, Task<StreamingAlbum>> getAlbumAsync,
            Func<string, Task<StreamingTrack>> getTrackAsync,
            Func<string, Task<IReadOnlyList<string>>> getAlbumTrackIdsAsync,
            Func<string, StreamingQuality?, Task<(string Url, string Extension)>> getStreamAsync,
            int maxConcurrentTracks,
            QobuzAlbum album,
            int namingFormatId,
            IAudioMetadataApplier metadataApplier,
            IAudioPostProcessor postProcessor,
            RemoteMediaUriPolicy? mediaUriPolicy = null,
            ILogger? logger = null)
            : base(
                "Qobuz",
                httpClient,
                getAlbumAsync,
                getTrackAsync,
                getAlbumTrackIdsAsync,
                getStreamAsync,
                maxConcurrentTracks,
                streamProvider: null,
                metadataApplier: metadataApplier,
                logger: logger,
                postProcessor: postProcessor,
                mediaUriPolicy: mediaUriPolicy)
        {
            _album = album ?? throw new ArgumentNullException(nameof(album));
            _maxConcurrentTracks = Math.Max(1, maxConcurrentTracks);
            _namingFormatId = namingFormatId;
        }

        public override async Task<DownloadResult> DownloadAlbumAsync(
            string albumId,
            string outputDirectory,
            StreamingQuality quality,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tracks = _album.GetTracks();
            var started = DateTime.UtcNow;
            var result = new DownloadResult { Success = true, Duration = TimeSpan.Zero };
            var total = tracks.Count;

            if (total == 0)
            {
                result.Success = false;
                result.ErrorMessage = $"No tracks returned for album {albumId}";
                result.FilePaths = new List<string>();
                result.TotalSize = 0;
                result.Duration = DateTime.UtcNow - started;
                return result;
            }

            Directory.CreateDirectory(outputDirectory);

            var files = new List<string>();
            var resultsLock = new object();
            int completed = 0;

            using var semaphore = new SemaphoreSlim(_maxConcurrentTracks, _maxConcurrentTracks);

            var tasks = tracks.Select(async qTrack =>
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var outputPath = BuildTrackOutputPath(outputDirectory, qTrack, _album.MediaCount, _namingFormatId);

                    // Base engine: stream-URL resolution (via getStream), SSRF guard, retry-with-resume,
                    // atomic move, lyrics post-processing, and metadata tagging.
                    var tr = await DownloadTrackAsync(qTrack.Id, outputPath, quality, cancellationToken).ConfigureAwait(false);

                    // Qobuz audio-payload validation (the base engine only checks for an empty file).
                    if (tr.Success && !string.IsNullOrEmpty(tr.FilePath))
                    {
                        try
                        {
                            ValidateDownloadedPayloadOrThrow(tr.FilePath);
                        }
                        catch (Exception ex)
                        {
                            TryDeleteFile(tr.FilePath);
                            tr = new CommonTrackDownloadResult
                            {
                                TrackId = tr.TrackId,
                                Success = false,
                                ErrorMessage = ex.Message,
                            };
                        }
                    }

                    lock (resultsLock)
                    {
                        result.TrackResults.Add(tr);
                        if (tr.Success && !string.IsNullOrEmpty(tr.FilePath))
                        {
                            files.Add(tr.FilePath);
                        }
                    }

                    var done = Interlocked.Increment(ref completed);
                    progress?.Report(new DownloadProgress
                    {
                        CompletedTracks = done,
                        TotalTracks = total,
                        PercentComplete = (double)done / total * 100,
                        CurrentTrack = qTrack.Title,
                    });
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks).ConfigureAwait(false);

            var successful = result.TrackResults.Count(r => r.Success);
            // Mirror Common's AlbumCompletionPolicy (every track must land). The caller
            // (TrackDownloadService) applies the canonical Qobuz DownloadPolicy and throws
            // AlbumDownloadException on an incomplete album, so the host reports Failed.
            result.Success = successful == total;
            result.FilePaths = files;
            result.TotalSize = files.Where(File.Exists).Select(f => new FileInfo(f).Length).Sum();
            result.Duration = DateTime.UtcNow - started;
            return result;
        }

        /// <summary>
        /// Builds the per-track output path via <see cref="TrackFileNameBuilder"/> (multi-disc aware,
        /// special-character safe). The extension is provisional — the base engine overwrites it with the
        /// actual returned format's extension via <c>Path.ChangeExtension</c> using <c>getStream</c>'s value,
        /// matching the prior bespoke behaviour where the filename's extension followed the resolved format.
        /// </summary>
        internal static string BuildTrackOutputPath(string outputDirectory, QobuzTrack track, int mediaCount, int namingFormatId)
        {
            var filename = TrackFileNameBuilder.Build(track.TrackNumber, track.Title, namingFormatId, track.DiscNumber, mediaCount);
            return Path.Combine(outputDirectory, filename);
        }

        /// <summary>
        /// Replicates the prior bespoke audio-payload validation: magic-byte / text-sniff guard
        /// (<see cref="DownloadPayloadValidator.ValidateFileOrThrow"/>), the generic file check
        /// (<see cref="ValidationUtilities.ValidateDownloadedFile"/>), and the 1&#160;KiB minimum-size floor.
        /// Throws when the payload isn't a plausible non-trivial audio file.
        /// </summary>
        internal static void ValidateDownloadedPayloadOrThrow(string filePath)
        {
            DownloadPayloadValidator.ValidateFileOrThrow(filePath);

            if (!ValidationUtilities.ValidateDownloadedFile(filePath))
            {
                throw new InvalidOperationException($"Downloaded file failed validation: {Path.GetFileName(filePath)}");
            }

            var info = new FileInfo(filePath);
            if (!info.Exists || info.Length < 1024)
            {
                throw new InvalidOperationException($"Downloaded file validation failed: {Path.GetFileName(filePath)}");
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
