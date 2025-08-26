using System;
using System.Collections.Generic;
using System.Linq;

namespace Lidarr.Plugin.Common.Models
{
    /// <summary>
    /// Generic representation of an artist from any streaming service.
    /// </summary>
    public class StreamingArtist
    {
        /// <summary>
        /// Unique identifier from the streaming service.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Artist name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Biography or description.
        /// </summary>
        public string Biography { get; set; }

        /// <summary>
        /// Genres associated with this artist.
        /// </summary>
        public List<string> Genres { get; set; } = new List<string>();

        /// <summary>
        /// Country or origin.
        /// </summary>
        public string Country { get; set; }

        /// <summary>
        /// Artist image URLs by size.
        /// </summary>
        public Dictionary<string, string> ImageUrls { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// External URLs (official website, social media, etc.).
        /// </summary>
        public Dictionary<string, string> ExternalUrls { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Service-specific metadata.
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Gets the best available artist image URL.
        /// </summary>
        public string GetBestImageUrl(string preferredSize = "medium")
        {
            if (ImageUrls.ContainsKey(preferredSize))
                return ImageUrls[preferredSize];

            // Fallback to any available image
            return ImageUrls.Values.FirstOrDefault();
        }
    }

    /// <summary>
    /// Generic representation of an album from any streaming service.
    /// </summary>
    public class StreamingAlbum
    {
        /// <summary>
        /// Unique identifier from the streaming service.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Album title.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Primary artist.
        /// </summary>
        public StreamingArtist Artist { get; set; }

        /// <summary>
        /// Additional artists (collaborations, features).
        /// </summary>
        public List<StreamingArtist> AdditionalArtists { get; set; } = new List<StreamingArtist>();

        /// <summary>
        /// Release date.
        /// </summary>
        public DateTime? ReleaseDate { get; set; }

        /// <summary>
        /// Album type (album, single, EP, compilation).
        /// </summary>
        public StreamingAlbumType Type { get; set; } = StreamingAlbumType.Album;

        /// <summary>
        /// Number of tracks.
        /// </summary>
        public int TrackCount { get; set; }

        /// <summary>
        /// Total duration.
        /// </summary>
        public TimeSpan? Duration { get; set; }

        /// <summary>
        /// Genres associated with this album.
        /// </summary>
        public List<string> Genres { get; set; } = new List<string>();

        /// <summary>
        /// Record label.
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// UPC/EAN barcode.
        /// </summary>
        public string Upc { get; set; }

        /// <summary>
        /// Available audio qualities.
        /// </summary>
        public List<StreamingQuality> AvailableQualities { get; set; } = new List<StreamingQuality>();

        /// <summary>
        /// Album artwork URLs by size.
        /// </summary>
        public Dictionary<string, string> CoverArtUrls { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// External URLs (streaming service, official links).
        /// </summary>
        public Dictionary<string, string> ExternalUrls { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Service-specific metadata.
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Gets the primary artist name, handling various artists scenarios.
        /// </summary>
        public string GetPrimaryArtistName()
        {
            if (Artist != null && !string.IsNullOrEmpty(Artist.Name))
                return Artist.Name;

            if (AdditionalArtists.Any())
                return AdditionalArtists.First().Name;

            return "Unknown Artist";
        }

        /// <summary>
        /// Gets all artist names combined.
        /// </summary>
        public string GetAllArtistNames(string separator = ", ")
        {
            var artists = new List<string>();
            
            if (Artist != null && !string.IsNullOrEmpty(Artist.Name))
                artists.Add(Artist.Name);

            artists.AddRange(AdditionalArtists.Select(a => a.Name).Where(n => !string.IsNullOrEmpty(n)));

            return string.Join(separator, artists.Distinct());
        }

        /// <summary>
        /// Gets the best available cover art URL.
        /// </summary>
        public string GetBestCoverArtUrl(string preferredSize = "large")
        {
            if (CoverArtUrls.ContainsKey(preferredSize))
                return CoverArtUrls[preferredSize];

            // Fallback order: large -> medium -> small -> any
            var fallbackOrder = new[] { "large", "medium", "small" };
            foreach (var size in fallbackOrder)
            {
                if (CoverArtUrls.ContainsKey(size))
                    return CoverArtUrls[size];
            }

            return CoverArtUrls.Values.FirstOrDefault();
        }

        /// <summary>
        /// Gets the highest available audio quality.
        /// </summary>
        public StreamingQuality GetBestQuality()
        {
            if (!AvailableQualities.Any())
                return null;

            return AvailableQualities.OrderByDescending(q => q.BitDepth ?? 0)
                                   .ThenByDescending(q => q.SampleRate ?? 0)
                                   .ThenByDescending(q => q.Bitrate ?? 0)
                                   .First();
        }
    }

    /// <summary>
    /// Generic representation of a track from any streaming service.
    /// </summary>
    public class StreamingTrack
    {
        /// <summary>
        /// Unique identifier from the streaming service.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Track title.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Primary artist.
        /// </summary>
        public StreamingArtist Artist { get; set; }

        /// <summary>
        /// Album this track belongs to.
        /// </summary>
        public StreamingAlbum Album { get; set; }

        /// <summary>
        /// Track number within the album.
        /// </summary>
        public int? TrackNumber { get; set; }

        /// <summary>
        /// Disc number for multi-disc albums.
        /// </summary>
        public int? DiscNumber { get; set; }

        /// <summary>
        /// Track duration.
        /// </summary>
        public TimeSpan? Duration { get; set; }

        /// <summary>
        /// Explicit content flag.
        /// </summary>
        public bool IsExplicit { get; set; }

        /// <summary>
        /// ISRC code.
        /// </summary>
        public string Isrc { get; set; }

        /// <summary>
        /// Featured artists.
        /// </summary>
        public List<StreamingArtist> FeaturedArtists { get; set; } = new List<StreamingArtist>();

        /// <summary>
        /// Available audio qualities for this specific track.
        /// </summary>
        public List<StreamingQuality> AvailableQualities { get; set; } = new List<StreamingQuality>();

        /// <summary>
        /// Preview URL (30-90 second snippet).
        /// </summary>
        public string PreviewUrl { get; set; }

        /// <summary>
        /// Popularity/play count (service-specific scale).
        /// </summary>
        public long? Popularity { get; set; }

        /// <summary>
        /// Service-specific metadata.
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Gets the full track title including featured artists.
        /// </summary>
        public string GetFullTitle()
        {
            if (!FeaturedArtists.Any())
                return Title;

            var features = string.Join(", ", FeaturedArtists.Select(a => a.Name));
            return $"{Title} (feat. {features})";
        }

        /// <summary>
        /// Gets the highest available audio quality for this track.
        /// </summary>
        public StreamingQuality GetBestQuality()
        {
            // Use album quality if track-specific not available
            if (!AvailableQualities.Any() && Album?.AvailableQualities.Any() == true)
                return Album.GetBestQuality();

            if (!AvailableQualities.Any())
                return null;

            return AvailableQualities.OrderByDescending(q => q.BitDepth ?? 0)
                                   .ThenByDescending(q => q.SampleRate ?? 0)
                                   .ThenByDescending(q => q.Bitrate ?? 0)
                                   .First();
        }
    }

    /// <summary>
    /// Generic representation of audio quality for streaming services.
    /// </summary>
    public class StreamingQuality
    {
        /// <summary>
        /// Service-specific quality identifier.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Human-readable quality name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Audio format (MP3, FLAC, AAC, etc.).
        /// </summary>
        public string Format { get; set; }

        /// <summary>
        /// Bitrate in kbps (for lossy formats).
        /// </summary>
        public int? Bitrate { get; set; }

        /// <summary>
        /// Sample rate in Hz.
        /// </summary>
        public int? SampleRate { get; set; }

        /// <summary>
        /// Bit depth (16, 24, etc.).
        /// </summary>
        public int? BitDepth { get; set; }

        /// <summary>
        /// Whether this is a lossless format.
        /// </summary>
        public bool IsLossless => 
            Format?.ToUpperInvariant().Contains("FLAC") == true ||
            Format?.ToUpperInvariant().Contains("ALAC") == true ||
            Format?.ToUpperInvariant().Contains("WAV") == true;

        /// <summary>
        /// Whether this is high-resolution audio (>44.1kHz or >16bit).
        /// </summary>
        public bool IsHighResolution => 
            (SampleRate.HasValue && SampleRate.Value > 44100) ||
            (BitDepth.HasValue && BitDepth.Value > 16);

        /// <summary>
        /// Gets a quality tier for comparison purposes.
        /// </summary>
        public StreamingQualityTier GetTier()
        {
            if (IsHighResolution)
                return StreamingQualityTier.HiRes;

            if (IsLossless)
                return StreamingQualityTier.Lossless;

            if (Bitrate >= 320)
                return StreamingQualityTier.High;

            if (Bitrate >= 160)
                return StreamingQualityTier.Normal;

            return StreamingQualityTier.Low;
        }

        public override string ToString()
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(Format))
                parts.Add(Format.ToUpperInvariant());

            if (IsLossless)
            {
                if (SampleRate.HasValue && BitDepth.HasValue)
                    parts.Add($"{SampleRate / 1000.0:F1}kHz/{BitDepth}bit");
            }
            else if (Bitrate.HasValue)
            {
                parts.Add($"{Bitrate}kbps");
            }

            return string.Join(" ", parts);
        }
    }

    /// <summary>
    /// Album types for streaming services.
    /// </summary>
    public enum StreamingAlbumType
    {
        Album,
        Single,
        EP,
        Compilation,
        Soundtrack,
        Live,
        Remix,
        Bootleg
    }

    /// <summary>
    /// Universal quality tiers for comparison across services.
    /// </summary>
    public enum StreamingQualityTier
    {
        Low = 1,      // MP3-96/128, AAC-96
        Normal = 2,   // MP3-160/256, AAC-128/256
        High = 3,     // MP3-320, AAC-320
        Lossless = 4, // FLAC-CD, ALAC-CD (44.1kHz/16bit)
        HiRes = 5     // FLAC-Hi-Res (>44.1kHz or >16bit)
    }

    /// <summary>
    /// Represents a search result from a streaming service.
    /// </summary>
    public class StreamingSearchResult
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public StreamingSearchType Type { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public string Genre { get; set; }
        public string Label { get; set; }
        public string CoverArtUrl { get; set; }
        public int? TrackCount { get; set; }
        public TimeSpan? Duration { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Types of searches supported by streaming services.
    /// </summary>
    public enum StreamingSearchType
    {
        Album,
        Artist, 
        Track,
        Playlist,
        Label
    }
}