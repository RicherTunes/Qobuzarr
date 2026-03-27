using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;
using Lidarr.Plugin.Qobuzarr.Security;

namespace Lidarr.Plugin.Qobuzarr.Models
{
    /// <summary>
    /// Represents a music track from the Qobuz API with comprehensive metadata.
    /// Contains all information needed for downloading, metadata embedding, and catalog integration.
    /// </summary>
    /// <remarks>
    /// This model maps directly to Qobuz API track responses and includes:
    /// - Basic track information (title, duration, track number)
    /// - Performer and composer details for classical music
    /// - Technical quality specifications (bit depth, sample rate)
    /// - Copyright and licensing information
    /// - Purchase and streaming availability status
    /// 
    /// The class provides helper methods for safe access to potentially null properties
    /// and formatting methods for display and filename generation.
    /// </remarks>
    public class QobuzTrack
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("track_number")]
        public int TrackNumber { get; set; }

        [JsonProperty("media_number")]
        public int DiscNumber { get; set; } = 1;

        [JsonProperty("duration")]
        public int DurationSeconds { get; set; }

        [JsonProperty("performer")]
        public QobuzArtist Performer { get; set; }

        [JsonProperty("performers")]
        public string Performers { get; set; }

        [JsonProperty("composer")]
        public QobuzComposer Composer { get; set; }

        [JsonProperty("isrc")]
        public string ISRC { get; set; }

        [JsonProperty("copyright")]
        public string Copyright { get; set; }

        [JsonProperty("streamable")]
        public bool Streamable { get; set; }

        [JsonProperty("downloadable")]
        public bool Downloadable { get; set; }

        [JsonProperty("purchasable")]
        public bool Purchasable { get; set; }

        [JsonProperty("sampleable")]
        public bool Sampleable { get; set; }

        [JsonProperty("previewable")]
        public bool Previewable { get; set; }

        [JsonProperty("maximum_bit_depth")]
        public int? MaximumBitDepth { get; set; }

        [JsonProperty("maximum_sampling_rate")]
        public double? MaximumSampleRate { get; set; }

        [JsonProperty("maximum_channel_count")]
        public int? MaximumChannelCount { get; set; }

        [JsonProperty("parental_warning")]
        public bool ParentalWarning { get; set; }

        [JsonProperty("work")]
        public string Work { get; set; }

        [JsonProperty("part")]
        public string Part { get; set; }

        [JsonProperty("album")]
        public QobuzAlbum Album { get; set; }

        /// <summary>
        /// Album artist name for compatibility
        /// </summary>
        public string AlbumArtistName => Album?.Artist?.Name ?? "Various Artists";

        /// <summary>
        /// Album title for compatibility
        /// </summary>
        public string AlbumTitle => Album?.Title ?? string.Empty;

        /// <summary>
        /// Get track duration as TimeSpan
        /// </summary>
        public TimeSpan Duration => TimeSpan.FromSeconds(DurationSeconds);

        /// <summary>
        /// Get quality description based on maximum quality available
        /// </summary>
        public string Quality
        {
            get
            {
                if (MaximumSampleRate > 48000 || MaximumBitDepth > 16)
                    return "Hi-Res";
                return MaximumBitDepth > 0 ? "Lossless" : "Lossy";
            }
        }

        /// <summary>
        /// Get bitrate estimate based on quality (for compatibility)
        /// </summary>
        public int BitRate
        {
            get
            {
                if (MaximumSampleRate > 48000 && MaximumBitDepth >= 24)
                    return 4608; // Hi-Res estimate
                if (MaximumBitDepth >= 16)
                    return 1411; // CD quality
                return 320; // MP3 fallback
            }
        }

        /// <summary>
        /// Get sample rate (alias for MaximumSampleRate for compatibility)
        /// </summary>
        public double? SampleRate => MaximumSampleRate;

        /// <summary>
        /// Get bit depth (alias for MaximumBitDepth for compatibility)
        /// </summary>
        public int? BitDepth => MaximumBitDepth;

        /// <summary>
        /// Get full track title including version if available.
        /// Version field is sanitized to prevent injection attacks.
        /// </summary>
        public string GetFullTitle()
        {
            var title = string.IsNullOrWhiteSpace(Title) ? "Unknown Track" : Title;

            // Sanitize version to prevent injection attacks
            var sanitizedVersion = MetadataSanitizer.SanitizeVersion(Version);

            if (!string.IsNullOrWhiteSpace(sanitizedVersion) && !title.Contains(sanitizedVersion))
            {
                return $"{title} ({sanitizedVersion})";
            }
            return title;
        }

        /// <summary>
        /// Get performer name, falling back to "Various Artists"
        /// </summary>
        public string GetPerformerName()
        {
            if (Performer?.Name.IsNotNullOrWhiteSpace() == true)
                return Performer.Name;

            if (Performers.IsNotNullOrWhiteSpace())
                return Performers;

            return "Various Artists";
        }

        /// <summary>
        /// Get a safe filename for this track
        /// </summary>
        public string GetSafeFileName(string extension = "flac")
        {
            return global::Lidarr.Plugin.Common.Utilities.FileSystemUtilities.CreateTrackFileName(GetFullTitle(), TrackNumber, extension);
        }

        /// <summary>
        /// Check if the track has Hi-Res quality available
        /// </summary>
        public bool HasHiResQuality()
        {
            return MaximumSampleRate > 48000 || MaximumBitDepth > 16;
        }

        /// <summary>
        /// Get the estimated file size for a given format
        /// </summary>
        public long GetEstimatedFileSize(int formatId)
        {
            var sizePerMinute = formatId switch
            {
                5 => 2.4, // MP3 320kbps
                6 => 10.5, // FLAC CD
                7 => 24.0, // FLAC 24/96
                27 => 36.0, // FLAC 24/192
                _ => 10.0
            };

            var durationMinutes = Duration.TotalMinutes;
            return (long)(sizePerMinute * durationMinutes * 1024 * 1024); // Convert to bytes
        }

        /// <summary>
        /// Check if track has explicit content
        /// </summary>
        public bool IsExplicit()
        {
            return ParentalWarning;
        }

        /// <summary>
        /// Get composer name if available
        /// </summary>
        public string GetComposerName()
        {
            return Composer?.Name ?? string.Empty;
        }
    }

    public class QobuzComposer
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("slug")]
        public string Slug { get; set; } = string.Empty;
    }
}
