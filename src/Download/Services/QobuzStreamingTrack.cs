using System;
using Lidarr.Plugin.Abstractions.Models;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Lidarr.Plugin.Qobuzarr.Download.Services
{
    /// <summary>
    /// A <see cref="StreamingTrack"/> that carries the original Qobuz <see cref="QobuzTrack"/> and
    /// <see cref="QobuzAlbum"/> models alongside the Common-shaped fields the
    /// <see cref="Lidarr.Plugin.Common.Services.Download.SimpleDownloadOrchestrator"/> consumes.
    ///
    /// <para>Wave B (orchestrator adoption) routes downloads through Common's
    /// <c>SimpleDownloadOrchestrator</c>, which hands the post-processor / metadata-applier seams a
    /// <see cref="StreamingTrack"/>. Qobuz's tagging (label comment, composers, performer) and lyrics
    /// enrichment need the rich Qobuz models, so this carrier preserves them by reference rather than
    /// flattening to the generic shape — guaranteeing byte-for-byte tag parity with the previous
    /// bespoke <c>ApplyMetadataTagsAsync</c> path.</para>
    /// </summary>
    public sealed class QobuzStreamingTrack : StreamingTrack
    {
        /// <summary>The original Qobuz track model (source of title/track/disc/performer/composer).</summary>
        public QobuzTrack QobuzTrack { get; }

        /// <summary>The owning Qobuz album model (source of album/artist/year/genre/label).</summary>
        public QobuzAlbum QobuzAlbum { get; }

        private QobuzStreamingTrack(QobuzTrack track, QobuzAlbum album)
        {
            QobuzTrack = track ?? throw new ArgumentNullException(nameof(track));
            QobuzAlbum = album ?? throw new ArgumentNullException(nameof(album));
        }

        /// <summary>
        /// Builds a carrier from the Qobuz models, populating the generic <see cref="StreamingTrack"/>
        /// fields so any orchestrator-side logic that reads them (telemetry, progress labels) still works.
        /// </summary>
        public static QobuzStreamingTrack From(QobuzTrack track, QobuzAlbum album)
        {
            if (track == null) throw new ArgumentNullException(nameof(track));
            if (album == null) throw new ArgumentNullException(nameof(album));

            var carrier = new QobuzStreamingTrack(track, album)
            {
                Id = track.Id ?? string.Empty,
                Title = track.Title ?? string.Empty,
                TrackNumber = track.TrackNumber,
                DiscNumber = track.DiscNumber,
                Duration = TimeSpan.FromSeconds(Math.Max(0, track.DurationSeconds)),
            };

            if (track.Performer?.Name != null)
            {
                carrier.Artist = new StreamingArtist { Id = track.Performer.Id ?? string.Empty, Name = track.Performer.Name };
            }

            carrier.Album = new StreamingAlbum
            {
                Id = album.Id ?? string.Empty,
                Title = album.Title ?? string.Empty,
                Artist = new StreamingArtist { Name = album.Artist?.Name ?? string.Empty },
            };

            return carrier;
        }
    }
}
