using System;

namespace Lidarr.Plugin.Qobuzarr.Models
{
    /// <summary>
    /// Represents a track prepared for download with optimized metadata from intelligent processing
    /// Contains streaming URL and comprehensive metadata from either Lidarr, Qobuz, or hybrid sources
    /// </summary>
    public class TrackDownload
    {
        /// <summary>
        /// Qobuz streaming URL for audio download
        /// </summary>
        public string StreamingUrl { get; set; }

        /// <summary>
        /// Original Qobuz track ID for reference
        /// </summary>
        public int? QobuzTrackId { get; set; }

        #region Basic Track Information

        /// <summary>
        /// Track title
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Primary artist name
        /// </summary>
        public string Artist { get; set; }

        /// <summary>
        /// Album artist name (may differ from track artist for compilations)
        /// </summary>
        public string AlbumArtist { get; set; }

        /// <summary>
        /// Album title
        /// </summary>
        public string Album { get; set; }

        /// <summary>
        /// Track number within the disc
        /// </summary>
        public int? TrackNumber { get; set; }

        /// <summary>
        /// Disc number for multi-disc albums
        /// </summary>
        public int? DiscNumber { get; set; }

        /// <summary>
        /// Track duration
        /// </summary>
        public TimeSpan? Duration { get; set; }

        #endregion

        #region Release Information

        /// <summary>
        /// Album release date
        /// </summary>
        public DateTime? ReleaseDate { get; set; }

        /// <summary>
        /// Genre information (comma-separated if multiple)
        /// </summary>
        public string Genre { get; set; }

        /// <summary>
        /// Record label
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// Album type (e.g., Album, EP, Single, Compilation)
        /// </summary>
        public string AlbumType { get; set; }

        /// <summary>
        /// Country of release
        /// </summary>
        public string Country { get; set; }

        #endregion

        #region Additional Credits

        /// <summary>
        /// Composer name
        /// </summary>
        public string Composer { get; set; }

        #endregion

        #region MusicBrainz Identifiers (from Lidarr)

        /// <summary>
        /// MusicBrainz track ID for precise identification
        /// </summary>
        public string MusicBrainzTrackId { get; set; }

        /// <summary>
        /// MusicBrainz album/release ID
        /// </summary>
        public string MusicBrainzAlbumId { get; set; }

        /// <summary>
        /// MusicBrainz artist ID
        /// </summary>
        public string MusicBrainzArtistId { get; set; }

        /// <summary>
        /// MusicBrainz release group ID
        /// </summary>
        public string MusicBrainzReleaseGroupId { get; set; }

        #endregion

        #region Audio Quality Information

        /// <summary>
        /// Audio quality format (e.g., "FLAC 24/96", "MP3 320")
        /// </summary>
        public string Quality { get; set; }

        /// <summary>
        /// Bit rate in kbps
        /// </summary>
        public int? BitRate { get; set; }

        /// <summary>
        /// Sample rate in Hz
        /// </summary>
        public int? SampleRate { get; set; }

        /// <summary>
        /// Bit depth (for lossless formats)
        /// </summary>
        public int? BitDepth { get; set; }

        #endregion

        #region Metadata Source Information

        /// <summary>
        /// Source of metadata (e.g., "Lidarr", "Qobuz", "Lidarr+Qobuz")
        /// </summary>
        public string MetadataSource { get; set; }

        #endregion

        /// <summary>
        /// Gets a display-friendly representation of the track
        /// </summary>
        public string GetDisplayTitle()
        {
            var trackNum = TrackNumber?.ToString("D2") ?? "??";
            return $"{trackNum}. {Artist} - {Title}";
        }

        /// <summary>
        /// Gets comprehensive quality information as a string
        /// </summary>
        public string GetQualityInfo()
        {
            if (string.IsNullOrWhiteSpace(Quality))
                return "Unknown quality";

            var info = Quality;
            
            if (BitRate.HasValue)
                info += $" ({BitRate}kbps)";
            
            if (SampleRate.HasValue && BitDepth.HasValue)
                info += $" - {SampleRate}Hz/{BitDepth}bit";
            else if (SampleRate.HasValue)
                info += $" - {SampleRate}Hz";

            return info;
        }

        /// <summary>
        /// Validates that essential information is present for download
        /// </summary>
        public bool IsValidForDownload()
        {
            return !string.IsNullOrWhiteSpace(StreamingUrl) &&
                   !string.IsNullOrWhiteSpace(Title) &&
                   !string.IsNullOrWhiteSpace(Artist) &&
                   !string.IsNullOrWhiteSpace(Album);
        }

        /// <summary>
        /// Gets metadata completeness score (0.0 to 1.0)
        /// </summary>
        public double GetMetadataCompleteness()
        {
            int totalFields = 20; // Total number of metadata fields
            int completedFields = 0;

            // Required fields (weighted more heavily)
            if (!string.IsNullOrWhiteSpace(Title)) completedFields += 2;
            if (!string.IsNullOrWhiteSpace(Artist)) completedFields += 2;
            if (!string.IsNullOrWhiteSpace(Album)) completedFields += 2;
            if (TrackNumber.HasValue) completedFields += 2;

            // Standard fields
            if (!string.IsNullOrWhiteSpace(AlbumArtist)) completedFields++;
            if (DiscNumber.HasValue) completedFields++;
            if (Duration.HasValue) completedFields++;
            if (ReleaseDate.HasValue) completedFields++;
            if (!string.IsNullOrWhiteSpace(Genre)) completedFields++;
            if (!string.IsNullOrWhiteSpace(Label)) completedFields++;
            if (!string.IsNullOrWhiteSpace(Quality)) completedFields++;

            // Enhanced fields (MusicBrainz identifiers)
            if (!string.IsNullOrWhiteSpace(MusicBrainzTrackId)) completedFields++;
            if (!string.IsNullOrWhiteSpace(MusicBrainzAlbumId)) completedFields++;
            if (!string.IsNullOrWhiteSpace(MusicBrainzArtistId)) completedFields++;

            // Additional fields
            if (!string.IsNullOrWhiteSpace(Composer)) completedFields++;
            if (!string.IsNullOrWhiteSpace(AlbumType)) completedFields++;
            if (BitRate.HasValue) completedFields++;
            if (SampleRate.HasValue) completedFields++;

            return Math.Min(1.0, (double)completedFields / totalFields);
        }

        /// <summary>
        /// Creates a copy of this TrackDownload with the same metadata
        /// </summary>
        public TrackDownload Clone()
        {
            return new TrackDownload
            {
                StreamingUrl = StreamingUrl,
                QobuzTrackId = QobuzTrackId,
                Title = Title,
                Artist = Artist,
                AlbumArtist = AlbumArtist,
                Album = Album,
                TrackNumber = TrackNumber,
                DiscNumber = DiscNumber,
                Duration = Duration,
                ReleaseDate = ReleaseDate,
                Genre = Genre,
                Label = Label,
                AlbumType = AlbumType,
                Country = Country,
                Composer = Composer,
                MusicBrainzTrackId = MusicBrainzTrackId,
                MusicBrainzAlbumId = MusicBrainzAlbumId,
                MusicBrainzArtistId = MusicBrainzArtistId,
                MusicBrainzReleaseGroupId = MusicBrainzReleaseGroupId,
                Quality = Quality,
                BitRate = BitRate,
                SampleRate = SampleRate,
                BitDepth = BitDepth,
                MetadataSource = MetadataSource
            };
        }
    }
}