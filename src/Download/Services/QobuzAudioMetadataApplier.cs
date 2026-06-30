using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Abstractions.Models;
using Lidarr.Plugin.Common.Interfaces;
using NLog;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// Qobuz metadata applier used by Common's <c>SimpleDownloadOrchestrator</c> (Wave B). Replicates,
    /// field-for-field, the tags previously written by <c>TrackDownloadService.ApplyMetadataTagsAsync</c>
    /// so the orchestrator path is byte-for-byte tag-compatible with the prior bespoke download loop.
    ///
    /// <para>The default <see cref="Lidarr.Plugin.Common.Services.Metadata"/> TagLib applier is NOT
    /// equivalent — it omits the Qobuz label comment and composer tags and derives performers/album-artists
    /// differently — so Qobuz supplies this custom applier. It reads the rich Qobuz models off the
    /// <see cref="QobuzStreamingTrack"/> carrier handed in by the orchestrator.</para>
    ///
    /// <para>Tagging is best-effort: a failure here must never fail a download (mirrors the original, which
    /// wrapped the TagLib work in <c>Task.Run</c> + a warn-only catch). The orchestrator additionally
    /// swallows applier exceptions, so this is defense-in-depth.</para>
    /// </summary>
    public sealed class QobuzAudioMetadataApplier : IAudioMetadataApplier
    {
        private readonly Logger _logger;

        public QobuzAudioMetadataApplier(Logger? logger = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
        }

        public Task ApplyAsync(string filePath, StreamingTrack metadata, CancellationToken cancellationToken = default)
        {
            // Only the Qobuz carrier exposes the rich models needed for full-fidelity tagging.
            if (metadata is not QobuzStreamingTrack carrier || string.IsNullOrWhiteSpace(filePath))
            {
                return Task.CompletedTask;
            }

            var track = carrier.QobuzTrack;
            var album = carrier.QobuzAlbum;

            return Task.Run(() =>
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
            }, cancellationToken);
        }
    }
}
