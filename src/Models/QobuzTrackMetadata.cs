using System;
using System.Collections.Generic;

namespace Lidarr.Plugin.Qobuzarr.Models
{
    /// <summary>
    /// Contains comprehensive metadata information for a Qobuz track.
    /// Used for applying tags to downloaded audio files.
    /// </summary>
    public class QobuzTrackMetadata
    {
        /// <summary>
        /// Gets or sets the track title.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the primary artist name.
        /// </summary>
        public string Artist { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the album title.
        /// </summary>
        public string Album { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the album artist name (may differ from track artist for compilations).
        /// </summary>
        public string AlbumArtist { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the track number within the album.
        /// </summary>
        public int? TrackNumber { get; set; }

        /// <summary>
        /// Gets or sets the disc number for multi-disc albums.
        /// </summary>
        public int? DiscNumber { get; set; }

        /// <summary>
        /// Gets or sets the release year.
        /// </summary>
        public int? Year { get; set; }

        /// <summary>
        /// Gets or sets the primary genre.
        /// </summary>
        public string Genre { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the list of all applicable genres.
        /// </summary>
        public List<string> Genres { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the track duration.
        /// </summary>
        public TimeSpan? Duration { get; set; }

        /// <summary>
        /// Gets or sets additional comments or notes about the track.
        /// </summary>
        public string Comment { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the composer name(s).
        /// </summary>
        public string Composer { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the conductor name (primarily for classical music).
        /// </summary>
        public string Conductor { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the copyright information.
        /// </summary>
        public string Copyright { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the record label name.
        /// </summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the International Standard Recording Code.
        /// </summary>
        public string ISRC { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the Universal Product Code for the album.
        /// </summary>
        public string UPC { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether the track contains explicit content.
        /// </summary>
        public bool IsExplicit { get; set; }
    }
}
