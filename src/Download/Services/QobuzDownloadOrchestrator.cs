using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Lidarr.Plugin.Abstractions.Models;
using Lidarr.Plugin.Common.Interfaces;
using Lidarr.Plugin.Common.Services.Download;
using Lidarr.Plugin.Common.Utilities;
using Lidarr.Plugin.Qobuzarr.Models;
using Lidarr.Plugin.Qobuzarr.Utilities;
using Microsoft.Extensions.Logging;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Qobuz adoption of Common's <see cref="SimpleDownloadOrchestrator"/>. The base orchestrator owns the
    /// whole download path — the shared album loop (bounded concurrency, AlbumCompletionPolicy) and the
    /// robust per-track URL engine (SSRF guard, retry-with-resume on transient truncation, atomic move,
    /// post-processing, metadata tagging, artwork embedding) — which qobuz shares with tidal/amazon/apple.
    ///
    /// <para>Qobuz customizes exactly two seams instead of overriding the loop:
    /// <see cref="BuildTrackOutputPath(string, StreamingTrack)"/> names files via
    /// <see cref="TrackFileNameBuilder"/> (multi-disc aware, naming-format setting), and
    /// <see cref="ValidateDownloadedPayload(string, StreamingTrack)"/> applies the post-download
    /// audio-payload validation (<see cref="DownloadPayloadValidator"/> magic-byte sniff +
    /// <see cref="ValidationUtilities"/> + the 1&#160;KiB floor). A validation throw makes the base engine
    /// delete the payload and record the track failed, feeding the incomplete⇒Failed album contract.</para>
    /// </summary>
    public class QobuzDownloadOrchestrator : SimpleDownloadOrchestrator
    {
        private readonly QobuzAlbum _album;
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
            _namingFormatId = namingFormatId;
        }

        /// <summary>
        /// Names tracks via <see cref="TrackFileNameBuilder"/> using the authoritative
        /// <see cref="QobuzTrack"/> (disc number, media count) resolved from the album by id. The extension
        /// is provisional — the base engine overwrites it with the resolved format's extension.
        /// </summary>
        protected override string BuildTrackOutputPath(string outputDirectory, StreamingTrack? track)
        {
            var qobuzTrack = _album.GetTracks().FirstOrDefault(t => t.Id == track?.Id);
            if (qobuzTrack == null)
            {
                return base.BuildTrackOutputPath(outputDirectory, track);
            }

            return BuildTrackOutputPath(outputDirectory, qobuzTrack, _album.MediaCount, _namingFormatId);
        }

        /// <summary>
        /// Rejects non-audio payloads (HTML soft-404s, truncated stubs) on every download path —
        /// the base engine deletes the file and records the track as failed.
        /// </summary>
        protected override void ValidateDownloadedPayload(string filePath, StreamingTrack? track)
        {
            ValidateDownloadedPayloadOrThrow(filePath);
        }

        /// <summary>
        /// Builds the per-track output path via <see cref="TrackFileNameBuilder"/> (multi-disc aware,
        /// special-character safe).
        /// </summary>
        internal static string BuildTrackOutputPath(string outputDirectory, QobuzTrack track, int mediaCount, int namingFormatId)
        {
            var filename = TrackFileNameBuilder.Build(track.TrackNumber, track.Title, namingFormatId, track.DiscNumber, mediaCount);
            return Path.Combine(outputDirectory, filename);
        }

        /// <summary>
        /// The Qobuz audio-payload validation: magic-byte / text-sniff guard
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
    }
}
